package com.canfx.canfxgen

import com.intellij.notification.NotificationAction
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.progress.Task
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.LocalFileSystem
import com.intellij.openapi.vfs.VfsUtil
import java.io.File

/**
 * canfxgen komutlarını çalıştırma akışı: (isteğe bağlı) önce önizleme (--preview) gösterilir, onaylanırsa uygulanır;
 * değişen dosyalar yenilenir, sonuç ve uyarılar bildirilir.
 */
object CanFxFlow {
    /**
     * @param title Bildirim ve pencere başlığı.
     * @param preview Önce değişiklikleri göster, onayla uygula.
     * @param open Uygulandıktan sonra açılacak dosyayı seçer (ör. üretilen entity).
     * @param onFinished Akış bittiğinde (uygulandı, iptal edildi ya da hata) çağrılır; zincirleme işlemler için.
     */
    fun run(
        project: Project,
        title: String,
        workingDirectory: String,
        arguments: List<String>,
        preview: Boolean,
        open: ((List<CanGenChange>) -> String?)? = null,
        onFinished: () -> Unit = {},
    ) {
        // Editördeki son değişiklikler diskte olsun (canfxgen dosyadan okur).
        ApplicationManager.getApplication().runWriteAction { FileDocumentManager.getInstance().saveAllDocuments() }

        if (!preview) {
            apply(project, title, workingDirectory, arguments, open, onFinished)
            return
        }

        background(project, "canfxgen: $title (preview)", workingDirectory, arguments + "--preview") { result ->
            val changes = CanGenRunner.changes(result)
            if (!result.ok && changes.isEmpty()) {
                notify(project, title, (result.error ?: "Unknown error").toHtml(), NotificationType.ERROR)
                onFinished()
                return@background
            }

            val warnings = result.json["warnings"].arr().mapNotNull { it as? String }
            if (changes.isEmpty()) {
                notify(project, title, (listOf("Nothing to change (already in the requested state).") + warnings.map { "⚠ ${it.toHtml()}" }).joinToString("<br/>"), NotificationType.INFORMATION)
                onFinished()
                return@background
            }

            val solution = result.json.str("solution") ?: ""
            if (PreviewDialog(project, title, solution, changes, warnings).showAndGet())
                apply(project, title, workingDirectory, arguments, open, onFinished)
            else
                onFinished()
        }
    }

    private fun apply(
        project: Project,
        title: String,
        workingDirectory: String,
        arguments: List<String>,
        open: ((List<CanGenChange>) -> String?)?,
        onFinished: () -> Unit,
    ) {
        background(project, "canfxgen: $title", workingDirectory, arguments) { result ->
            val changes = CanGenRunner.changes(result)
            report(project, title, result, changes)
            open?.invoke(changes)?.let { open(project, listOf(it)) }
            onFinished()
        }
    }

    /** canfxgen'i arka planda çalıştırır, dosyaları yeniler; sonucu EDT'de verir. */
    private fun background(project: Project, taskTitle: String, workingDirectory: String, arguments: List<String>, done: (CanGenResult) -> Unit) {
        object : Task.Backgroundable(project, taskTitle, true) {
            override fun run(indicator: ProgressIndicator) {
                indicator.isIndeterminate = true
                val result = CanGenRunner.run(workingDirectory, *arguments.toTypedArray())
                refresh(CanGenRunner.changes(result))
                ApplicationManager.getApplication().invokeLater {
                    if (!project.isDisposed) done(result)
                }
            }
        }.queue()
    }

    internal fun refresh(changes: List<CanGenChange>) {
        val files = changes.filter { it.kind in setOf("created", "overwritten", "updated", "deleted") }.map { File(it.path) }
        if (files.isEmpty()) return
        LocalFileSystem.getInstance().refreshIoFiles(files.filter { it.exists() }, false, false, null)
        files.mapNotNull { it.parentFile?.takeIf(File::exists) ?: it.parentFile?.parentFile }.distinct()
            .forEach { VfsUtil.markDirtyAndRefresh(false, true, true, it) }
    }

    private fun report(project: Project, title: String, result: CanGenResult, changes: List<CanGenChange>) {
        if (!result.ok && changes.isEmpty()) {
            notify(project, title, (result.error ?: "Unknown error").toHtml(), NotificationType.ERROR)
            return
        }

        val solution = result.json.str("solution") ?: ""
        val created = changes.filter { it.kind == "created" || it.kind == "overwritten" }
        val updated = changes.filter { it.kind == "updated" }
        val deleted = changes.filter { it.kind == "deleted" }
        val failed = changes.filter { it.kind == "failed" }
        val warnings = result.json["warnings"].arr().mapNotNull { it as? String }

        val lines = buildList {
            add("${created.size} created, ${updated.size} updated" + (if (deleted.isNotEmpty()) ", ${deleted.size} deleted" else "") + ".")
            failed.forEach { add("⚠ ${relative(solution, it.path).toHtml()}: ${(it.note ?: "failed").toHtml()}") }
            warnings.forEach { add("⚠ ${it.toHtml()}") }
        }

        val type = if (failed.isNotEmpty() || !result.ok) NotificationType.WARNING else NotificationType.INFORMATION
        val notification = NotificationGroupManager.getInstance().getNotificationGroup("canfxgen")
            .createNotification("canfxgen: $title", lines.joinToString("<br/>"), type)
        val files = (created + updated).map { it.path }
        if (files.isNotEmpty())
            notification.addAction(NotificationAction.createSimple("Open files") { open(project, files) })
        notification.notify(project)
    }

    internal fun notify(project: Project, title: String, html: String, type: NotificationType) {
        NotificationGroupManager.getInstance().getNotificationGroup("canfxgen")
            .createNotification("canfxgen: $title", html, type)
            .notify(project)
    }

    internal fun open(project: Project, paths: List<String>) {
        val manager = FileEditorManager.getInstance(project)
        val fs = LocalFileSystem.getInstance()
        paths.mapNotNull { fs.findFileByPath(it) ?: fs.refreshAndFindFileByPath(it) }.forEach { manager.openFile(it, true) }
    }

    internal fun relative(solution: String, path: String): String =
        if (solution.isNotEmpty() && path.startsWith(solution)) path.substring(solution.length).trimStart('/', '\\') else path

    internal fun String.toHtml(): String =
        replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\n", "<br/>")
}

package com.canfx.canfxgen

import com.intellij.notification.NotificationAction
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.progress.Task
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.LocalFileSystem
import com.intellij.openapi.vfs.VfsUtil
import java.io.File

/** CRUD üretimini arka planda çalıştırır, dosyaları yeniler, sonucu bildirir ve üretilen dosyaları açar. */
object CanGen {
    /**
     * @param entity Bildirimlerde gösterilen entity adı.
     * @param target canfxgen'e verilen entity adı ya da dosya yolu.
     */
    fun generateCrud(project: Project, entity: String, target: String, workingDirectory: String, options: GenerateCrudOptions) {
        val arguments = buildList {
            add("crud")
            add(target)
            if (options.plural.isNotBlank()) {
                add("--plural")
                add(options.plural.trim())
            }
            if (options.caching) add("--caching")
            if (options.logging) add("--logging")
            if (options.transaction) add("--transaction")
            if (options.secured) add("--secured")
            if (options.dynamicQuery) add("--dynamic-query")
            // Hiçbiri seçilmezse canfxgen "hepsi açık" sayar: en az birini gönder.
            if (!options.caching && !options.logging && !options.transaction && !options.secured && !options.dynamicQuery) add("--no-mechanisms")
        }

        object : Task.Backgroundable(project, "canfxgen: generating CRUD for $entity", true) {
            override fun run(indicator: ProgressIndicator) {
                indicator.isIndeterminate = true
                val result = CanGenRunner.run(workingDirectory, *arguments.toTypedArray())
                val changes = CanGenRunner.changes(result)

                refresh(changes)

                ApplicationManager.getApplication().invokeLater {
                    if (project.isDisposed) return@invokeLater
                    report(project, entity, result, changes)
                }
            }
        }.queue()
    }

    private fun refresh(changes: List<CanGenChange>) {
        val files = changes.filter { it.kind in setOf("created", "overwritten", "updated") }.map { File(it.path) }
        if (files.isNotEmpty()) LocalFileSystem.getInstance().refreshIoFiles(files, false, false, null)
        files.mapNotNull { it.parentFile }.distinct().forEach { VfsUtil.markDirtyAndRefresh(false, true, true, it) }
    }

    private fun report(project: Project, entity: String, result: CanGenResult, changes: List<CanGenChange>) {
        val group = NotificationGroupManager.getInstance().getNotificationGroup("canfxgen")

        if (!result.ok && changes.isEmpty()) {
            group.createNotification("canfxgen: $entity", (result.error ?: "Unknown error").toHtml(), NotificationType.ERROR).notify(project)
            return
        }

        val solution = result.json.str("solution") ?: ""
        fun relative(path: String) = if (solution.isNotEmpty() && path.startsWith(solution)) path.substring(solution.length).trimStart('/', '\\') else path

        val created = changes.filter { it.kind == "created" || it.kind == "overwritten" }
        val updated = changes.filter { it.kind == "updated" }
        val skipped = changes.filter { it.kind == "skipped" }
        val failed = changes.filter { it.kind == "failed" }

        val entityJson = result.json["entity"].obj()
        val lines = buildList {
            add("${created.size} file(s) created, ${updated.size} file(s) updated" + if (skipped.isNotEmpty()) ", ${skipped.size} file(s) already existed and were left unchanged." else ".")
            failed.forEach { add("⚠ ${relative(it.path)}: ${it.note ?: "failed"}") }
            if (!entityJson.bool("hasFactory")) add("⚠ $entity has no Create(...) factory method or public constructor: the create handler contains a TODO.")
            if (!entityJson.bool("hasUpdate")) add("⚠ $entity has no Update(...) method or public setters: the update handler contains a TODO.")
            add("Places to complete are marked <b>TODO(canfxgen)</b> (View → Tool Windows → TODO).")
        }

        val type = when {
            failed.isNotEmpty() -> NotificationType.WARNING
            else -> NotificationType.INFORMATION
        }

        val notification = group.createNotification("canfxgen: $entity", lines.joinToString("<br/>"), type)
        val files = (created + updated).map { it.path }
        if (files.isNotEmpty()) {
            notification.addAction(NotificationAction.createSimple("Open files") { open(project, files) })
        }
        notification.notify(project)

        if (CanGenSettings.getInstance().state.openGeneratedFiles) {
            created.firstOrNull { it.path.endsWith("Create${entity}Command.cs") }?.let { open(project, listOf(it.path)) }
        }
    }

    private fun open(project: Project, paths: List<String>) {
        val manager = FileEditorManager.getInstance(project)
        val fs = LocalFileSystem.getInstance()
        paths.mapNotNull { fs.findFileByPath(it) ?: fs.refreshAndFindFileByPath(it) }.forEach { manager.openFile(it, false) }
    }

    private fun String.toHtml(): String =
        replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\n", "<br/>")
}

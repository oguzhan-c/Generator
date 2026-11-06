package com.canfx.canfxgen

import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.InputValidator
import com.intellij.openapi.ui.Messages
import java.io.File

// ====================================================================== Migrations (EF Core)

/** Migration eylemleri: çözüm kökünde canfxgen migration (Persistence + WebAPI kendiliğinden verilir). */
abstract class MigrationAction : CanFxAction() {
    protected fun root(e: AnActionEvent): File? =
        CanFxTargets.solutionRoot(e.getData(CommonDataKeys.VIRTUAL_FILE)) ?: CanFxTargets.solutionRoot(e.project?.basePath)

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.project != null && CanFxTargets.isCanFxSolution(root(e))
    }

    protected fun persistence(root: File): File? =
        File(root, "src").listFiles()?.firstOrNull { it.isDirectory && it.name.endsWith(".Persistence") }

    protected fun hasMigrations(root: File): Boolean =
        persistence(root)?.let { File(it, "Migrations").listFiles()?.any { f -> f.name.endsWith(".cs") } } == true

    protected fun run(project: Project, root: File, title: String, arguments: List<String>) =
        CanFxFlow.run(project, title, root.path, arguments, preview = false,
            open = { changes -> changes.firstOrNull { it.kind == "created" && !it.path.endsWith(".Designer.cs") && !it.path.endsWith("Snapshot.cs") }?.path })
}

class AddMigrationAction : MigrationAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val root = root(e) ?: return
        val first = !hasMigrations(root)
        val validator = object : InputValidator {
            override fun checkInput(input: String?) = input?.matches(Regex("[A-Za-z_][A-Za-z0-9_]*")) == true
            override fun canClose(input: String?) = checkInput(input)
        }
        val result = Messages.showInputDialogWithCheckBox(
            "Migration name (e.g. AddBrandCode). It is created in the Persistence project; the WebAPI settings are used.",
            "Add Migration",
            "Use migrations on startup in Development (Database:StartupMode = Migrate, data is kept)",
            first, true, null, if (first) "Initial" else "", validator,
        )
        val name = result.first?.trim().takeUnless { it.isNullOrEmpty() } ?: return
        val arguments = buildList {
            addAll(listOf("migration", "add", name))
            if (result.second == true) addAll(listOf("--startup-mode", "Migrate"))
        }
        run(project, root, "Migration $name", arguments)
    }
}

class UpdateDatabaseAction : MigrationAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val root = root(e) ?: return
        if (!hasMigrations(root)) {
            Messages.showInfoMessage(project, "There are no migrations yet. Add one first (CanFx → Migrations → Add Migration).", "Update Database")
            return
        }
        if (Messages.showOkCancelDialog(project,
                "Applies the pending migrations to the database in ConnectionStrings (Development settings).\n" +
                    "The database must be running (run the application once, DevServices starts it).",
                "Update Database", "Update", "Cancel", null) != Messages.OK) return
        run(project, root, "Update database", listOf("migration", "update"))
    }
}

class RemoveMigrationAction : MigrationAction() {
    override fun update(e: AnActionEvent) {
        super.update(e)
        if (e.presentation.isVisible) e.presentation.isEnabled = root(e)?.let(::hasMigrations) == true
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val root = root(e) ?: return
        val last = persistence(root)?.let { File(it, "Migrations").listFiles() }
            ?.map { it.name }?.filter { Regex("""^\d{14}_\w+\.cs$""").matches(it) }?.maxOrNull()?.removeSuffix(".cs") ?: "the last migration"
        if (Messages.showOkCancelDialog(project,
                "Removes $last and reverts the model snapshot.\nIf it is already applied to the database, update the database to the previous migration first.",
                "Remove Last Migration", "Remove", "Cancel", null) != Messages.OK) return
        run(project, root, "Remove migration", listOf("migration", "remove"))
    }
}

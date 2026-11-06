package com.canfx.canfxgen

import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.progress.ProgressManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.DialogWrapper
import com.intellij.openapi.ui.Messages
import com.intellij.ui.dsl.builder.bind
import com.intellij.ui.dsl.builder.panel
import java.io.File
import javax.swing.JComponent

// ====================================================================== Database

/**
 * Veritabanı seçimi: PostgreSQL / SQL Server şablondaki hazır yorumlardan açılır (paket, sağlayıcı satırı,
 * docker-compose, bağlantı dizesi, DevServices). Başka bir veritabanı elle ayarlanır; canfxgen ona dokunmaz.
 */
class DatabaseDialog(project: Project, private val current: String) : DialogWrapper(project) {
    var choice: String = if (current == "sqlserver") "sqlserver" else if (current == "custom") "other" else "postgres"

    init {
        title = "Database"
        setOKButtonText(if (current == "custom") "OK" else "Preview")
        init()
    }

    override fun createCenterPanel(): JComponent = panel {
        row {
            label("<html>Current: <b>" + when (current) {
                "postgres" -> "PostgreSQL"
                "sqlserver" -> "SQL Server"
                "custom" -> "another provider, set up by hand"
                else -> "not chosen yet"
            } + "</b></html>")
        }
        buttonsGroup {
            row { radioButton("PostgreSQL", "postgres") }
            row { radioButton("SQL Server", "sqlserver") }
            row {
                radioButton("Another database (by hand)", "other")
                    .comment("SQLite, MySQL, Oracle ...: add its EF Core package and the options.UseXxx(...) line yourself;<br/>" +
                        "see the \"Veritabanı\" comment in Persistence/PersistenceServiceRegistration.cs. canfxgen does not touch it.")
            }
        }.bind(::choice)
        row {
            comment("PostgreSQL / SQL Server: the provider line, the docker-compose service and volume, the Development connection<br/>" +
                "string and the DevServices entry are enabled from the comments; the other provider is commented out.<br/>" +
                "The NuGet package is not added: you pick its version (dotnet add package); the command is shown if it is missing.")
        }
    }
}

class DatabaseAction : CanFxAction() {
    private fun root(e: AnActionEvent): File? =
        CanFxTargets.solutionRoot(e.getData(CommonDataKeys.VIRTUAL_FILE)) ?: CanFxTargets.solutionRoot(e.project?.basePath)

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.project != null && CanFxTargets.isCanFxSolution(root(e))
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val root = root(e) ?: return
        val result = ProgressManager.getInstance().runProcessWithProgressSynchronously<CanGenResult, RuntimeException>(
            { CanGenRunner.run(root.path, "database") }, "Reading the database provider", true, project
        )
        if (!result.ok) {
            Messages.showErrorDialog(project, result.error ?: "canfxgen failed.", "Database")
            return
        }
        val current = result.json.str("provider") ?: "none"
        val dialog = DatabaseDialog(project, current)
        if (!dialog.showAndGet() || dialog.choice == "other") return
        if (dialog.choice == current) {
            CanFxFlow.notify(project, "Database", "Already ${if (current == "postgres") "PostgreSQL" else "SQL Server"}.", com.intellij.notification.NotificationType.INFORMATION)
            return
        }
        CanFxFlow.run(project, "Database: ${dialog.choice}", root.path, listOf("database", dialog.choice), preview = true)
    }
}

package com.canfx.canfxgen

import com.intellij.icons.AllIcons
import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.actionSystem.DefaultActionGroup
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.DialogWrapper
import com.intellij.openapi.ui.InputValidator
import com.intellij.openapi.ui.Messages
import com.intellij.openapi.ui.ValidationInfo
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.ui.dsl.builder.AlignX
import com.intellij.ui.dsl.builder.bindIntText
import com.intellij.ui.dsl.builder.bindItem
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.bindText
import com.intellij.ui.dsl.builder.panel
import com.intellij.openapi.ui.ComboBox
import javax.swing.JComponent

/** Sağ tık menüsündeki "CanFx" alt menüsü: yalnızca CanFx çözümlerinde görünür. */
class CanFxActionGroup : DefaultActionGroup() {
    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(e: AnActionEvent) {
        val root = CanFxTargets.solutionRoot(e.getData(CommonDataKeys.VIRTUAL_FILE)) ?: CanFxTargets.solutionRoot(e.project?.basePath)
        e.presentation.isEnabledAndVisible = e.project != null && CanFxTargets.isCanFxSolution(root)
    }
}

/** Ortak: BGT güncelleme. */
abstract class CanFxAction : AnAction() {
    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT
}

// ====================================================================== Add → CanFx Entity (Not 2, 3)

/** Domain projesinde Add (New) menüsünde: yalnızca ad sorulur; taban sınıf seçenekleri dosyada yorum olarak gelir. */
class NewEntityAction : CanFxAction() {
    init {
        templatePresentation.icon = AllIcons.Nodes.Class
    }

    override fun update(e: AnActionEvent) {
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE)
        e.presentation.isEnabledAndVisible = e.project != null && file != null && EntityDetector.isInDomainProject(file)
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return
        val directory = CanFxTargets.directoryOf(file)

        val name = Messages.showInputDialog(
            project,
            "Entity name (singular, e.g. Brand). The base class options are listed as comments in the new file.",
            "New CanFx Entity",
            AllIcons.Nodes.Class,
            "",
            object : InputValidator {
                override fun checkInput(inputString: String?) = inputString?.trim()?.matches(Regex("[A-Za-z][A-Za-z0-9]*")) == true
                override fun canClose(inputString: String?) = checkInput(inputString)
            },
        )?.trim() ?: return

        CanFxFlow.run(project, "New entity $name", directory, listOf("generate", "entity", name, "--folder", directory), preview = false,
            open = { changes -> changes.firstOrNull { it.kind == "created" && it.path.endsWith("$name.cs", ignoreCase = true) }?.path })
    }
}

// ====================================================================== Add Seed Data (Not 13)

class SeedOptions {
    var required = true
    var sample = true
    var count = 20
    var hasData = false
}

class SeedDataDialog(project: Project, private val entity: String, private val options: SeedOptions) : DialogWrapper(project) {
    init {
        title = "Add Seed Data: $entity"
        setOKButtonText("Generate")
        init()
    }

    override fun createCenterPanel(): JComponent = panel {
        row {
            checkBox("Required data (every environment)").bindSelected(options::required)
                .comment("Persistence/Seeding/Data/*.json → ${entity}Seeder adds the missing records on startup.")
        }
        row {
            checkBox("Sample data (Development only)").bindSelected(options::sample)
                .comment("${entity}SampleSeeder fills screens with sample values (SampleData), related records are picked from existing ones.")
        }
        row("Sample count:") { intTextField(1..10_000).bindIntText(options::count) }
        row {
            checkBox("HasData (EF Core migration)").bindSelected(options::hasData)
                .comment("Records are written into the migration; Ids must be fixed. For rarely changing fixed lists.")
        }
    }

    override fun doValidate(): ValidationInfo? =
        if (!options.required && !options.sample && !options.hasData) ValidationInfo("Choose at least one.") else null
}

class SeedDataAction : CanFxAction() {
    override fun update(e: AnActionEvent) {
        val entity = e.getData(CommonDataKeys.VIRTUAL_FILE)?.let(EntityDetector::entityName)
        e.presentation.isEnabledAndVisible = e.project != null && entity != null
        if (entity != null) e.presentation.text = "Add Seed Data for $entity..."
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return
        val entity = EntityDetector.entityName(file) ?: return
        val options = SeedOptions()
        if (!SeedDataDialog(project, entity, options).showAndGet()) return
        if (!options.required && !options.sample && !options.hasData) return

        val arguments = buildList {
            addAll(listOf("generate", "seeder", file.path))
            if (!options.sample) add("--required-only")
            if (!options.required) add("--sample-only")
            if (options.sample) addAll(listOf("--count", options.count.toString()))
            if (options.hasData) add("--has-data")
        }
        CanFxFlow.run(project, "Seed data for $entity", CanFxTargets.directoryOf(file), arguments, preview = false,
            open = { changes -> changes.firstOrNull { it.path.endsWith("${entity}Seeder.cs") }?.path })
    }
}

// ====================================================================== Mechanisms (Not 5)

private val toggleChoices = listOf("Keep", "Enable", "Disable")

class MechanismsDialog(
    project: Project,
    private val target: String,
    private val current: Set<String>?,
    private val allowDynamic: Boolean,
    val choices: MutableMap<String, String>,
) : DialogWrapper(project) {
    init {
        title = "Mechanisms: $target"
        setOKButtonText("Preview")
        init()
    }

    override fun createCenterPanel(): JComponent = panel {
        row {
            label(
                "<html>Turns mechanisms on or off for <b>$target</b>" +
                    (if (current == null) " (all requests of the feature)" else "") +
                    ".<br/>Only the request class changes (interfaces and their members); the changes are shown before they are applied.</html>"
            )
        }
        val names = linkedMapOf(
            "caching" to "Caching (queries cached; commands clear the cache)",
            "logging" to "Logging",
            "transaction" to "Transaction",
            "secured" to "Secured operation (permissions)",
        )
        if (allowDynamic) names["dynamic-query"] = "Dynamic query (GetListByDynamic)"
        for ((key, label) in names) {
            choices[key] = "Keep"
            val state = current?.let { if (key in it) "  — currently on" else "  — currently off" } ?: ""
            row("$label$state:") {
                comboBox(toggleChoices).bindItem({ choices[key] ?: "Keep" }, { choices[key] = it ?: "Keep" })
            }
        }
    }
}

class MechanismsAction : CanFxAction() {
    private data class Target(val name: String, val current: Set<String>?, val dynamic: Boolean)

    private fun target(file: VirtualFile): Target? {
        CanFxTargets.featureFolder(file)?.let { return Target(it, null, true) }
        EntityDetector.entityName(file)?.let { return Target(it, null, true) }
        CanFxTargets.requestName(file)?.let { return Target(it, CanFxTargets.enabledMechanisms(file), false) }
        return null
    }

    override fun update(e: AnActionEvent) {
        val target = e.getData(CommonDataKeys.VIRTUAL_FILE)?.let(::target)
        e.presentation.isEnabledAndVisible = e.project != null && target != null
        if (target != null) e.presentation.text = "Mechanisms of ${target.name}..."
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return
        val target = target(file) ?: return
        val choices = linkedMapOf<String, String>()
        if (!MechanismsDialog(project, target.name, target.current, target.dynamic, choices).showAndGet()) return

        val enable = choices.filterValues { it == "Enable" }.keys.toList()
        val disable = choices.filterValues { it == "Disable" }.keys.toList()
        val directory = CanFxTargets.directoryOf(file)
        val steps = buildList {
            if (enable.isNotEmpty()) add(listOf("enable") + enable + target.name)
            if (disable.isNotEmpty()) add(listOf("disable") + disable + target.name)
        }
        runSteps(project, target.name, directory, steps)
    }
}

/** Komutları sırayla (her biri önizlemeli) çalıştırır. */
internal fun runSteps(project: Project, title: String, directory: String, steps: List<List<String>>) {
    if (steps.isEmpty()) return
    val step = steps.first()
    CanFxFlow.run(project, "$title: ${step.dropLast(1).joinToString(" ")}", directory, step, preview = true,
        onFinished = { runSteps(project, title, directory, steps.drop(1)) })
}

// ====================================================================== Rate limit (Not 8)

class RateLimitOptions {
    var policy = "reports"
    var permit = 60
    var window = "00:01:00"
    var partitionBy = "UserOrIp"
    var remove = false
}

class RateLimitDialog(project: Project, private val target: String, private val options: RateLimitOptions) : DialogWrapper(project) {
    init {
        title = "Rate Limit: $target"
        setOKButtonText("Preview")
        init()
    }

    override fun createCenterPanel(): JComponent = panel {
        row {
            label("<html>Binds a rate limit policy to <b>$target</b> ([EnableRateLimiting]).<br/>" +
                "If the policy is missing in appsettings.json (RateLimiting:Policies) it is added with the values below.</html>")
        }
        row("Policy:") {
            val combo = ComboBox(arrayOf("reports", "auth", "sensitive")).apply { isEditable = true }
            cell(combo).bindItem({ options.policy }, { options.policy = it ?: "reports" }).align(AlignX.FILL)
                .comment("auth: sign-in / register (per IP), sensitive: code sending, password. Others: lower case, digits and dashes.")
        }
        row("Permit limit:") { intTextField(1..1_000_000).bindIntText(options::permit) }
        row("Window:") { textField().bindText(options::window).comment("hh:mm:ss, e.g. 00:01:00") }
        row("Partition by:") { comboBox(listOf("UserOrIp", "Ip", "Tenant")).bindItem({ options.partitionBy }, { options.partitionBy = it ?: "UserOrIp" }) }
        row { checkBox("Remove the binding instead").bindSelected(options::remove) }
    }
}

class RateLimitAction : CanFxAction() {
    private fun target(e: AnActionEvent, file: VirtualFile): String? =
        CanFxTargets.requestName(file) ?: CanFxTargets.controllerTarget(file, e.getData(CommonDataKeys.EDITOR))

    override fun update(e: AnActionEvent) {
        val target = e.getData(CommonDataKeys.VIRTUAL_FILE)?.let { target(e, it) }
        e.presentation.isEnabledAndVisible = e.project != null && target != null
        if (target != null) e.presentation.text = "Rate Limit for $target..."
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return
        val target = target(e, file) ?: return
        val options = RateLimitOptions()
        if (!RateLimitDialog(project, target, options).showAndGet()) return

        val arguments = buildList {
            addAll(listOf("rate-limit", options.policy.trim(), target))
            if (options.remove) add("--remove")
            else addAll(listOf("--permit", options.permit.toString(), "--window", options.window.trim(), "--partition-by", options.partitionBy))
        }
        CanFxFlow.run(project, "Rate limit for $target", CanFxTargets.directoryOf(file), arguments, preview = true)
    }
}

// ====================================================================== Features (Not 6)

class FeaturesDialog(project: Project, private val current: Map<String, Any?>, val choices: MutableMap<String, String>) : DialogWrapper(project) {
    init {
        title = "CanFx Features"
        setOKButtonText("Preview")
        init()
    }

    override fun createCenterPanel(): JComponent = panel {
        row {
            label("<html>Adds or removes template features: package, registration, appsettings and docker-compose together.<br/>" +
                "Each change is previewed before it is applied (the template is generated twice for comparison, this takes a few seconds).</html>")
        }
        for ((key, label) in FEATURES) {
            choices[key] = "Keep"
            val state = when (current[FEATURE_OPTION.getValue(key)]) {
                true -> "  — currently on"
                false -> "  — currently off"
                else -> ""
            }
            row("$label$state:") {
                comboBox(listOf("Keep", "Add", "Remove")).bindItem({ choices[key] ?: "Keep" }, { choices[key] = it ?: "Keep" })
            }
        }
    }

    companion object {
        val FEATURES = linkedMapOf(
            "redis" to "Redis (distributed cache, shared keys, rate limit counters)",
            "search" to "Search (in-memory / Elasticsearch)",
            "event-bus" to "Event bus + outbox (RabbitMQ)",
            "hangfire" to "Hangfire (persistent jobs, cron, dashboard)",
            "file-storage" to "File storage (local / S3 / Azure Blob)",
            "realtime" to "Realtime notifications (SignalR)",
            "sms" to "SMS",
            "open-telemetry" to "OpenTelemetry",
            "postman" to "Postman sync (Development)",
        )
        val FEATURE_OPTION = mapOf(
            "redis" to "Redis", "search" to "Search", "event-bus" to "EventBus", "hangfire" to "Hangfire", "file-storage" to "FileStorage",
            "realtime" to "Realtime", "sms" to "Sms", "open-telemetry" to "OpenTelemetry", "postman" to "Postman",
        )
    }
}

class FeaturesAction : CanFxAction() {
    override fun update(e: AnActionEvent) {
        val root = CanFxTargets.solutionRoot(e.getData(CommonDataKeys.VIRTUAL_FILE)) ?: CanFxTargets.solutionRoot(e.project?.basePath)
        e.presentation.isEnabledAndVisible = e.project != null && CanFxTargets.isCanFxSolution(root)
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val root = CanFxTargets.solutionRoot(e.getData(CommonDataKeys.VIRTUAL_FILE)) ?: CanFxTargets.solutionRoot(project.basePath) ?: return
        val choices = linkedMapOf<String, String>()
        if (!FeaturesDialog(project, CanFxTargets.templateOptions(root), choices).showAndGet()) return

        val steps = choices.filterValues { it != "Keep" }.map { (feature, choice) ->
            listOf(if (choice == "Add") "add" else "remove", "feature", feature)
        }
        runSteps(project, "Features", root.path, steps)
    }
}

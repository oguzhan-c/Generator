package com.canfx.canfxgen

import com.intellij.icons.AllIcons
import com.intellij.notification.NotificationType
import com.intellij.openapi.progress.ProgressManager
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

// ====================================================================== Mechanisms (Not 5)

/** Bir mekanizmanın hedefteki durumu (canfxgen mechanisms --json). */
data class MechanismState(val name: String, val enabled: Int, val total: Int) {
    val state: String get() = when {
        total == 0 -> NOT_APPLICABLE
        enabled == 0 -> OFF
        enabled == total -> ON
        else -> MIXED
    }

    companion object {
        const val ON = "On"
        const val OFF = "Off"
        const val MIXED = "Mixed (keep)"
        const val NOT_APPLICABLE = "n/a"
    }
}

private val mechanismLabels = linkedMapOf(
    "caching" to "Caching (queries cached; commands clear the cache)",
    "logging" to "Logging",
    "transaction" to "Transaction",
    "secured" to "Secured operation (permissions)",
    "dynamic-query" to "Dynamic query (GetListByDynamic)",
)

/**
 * Mekanizmalar mevcut durumlarıyla açılır (açık / kapalı / karışık); yalnızca değiştirilen satırlar uygulanır.
 */
class MechanismsDialog(
    project: Project,
    private val target: String,
    private val singleRequest: Boolean,
    private val requests: Int,
    private val states: List<MechanismState>,
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
                "<html>Mechanisms of <b>$target</b>" +
                    (if (singleRequest) "" else " ($requests requests)") +
                    ", as they are now. Change a row to turn it on or off;<br/>" +
                    "only changed rows are applied and the changes are shown before they are applied.</html>"
            )
        }
        for (state in states) {
            val label = mechanismLabels[state.name] ?: state.name
            if (state.total == 0) {
                row("$label:") { label("not applicable") }
                continue
            }
            choices[state.name] = state.state
            val options = if (state.state == MechanismState.MIXED) listOf(MechanismState.MIXED, MechanismState.ON, MechanismState.OFF) else listOf(MechanismState.ON, MechanismState.OFF)
            val detail = if (state.state == MechanismState.MIXED) "  (on in ${state.enabled} of ${state.total})" else ""
            row("$label$detail:") {
                comboBox(options).bindItem({ choices[state.name] ?: state.state }, { choices[state.name] = it ?: state.state })
            }
        }
    }
}

class MechanismsAction : CanFxAction() {
    private fun target(file: VirtualFile): String? =
        CanFxTargets.featureFolder(file) ?: EntityDetector.entityName(file) ?: CanFxTargets.requestName(file)

    override fun update(e: AnActionEvent) {
        val target = e.getData(CommonDataKeys.VIRTUAL_FILE)?.let(::target)
        e.presentation.isEnabledAndVisible = e.project != null && target != null
        if (target != null) e.presentation.text = "Mechanisms of $target..."
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return
        val target = target(file) ?: return
        val directory = CanFxTargets.directoryOf(file)

        // Mevcut durum (Roslyn ile canfxgen okur).
        val result = ProgressManager.getInstance().runProcessWithProgressSynchronously<CanGenResult, RuntimeException>(
            { CanGenRunner.run(directory, "mechanisms", target) }, "Reading mechanisms of $target", true, project
        )
        if (!result.ok) {
            Messages.showErrorDialog(project, result.error ?: "canfxgen failed.", "Mechanisms: $target")
            return
        }
        val states = result.json["mechanisms"].arr().map { it.obj() }.map {
            MechanismState(it.str("name") ?: "", (it["enabled"] as? Double)?.toInt() ?: 0, (it["total"] as? Double)?.toInt() ?: 0)
        }
        val name = result.json.str("target") ?: target
        val requests = (result.json["requests"] as? Double)?.toInt() ?: 0
        val choices = linkedMapOf<String, String>()
        if (!MechanismsDialog(project, name, result.json.bool("singleRequest"), requests, states, choices).showAndGet()) return

        val before = states.associate { it.name to it.state }
        val enable = choices.filter { (key, value) -> value == MechanismState.ON && before[key] != MechanismState.ON }.keys.toList()
        val disable = choices.filter { (key, value) -> value == MechanismState.OFF && before[key] != MechanismState.OFF }.keys.toList()
        if (enable.isEmpty() && disable.isEmpty()) {
            CanFxFlow.notify(project, "Mechanisms: $name", "Nothing changed.", NotificationType.INFORMATION)
            return
        }
        val steps = buildList {
            if (enable.isNotEmpty()) add(listOf("enable") + enable + target)
            if (disable.isNotEmpty()) add(listOf("disable") + disable + target)
        }
        runSteps(project, name, directory, steps)
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
            label("<html>Template features of this solution, as they are now. Change a row to add or remove the feature<br/>" +
                "(package, registration, appsettings and docker-compose together); only changed rows are applied, each with a preview.</html>")
        }
        for ((key, label) in FEATURES) {
            // Mevcut durum canfx.json'dan; bilinmiyorsa (eski proje) Off sayılır.
            val on = current[FEATURE_OPTION.getValue(key)] == true
            choices[key] = if (on) MechanismState.ON else MechanismState.OFF
            row("$label:") {
                comboBox(listOf(MechanismState.ON, MechanismState.OFF)).bindItem({ choices[key] }, { choices[key] = it ?: choices.getValue(key) })
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
        val current = CanFxTargets.templateOptions(root)
        val choices = linkedMapOf<String, String>()
        if (!FeaturesDialog(project, current, choices).showAndGet()) return

        // Yalnızca değiştirilen satırlar.
        val steps = choices.filter { (feature, choice) ->
            (choice == MechanismState.ON) != (current[FeaturesDialog.FEATURE_OPTION.getValue(feature)] == true)
        }.map { (feature, choice) -> listOf(if (choice == MechanismState.ON) "add" else "remove", "feature", feature) }
        if (steps.isEmpty()) {
            CanFxFlow.notify(project, "Features", "Nothing changed.", NotificationType.INFORMATION)
            return
        }
        runSteps(project, "Features", root.path, steps)
    }
}

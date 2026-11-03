package com.canfx.canfxgen

import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.progress.ProgressManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.ComboBox
import com.intellij.openapi.ui.DialogWrapper
import com.intellij.openapi.ui.Messages
import com.intellij.openapi.ui.ValidationInfo
import com.intellij.ui.ToolbarDecorator
import com.intellij.ui.components.JBLabel
import com.intellij.ui.dsl.builder.AlignX
import com.intellij.ui.dsl.builder.bindIntText
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.panel
import com.intellij.ui.table.JBTable
import java.awt.Dimension
import java.io.File
import javax.swing.DefaultCellEditor
import javax.swing.JComponent
import javax.swing.table.DefaultTableModel

// ====================================================================== Add Seed Data

/** Seed diyaloğunun açılış durumu (canfxgen generate seeder --describe). */
class SeedState(json: Map<String, Any?>) {
    class Field(val name: String, val type: String, val valueType: String, val nullable: Boolean, val enumValues: List<String>,
        val related: String?, val sampleKind: String, val sampleKinds: List<String>)

    val entity: String = json.str("entity") ?: ""
    val creation: String = json.str("creation") ?: ""
    val canCreate: Boolean = json.bool("canCreate")
    val fields: List<Field> = json["fields"].arr().map { it.obj() }.map {
        Field(it.str("name") ?: "", it.str("type") ?: "", it.str("valueType") ?: "other", it.bool("nullable"),
            it["enumValues"].arr().mapNotNull { v -> v as? String }, it.str("related"), it.str("sampleKind") ?: "Default",
            it["sampleKinds"].arr().mapNotNull { v -> v as? String })
    }
    val examples: Map<String, String> = json["kinds"].arr().map { it.obj() }.associate { (it.str("kind") ?: "") to (it.str("example") ?: "") }
    val requiredStorage: String = json.str("requiredStorage") ?: "none"
    val records: List<Map<String, String?>> = json["records"].arr().map { r -> r.obj().mapValues { (_, v) -> v?.toString() } }
    val sampleExists: Boolean = json.bool("sampleExists")
    val sampleCount: Int = (json["sampleCount"] as? Double)?.toInt() ?: 20
    val sampleKinds: Map<String, String> = json["sampleKinds"].obj().mapValues { (_, v) -> v as? String ?: "" }
}

/**
 * Seed verisi: projedeki mevcut durumla açılır. Başlangıç verisi tabloda girilir (JSON dosyasına ya da HasData ile
 * migration'a yazılır); örnek veride kayıt sayısı ve alan başına değer türü seçilir.
 */
class SeedDataDialog(project: Project, private val state: SeedState) : DialogWrapper(project) {
    var initial = state.requiredStorage != "none" || !state.sampleExists
    var migration = state.requiredStorage == "migration"
    var sample = state.sampleExists || state.requiredStorage == "none"
    var count = state.sampleCount
    val kinds = linkedMapOf<String, String>().apply { state.fields.forEach { put(it.name, state.sampleKinds[it.name] ?: it.sampleKind) } }

    // Tablo: görünen sütunlar alanlar; Id (HasData) gizli tutulur ve korunur.
    private val model = object : DefaultTableModel(state.fields.map { it.name }.toTypedArray(), 0) {}
    private val hiddenIds = mutableListOf<String?>()
    private val table = JBTable(model)

    init {
        title = "Seed Data: ${state.entity}"
        setOKButtonText("Preview")
        for (record in state.records) {
            model.addRow(state.fields.map { record[it.name] ?: "" }.toTypedArray())
            hiddenIds.add(record["Id"])
        }
        state.fields.forEachIndexed { index, field ->
            val values = when (field.valueType) {
                "enum" -> (if (field.nullable) listOf("") else emptyList()) + field.enumValues
                "bool" -> (if (field.nullable) listOf("") else emptyList()) + listOf("true", "false")
                else -> null
            }
            if (values != null) table.columnModel.getColumn(index).cellEditor = DefaultCellEditor(ComboBox(values.toTypedArray()))
        }
        table.preferredScrollableViewportSize = Dimension(560, 160)
        init()
    }

    /** Tablodaki kayıtlar (alan → metin), gizli Id'lerle. */
    fun records(): List<Map<String, String?>> {
        if (table.isEditing) table.cellEditor.stopCellEditing()
        return (0 until model.rowCount).map { row ->
            buildMap {
                hiddenIds.getOrNull(row)?.let { put("Id", it) }
                state.fields.forEachIndexed { column, field -> put(field.name, model.getValueAt(row, column)?.toString()) }
            }
        }
    }

    override fun createCenterPanel(): JComponent = panel {
        if (!state.canCreate) {
            row {
                label("<html><b>${state.entity}</b> has no factory method, public constructor or public setters; the generated seeder<br/>" +
                    "will contain a TODO for creating it.</html>")
            }
        }
        group("Initial data (every environment)") {
            row {
                checkBox("Add initial data").bindSelected(::initial)
                    .comment("Records the application needs to run (statuses, currencies, colors ...). Existing records are kept; missing ones are added.")
            }
            row {
                val decorated = ToolbarDecorator.createDecorator(table)
                    .setAddAction {
                        model.addRow(state.fields.map { f -> if (f.valueType == "enum" && !f.nullable) f.enumValues.firstOrNull() ?: "" else "" }.toTypedArray())
                        hiddenIds.add(null)
                    }
                    .setRemoveAction {
                        table.selectedRows.sortedDescending().forEach { model.removeRow(it); hiddenIds.removeAt(it) }
                    }
                    .disableUpDownActions()
                    .createPanel()
                cell(decorated).align(AlignX.FILL)
            }
            row {
                checkBox("Write into the migration (EF Core HasData, fixed Ids)").bindSelected(::migration)
                    .comment(if (migration) "Currently stored in ${state.entity}SeedData (migration)." else "Default: Seeding/Data/*.json read by ${state.entity}Seeder on startup.")
            }
        }
        group("Sample data (Development only)") {
            row {
                checkBox("Add sample data").bindSelected(::sample)
                    .comment("${state.entity}SampleSeeder fills screens in Development; values are generated, related records are picked from existing ones.")
            }
            row("Count:") { intTextField(1..10_000).bindIntText(::count) }
            for (field in state.fields) {
                row("${field.name} (${field.type}):") {
                    val example = JBLabel(state.examples[kinds[field.name]] ?: "")
                    val combo = ComboBox(field.sampleKinds.toTypedArray()).apply {
                        selectedItem = kinds[field.name]
                        addActionListener {
                            kinds[field.name] = selectedItem as String
                            example.text = state.examples[selectedItem as String] ?: ""
                        }
                    }
                    cell(combo)
                    cell(example)
                }
            }
        }
    }

    override fun doValidate(): ValidationInfo? =
        if (!initial && !sample) ValidationInfo("Choose initial data, sample data or both.") else null
}

class SeedDataAction : CanFxAction() {
    override fun update(e: AnActionEvent) {
        val entity = e.getData(CommonDataKeys.VIRTUAL_FILE)?.let(EntityDetector::entityName)
        e.presentation.isEnabledAndVisible = e.project != null && entity != null
        if (entity != null) e.presentation.text = "Seed Data for $entity..."
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return
        val entity = EntityDetector.entityName(file) ?: return
        val directory = CanFxTargets.directoryOf(file)

        val result = ProgressManager.getInstance().runProcessWithProgressSynchronously<CanGenResult, RuntimeException>(
            { CanGenRunner.run(directory, "generate", "seeder", file.path, "--describe") }, "Reading seed data of $entity", true, project
        )
        if (!result.ok) {
            Messages.showErrorDialog(project, result.error ?: "canfxgen failed.", "Seed Data: $entity")
            return
        }
        val dialog = SeedDataDialog(project, SeedState(result.json["seed"].obj()))
        if (!dialog.showAndGet()) return

        val arguments = buildList {
            addAll(listOf("generate", "seeder", file.path))
            if (!dialog.sample) add("--required-only")
            if (!dialog.initial) add("--sample-only")
            if (dialog.initial) {
                val data = File.createTempFile("canfx-seed-", ".json").apply { deleteOnExit() }
                data.writeText(JsonWriter.records(dialog.records()))
                addAll(listOf("--data", data.path))
                if (dialog.migration) add("--has-data")
            }
            if (dialog.sample) {
                addAll(listOf("--count", dialog.count.toString()))
                addAll(listOf("--sample-kinds", dialog.kinds.entries.joinToString("; ") { "${it.key}=${it.value}" }))
            }
        }
        CanFxFlow.run(project, "Seed data for $entity", directory, arguments, preview = true,
            open = { changes -> changes.firstOrNull { it.path.endsWith(".json") || it.path.endsWith("SeedData.cs") }?.path })
    }
}

/** Kayıtları canfxgen'in --data biçiminde yazar: [{ "Alan": "değer" | null }]. */
internal object JsonWriter {
    fun records(records: List<Map<String, String?>>): String =
        records.joinToString(",\n", "[\n", "\n]\n") { record ->
            record.entries.joinToString(", ", "  { ", " }") { (key, value) -> "${string(key)}: ${value?.let(::string) ?: "null"}" }
        }

    private fun string(value: String): String = buildString {
        append('"')
        for (c in value) {
            when (c) {
                '"' -> append("\\\"")
                '\\' -> append("\\\\")
                '\n' -> append("\\n")
                '\r' -> append("\\r")
                '\t' -> append("\\t")
                else -> if (c < ' ') append("\\u%04x".format(c.code)) else append(c)
            }
        }
        append('"')
    }
}

package com.canfx.canfxgen

import com.intellij.ide.util.PropertiesComponent
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.DialogWrapper
import com.intellij.ui.dsl.builder.AlignX
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.bindText
import com.intellij.ui.dsl.builder.panel
import javax.swing.JComponent

/** CRUD üretim seçenekleri. */
class GenerateCrudOptions {
    var plural: String = ""

    // Mekanizmalar (nArchitecture'daki gibi)
    var caching: Boolean = true
    var logging: Boolean = true
    var transaction: Boolean = true
    var secured: Boolean = true
    var dynamicQuery: Boolean = true

    /** Son seçilen mekanizmalar (proje başına): bir sonraki CRUD'da aynılarıyla açılır. */
    fun load(project: Project): GenerateCrudOptions = apply {
        val p = PropertiesComponent.getInstance(project)
        caching = p.getBoolean(KEY + "caching", true)
        logging = p.getBoolean(KEY + "logging", true)
        transaction = p.getBoolean(KEY + "transaction", true)
        secured = p.getBoolean(KEY + "secured", true)
        dynamicQuery = p.getBoolean(KEY + "dynamicQuery", true)
    }

    fun save(project: Project) {
        val p = PropertiesComponent.getInstance(project)
        p.setValue(KEY + "caching", caching, true)
        p.setValue(KEY + "logging", logging, true)
        p.setValue(KEY + "transaction", transaction, true)
        p.setValue(KEY + "secured", secured, true)
        p.setValue(KEY + "dynamicQuery", dynamicQuery, true)
    }

    private companion object {
        const val KEY = "canfx.crud."
    }
}

class GenerateCrudDialog(project: Project, private val entity: String, val options: GenerateCrudOptions) : DialogWrapper(project) {
    init {
        title = "Generate CRUD: $entity"
        setOKButtonText("Generate")
        init()
    }

    override fun createCenterPanel(): JComponent = panel {
        row {
            label(
                "<html>Generates Create/Update/Delete commands, GetById/GetList queries, DTOs, business rules, permissions,<br/>" +
                    "EF configuration and endpoints for $entity. Places to complete are marked <b>TODO(canfxgen)</b>.</html>"
            )
        }
        row("Plural name:") {
            textField()
                .bindText(options::plural)
                .align(AlignX.FILL)
                .comment("If empty, derived with English rules (Category → Categories). Used for the folder, table and route names.")
        }
        group("Mechanisms") {
            row { checkBox("Caching (queries are cached)").bindSelected(options::caching) }
            row { checkBox("Logging (commands are logged)").bindSelected(options::logging) }
            row { checkBox("Transaction (if off, the handler saves changes itself)").bindSelected(options::transaction) }
            row { checkBox("Secured Operation (permission check)").bindSelected(options::secured) }
            row { checkBox("Dynamic Query (GetListByDynamic)").bindSelected(options::dynamicQuery) }
        }
    }
}

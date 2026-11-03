package com.canfx.canfxgen

import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.DialogWrapper
import com.intellij.ui.JBColor
import com.intellij.ui.components.JBScrollPane
import java.awt.Dimension
import java.awt.Font
import javax.swing.JComponent
import javax.swing.JTextPane
import javax.swing.text.SimpleAttributeSet
import javax.swing.text.StyleConstants

/** canfxgen --preview çıktısı: dosya başına satır farkı (+ eklenen, - silinen) ve uyarılar. "Apply" uygular. */
class PreviewDialog(
    project: Project,
    private val operation: String,
    private val solution: String,
    private val changes: List<CanGenChange>,
    private val warnings: List<String>,
) : DialogWrapper(project) {
    init {
        title = "Preview: $operation"
        setOKButtonText("Apply")
        init()
    }

    override fun createCenterPanel(): JComponent {
        val pane = JTextPane().apply {
            isEditable = false
            font = Font(Font.MONOSPACED, Font.PLAIN, 12)
        }
        val doc = pane.styledDocument
        fun style(color: java.awt.Color? = null, bold: Boolean = false) = SimpleAttributeSet().apply {
            if (color != null) StyleConstants.setForeground(this, color)
            StyleConstants.setBold(this, bold)
        }

        val header = style(bold = true)
        val added = style(JBColor(0x1B7F37, 0x6AAB73))
        val removed = style(JBColor(0xC62828, 0xE06C75))
        val normal = style(JBColor.GRAY)
        val warning = style(JBColor(0x9A6700, 0xD7BA7D))

        warnings.forEach { doc.insertString(doc.length, "⚠ $it\n", warning) }
        if (warnings.isNotEmpty()) doc.insertString(doc.length, "\n", normal)

        for (change in changes) {
            val mark = when (change.kind) {
                "created" -> "NEW"
                "deleted" -> "DELETE"
                "failed" -> "FAILED"
                else -> "CHANGE"
            }
            doc.insertString(doc.length, "$mark  ${CanFxFlow.relative(solution, change.path)}${change.note?.let { "  ($it)" } ?: ""}\n", header)
            for (line in (change.diff ?: "").lines()) {
                if (line.isEmpty()) continue
                val attributes = when {
                    line.startsWith("+") -> added
                    line.startsWith("-") -> removed
                    else -> normal
                }
                doc.insertString(doc.length, line + "\n", attributes)
            }
            doc.insertString(doc.length, "\n", normal)
        }

        pane.caretPosition = 0
        return JBScrollPane(pane).apply { preferredSize = Dimension(980, 620) }
    }
}

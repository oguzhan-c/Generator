package com.canfx.canfxgen

import com.intellij.openapi.editor.Editor
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.vfs.VirtualFile
import java.io.File

/** Sağ tıklanan dosya / klasörden canfxgen hedefini (istek, entity, özellik, controller) metinden çıkarır. */
internal object CanFxTargets {
    private const val MAX_SIZE = 512 * 1024
    private val requestClass = Regex("""\bclass\s+(\w+)\s*(?:<[^>{]*>)?\s*:\s*(?:[\w.]+\.)?IRequest\b""")
    private val controllerClass = Regex("""\bclass\s+(\w+Controller)\b""")
    private val publicMethod = Regex("""\bpublic\s+[^;{}=()]*?\b(\w+)\s*\(""")

    fun text(file: VirtualFile): String? {
        if (file.isDirectory || file.extension != "cs" || file.length > MAX_SIZE) return null
        return FileDocumentManager.getInstance().getCachedDocument(file)?.text
            ?: runCatching { String(file.contentsToByteArray(), file.charset) }.getOrNull()
    }

    /** İstek sınıfının adı (IRequest uygulayan). */
    fun requestName(file: VirtualFile): String? = text(file)?.let { requestClass.find(it)?.groupValues?.get(1) }

    /**
     * Controller dosyasında hedef: imleç bir action'ın içindeyse <c>Controller.Action</c>, değilse controller'ın tamamı.
     */
    fun controllerTarget(file: VirtualFile, editor: Editor?): String? {
        val text = text(file) ?: return null
        val controller = controllerClass.find(text)?.groupValues?.get(1) ?: return null
        val caret = editor?.takeIf { FileDocumentManager.getInstance().getFile(it.document) == file }?.caretModel?.offset ?: return controller
        val method = publicMethod.findAll(text).filter { it.range.first < caret && it.groupValues[1] != controller }.lastOrNull()
        return if (method != null && !text.substring(method.range.first, caret).contains("\n    }\n")) "$controller.${method.groupValues[1]}" else controller
    }

    /** <c>.../Application/Features/{Ad}</c> klasörüyse özellik adı. */
    fun featureFolder(file: VirtualFile): String? =
        if (file.isDirectory && file.parent?.name == "Features" && file.parent?.parent?.name?.endsWith(".Application", true) == true) file.name else null

    /** Çözüm kökü: yukarı doğru .slnx / .sln bulunan ilk klasör. */
    fun solutionRoot(start: VirtualFile?): File? {
        var dir: File? = start?.let { File(it.path) }?.let { if (it.isDirectory) it else it.parentFile }
        while (dir != null) {
            if (dir.listFiles()?.any { it.extension == "slnx" || it.extension == "sln" } == true) return dir
            dir = dir.parentFile
        }
        return null
    }

    fun solutionRoot(basePath: String?): File? = basePath?.let { File(it) }?.let { root ->
        var dir: File? = root
        while (dir != null) {
            if (dir.listFiles()?.any { it.extension == "slnx" || it.extension == "sln" } == true) return dir
            dir = dir.parentFile
        }
        null
    }

    /** CanFx şablonuyla oluşturulmuş çözüm mü: canfx.json ya da src altında *.Domain projesi. */
    fun isCanFxSolution(root: File?): Boolean =
        root != null && (File(root, "canfx.json").exists() || File(root, "src").listFiles()?.any { it.isDirectory && it.name.endsWith(".Domain") } == true)

    /** canfx.json'daki seçenekler (yoksa boş). */
    fun templateOptions(root: File): Map<String, Any?> {
        val file = File(root, "canfx.json")
        if (!file.exists()) return emptyMap()
        val json = file.readText().lines().filterNot { it.trimStart().startsWith("//") }.joinToString("\n")
        return runCatching { Json.parse(json).obj()["options"].obj() }.getOrDefault(emptyMap())
    }

    /** Kopyalanabilecek en yakın klasör (dosyaysa üst klasörü). */
    fun directoryOf(file: VirtualFile): String = if (file.isDirectory) file.path else file.parent?.path ?: File(file.path).parent
}

package com.canfx.canfxgen

import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.configurations.PathEnvironmentVariableUtil
import com.intellij.execution.util.ExecUtil
import com.intellij.openapi.util.SystemInfo
import com.intellij.util.execution.ParametersListUtil
import java.io.File
import java.nio.charset.StandardCharsets

/** canfxgen'in tek bir çalıştırma sonucu. */
data class CanGenResult(val ok: Boolean, val json: Map<String, Any?>, val error: String?)

/** Bir dosya değişikliği (canfxgen --json çıktısındaki "changes" öğesi). */
data class CanGenChange(val path: String, val kind: String, val note: String?)

/** canfxgen komut satırı aracını çalıştırır. Uzun sürebilir: arka plan iş parçacığında çağır. */
object CanGenRunner {
    private const val TIMEOUT_MS = 120_000

    fun run(workingDirectory: String, vararg arguments: String): CanGenResult {
        val command = resolveCommand()
            ?: return CanGenResult(
                false,
                emptyMap(),
                "canfxgen was not found. Install it: dotnet tool install -g CanFx.Generator " +
                    "or set the command in Settings → Tools → CanFx Generator."
            )

        val commandLine = GeneralCommandLine(command + arguments.toList() + "--json")
            .withWorkDirectory(workingDirectory)
            .withCharset(StandardCharsets.UTF_8)
            .withParentEnvironmentType(GeneralCommandLine.ParentEnvironmentType.CONSOLE)

        val output = try {
            ExecUtil.execAndGetOutput(commandLine, TIMEOUT_MS)
        } catch (e: Exception) {
            return CanGenResult(false, emptyMap(), "canfxgen could not be started: ${e.message}")
        }

        if (output.isTimeout) return CanGenResult(false, emptyMap(), "canfxgen did not finish within ${TIMEOUT_MS / 1000} seconds.")

        // 'dotnet run' önce derleme satırları yazabilir: JSON, satır başındaki ilk '{' ile başlar.
        val stdout = output.stdout
        val start = stdout.indexOf("{\n").takeIf { it >= 0 } ?: stdout.indexOf('{')
        val json = if (start >= 0) runCatching { Json.parse(stdout.substring(start).trim()).obj() }.getOrNull() else null

        if (json == null) {
            val details = (output.stderr.ifBlank { stdout }).trim().take(2000)
            return CanGenResult(false, emptyMap(), "canfxgen returned unexpected output (exit code ${output.exitCode}):\n$details")
        }

        return CanGenResult(json.bool("ok"), json, json.str("error"))
    }

    fun changes(result: CanGenResult): List<CanGenChange> =
        result.json["changes"].arr().map { it.obj() }.map {
            CanGenChange(it.str("path") ?: "", it.str("kind") ?: "", it.str("note"))
        }

    /** Ayardaki komut; boşsa PATH ya da ~/.dotnet/tools/canfxgen. Dock'tan açılan IDE'nin PATH'i kısa olabilir. */
    private fun resolveCommand(): List<String>? {
        val configured = CanGenSettings.getInstance().state.command.trim()
        if (configured.isNotEmpty()) return ParametersListUtil.parse(configured)

        val executable = if (SystemInfo.isWindows) "canfxgen.exe" else "canfxgen"
        PathEnvironmentVariableUtil.findInPath(executable)?.let { return listOf(it.absolutePath) }

        val home = System.getProperty("user.home")
        val tool = File(home, ".dotnet${File.separator}tools${File.separator}$executable")
        return if (tool.canExecute()) listOf(tool.absolutePath) else null
    }
}

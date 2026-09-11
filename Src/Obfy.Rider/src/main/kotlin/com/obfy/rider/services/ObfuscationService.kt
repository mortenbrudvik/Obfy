package com.obfy.rider.services

import com.intellij.openapi.components.Service
import com.intellij.openapi.diagnostic.Logger
import java.io.BufferedReader
import java.io.File
import java.io.InputStreamReader
import java.util.concurrent.TimeUnit

/**
 * Service that invokes the Obfy CLI to perform obfuscation.
 * Equivalent to VS2022's ObfuscationServiceWrapper.cs
 */
@Service(Service.Level.APP)
class ObfuscationService {

    private val logger = Logger.getInstance(ObfuscationService::class.java)
    private var cachedCliPath: String? = null

    /**
     * Obfuscate an assembly using the Obfy CLI
     */
    fun obfuscate(
        assemblyPath: String,
        outputPath: String?,
        settings: ObfySettings,
        outputService: OutputService
    ): ObfuscationResult {
        val startTime = System.currentTimeMillis()

        val cliPath = findCliPath()
        if (cliPath == null) {
            val message = "Obfy CLI not found. Please ensure Obfy is installed and in PATH."
            outputService.error(message)
            return ObfuscationResult.failure(message)
        }

        val args = buildArguments(assemblyPath, outputPath, settings)
        outputService.info("Executing: obfy $args")

        return try {
            val result = runCli(cliPath, args, outputService)
            val elapsedTime = System.currentTimeMillis() - startTime
            val resolvedOutput = if (result.success) (outputPath ?: assemblyPath) else result.outputPath
            result.copy(elapsedTimeMs = elapsedTime, outputPath = resolvedOutput)
        } catch (e: Exception) {
            logger.error("CLI execution failed", e)
            ObfuscationResult.failure(e.message ?: "Unknown error")
        }
    }

    /**
     * Find the Obfy CLI executable
     */
    private fun findCliPath(): String? {
        // Return cached path if still valid
        cachedCliPath?.let { if (File(it).exists()) return it }

        val userHome = System.getProperty("user.home")
        val programFiles = System.getenv("ProgramFiles") ?: "C:\\Program Files"
        val programFilesX86 = System.getenv("ProgramFiles(x86)")

        // Global dotnet tool install was removed; search it last as a fallback only.
        val possiblePaths = buildList {
            add("$programFiles\\Obfy\\obfy.exe")
            if (!programFilesX86.isNullOrBlank()) {
                add("$programFilesX86\\Obfy\\obfy.exe")
            }
            add(File(System.getProperty("user.dir"), "obfy.exe").absolutePath)
        }

        for (path in possiblePaths) {
            if (File(path).exists()) {
                cachedCliPath = path
                logger.info("Found Obfy CLI at: $path")
                return path
            }
        }

        val pathEnv = System.getenv("PATH")
        if (pathEnv != null) {
            for (dir in pathEnv.split(File.pathSeparator)) {
                if (dir.isBlank()) continue
                val exePath = File(dir, "obfy.exe")
                if (exePath.exists()) {
                    cachedCliPath = exePath.absolutePath
                    logger.info("Found Obfy CLI in PATH: ${exePath.absolutePath}")
                    return cachedCliPath
                }
            }
        }

        val dotnetTool = File(userHome, ".dotnet/tools/obfy.exe")
        if (dotnetTool.exists()) {
            cachedCliPath = dotnetTool.absolutePath
            logger.info("Found Obfy CLI at fallback dotnet tool path: ${dotnetTool.absolutePath}")
            return cachedCliPath
        }

        logger.warn("Obfy CLI not found")
        return null
    }

    /**
     * Build command line arguments for the CLI
     */
    private fun buildArguments(assemblyPath: String, outputPath: String?, settings: ObfySettings): String {
        val sb = StringBuilder()

        // Input file (quoted for spaces)
        sb.append("\"$assemblyPath\"")

        // Output directory
        if (!outputPath.isNullOrEmpty()) {
            val outputDir = File(outputPath).parent
            if (!outputDir.isNullOrEmpty()) {
                sb.append(" -o \"$outputDir\"")
            }
        }

        // Level
        sb.append(" -l ${settings.level.name.lowercase()}")

        // Individual toggles for custom level
        if (settings.level == ObfuscationLevel.Custom) {
            if (!settings.stringEncryption) sb.append(" --no-string-encryption")
            if (!settings.symbolRenaming) sb.append(" --no-symbol-renaming")
            if (settings.controlFlow) sb.append(" --control-flow")
            else sb.append(" --no-control-flow")
            if (settings.antiDebug) sb.append(" --anti-debug")
            if (settings.antiDump) sb.append(" --anti-dump")
            if (settings.referenceProxy) sb.append(" --reference-proxy")
            if (settings.antiTamper) sb.append(" --anti-tamper")
            if (settings.antiDecompiler) sb.append(" --anti-decompiler")
            if (settings.constantEncryption) sb.append(" --encrypt-constants")
            if (settings.resourceEncryption) sb.append(" --encrypt-resources")
        }

        return sb.toString()
    }

    /**
     * Run the CLI process and capture output
     */
    private fun runCli(cliPath: String, args: String, outputService: OutputService): ObfuscationResult {
        val outputBuilder = StringBuilder()
        val errorBuilder = StringBuilder()

        val processBuilder = ProcessBuilder()
            .command("cmd", "/c", "\"$cliPath\" $args")
            .redirectErrorStream(false)

        val process = processBuilder.start()

        // Read stdout in background thread
        val stdoutThread = Thread {
            BufferedReader(InputStreamReader(process.inputStream)).use { reader ->
                reader.lines().forEach { line ->
                    outputBuilder.appendLine(line)
                    outputService.info(line)
                }
            }
        }

        // Read stderr in background thread
        val stderrThread = Thread {
            BufferedReader(InputStreamReader(process.errorStream)).use { reader ->
                reader.lines().forEach { line ->
                    errorBuilder.appendLine(line)
                    outputService.error(line)
                }
            }
        }

        stdoutThread.start()
        stderrThread.start()

        // Wait for process to complete (5 minute timeout)
        val completed = process.waitFor(5, TimeUnit.MINUTES)

        if (!completed) {
            process.destroyForcibly()
            return ObfuscationResult.failure("Process timed out")
        }

        // Wait for output threads to finish
        stdoutThread.join(1000)
        stderrThread.join(1000)

        val success = process.exitValue() == 0

        return if (success) {
            ObfuscationResult(
                success = true,
                statistics = parseStatistics(outputBuilder.toString())
            )
        } else {
            ObfuscationResult(
                success = false,
                errorMessage = errorBuilder.toString().trim().ifEmpty {
                    "Process exited with code ${process.exitValue()}"
                }
            )
        }
    }

    /**
     * Parse obfuscation statistics from CLI output
     */
    private fun parseStatistics(output: String): ObfuscationStatistics {
        var total = 0
        var strings = 0
        var symbols = 0

        for (line in output.lines()) {
            when {
                line.contains("strings encrypted", ignoreCase = true) -> {
                    strings = extractNumber(line)
                    total += strings
                }
                line.contains("symbols renamed", ignoreCase = true) -> {
                    symbols = extractNumber(line)
                    total += symbols
                }
                line.contains("transformations", ignoreCase = true) -> {
                    total = extractNumber(line)
                }
            }
        }

        return ObfuscationStatistics(total, strings, symbols)
    }

    private fun extractNumber(line: String): Int {
        val parts = line.split(":")
        return parts.getOrNull(1)?.trim()?.toIntOrNull() ?: 0
    }
}

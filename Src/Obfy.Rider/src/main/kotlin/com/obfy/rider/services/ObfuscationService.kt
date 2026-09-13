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
        outputService: OutputService,
        configPath: String? = null
    ): ObfuscationResult {
        val startTime = System.currentTimeMillis()

        val cliPath = findCliPath()
        if (cliPath == null) {
            val message = "Obfy CLI not found. Please ensure Obfy is installed and in PATH."
            outputService.error(message)
            return ObfuscationResult.failure(message)
        }

        val inPlace = !outputPath.isNullOrEmpty() && File(outputPath).canonicalPath == File(assemblyPath).canonicalPath
        var tempDir: File? = null
        val cliOutput = if (inPlace) {
            tempDir = File(System.getProperty("java.io.tmpdir"), "obfy_" + System.nanoTime())
            tempDir.mkdirs()
            File(tempDir, File(assemblyPath).name).absolutePath
        } else {
            outputPath
        }

        val args = buildArguments(assemblyPath, cliOutput, configPath)
        outputService.info("Executing: obfy ${args.joinToString(" ")}")

        return try {
            val result = runCli(cliPath, args, outputService)
            val elapsedTime = System.currentTimeMillis() - startTime
            if (result.success && inPlace && cliOutput != null) {
                File(cliOutput).copyTo(File(assemblyPath), overwrite = true)
            }
            tempDir?.deleteRecursively()
            val resolvedOutput = if (result.success) (outputPath ?: assemblyPath) else result.outputPath
            result.copy(elapsedTimeMs = elapsedTime, outputPath = resolvedOutput)
        } catch (e: Exception) {
            tempDir?.deleteRecursively()
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
    private fun buildArguments(assemblyPath: String, outputPath: String?, configPath: String?): List<String> {
        val args = mutableListOf(assemblyPath)
        if (!outputPath.isNullOrEmpty()) {
            val outputDir = File(outputPath).parent
            if (!outputDir.isNullOrEmpty()) {
                args.add("-o")
                args.add(outputDir)
            }
        }
        if (!configPath.isNullOrEmpty() && File(configPath).exists()) {
            args.add("-c")
            args.add(configPath)
        }
        return args
    }

    /**
     * Run the CLI process and capture output
     */
    private fun runCli(cliPath: String, args: List<String>, outputService: OutputService): ObfuscationResult {
        val outputBuilder = StringBuilder()
        val errorBuilder = StringBuilder()

        val processBuilder = ProcessBuilder()
            .command(listOf(cliPath) + args)
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

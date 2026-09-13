package com.obfy.rider.services

import java.io.File
import java.io.IOException
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.Files
import java.nio.file.StandardCopyOption

/**
 * CLI argv and in-place copy helpers, extracted so JVM tests can cover them without a process.
 */
object ObfyCli {

    fun buildArguments(assemblyPath: String, outputPath: String?, configPath: String?): List<String> {
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

    fun copyTempOutputToDestination(tempOutputPath: String, destinationPath: String) {
        val tempOutput = File(tempOutputPath)
        if (!tempOutput.isFile) {
            throw IOException("CLI succeeded but the temp output was not found; original assembly was not modified.")
        }

        val dest = File(destinationPath)
        val destDir = dest.parentFile
            ?: throw IOException("Destination path has no directory: $destinationPath")
        val staging = File(destinationPath + ".obfynew")
        tempOutput.copyTo(staging, overwrite = true)
        try {
            Files.move(
                staging.toPath(),
                dest.toPath(),
                StandardCopyOption.REPLACE_EXISTING,
                StandardCopyOption.ATOMIC_MOVE
            )
        } catch (_: AtomicMoveNotSupportedException) {
            Files.move(staging.toPath(), dest.toPath(), StandardCopyOption.REPLACE_EXISTING)
        }

        val tempDir = tempOutput.parentFile ?: return
        tempDir.listFiles()?.forEach { file ->
            if (file.isFile && !file.name.equals(tempOutput.name, ignoreCase = true) &&
                !file.name.endsWith(".obfynew", ignoreCase = true)
            ) {
                file.copyTo(File(destDir, file.name), overwrite = true)
            }
        }
    }
}

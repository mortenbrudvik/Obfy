package com.obfy.rider.services

import java.io.File
import java.io.IOException
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class ObfyCliTest {
    @Test
    fun buildArguments_withConfigFile_passesDashC() {
        val dir = File(System.getProperty("java.io.tmpdir"), "obfy-cli-args-" + System.nanoTime())
        try {
            dir.mkdirs()
            val config = File(dir, "obfy.json").apply { writeText("{}") }
            val args = ObfyCli.buildArguments(
                File(dir, "App.dll").absolutePath,
                File(dir, "out/App.dll").absolutePath,
                config.absolutePath
            )
            assertTrue(args.contains("-c"))
            assertTrue(args.contains(config.absolutePath))
            assertFalse(args.contains("-l"))
            assertFalse(args.any { it.contains("anti-debug") })
        } finally {
            dir.deleteRecursively()
        }
    }

    @Test
    fun copyTempOutput_copiesSidecarsAndReplacesPrimary() {
        val root = File(System.getProperty("java.io.tmpdir"), "obfy-cli-copy-" + System.nanoTime())
        try {
            val destDir = File(root, "dest").apply { mkdirs() }
            val tempDir = File(root, "temp").apply { mkdirs() }
            val dest = File(destDir, "App.dll").apply { writeText("original") }
            val tempOutput = File(tempDir, "App.dll").apply { writeText("obfuscated") }
            File(tempDir, "App.launcher.exe").writeText("launcher")
            File(tempDir, "App.launcher.runtimeconfig.json").writeText("{}")

            ObfyCli.copyTempOutputToDestination(tempOutput.absolutePath, dest.absolutePath)

            assertEquals("obfuscated", dest.readText())
            assertEquals("launcher", File(destDir, "App.launcher.exe").readText())
            assertEquals("{}", File(destDir, "App.launcher.runtimeconfig.json").readText())
        } finally {
            root.deleteRecursively()
        }
    }

    @Test
    fun copyTempOutput_missingTemp_throwsAndLeavesDestination() {
        val root = File(System.getProperty("java.io.tmpdir"), "obfy-cli-miss-" + System.nanoTime())
        try {
            root.mkdirs()
            val dest = File(root, "App.dll").apply { writeText("original") }
            assertFailsWith<IOException> {
                ObfyCli.copyTempOutputToDestination(File(root, "missing/App.dll").absolutePath, dest.absolutePath)
            }
            assertEquals("original", dest.readText())
        } finally {
            root.deleteRecursively()
        }
    }
}

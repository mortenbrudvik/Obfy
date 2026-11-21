package com.obfy.rider.services

import java.io.File
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * Post-build obfuscation has to visit every output the build produced (each target framework), not just the
 * newest file by timestamp: MSBuild keeps the compiler's timestamp when it copies to bin\, so "newest" says
 * nothing about which build wrote a file.
 */
class PostBuildAssemblySelectionTest {
    private val root = File(System.getProperty("java.io.tmpdir"), "obfy-rider-select-" + System.nanoTime())

    private fun dll(relative: String): File {
        val file = File(root, relative)
        file.parentFile.mkdirs()
        file.writeText(relative)
        return file
    }

    private fun names(files: List<File>) =
        files.map { it.relativeTo(root).path.replace('\\', '/') }.sorted()

    @AfterTest
    fun cleanUp() {
        root.deleteRecursively()
    }

    @Test
    fun releaseOnly_returnsEveryTargetFrameworkOfTheReleaseConfiguration() {
        dll("bin/Release/net8.0/App.dll")
        dll("bin/Release/net10.0/App.dll")
        dll("bin/Debug/net10.0/App.dll")

        val found = AssemblyLocator.findOutputAssemblies(root, "App", releaseOnly = true)

        assertEquals(listOf("bin/Release/net10.0/App.dll", "bin/Release/net8.0/App.dll"), names(found))
    }

    @Test
    fun notReleaseOnly_includesDebugToo() {
        dll("bin/Release/net10.0/App.dll")
        dll("bin/Debug/net10.0/App.dll")

        val found = AssemblyLocator.findOutputAssemblies(root, "App", releaseOnly = false)

        assertEquals(listOf("bin/Debug/net10.0/App.dll", "bin/Release/net10.0/App.dll"), names(found))
    }

    @Test
    fun referenceAssembliesAndPublishFoldersAreNotBuildOutputs() {
        dll("bin/Release/net10.0/App.dll")
        dll("bin/Release/net10.0/ref/App.dll")
        dll("bin/Release/net10.0/refint/App.dll")
        dll("bin/Release/net10.0/win-x64/publish/App.dll")

        val found = AssemblyLocator.findOutputAssemblies(root, "App", releaseOnly = true)

        assertEquals(listOf("bin/Release/net10.0/App.dll"), names(found))
    }

    @Test
    fun anApphostExeBesideItsDll_isNotAManagedOutput() {
        dll("bin/Release/net10.0/App.dll")
        dll("bin/Release/net10.0/App.exe")

        val found = AssemblyLocator.findOutputAssemblies(root, "App", releaseOnly = true)

        assertEquals(listOf("bin/Release/net10.0/App.dll"), names(found))
    }

    @Test
    fun aNetFrameworkExeWithoutADll_isTheOutput() {
        dll("bin/Release/net48/App.exe")

        val found = AssemblyLocator.findOutputAssemblies(root, "App", releaseOnly = true)

        assertEquals(listOf("bin/Release/net48/App.exe"), names(found))
    }

    @Test
    fun missingBinFolder_returnsNothing() {
        root.mkdirs()
        assertTrue(AssemblyLocator.findOutputAssemblies(root, "App", releaseOnly = true).isEmpty())
    }
}

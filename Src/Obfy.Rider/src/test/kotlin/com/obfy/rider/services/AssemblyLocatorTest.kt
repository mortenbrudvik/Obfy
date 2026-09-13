package com.obfy.rider.services

import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class AssemblyLocatorTest {
    @Test
    fun isProjectFileName_acceptsCsharpVbAndFsharp() {
        assertTrue(AssemblyLocator.isProjectFileName("App.csproj"))
        assertTrue(AssemblyLocator.isProjectFileName("Lib.vbproj"))
        assertTrue(AssemblyLocator.isProjectFileName("Tool.fsproj"))
        assertFalse(AssemblyLocator.isProjectFileName("App.sln"))
    }

    @Test
    fun isReleasePath_readsSegmentAfterBin() {
        assertTrue(AssemblyLocator.isReleasePath("C:/src/bin/Release/net10.0/App.dll"))
        assertFalse(AssemblyLocator.isReleasePath("C:/src/bin/Debug/net10.0/App.dll"))
    }

    @Test
    fun findOutputAssembly_prefersNewestNonRefDll() {
        val root = File(System.getProperty("java.io.tmpdir"), "obfy-rider-" + System.nanoTime())
        try {
            val release = File(root, "bin/Release/net10.0")
            val debug = File(root, "bin/Debug/net10.0")
            val ref = File(root, "bin/Release/net10.0/ref")
            release.mkdirs()
            debug.mkdirs()
            ref.mkdirs()
            File(root, "App.csproj").writeText("<Project />")
            val releaseDll = File(release, "App.dll").apply { writeText("rel") }
            File(debug, "App.dll").apply { writeText("dbg") }
            File(ref, "App.dll").apply { writeText("ref") }
            releaseDll.setLastModified(System.currentTimeMillis() + 60_000)

            val found = AssemblyLocator.findOutputAssembly(root, "App")
            assertEquals(releaseDll.canonicalFile, found?.canonicalFile)
            assertEquals("App", AssemblyLocator.findProjectName(root))
        } finally {
            root.deleteRecursively()
        }
    }

    @Test
    fun findOutputAssembly_missingBin_returnsNull() {
        val root = File(System.getProperty("java.io.tmpdir"), "obfy-rider-empty-" + System.nanoTime())
        try {
            root.mkdirs()
            assertNull(AssemblyLocator.findOutputAssembly(root, "App"))
        } finally {
            root.deleteRecursively()
        }
    }
}

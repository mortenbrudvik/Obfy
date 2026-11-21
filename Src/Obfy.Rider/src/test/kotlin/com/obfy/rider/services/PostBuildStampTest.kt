package com.obfy.rider.services

import java.io.File
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class PostBuildStampTest {
    private val projectDir = File(System.getProperty("java.io.tmpdir"), "obfy-rider-stamp-" + System.nanoTime())
    private val binDir = File(projectDir, "bin/Release/net10.0")
    private val assembly = File(binDir, "App.dll")

    private fun createAssembly(text: String = "freshly built") {
        binDir.mkdirs()
        assembly.writeText(text)
    }

    @AfterTest
    fun cleanUp() {
        projectDir.deleteRecursively()
    }

    @Test
    fun freshlyBuiltAssembly_isNotAlreadyObfuscated() {
        createAssembly()
        assertFalse(PostBuildStamp.isAlreadyObfuscated(projectDir, assembly))
    }

    @Test
    fun afterRecording_anUntouchedAssembly_isSkipped() {
        createAssembly("obfuscated output")
        PostBuildStamp.record(projectDir, assembly)

        // A Debug build leaves the Release output exactly as it was.
        assertTrue(PostBuildStamp.isAlreadyObfuscated(projectDir, assembly))
    }

    @Test
    fun afterRecording_aRebuiltAssembly_isObfuscatedAgain() {
        createAssembly("obfuscated output")
        PostBuildStamp.record(projectDir, assembly)

        assembly.writeText("a different, rebuilt assembly")

        assertFalse(PostBuildStamp.isAlreadyObfuscated(projectDir, assembly))
    }

    @Test
    fun afterRecording_aRecopiedAssemblyOfTheSameSize_isObfuscatedAgain() {
        createAssembly("obfuscated output")
        PostBuildStamp.record(projectDir, assembly)

        // MSBuild copies obj\ over bin\ and keeps the source timestamp, so only the timestamp may differ.
        assembly.setLastModified(assembly.lastModified() - 5 * 60_000)

        assertFalse(PostBuildStamp.isAlreadyObfuscated(projectDir, assembly))
    }

    @Test
    fun stamps_areKeptPerAssembly() {
        createAssembly("obfuscated output")
        val other = File(binDir, "Other.dll").apply { writeText("other") }
        PostBuildStamp.record(projectDir, assembly)

        assertFalse(PostBuildStamp.isAlreadyObfuscated(projectDir, other))
    }

    @Test
    fun stamps_areNotWrittenNextToTheAssembly() {
        createAssembly()
        PostBuildStamp.record(projectDir, assembly)

        assertEquals(listOf("App.dll"), binDir.listFiles()!!.map { it.name })
        assertTrue(File(projectDir, "obj").isDirectory)
    }

    @Test
    fun stampFormat_isSharedWithTheVisualStudioExtension() {
        createAssembly("obfuscated output")
        PostBuildStamp.record(projectDir, assembly)

        // obj\obfy-postbuild\<name>-<16 hex>.stamp containing "<length>:<unix milliseconds>"; the Visual Studio
        // extension writes and reads the same, so a project built from both IDEs is never obfuscated twice.
        val stamp = File(projectDir, "obj/obfy-postbuild").listFiles()!!.single()
        assertTrue(Regex("^App-[0-9a-f]{16}\\.stamp$").matches(stamp.name), stamp.name)

        // The 16 hex digits are the first 8 bytes of SHA-256 over the upper-cased full assembly path.
        val hash = java.security.MessageDigest.getInstance("SHA-256")
            .digest(assembly.absoluteFile.path.uppercase().toByteArray(Charsets.UTF_8))
        assertEquals("App-" + hash.take(8).joinToString("") { "%02x".format(it) } + ".stamp", stamp.name)
        assertEquals("${assembly.length()}:${assembly.lastModified()}", stamp.readText())
    }

    @Test
    fun missingAssembly_isNeverSkippedAndCanBeRecordedWithoutThrowing() {
        binDir.mkdirs()
        val missing = File(binDir, "Gone.dll")

        PostBuildStamp.record(projectDir, missing)

        assertFalse(PostBuildStamp.isAlreadyObfuscated(projectDir, missing))
    }
}

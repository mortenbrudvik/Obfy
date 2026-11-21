package com.obfy.rider.services

import java.io.File
import java.io.IOException
import java.security.MessageDigest

/**
 * Remembers which output assembly the post-build step already obfuscated in place, so a build that did not
 * produce a new assembly (a Debug build next to an obfuscated Release output, or a project that was
 * already up to date) does not obfuscate the same file a second time.
 *
 * The stamp is the size and last-write time of the assembly right after it was obfuscated. A real build
 * replaces the file (MSBuild copies `obj\` over `bin\` because the obfuscated file differs from the
 * compiler output), which changes at least one of the two. Stamps live under the project's `obj\` folder,
 * never next to the assembly, so they are not shipped with the build output. The stamp path and content
 * match the Visual Studio extension's, so the two IDEs agree on what has been obfuscated.
 */
object PostBuildStamp {

    private const val STAMP_FOLDER = "obfy-postbuild"

    fun isAlreadyObfuscated(projectDir: File, assembly: File): Boolean {
        val fingerprint = fingerprint(assembly) ?: return false
        return try {
            val stamp = stampFile(projectDir, assembly)
            stamp.isFile && stamp.readText() == fingerprint
        } catch (_: IOException) {
            // An unreadable stamp means "unknown": obfuscate rather than leave the output unprotected.
            false
        }
    }

    fun record(projectDir: File, assembly: File) {
        val fingerprint = fingerprint(assembly) ?: return
        try {
            val stamp = stampFile(projectDir, assembly)
            stamp.parentFile.mkdirs()
            stamp.writeText(fingerprint)
        } catch (_: IOException) {
            // Without a stamp the next build obfuscates again; that must not turn a successful run into a failure.
        }
    }

    private fun fingerprint(assembly: File): String? =
        if (assembly.isFile) "${assembly.length()}:${assembly.lastModified()}" else null

    private fun stampFile(projectDir: File, assembly: File): File {
        val key = assembly.absoluteFile.normalize().path.uppercase()
        val hash = MessageDigest.getInstance("SHA-256").digest(key.toByteArray(Charsets.UTF_8))
        val suffix = hash.take(8).joinToString("") { "%02x".format(it) }
        return File(File(File(projectDir, "obj"), STAMP_FOLDER), "${assembly.nameWithoutExtension}-$suffix.stamp")
    }
}

package com.obfy.rider.services

import com.intellij.openapi.vfs.VirtualFile
import java.io.File

/**
 * Locates .NET project output assemblies under bin/Release or bin/Debug.
 */
object AssemblyLocator {

    fun findProjectDirectory(file: VirtualFile): VirtualFile? {
        if (isProjectFileName(file.name)) {
            return file.parent
        }
        if (file.isDirectory && file.children.any { isProjectFileName(it.name) }) {
            return file
        }
        return null
    }

    fun findProjectName(projectDir: File): String {
        val projectFile = projectDir.listFiles()?.firstOrNull { isProjectFileName(it.name) }
        return projectFile?.nameWithoutExtension ?: projectDir.name
    }

    fun findProjectName(projectDir: VirtualFile): String {
        val projectFile = projectDir.children.firstOrNull { isProjectFileName(it.name) }
        return projectFile?.nameWithoutExtension ?: projectDir.name
    }

    fun findOutputAssembly(projectDir: File, projectName: String, releaseOnly: Boolean = true): File? {
        val binDir = File(projectDir, "bin")
        if (!binDir.isDirectory) return null

        val configs = if (releaseOnly) listOf("Release") else listOf("Release", "Debug")
        val candidates = mutableListOf<File>()

        for (config in configs) {
            val configDir = File(binDir, config)
            if (!configDir.isDirectory) continue

            configDir.walkTopDown()
                .maxDepth(5)
                .filter { it.isFile }
                .filter { it.name.equals("$projectName.dll", ignoreCase = true) || it.name.equals("$projectName.exe", ignoreCase = true) }
                .filter { it.parentFile?.name != "ref" }
                .forEach { candidates.add(it) }
        }

        return candidates.maxByOrNull { it.lastModified() }
    }

    /**
     * Every build output of the project (one per target framework) under bin/Release, and bin/Debug unless
     * [releaseOnly]. Post-build obfuscation needs all of them rather than the newest by timestamp: MSBuild keeps
     * the compiler's timestamp when it copies to bin, so "newest" says nothing about which build wrote a file.
     *
     * Reference assemblies (`ref`, `refint`) and `publish` folders are not build outputs. An `.exe` beside a
     * `.dll` of the same name is the native apphost, not a managed assembly; an `.exe` alone is a .NET
     * Framework executable and is the output.
     */
    fun findOutputAssemblies(projectDir: File, projectName: String, releaseOnly: Boolean = true): List<File> {
        val binDir = File(projectDir, "bin")
        if (!binDir.isDirectory) return emptyList()

        val configs = if (releaseOnly) listOf("Release") else listOf("Release", "Debug")
        val skippedFolders = setOf("ref", "refint", "publish")
        val outputs = mutableListOf<File>()

        for (config in configs) {
            val configDir = File(binDir, config)
            if (!configDir.isDirectory) continue

            configDir.walkTopDown()
                .maxDepth(5)
                .onEnter { it.name.lowercase() !in skippedFolders }
                .filter { it.isFile }
                .filter { it.name.equals("$projectName.dll", ignoreCase = true) || it.name.equals("$projectName.exe", ignoreCase = true) }
                .filterNot { exe ->
                    exe.extension.equals("exe", ignoreCase = true) &&
                        File(exe.parentFile, "$projectName.dll").isFile
                }
                .forEach { outputs.add(it) }
        }

        return outputs
    }

    fun findOutputAssembly(projectDir: VirtualFile, releaseOnly: Boolean = true): String? {
        val name = findProjectName(projectDir)
        return findOutputAssembly(File(projectDir.path), name, releaseOnly)?.absolutePath
    }

    fun isReleasePath(path: String): Boolean {
        val parts = path.split('/', '\\')
        val binIndex = parts.indexOfFirst { it.equals("bin", ignoreCase = true) }
        if (binIndex >= 0 && binIndex + 1 < parts.size) {
            return parts[binIndex + 1].equals("Release", ignoreCase = true)
        }
        return false
    }

    fun isProjectFileName(name: String): Boolean {
        val lower = name.lowercase()
        return lower.endsWith(".csproj") || lower.endsWith(".vbproj") || lower.endsWith(".fsproj")
    }

    fun findProjectDirectories(root: File): List<File> {
        val skip = setOf("bin", "obj", ".git", ".idea", "node_modules", "packages")
        return root.walkTopDown()
            .onEnter { it.name !in skip }
            .maxDepth(8)
            .filter { it.isFile && isProjectFileName(it.name) }
            .map { it.parentFile }
            .distinct()
            .toList()
    }
}

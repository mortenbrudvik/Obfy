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

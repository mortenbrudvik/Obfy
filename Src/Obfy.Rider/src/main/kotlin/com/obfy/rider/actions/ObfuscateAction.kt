package com.obfy.rider.actions

import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.components.service
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.progress.ProgressManager
import com.intellij.openapi.progress.Task
import com.intellij.openapi.vfs.VirtualFile
import com.jetbrains.rider.projectView.workspace.ProjectModelEntity
import com.jetbrains.rider.projectView.workspace.getProjectModelEntities
import com.obfy.rider.services.ObfuscationService
import com.obfy.rider.services.ObfySettings
import com.obfy.rider.services.OutputService
import com.obfy.rider.services.ProjectSettingsService
import com.obfy.rider.settings.ObfySettingsState
import java.io.File

/**
 * Action that obfuscates the selected project's output assembly.
 * Equivalent to VS2022's ObfuscateCommand.cs
 */
class ObfuscateAction : AnAction() {

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val virtualFile = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return

        val projectDir = getProjectDirectory(virtualFile) ?: run {
            showError(project, "Could not determine project directory")
            return
        }

        val projectName = projectDir.name

        val outputService = project.service<OutputService>()
        val settingsService = project.service<ProjectSettingsService>()
        val obfuscationService = service<ObfuscationService>()
        val globalSettings = ObfySettingsState.getInstance()

        // Show and activate output window
        if (globalSettings.showOutputWindow) {
            outputService.activate()
        }

        outputService.clear()
        outputService.info("Starting obfuscation for project: $projectName")

        // Run obfuscation in background
        ProgressManager.getInstance().run(object : Task.Backgroundable(
            project,
            "Obfuscating $projectName...",
            true
        ) {
            override fun run(indicator: ProgressIndicator) {
                indicator.isIndeterminate = false
                indicator.fraction = 0.1

                // Load project settings or use defaults
                val settings = settingsService.loadSettings(projectDir)
                outputService.info("Using settings (Level: ${settings.level})")

                indicator.fraction = 0.2
                indicator.text = "Finding output assembly..."

                // Find output assembly
                val assemblyPath = findOutputAssembly(projectDir)
                if (assemblyPath == null) {
                    outputService.error("Output assembly not found. Build the project first.")
                    showError(project, "Output assembly not found. Please build the project first.")
                    return
                }

                outputService.info("Assembly: $assemblyPath")

                indicator.fraction = 0.3
                indicator.text = "Running obfuscation..."

                // Run obfuscation
                val result = obfuscationService.obfuscate(
                    assemblyPath,
                    null,
                    settings,
                    outputService
                )

                indicator.fraction = 1.0

                if (result.success) {
                    outputService.success("Obfuscation complete!")
                    outputService.info("  Total transformations: ${result.statistics.totalTransformations}")
                    outputService.info("  Strings encrypted: ${result.statistics.stringsEncrypted}")
                    outputService.info("  Symbols renamed: ${result.statistics.symbolsRenamed}")
                    outputService.info("  Elapsed: ${result.elapsedTimeMs}ms")

                    showSuccess(project, "Obfuscation complete: ${result.statistics.totalTransformations} transformations")
                } else {
                    outputService.error("Obfuscation failed: ${result.errorMessage}")
                    showError(project, "Obfuscation failed: ${result.errorMessage}")
                }
            }
        })
    }

    override fun update(e: AnActionEvent) {
        val virtualFile = e.getData(CommonDataKeys.VIRTUAL_FILE)
        e.presentation.isEnabledAndVisible = virtualFile != null && isSupportedProject(virtualFile)
    }

    private fun isSupportedProject(file: VirtualFile): Boolean {
        val name = file.name.lowercase()
        return name.endsWith(".csproj") ||
               name.endsWith(".vbproj") ||
               name.endsWith(".fsproj") ||
               (file.isDirectory && file.children.any {
                   it.name.endsWith(".csproj") ||
                   it.name.endsWith(".vbproj") ||
                   it.name.endsWith(".fsproj")
               })
    }

    private fun getProjectDirectory(file: VirtualFile): VirtualFile? {
        // If it's a project file, return its parent directory
        if (file.name.endsWith(".csproj") ||
            file.name.endsWith(".vbproj") ||
            file.name.endsWith(".fsproj")) {
            return file.parent
        }
        // If it's a directory containing a project file, return it
        if (file.isDirectory) {
            return file
        }
        return null
    }

    private fun findOutputAssembly(projectDir: VirtualFile): String? {
        val projectName = findProjectName(projectDir) ?: projectDir.name
        val basePath = projectDir.path

        // Try common output paths
        val possiblePaths = listOf(
            // .NET 10/9/8 Release
            "$basePath/bin/Release/net10.0/$projectName.dll",
            "$basePath/bin/Release/net9.0/$projectName.dll",
            "$basePath/bin/Release/net8.0/$projectName.dll",
            // .NET 10/9/8 Debug
            "$basePath/bin/Debug/net10.0/$projectName.dll",
            "$basePath/bin/Debug/net9.0/$projectName.dll",
            "$basePath/bin/Debug/net8.0/$projectName.dll",
            // Legacy paths
            "$basePath/bin/Release/$projectName.dll",
            "$basePath/bin/Debug/$projectName.dll",
            // Executable
            "$basePath/bin/Release/net10.0/$projectName.exe",
            "$basePath/bin/Release/net9.0/$projectName.exe",
            "$basePath/bin/Release/net8.0/$projectName.exe",
            "$basePath/bin/Debug/net10.0/$projectName.exe",
            "$basePath/bin/Debug/net9.0/$projectName.exe",
            "$basePath/bin/Debug/net8.0/$projectName.exe"
        )

        for (path in possiblePaths) {
            if (File(path).exists()) {
                return path
            }
        }

        return null
    }

    private fun findProjectName(projectDir: VirtualFile): String? {
        // Find the .csproj file and extract the name
        val projectFile = projectDir.children.firstOrNull {
            it.name.endsWith(".csproj") ||
            it.name.endsWith(".vbproj") ||
            it.name.endsWith(".fsproj")
        }
        return projectFile?.nameWithoutExtension
    }

    private fun showError(project: com.intellij.openapi.project.Project, message: String) {
        NotificationGroupManager.getInstance()
            .getNotificationGroup("Obfy Notifications")
            .createNotification("Obfy", message, NotificationType.ERROR)
            .notify(project)
    }

    private fun showSuccess(project: com.intellij.openapi.project.Project, message: String) {
        NotificationGroupManager.getInstance()
            .getNotificationGroup("Obfy Notifications")
            .createNotification("Obfy", message, NotificationType.INFORMATION)
            .notify(project)
    }
}

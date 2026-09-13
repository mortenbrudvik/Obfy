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
import com.obfy.rider.services.AssemblyLocator
import com.obfy.rider.services.ObfuscationService
import com.obfy.rider.services.OutputService
import com.obfy.rider.services.ProjectSettingsService
import com.obfy.rider.settings.ObfySettingsState

/**
 * Action that obfuscates the selected project's output assembly.
 * Equivalent to VS2022's ObfuscateCommand.cs
 */
class ObfuscateAction : AnAction() {

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val virtualFile = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return

        val projectDir = AssemblyLocator.findProjectDirectory(virtualFile) ?: run {
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

                val assemblyPath = AssemblyLocator.findOutputAssembly(projectDir)
                if (assemblyPath == null) {
                    outputService.error("Output assembly not found. Build the project first.")
                    showError(project, "Output assembly not found. Please build the project first.")
                    return
                }

                outputService.info("Assembly: $assemblyPath")

                indicator.fraction = 0.3
                indicator.text = "Running obfuscation..."

                val configFile = projectDir?.let { java.io.File(it.path, "obfy.json") }
                val result = obfuscationService.obfuscate(
                    assemblyPath,
                    assemblyPath,
                    settings,
                    outputService,
                    configFile?.takeIf { it.exists() }?.absolutePath
                )

                indicator.fraction = 1.0

                if (result.success) {
                    outputService.success("Obfuscation complete!")
                    outputService.info("  Output: ${result.outputPath ?: assemblyPath}")
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
        return AssemblyLocator.isProjectFileName(name) ||
               (file.isDirectory && file.children.any { AssemblyLocator.isProjectFileName(it.name) })
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

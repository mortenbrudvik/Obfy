package com.obfy.rider.actions

import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.components.service
import com.intellij.openapi.vfs.VirtualFile
import com.obfy.rider.services.ProjectSettingsService
import com.obfy.rider.ui.SettingsDialog

/**
 * Action that opens the project settings dialog.
 * Equivalent to VS2022's OpenSettingsCommand.cs
 */
class OpenSettingsAction : AnAction() {

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val virtualFile = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return

        val projectDir = getProjectDirectory(virtualFile) ?: return
        val projectName = projectDir.name

        val settingsService = project.service<ProjectSettingsService>()

        // Load current settings or use defaults
        val settings = settingsService.loadSettings(projectDir)

        // Show settings dialog
        val dialog = SettingsDialog(project, projectName, settings)

        if (dialog.showAndGet()) {
            // Save settings on OK
            val newSettings = dialog.getSettings()
            settingsService.saveSettings(projectDir, newSettings)
        }
    }

    override fun update(e: AnActionEvent) {
        val virtualFile = e.getData(CommonDataKeys.VIRTUAL_FILE)
        e.presentation.isEnabledAndVisible = virtualFile != null && isProjectFile(virtualFile)
    }

    private fun isProjectFile(file: VirtualFile): Boolean {
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
}

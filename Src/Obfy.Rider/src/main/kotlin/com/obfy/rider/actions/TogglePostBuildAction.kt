package com.obfy.rider.actions

import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.actionSystem.Toggleable
import com.intellij.openapi.components.service
import com.intellij.openapi.vfs.VirtualFile
import com.obfy.rider.services.OutputService
import com.obfy.rider.services.ProjectSettingsService

/**
 * Action that toggles post-build obfuscation for a project.
 * Equivalent to VS2022's TogglePostBuildCommand.cs
 */
class TogglePostBuildAction : AnAction(), Toggleable {

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val virtualFile = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return

        val projectDir = getProjectDirectory(virtualFile) ?: return

        val settingsService = project.service<ProjectSettingsService>()
        val outputService = project.service<OutputService>()

        // Toggle the setting
        val currentState = settingsService.isPostBuildEnabled(projectDir)
        val newState = !currentState

        settingsService.setPostBuildEnabled(projectDir, newState)

        // Update the action state
        Toggleable.setSelected(e.presentation, newState)

        val message = if (newState) "enabled" else "disabled"
        val projectName = projectDir.name
        outputService.info("Post-build obfuscation $message for $projectName")
    }

    override fun update(e: AnActionEvent) {
        val project = e.project
        val virtualFile = e.getData(CommonDataKeys.VIRTUAL_FILE)

        if (project == null || virtualFile == null) {
            e.presentation.isEnabledAndVisible = false
            return
        }

        val projectDir = getProjectDirectory(virtualFile)
        if (projectDir == null) {
            e.presentation.isEnabledAndVisible = false
            return
        }

        e.presentation.isEnabledAndVisible = true

        // Check current state and update presentation
        val settingsService = project.service<ProjectSettingsService>()
        val enabled = settingsService.isPostBuildEnabled(projectDir)

        Toggleable.setSelected(e.presentation, enabled)
        e.presentation.text = if (enabled)
            "Disable Post-Build Obfuscation"
        else
            "Enable Post-Build Obfuscation"
    }

    private fun getProjectDirectory(file: VirtualFile): VirtualFile? {
        // If it's a project file, return its parent directory
        if (file.name.endsWith(".csproj") ||
            file.name.endsWith(".vbproj") ||
            file.name.endsWith(".fsproj")) {
            return file.parent
        }
        // If it's a directory containing a project file, return it
        if (file.isDirectory && file.children.any {
                it.name.endsWith(".csproj") ||
                it.name.endsWith(".vbproj") ||
                it.name.endsWith(".fsproj")
            }) {
            return file
        }
        return null
    }
}

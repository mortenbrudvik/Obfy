package com.obfy.rider.toolwindow

import com.intellij.execution.filters.TextConsoleBuilderFactory
import com.intellij.openapi.components.service
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.ToolWindow
import com.intellij.openapi.wm.ToolWindowFactory
import com.intellij.ui.content.ContentFactory
import com.obfy.rider.services.OutputService

/**
 * Factory for creating the Obfy output tool window.
 * Provides a console view for displaying obfuscation logs.
 */
class ObfyToolWindowFactory : ToolWindowFactory, DumbAware {

    override fun createToolWindowContent(project: Project, toolWindow: ToolWindow) {
        // Create console view for output
        val consoleView = TextConsoleBuilderFactory.getInstance()
            .createBuilder(project)
            .console

        // Register with output service
        val outputService = project.service<OutputService>()
        outputService.setConsoleView(consoleView)

        // Create content and add to tool window
        val content = ContentFactory.getInstance()
            .createContent(consoleView.component, "", false)

        toolWindow.contentManager.addContent(content)
    }

    override fun shouldBeAvailable(project: Project): Boolean = true
}

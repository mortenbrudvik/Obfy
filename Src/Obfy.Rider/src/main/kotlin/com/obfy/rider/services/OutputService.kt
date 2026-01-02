package com.obfy.rider.services

import com.intellij.execution.ui.ConsoleView
import com.intellij.execution.ui.ConsoleViewContentType
import com.intellij.openapi.components.Service
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.ToolWindowManager
import java.time.LocalTime
import java.time.format.DateTimeFormatter

/**
 * Service for logging output to the Obfy tool window.
 * Equivalent to VS2022's OutputService.cs
 */
@Service(Service.Level.PROJECT)
class OutputService(private val project: Project) {

    private var consoleView: ConsoleView? = null
    private val timeFormatter = DateTimeFormatter.ofPattern("HH:mm:ss")

    /**
     * Set the console view from the tool window factory
     */
    fun setConsoleView(console: ConsoleView) {
        this.consoleView = console
    }

    /**
     * Log an info message
     */
    fun info(message: String) {
        writeLine(message, "INFO", ConsoleViewContentType.NORMAL_OUTPUT)
    }

    /**
     * Log a warning message
     */
    fun warning(message: String) {
        writeLine(message, "WARN", ConsoleViewContentType.LOG_WARNING_OUTPUT)
    }

    /**
     * Log an error message
     */
    fun error(message: String) {
        writeLine(message, "ERROR", ConsoleViewContentType.ERROR_OUTPUT)
    }

    /**
     * Log a success message
     */
    fun success(message: String) {
        writeLine(message, "OK", ConsoleViewContentType.SYSTEM_OUTPUT)
    }

    /**
     * Clear the console output
     */
    fun clear() {
        consoleView?.clear()
    }

    /**
     * Show and activate the Obfy tool window
     */
    fun activate() {
        val toolWindow = ToolWindowManager.getInstance(project).getToolWindow("Obfy")
        toolWindow?.show()
    }

    private fun writeLine(message: String, level: String, contentType: ConsoleViewContentType) {
        val timestamp = LocalTime.now().format(timeFormatter)
        val formatted = "[$timestamp] [$level] $message\n"
        consoleView?.print(formatted, contentType)
    }
}

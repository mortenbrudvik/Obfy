package com.obfy.rider.services

import com.intellij.openapi.components.Service
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import java.io.File

/**
 * Service for managing per-project obfy.json settings files.
 * Equivalent to VS2022's ProjectSettingsService.cs
 */
@Service(Service.Level.PROJECT)
class ProjectSettingsService(private val project: Project) {

    private val logger = Logger.getInstance(ProjectSettingsService::class.java)
    private val settingsFileName = "obfy.json"

    /**
     * Load settings for a project directory
     */
    fun loadSettings(projectDir: VirtualFile?): ObfySettings {
        val dir = projectDir?.path ?: return ObfySettings.default()
        return loadSettingsForPath(dir)
    }

    /**
     * Load settings from a specific directory path
     */
    fun loadSettingsForPath(projectDirPath: String): ObfySettings {
        val settingsFile = File(projectDirPath, settingsFileName)

        return if (settingsFile.exists()) {
            try {
                ObfySettings.fromJson(settingsFile.readText())
            } catch (e: Exception) {
                logger.warn("Failed to parse obfy.json: ${e.message}")
                throw e
            }
        } else {
            ObfySettings.default()
        }
    }

    /**
     * Save settings to a project directory
     */
    fun saveSettings(projectDir: VirtualFile?, settings: ObfySettings) {
        val dir = projectDir?.path ?: return
        saveSettingsForPath(dir, settings)
    }

    /**
     * Save settings to a specific directory path
     */
    fun saveSettingsForPath(projectDirPath: String, settings: ObfySettings) {
        try {
            val settingsFile = File(projectDirPath, settingsFileName)
            val existing = if (settingsFile.exists()) settingsFile.readText() else null
            settingsFile.writeText(settings.toJson(existing))
            logger.info("Saved settings to ${settingsFile.absolutePath}")
        } catch (e: Exception) {
            logger.error("Failed to save obfy.json: ${e.message}")
        }
    }

    /**
     * Check if post-build obfuscation is enabled for a project
     */
    fun isPostBuildEnabled(projectDir: VirtualFile?): Boolean {
        return loadSettings(projectDir).postBuildEnabled
    }

    /**
     * Enable or disable post-build obfuscation for a project
     */
    fun setPostBuildEnabled(projectDir: VirtualFile?, enabled: Boolean) {
        val dir = projectDir?.path ?: return
        val settingsFile = File(dir, settingsFileName)
        if (settingsFile.exists()) {
            settingsFile.writeText(ObfySettings.patchPostBuildEnabled(settingsFile.readText(), enabled))
            return
        }
        val settings = ObfySettings.default()
        settings.postBuildEnabled = enabled
        saveSettingsForPath(dir, settings)
    }

    /**
     * Check if settings file exists for a project
     */
    fun hasSettings(projectDir: VirtualFile?): Boolean {
        val dir = projectDir?.path ?: return false
        return File(dir, settingsFileName).exists()
    }
}

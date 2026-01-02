package com.obfy.rider.services

import com.google.gson.Gson
import com.google.gson.GsonBuilder
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
    private val gson: Gson = GsonBuilder().setPrettyPrinting().create()
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
                gson.fromJson(settingsFile.readText(), ObfySettings::class.java)
            } catch (e: Exception) {
                logger.warn("Failed to parse obfy.json: ${e.message}")
                ObfySettings.default()
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
            settingsFile.writeText(gson.toJson(settings))
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
        val settings = loadSettings(projectDir)
        settings.postBuildEnabled = enabled
        saveSettings(projectDir, settings)
    }

    /**
     * Check if settings file exists for a project
     */
    fun hasSettings(projectDir: VirtualFile?): Boolean {
        val dir = projectDir?.path ?: return false
        return File(dir, settingsFileName).exists()
    }
}

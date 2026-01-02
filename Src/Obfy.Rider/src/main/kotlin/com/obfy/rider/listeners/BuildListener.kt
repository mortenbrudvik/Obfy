package com.obfy.rider.listeners

import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.project.Project
import com.intellij.openapi.startup.ProjectActivity

/**
 * Startup activity for the Obfy plugin.
 * Post-build obfuscation can be triggered manually or configured
 * using IDE-specific build hooks.
 */
class BuildListenerStartupActivity : ProjectActivity {

    private val logger = Logger.getInstance(BuildListenerStartupActivity::class.java)

    override suspend fun execute(project: Project) {
        logger.info("Obfy plugin initialized for project: ${project.name}")
        // Post-build obfuscation is available via the Settings dialog.
        // Users can manually trigger obfuscation via right-click menu.
        // For automatic post-build, users should configure a post-build event
        // in their .csproj file to invoke the obfy CLI.
    }
}

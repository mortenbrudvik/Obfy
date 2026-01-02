package com.obfy.rider.settings

import com.intellij.openapi.options.Configurable
import com.intellij.openapi.project.Project
import com.intellij.ui.dsl.builder.bindItem
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.panel
import com.obfy.rider.services.ObfuscationLevel
import javax.swing.JComponent

/**
 * Settings page for Obfy in IDE Settings > Tools > Obfy
 */
class ObfySettingsConfigurable(private val project: Project) : Configurable {

    private val settings = ObfySettingsState.getInstance()

    private var defaultLevel = settings.defaultLevel
    private var showOutputWindow = settings.showOutputWindow
    private var releaseOnly = settings.releaseOnly

    override fun getDisplayName(): String = "Obfy"

    override fun createComponent(): JComponent = panel {
        group("Default Settings") {
            row("Default level:") {
                comboBox(ObfuscationLevel.entries)
                    .bindItem({ defaultLevel }, { defaultLevel = it ?: ObfuscationLevel.Standard })
                    .comment("Default obfuscation level for new projects")
            }
        }

        group("Output") {
            row {
                checkBox("Show output window when obfuscating")
                    .bindSelected({ showOutputWindow }, { showOutputWindow = it })
            }
        }

        group("Build Integration") {
            row {
                checkBox("Only obfuscate on Release builds")
                    .bindSelected({ releaseOnly }, { releaseOnly = it })
                    .comment("When enabled, post-build obfuscation only runs for Release configuration")
            }
        }
    }

    override fun isModified(): Boolean {
        return defaultLevel != settings.defaultLevel ||
               showOutputWindow != settings.showOutputWindow ||
               releaseOnly != settings.releaseOnly
    }

    override fun apply() {
        settings.defaultLevel = defaultLevel
        settings.showOutputWindow = showOutputWindow
        settings.releaseOnly = releaseOnly
    }

    override fun reset() {
        defaultLevel = settings.defaultLevel
        showOutputWindow = settings.showOutputWindow
        releaseOnly = settings.releaseOnly
    }
}

package com.obfy.rider.ui

import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.DialogWrapper
import com.intellij.ui.dsl.builder.bindItem
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.panel
import com.intellij.ui.dsl.builder.toNullableProperty
import com.obfy.rider.services.ObfuscationLevel
import com.obfy.rider.services.ObfySettings
import javax.swing.JComponent

/**
 * Settings dialog for per-project obfuscation settings.
 * Equivalent to VS2022's SettingsDialog.xaml/SettingsDialogViewModel.cs
 */
class SettingsDialog(
    project: Project,
    private val projectName: String,
    initialSettings: ObfySettings
) : DialogWrapper(project, true) {

    private val state = SettingsDialogState(initialSettings)

    init {
        title = "Obfy Settings"
        init()
    }

    override fun createCenterPanel(): JComponent = panel {
        row {
            label("Settings for: $projectName")
                .bold()
        }

        group("Obfuscation Level") {
            row("Level:") {
                comboBox(ObfuscationLevel.entries)
                    .bindItem(state::level.toNullableProperty())
                    .onChanged { combo ->
                        combo.item?.let { state.applyLevelPreset(it) }
                    }
                    .comment("Choose a preset or use Custom for fine-grained control")
            }
        }

        group("Protection Options") {
            row {
                checkBox("String Encryption")
                    .bindSelected(state::stringEncryption)
                    .comment("Encrypt string literals in the assembly (AES-256)")
                    .onChanged { state.considerCustomLevel() }
            }
            row {
                checkBox("Symbol Renaming")
                    .bindSelected(state::symbolRenaming)
                    .comment("Rename types, methods, fields, and properties")
                    .onChanged { state.considerCustomLevel() }
            }
            row {
                checkBox("Control Flow Obfuscation")
                    .bindSelected(state::controlFlow)
                    .comment("Transform code flow to make analysis harder")
                    .onChanged { state.considerCustomLevel() }
            }
            row {
                checkBox("Anti-Debug")
                    .bindSelected(state::antiDebug)
                    .comment("Detect and prevent debugging attempts")
                    .onChanged { state.considerCustomLevel() }
            }
            row {
                checkBox("Anti-Dump")
                    .bindSelected(state::antiDump)
                    .comment("Wipe PE headers in memory to hinder dumping")
                    .onChanged { state.considerCustomLevel() }
            }
            row {
                checkBox("Reference Proxy")
                    .bindSelected(state::referenceProxy)
                    .comment("Hide in-module call targets behind proxy methods")
                    .onChanged { state.considerCustomLevel() }
            }
            row {
                checkBox("Anti-Tamper")
                    .bindSelected(state::antiTamper)
                    .comment("Verify assembly integrity at runtime")
                    .onChanged { state.considerCustomLevel() }
            }
            row {
                checkBox("Anti-Decompiler")
                    .bindSelected(state::antiDecompiler)
                    .comment("Add junk code to confuse decompilers")
                    .onChanged { state.considerCustomLevel() }
            }
            row {
                checkBox("Constant Encryption")
                    .bindSelected(state::constantEncryption)
                    .comment("Encrypt numeric constants in the assembly")
                    .onChanged { state.considerCustomLevel() }
            }
            row {
                checkBox("Resource Encryption")
                    .bindSelected(state::resourceEncryption)
                    .comment("Encrypt embedded resources")
                    .onChanged { state.considerCustomLevel() }
            }
        }

        group("Build Integration") {
            row {
                checkBox("Enable Post-Build Obfuscation")
                    .bindSelected(state::postBuildEnabled)
                    .comment("Automatically obfuscate after each successful build")
            }
        }
    }

    /**
     * Get the configured settings
     */
    fun getSettings(): ObfySettings = state.toSettings()
}

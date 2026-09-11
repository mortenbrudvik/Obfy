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

    // Mutable state for the dialog
    private var level = initialSettings.level
    private var postBuildEnabled = initialSettings.postBuildEnabled
    private var stringEncryption = initialSettings.stringEncryption
    private var symbolRenaming = initialSettings.symbolRenaming
    private var controlFlow = initialSettings.controlFlow
    private var antiDebug = initialSettings.antiDebug
    private var antiDump = initialSettings.antiDump
    private var referenceProxy = initialSettings.referenceProxy
    private var antiTamper = initialSettings.antiTamper
    private var antiDecompiler = initialSettings.antiDecompiler
    private var constantEncryption = initialSettings.constantEncryption
    private var resourceEncryption = initialSettings.resourceEncryption

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
                    .bindItem(::level.toNullableProperty())
                    .onChanged { combo ->
                        combo.item?.let { applyLevelPreset(it) }
                    }
                    .comment("Choose a preset or use Custom for fine-grained control")
            }
        }

        group("Protection Options") {
            row {
                checkBox("String Encryption")
                    .bindSelected(::stringEncryption)
                    .comment("Encrypt string literals in the assembly (AES-256)")
                    .onChanged { updateLevelIfChanged() }
            }
            row {
                checkBox("Symbol Renaming")
                    .bindSelected(::symbolRenaming)
                    .comment("Rename types, methods, fields, and properties")
                    .onChanged { updateLevelIfChanged() }
            }
            row {
                checkBox("Control Flow Obfuscation")
                    .bindSelected(::controlFlow)
                    .comment("Transform code flow to make analysis harder")
                    .onChanged { updateLevelIfChanged() }
            }
            row {
                checkBox("Anti-Debug")
                    .bindSelected(::antiDebug)
                    .comment("Detect and prevent debugging attempts")
                    .onChanged { updateLevelIfChanged() }
            }
            row {
                checkBox("Anti-Dump")
                    .bindSelected(::antiDump)
                    .comment("Wipe PE headers in memory to hinder dumping")
                    .onChanged { updateLevelIfChanged() }
            }
            row {
                checkBox("Reference Proxy")
                    .bindSelected(::referenceProxy)
                    .comment("Hide in-module call targets behind proxy methods")
                    .onChanged { updateLevelIfChanged() }
            }
            row {
                checkBox("Anti-Tamper")
                    .bindSelected(::antiTamper)
                    .comment("Verify assembly integrity at runtime")
                    .onChanged { updateLevelIfChanged() }
            }
            row {
                checkBox("Anti-Decompiler")
                    .bindSelected(::antiDecompiler)
                    .comment("Add junk code to confuse decompilers")
                    .onChanged { updateLevelIfChanged() }
            }
            row {
                checkBox("Constant Encryption")
                    .bindSelected(::constantEncryption)
                    .comment("Encrypt numeric constants in the assembly")
                    .onChanged { updateLevelIfChanged() }
            }
            row {
                checkBox("Resource Encryption")
                    .bindSelected(::resourceEncryption)
                    .comment("Encrypt embedded resources")
                    .onChanged { updateLevelIfChanged() }
            }
        }

        group("Build Integration") {
            row {
                checkBox("Enable Post-Build Obfuscation")
                    .bindSelected(::postBuildEnabled)
                    .comment("Automatically obfuscate after each successful build")
            }
        }
    }

    /**
     * Apply level preset to individual toggles
     */
    private fun applyLevelPreset(selectedLevel: ObfuscationLevel) {
        val preset = ObfySettings.forLevel(selectedLevel)
        stringEncryption = preset.stringEncryption
        symbolRenaming = preset.symbolRenaming
        controlFlow = preset.controlFlow
        antiDebug = preset.antiDebug
        antiDump = preset.antiDump
        referenceProxy = preset.referenceProxy
        antiTamper = preset.antiTamper
        antiDecompiler = preset.antiDecompiler
        constantEncryption = preset.constantEncryption
        resourceEncryption = preset.resourceEncryption
    }

    /**
     * Switch to Custom level if individual toggles are changed
     */
    private fun updateLevelIfChanged() {
        if (level != ObfuscationLevel.Custom) {
            val preset = ObfySettings.forLevel(level)
            if (stringEncryption != preset.stringEncryption ||
                symbolRenaming != preset.symbolRenaming ||
                controlFlow != preset.controlFlow ||
                antiDebug != preset.antiDebug ||
                antiDump != preset.antiDump ||
                referenceProxy != preset.referenceProxy ||
                antiTamper != preset.antiTamper ||
                antiDecompiler != preset.antiDecompiler ||
                constantEncryption != preset.constantEncryption ||
                resourceEncryption != preset.resourceEncryption) {
                level = ObfuscationLevel.Custom
            }
        }
    }

    /**
     * Get the configured settings
     */
    fun getSettings(): ObfySettings = ObfySettings(
        level = level,
        postBuildEnabled = postBuildEnabled,
        stringEncryption = stringEncryption,
        symbolRenaming = symbolRenaming,
        controlFlow = controlFlow,
        antiDebug = antiDebug,
        antiDump = antiDump,
        referenceProxy = referenceProxy,
        antiTamper = antiTamper,
        antiDecompiler = antiDecompiler,
        constantEncryption = constantEncryption,
        resourceEncryption = resourceEncryption
    )
}

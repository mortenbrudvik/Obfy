package com.obfy.rider.ui

import com.obfy.rider.services.ObfuscationLevel
import com.obfy.rider.services.ObfySettings

/**
 * Dialog-owned settings fields, including protections the UI does not show.
 * [toSettings] must round-trip [methodEncryption] and [proxyExternalCalls] so Aggressive
 * JSON keeps `protection.methodEncryption: true` (config files skip Core ApplyLevel).
 */
internal class SettingsDialogState(initial: ObfySettings) {
    var level = initial.level
    var postBuildEnabled = initial.postBuildEnabled
    var stringEncryption = initial.stringEncryption
    var symbolRenaming = initial.symbolRenaming
    var controlFlow = initial.controlFlow
    var antiDebug = initial.antiDebug
    var antiDump = initial.antiDump
    var referenceProxy = initial.referenceProxy
    var antiTamper = initial.antiTamper
    var antiDecompiler = initial.antiDecompiler
    var constantEncryption = initial.constantEncryption
    var resourceEncryption = initial.resourceEncryption
    var methodEncryption = initial.methodEncryption
    var proxyExternalCalls = initial.proxyExternalCalls

    fun applyLevelPreset(selectedLevel: ObfuscationLevel) {
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
        methodEncryption = preset.methodEncryption
        proxyExternalCalls = preset.proxyExternalCalls
    }

    fun considerCustomLevel() {
        if (level == ObfuscationLevel.Custom) return
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
            resourceEncryption != preset.resourceEncryption ||
            methodEncryption != preset.methodEncryption ||
            proxyExternalCalls != preset.proxyExternalCalls
        ) {
            level = ObfuscationLevel.Custom
        }
    }

    fun toSettings(): ObfySettings = ObfySettings(
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
        resourceEncryption = resourceEncryption,
        methodEncryption = methodEncryption,
        proxyExternalCalls = proxyExternalCalls
    )
}

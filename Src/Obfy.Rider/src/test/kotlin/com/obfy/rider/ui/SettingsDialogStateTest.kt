package com.obfy.rider.ui

import com.obfy.rider.services.ObfuscationLevel
import com.obfy.rider.services.ObfySettings
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class SettingsDialogStateTest {
    @Test
    fun aggressiveInitial_toSettings_keepsMethodEncryption() {
        val saved = SettingsDialogState(ObfySettings.forLevel(ObfuscationLevel.Aggressive)).toSettings()
        assertTrue(saved.methodEncryption)
        assertFalse(saved.proxyExternalCalls)
        assertEquals(ObfuscationLevel.Aggressive, saved.level)
    }

    @Test
    fun applyLevelPresetAggressive_toSettings_writesMethodEncryption() {
        val state = SettingsDialogState(ObfySettings.forLevel(ObfuscationLevel.Standard))
        assertFalse(state.methodEncryption)
        state.level = ObfuscationLevel.Aggressive
        state.applyLevelPreset(ObfuscationLevel.Aggressive)
        val saved = state.toSettings()
        assertTrue(saved.methodEncryption)
        assertFalse(saved.proxyExternalCalls)
        val json = saved.toJson()
        assertTrue(json.contains("\"methodEncryption\": true"))
        assertTrue(json.contains("\"proxyExternalCalls\": false"))
    }

    @Test
    fun existingTrue_okWithoutChangingLevel_doesNotClobber() {
        val initial = ObfySettings.forLevel(ObfuscationLevel.Custom).apply {
            methodEncryption = true
        }
        val saved = SettingsDialogState(initial).toSettings()
        assertTrue(saved.methodEncryption)
    }
}

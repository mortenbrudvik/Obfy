package com.obfy.rider.services

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class ObfySettingsTest {
    @Test
    fun forLevelMinimal_disablesStringEncryption() {
        val settings = ObfySettings.forLevel(ObfuscationLevel.Minimal)
        assertFalse(settings.stringEncryption)
        assertTrue(settings.symbolRenaming)
        assertEquals(ObfuscationLevel.Minimal, settings.level)
    }

    @Test
    fun toJson_roundTripsNestedProtection() {
        val original = ObfySettings.forLevel(ObfuscationLevel.Aggressive)
        original.postBuildEnabled = true
        val loaded = ObfySettings.fromJson(original.toJson())
        assertEquals(ObfuscationLevel.Aggressive, loaded.level)
        assertTrue(loaded.postBuildEnabled)
        assertTrue(loaded.antiDebug)
        assertTrue(loaded.controlFlow)
        assertTrue(loaded.antiTamper)
    }

    @Test
    fun fromJson_legacyFlatRoot_readsBooleans() {
        val json = """{"level":"standard","antiDebug":true,"stringEncryption":false}"""
        val settings = ObfySettings.fromJson(json)
        assertEquals(ObfuscationLevel.Standard, settings.level)
        assertTrue(settings.antiDebug)
        assertFalse(settings.stringEncryption)
    }
}

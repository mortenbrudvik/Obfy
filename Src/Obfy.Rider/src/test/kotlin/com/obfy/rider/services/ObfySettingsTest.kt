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
        assertTrue(loaded.methodEncryption)
        assertFalse(loaded.proxyExternalCalls)
    }

    @Test
    fun toJson_aggressive_writesMethodEncryption() {
        val json = ObfySettings.forLevel(ObfuscationLevel.Aggressive).toJson()
        assertTrue(json.contains("\"methodEncryption\": true"))
        assertTrue(json.contains("\"proxyExternalCalls\": false"))
    }

    @Test
    fun toJson_existingDocument_keepsUnknownCoreKeys() {
        val existing = """
            {
              "virtualization": { "enabled": true },
              "packing": { "enabled": true },
              "symbolRenaming": { "enabled": true, "preservePublicApi": true }
            }
        """.trimIndent()
        val json = ObfySettings.forLevel(ObfuscationLevel.Standard).apply {
            postBuildEnabled = true
        }.toJson(existing)
        assertTrue(json.contains("virtualization"))
        assertTrue(json.contains("packing"))
        assertTrue(json.contains("preservePublicApi"))
        assertTrue(json.contains("\"postBuildEnabled\": true"))
    }

    @Test
    fun patchPostBuildEnabled_doesNotDropUnknownKeys() {
        val existing = """{"virtualization":{"enabled":true},"postBuildEnabled":false}"""
        val patched = ObfySettings.patchPostBuildEnabled(existing, true)
        assertTrue(patched.contains("virtualization"))
        assertTrue(patched.contains("\"postBuildEnabled\": true"))
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

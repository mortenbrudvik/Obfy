package com.obfy.rider.services

import com.google.gson.JsonParser
import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * The CLI reads a `-c` file as Custom and uses it as written, while `-l` applies the Core preset
 * (ObfySettings.ApplyLevel). A file written for Minimal/Standard/Aggressive therefore has to carry every
 * value that preset sets, or the same level behaves differently with and without an obfy.json.
 * The expected values below are the ones in Obfy.Core/Models/ObfySettings.cs.
 */
class ObfySettingsPresetFileTest {
    private fun written(level: ObfuscationLevel, existing: String? = null) =
        JsonParser.parseString(ObfySettings.forLevel(level).toJson(existing)).asJsonObject

    private fun intensity(level: ObfuscationLevel) =
        written(level).getAsJsonObject("controlFlow").get("intensity").asInt

    private fun removeAttributes(level: ObfuscationLevel) =
        written(level).getAsJsonObject("metadata").get("removeAttributes").asBoolean

    @Test
    fun controlFlowIntensity_matchesTheCorePreset() {
        assertEquals(50, intensity(ObfuscationLevel.Minimal))
        assertEquals(50, intensity(ObfuscationLevel.Standard))
        assertEquals(80, intensity(ObfuscationLevel.Aggressive))
    }

    @Test
    fun removeAttributes_matchesTheCorePreset() {
        assertEquals(false, removeAttributes(ObfuscationLevel.Minimal))
        assertEquals(true, removeAttributes(ObfuscationLevel.Standard))
        assertEquals(true, removeAttributes(ObfuscationLevel.Aggressive))
    }

    @Test
    fun theRestOfThePresetOwnedValues_areWritten() {
        for (level in listOf(ObfuscationLevel.Minimal, ObfuscationLevel.Standard, ObfuscationLevel.Aggressive)) {
            val json = written(level)
            assertEquals("switch", json.getAsJsonObject("controlFlow").get("mode").asString)
            assertEquals("xor", json.getAsJsonObject("constantEncryption").get("algorithm").asString)
            assertEquals(true, json.getAsJsonObject("metadata").get("removeDebugInfo").asBoolean)
        }
    }

    @Test
    fun custom_doesNotOverwriteTheUsersTunedValues() {
        val existing = """
            {
              "level": "custom",
              "controlFlow": { "enabled": true, "intensity": 35 },
              "metadata": { "removeAttributes": false, "removeDebugInfo": true }
            }
        """.trimIndent()
        val json = written(ObfuscationLevel.Custom, existing)

        assertEquals(35, json.getAsJsonObject("controlFlow").get("intensity").asInt)
        assertEquals(false, json.getAsJsonObject("metadata").get("removeAttributes").asBoolean)
    }
}

package com.obfy.rider.services

import com.google.gson.JsonElement
import com.google.gson.JsonObject
import com.google.gson.JsonParser

/**
 * Obfuscation level presets
 */
enum class ObfuscationLevel {
    Minimal,
    Standard,
    Aggressive,
    Custom
}

/**
 * Obfuscation settings model (mirrors VS2022 extension)
 */
data class ObfySettings(
    var level: ObfuscationLevel = ObfuscationLevel.Standard,
    var postBuildEnabled: Boolean = false,
    var stringEncryption: Boolean = true,
    var symbolRenaming: Boolean = true,
    var controlFlow: Boolean = false,
    var antiDebug: Boolean = false,
    var antiDump: Boolean = false,
    var referenceProxy: Boolean = false,
    var antiTamper: Boolean = false,
    var antiDecompiler: Boolean = false,
    var constantEncryption: Boolean = false,
    var resourceEncryption: Boolean = false
) {
    /**
     * Serialize to Core nested obfy.json so the CLI can deserialize Obfy.Core.Models.ObfySettings.
     * When [existingJson] is present, unknown Core keys are kept.
     */
    fun toJson(existingJson: String? = null): String {
        val root = parseObject(existingJson) ?: JsonObject()
        root.addProperty("level", level.name.lowercase())
        root.addProperty("postBuildEnabled", postBuildEnabled)
        setEnabled(root, "stringEncryption", stringEncryption)
        setEnabled(root, "controlFlow", controlFlow)
        setEnabled(root, "symbolRenaming", symbolRenaming)
        setEnabled(root, "constantEncryption", constantEncryption)
        setEnabled(root, "resourceEncryption", resourceEncryption)

        val protection = root.get("protection")?.takeIf { it.isJsonObject }?.asJsonObject ?: JsonObject()
        protection.addProperty("antiDebug", antiDebug)
        protection.addProperty("antiDump", antiDump)
        protection.addProperty("referenceProxy", referenceProxy)
        setEnabled(protection, "antiTamper", antiTamper)
        setEnabled(protection, "antiDecompiler", antiDecompiler)
        root.add("protection", protection)

        return com.google.gson.GsonBuilder().setPrettyPrinting().create().toJson(root)
    }

    companion object {
        /**
         * Create default settings
         */
        fun default(): ObfySettings = forLevel(ObfuscationLevel.Standard)

        /**
         * Create settings for a specific level preset
         */
        fun forLevel(level: ObfuscationLevel): ObfySettings = when (level) {
            ObfuscationLevel.Minimal -> ObfySettings(
                level = level,
                stringEncryption = false,
                symbolRenaming = true,
                controlFlow = false,
                antiDebug = false,
                antiDump = false,
                referenceProxy = false,
                antiTamper = false,
                antiDecompiler = false,
                constantEncryption = false,
                resourceEncryption = false
            )
            ObfuscationLevel.Standard -> ObfySettings(
                level = level,
                stringEncryption = true,
                symbolRenaming = true,
                controlFlow = false,
                antiDebug = false,
                antiDump = false,
                referenceProxy = false,
                antiTamper = false,
                antiDecompiler = false,
                constantEncryption = false,
                resourceEncryption = false
            )
            ObfuscationLevel.Aggressive -> ObfySettings(
                level = level,
                stringEncryption = true,
                symbolRenaming = true,
                controlFlow = true,
                antiDebug = true,
                antiDump = true,
                referenceProxy = true,
                antiTamper = true,
                antiDecompiler = true,
                constantEncryption = true,
                resourceEncryption = true
            )
            ObfuscationLevel.Custom -> ObfySettings(level = level)
        }

        /**
         * Load Core nested JSON or legacy flat JSON.
         */
        fun fromJson(json: String): ObfySettings {
            val root = JsonParser.parseString(json).asJsonObject
            val settings = ObfySettings()

            parseLevel(root)?.let { settings.level = it }
            settings.postBuildEnabled = root.bool("postBuildEnabled", settings.postBuildEnabled)
            settings.stringEncryption = root.enabled("stringEncryption", settings.stringEncryption)
            settings.symbolRenaming = root.enabled("symbolRenaming", settings.symbolRenaming)
            settings.controlFlow = root.enabled("controlFlow", settings.controlFlow)
            settings.constantEncryption = root.enabled("constantEncryption", settings.constantEncryption)
            settings.resourceEncryption = root.enabled("resourceEncryption", settings.resourceEncryption)

            val protection = root.get("protection")
            if (protection != null && protection.isJsonObject) {
                val p = protection.asJsonObject
                settings.antiDebug = p.bool("antiDebug", settings.antiDebug)
                settings.antiDump = p.bool("antiDump", settings.antiDump)
                settings.referenceProxy = p.bool("referenceProxy", settings.referenceProxy)
                settings.antiTamper = p.enabled("antiTamper", settings.antiTamper)
                settings.antiDecompiler = p.enabled("antiDecompiler", settings.antiDecompiler)
            } else {
                settings.antiDebug = root.bool("antiDebug", settings.antiDebug)
                settings.antiDump = root.bool("antiDump", settings.antiDump)
                settings.referenceProxy = root.bool("referenceProxy", settings.referenceProxy)
                settings.antiTamper = root.enabled("antiTamper", settings.antiTamper)
                settings.antiDecompiler = root.enabled("antiDecompiler", settings.antiDecompiler)
            }

            return settings
        }

        private fun parseLevel(root: JsonObject): ObfuscationLevel? {
            if (!root.has("level") || root.get("level").isJsonNull) return null
            val el = root.get("level")
            if (el.isJsonPrimitive) {
                val primitive = el.asJsonPrimitive
                if (primitive.isString) {
                    return ObfuscationLevel.entries.firstOrNull { it.name.equals(primitive.asString, ignoreCase = true) }
                }
                if (primitive.isNumber) {
                    val n = primitive.asInt
                    return ObfuscationLevel.entries.getOrNull(n)
                }
            }
            return null
        }

        fun patchPostBuildEnabled(existingJson: String, enabled: Boolean): String {
            val root = parseObject(existingJson) ?: JsonObject()
            root.addProperty("postBuildEnabled", enabled)
            return com.google.gson.GsonBuilder().setPrettyPrinting().create().toJson(root)
        }

        private fun parseObject(json: String?): JsonObject? {
            if (json.isNullOrBlank()) return null
            return try {
                JsonParser.parseString(json).takeIf { it.isJsonObject }?.asJsonObject
            } catch (_: Exception) {
                JsonObject()
            }
        }

        private fun setEnabled(parent: JsonObject, name: String, enabled: Boolean) {
            val existing = parent.get(name)
            if (existing != null && existing.isJsonObject) {
                existing.asJsonObject.addProperty("enabled", enabled)
            } else {
                parent.add(name, enabledObject(enabled))
            }
        }

        private fun enabledObject(enabled: Boolean): JsonObject =
            JsonObject().apply { addProperty("enabled", enabled) }

        private fun JsonObject.bool(name: String, default: Boolean): Boolean {
            val el = getIgnoreCase(name) ?: return default
            return el.asBoolOrNull() ?: default
        }

        private fun JsonObject.enabled(name: String, default: Boolean): Boolean {
            val el = getIgnoreCase(name) ?: return default
            el.asBoolOrNull()?.let { return it }
            if (el.isJsonObject) {
                return el.asJsonObject.bool("enabled", default)
            }
            return default
        }

        private fun JsonObject.getIgnoreCase(name: String): JsonElement? {
            if (has(name)) return get(name)
            entrySet().forEach { (key, value) ->
                if (key.equals(name, ignoreCase = true)) return value
            }
            return null
        }

        private fun JsonElement.asBoolOrNull(): Boolean? {
            if (!isJsonPrimitive) return null
            val primitive = asJsonPrimitive
            return if (primitive.isBoolean) primitive.asBoolean else null
        }
    }
}

/**
 * Result of an obfuscation operation
 */
data class ObfuscationResult(
    val success: Boolean,
    val errorMessage: String? = null,
    val outputPath: String? = null,
    val statistics: ObfuscationStatistics = ObfuscationStatistics(),
    val elapsedTimeMs: Long = 0
) {
    companion object {
        fun failure(message: String): ObfuscationResult = ObfuscationResult(
            success = false,
            errorMessage = message
        )
    }
}

/**
 * Statistics from obfuscation
 */
data class ObfuscationStatistics(
    val totalTransformations: Int = 0,
    val stringsEncrypted: Int = 0,
    val symbolsRenamed: Int = 0
)

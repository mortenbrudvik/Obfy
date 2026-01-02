package com.obfy.rider.services

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
    var antiTamper: Boolean = false,
    var antiDecompiler: Boolean = false
) {
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
                antiTamper = false,
                antiDecompiler = false
            )
            ObfuscationLevel.Standard -> ObfySettings(
                level = level,
                stringEncryption = true,
                symbolRenaming = true,
                controlFlow = false,
                antiDebug = false,
                antiTamper = false,
                antiDecompiler = false
            )
            ObfuscationLevel.Aggressive -> ObfySettings(
                level = level,
                stringEncryption = true,
                symbolRenaming = true,
                controlFlow = true,
                antiDebug = true,
                antiTamper = true,
                antiDecompiler = true
            )
            ObfuscationLevel.Custom -> ObfySettings(level = level)
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

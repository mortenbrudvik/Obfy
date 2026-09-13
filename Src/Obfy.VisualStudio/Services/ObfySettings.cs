using System;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Result of an obfuscation operation.
/// </summary>
public class ObfuscationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputPath { get; set; }
    public ObfuscationStatistics Statistics { get; set; } = new();
    public TimeSpan ElapsedTime { get; set; }

    public static ObfuscationResult Failure(string inputPath, string message, Exception? ex = null)
    {
        return new ObfuscationResult
        {
            Success = false,
            ErrorMessage = message,
            OutputPath = inputPath
        };
    }
}

/// <summary>
/// Statistics from obfuscation.
/// </summary>
public class ObfuscationStatistics
{
    public int TotalTransformations { get; set; }
    public int StringsEncrypted { get; set; }
    public int SymbolsRenamed { get; set; }
}

/// <summary>
/// Settings for obfuscation level.
/// </summary>
public enum ObfuscationLevel
{
    Minimal,
    Standard,
    Aggressive,
    Custom
}

/// <summary>
/// Obfuscation settings model (subset for VS extension).
/// </summary>
public class ObfySettings
{
    public ObfuscationLevel Level { get; set; } = ObfuscationLevel.Standard;
    public bool PostBuildEnabled { get; set; }
    public bool AntiDebug { get; set; }
    public bool AntiDump { get; set; }
    public bool ReferenceProxy { get; set; }
    public bool AntiTamper { get; set; }
    public bool AntiDecompiler { get; set; }
    public bool StringEncryption { get; set; } = true;
    public bool ControlFlow { get; set; }
    public bool SymbolRenaming { get; set; } = true;
    public bool ConstantEncryption { get; set; }
    public bool ResourceEncryption { get; set; }

    public static ObfySettings ForLevel(ObfuscationLevel level)
    {
        var settings = new ObfySettings { Level = level };

        switch (level)
        {
            case ObfuscationLevel.Minimal:
                settings.StringEncryption = false;
                settings.SymbolRenaming = true;
                settings.ControlFlow = false;
                settings.AntiDebug = false;
                settings.AntiDump = false;
                settings.ReferenceProxy = false;
                settings.AntiTamper = false;
                settings.AntiDecompiler = false;
                settings.ConstantEncryption = false;
                settings.ResourceEncryption = false;
                break;
            case ObfuscationLevel.Standard:
                settings.StringEncryption = true;
                settings.SymbolRenaming = true;
                settings.ControlFlow = false;
                settings.AntiDebug = false;
                settings.AntiDump = false;
                settings.ReferenceProxy = false;
                settings.AntiTamper = false;
                settings.AntiDecompiler = false;
                settings.ConstantEncryption = false;
                settings.ResourceEncryption = false;
                break;
            case ObfuscationLevel.Aggressive:
                settings.StringEncryption = true;
                settings.SymbolRenaming = true;
                settings.ControlFlow = true;
                settings.AntiDebug = true;
                settings.AntiDump = true;
                settings.ReferenceProxy = true;
                settings.AntiTamper = true;
                settings.AntiDecompiler = true;
                settings.ConstantEncryption = true;
                settings.ResourceEncryption = true;
                break;
        }

        return settings;
    }
}

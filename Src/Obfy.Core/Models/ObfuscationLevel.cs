using System.ComponentModel;

namespace Obfy.Core.Models;

/// <summary>
/// Preset obfuscation levels that control which techniques are enabled.
/// </summary>
public enum ObfuscationLevel
{
    /// <summary>
    /// Minimal obfuscation: symbol renaming and debug-information removal.
    /// </summary>
    [Description("Minimal — rename symbols, strip debug information")]
    Minimal,

    /// <summary>
    /// Standard obfuscation: symbol renaming, string encryption, and metadata removal.
    /// </summary>
    [Description("Standard — rename, encrypt strings, strip metadata")]
    Standard,

    /// <summary>
    /// Aggressive obfuscation: all techniques enabled at maximum intensity.
    /// </summary>
    [Description("Aggressive — all protections at high intensity")]
    Aggressive,

    /// <summary>
    /// Custom obfuscation: use individual settings for each technique.
    /// </summary>
    [Description("Custom")]
    Custom
}

namespace Obfy.Core.Models;

/// <summary>
/// Preset obfuscation levels that control which techniques are enabled.
/// </summary>
public enum ObfuscationLevel
{
    /// <summary>
    /// Minimal obfuscation: symbol renaming only.
    /// </summary>
    Minimal,

    /// <summary>
    /// Standard obfuscation: symbol renaming, string encryption, and metadata removal.
    /// </summary>
    Standard,

    /// <summary>
    /// Aggressive obfuscation: all techniques enabled at maximum intensity.
    /// </summary>
    Aggressive,

    /// <summary>
    /// Custom obfuscation: use individual settings for each technique.
    /// </summary>
    Custom
}

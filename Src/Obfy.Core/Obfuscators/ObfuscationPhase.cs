namespace Obfy.Core.Obfuscators;

/// <summary>
/// Ordered phases that obfuscators run in. The numeric value is the execution priority
/// (<see cref="IObfuscator.Priority"/>): lower runs first. Encoding the documented priority ranges as
/// named phases keeps the ordering intent in one place instead of scattered magic numbers.
/// </summary>
public enum ObfuscationPhase
{
    /// <summary>
    /// Encrypt string literals. Runs first so later control-flow rewrites see decrypt calls, and
    /// injected helper types exist before renaming/metadata.
    /// </summary>
    StringEncryption = 10,

    /// <summary>Encrypt numeric constants.</summary>
    ConstantEncryption = 11,

    /// <summary>Encrypt embedded resources.</summary>
    ResourceEncryption = 15,

    /// <summary>Obfuscate control flow.</summary>
    ControlFlow = 30,

    /// <summary>Rename symbols.</summary>
    SymbolRenaming = 50,

    /// <summary>Inject anti-debugging checks.</summary>
    AntiDebug = 70,

    /// <summary>Inject anti-decompiler junk.</summary>
    AntiDecompiler = 72,

    /// <summary>Inject anti-tamper verification.</summary>
    AntiTamper = 75,

    /// <summary>Strip metadata (runs last).</summary>
    MetadataRemoval = 90
}

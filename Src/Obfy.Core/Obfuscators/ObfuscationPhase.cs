namespace Obfy.Core.Obfuscators;

/// <summary>
/// Ordered phases that obfuscators run in. The numeric value is the execution priority
/// (<see cref="IObfuscator.Priority"/>): lower runs first. Encoding the documented priority ranges as
/// named phases keeps the ordering intent in one place instead of scattered magic numbers.
/// </summary>
public enum ObfuscationPhase
{
    /// <summary>
    /// Embed sibling DLLs as resources and hook AssemblyResolve. Runs before string encryption
    /// so the injected helper exists for later passes, and before resource encryption so packed
    /// payloads can be excluded from encryption.
    /// </summary>
    DependencyEmbedding = 8,

    /// <summary>
    /// Encrypt string literals so later control-flow rewrites see decrypt calls, and injected
    /// helper types exist before renaming/metadata.
    /// </summary>
    StringEncryption = 10,

    /// <summary>Encrypt numeric constants.</summary>
    ConstantEncryption = 11,

    /// <summary>Encrypt embedded resources.</summary>
    ResourceEncryption = 15,

    /// <summary>Inject anti-debugging checks (before control flow/renaming so helpers are obfuscated).</summary>
    AntiDebug = 18,

    /// <summary>Inject anti-dump PE-header wipe and dbghelp MiniDumpWriteDump patch (before control flow/renaming).</summary>
    AntiDump = 19,

    /// <summary>
    /// Inject anti-decompiler junk and decoy ConfusedBy/Dotfuscator attributes (before renaming).
    /// Decoy type names are pinned so they survive symbol renaming.
    /// </summary>
    AntiDecompiler = 20,

    /// <summary>
    /// Inject watermark CA before renaming. The type name <c>WatermarkAttribute</c> is pinned;
    /// the customer id lives in the constructor argument and the <c>Id</c> field.
    /// </summary>
    Watermark = 21,

    /// <summary>Inject anti-tamper verification (before renaming).</summary>
    AntiTamper = 22,

    /// <summary>Encrypt method IL in the PE image; decrypted at module load.</summary>
    MethodEncryption = 25,

    /// <summary>Obfuscate control flow, including injected helpers.</summary>
    ControlFlow = 30,

    /// <summary>Hide call targets behind proxy methods.</summary>
    ReferenceProxy = 40,

    /// <summary>Rename symbols, including injected helpers.</summary>
    SymbolRenaming = 50,

    /// <summary>Strip metadata (runs last).</summary>
    MetadataRemoval = 90
}

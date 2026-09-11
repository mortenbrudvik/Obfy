using dnlib.DotNet;
using Microsoft.CodeAnalysis.CSharp;
using Obfy.Core.Models;

namespace Obfy.Core.Pipeline;

/// <summary>
/// Shared context passed through the obfuscation pipeline.
/// Contains the module/syntax tree being processed and accumulated results.
/// </summary>
public class PipelineContext
{
    /// <summary>
    /// Gets the type of target being processed.
    /// </summary>
    public TargetType TargetType { get; init; }

    /// <summary>
    /// Gets or sets the dnlib ModuleDef for assembly targets.
    /// </summary>
    public ModuleDef? Module { get; set; }

    /// <summary>
    /// Gets or sets the Roslyn compilation for source code targets.
    /// </summary>
    public CSharpCompilation? Compilation { get; set; }

    /// <summary>
    /// Returns the assembly module, throwing if this is not an assembly context. Assembly obfuscators
    /// call this instead of dereferencing <see cref="Module"/> with <c>!</c>.
    /// </summary>
    public ModuleDef RequireModule() =>
        Module ?? throw new InvalidOperationException(
            $"No assembly module is available (target type is {TargetType}).");

    /// <summary>
    /// Returns the Roslyn compilation, throwing if this is not a source-code context. Source
    /// obfuscators call this instead of dereferencing <see cref="Compilation"/> with <c>!</c>.
    /// </summary>
    public CSharpCompilation RequireCompilation() =>
        Compilation ?? throw new InvalidOperationException(
            $"No source compilation is available (target type is {TargetType}).");

    /// <summary>
    /// Gets the active obfuscation settings.
    /// </summary>
    public ObfySettings Settings { get; init; } = new();

    /// <summary>
    /// Gets the accumulated statistics from all obfuscators.
    /// </summary>
    public ObfuscationStatistics Statistics { get; } = new();

    /// <summary>
    /// Gets the symbol mapping (original name -> obfuscated name).
    /// Used for debugging and symbol map generation.
    /// </summary>
    public Dictionary<string, string> SymbolMap { get; } = new();

    /// <summary>
    /// Set when the anti-tamper type was injected. The assembly writer uses presence (not a token)
    /// as the signal to patch the integrity-hash blob after the module is written. Null when
    /// anti-tamper did not run.
    /// </summary>
    public AntiTamperMetadata? AntiTamperMetadata { get; set; }

    /// <summary>
    /// Set when method-body encryption was injected. The writer XOR-encrypts IL after RVAs are known.
    /// </summary>
    public MethodEncryptionMetadata? MethodEncryptionMetadata { get; set; }

    /// <summary>
    /// Gets or sets the input file path.
    /// </summary>
    public string? InputPath { get; set; }

    /// <summary>
    /// Gets or sets the output file path.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// Gets the list of items skipped during obfuscation.
    /// </summary>
    public List<SkippedItem> SkippedItems { get; } = new();

    /// <summary>
    /// Gets non-fatal warnings raised during obfuscation (e.g. a protection that cannot take effect
    /// for certain deployment models). Surfaced to the user so protections never silently do nothing.
    /// </summary>
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Gets the processing time entries for each obfuscator.
    /// </summary>
    public List<ProcessingTimeEntry> ProcessingTimes { get; } = new();

    /// <summary>
    /// Creates a context for assembly obfuscation.
    /// </summary>
    public static PipelineContext ForAssembly(ModuleDef module, ObfySettings settings)
    {
        return new PipelineContext
        {
            TargetType = TargetType.Assembly,
            Module = module,
            Settings = settings
        };
    }

    /// <summary>
    /// Creates a context for source code obfuscation.
    /// </summary>
    public static PipelineContext ForSourceCode(CSharpCompilation compilation, ObfySettings settings)
    {
        return new PipelineContext
        {
            TargetType = TargetType.SourceCode,
            Compilation = compilation,
            Settings = settings
        };
    }
}

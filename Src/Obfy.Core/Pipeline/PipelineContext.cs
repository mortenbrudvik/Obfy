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
    /// Gets additional data that can be shared between obfuscators.
    /// </summary>
    public Dictionary<string, object> SharedData { get; } = new();

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

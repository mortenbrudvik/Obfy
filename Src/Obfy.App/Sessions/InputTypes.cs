using Obfy.App.Models;
using Obfy.Core.Models.Solution;

namespace Obfy.App.Sessions;

/// <summary>How a run turns the included files into output. One rule, shared by the hint the page shows and the run.</summary>
public enum OutputMode
{
    /// <summary>Nothing included: nothing to write.</summary>
    None,

    /// <summary>Each included file is protected alone and written as <c>name.obfuscated.ext</c>.</summary>
    Each,

    /// <summary>Two or more assemblies, or any that came from a solution, are protected together into one folder.</summary>
    ClosedSet,

    /// <summary>Two or more assemblies are merged into one.</summary>
    Merge
}

/// <summary>
/// Where a run writes. Nothing is created to compute it. <paramref name="Directory"/> is the folder the user chose, the
/// closed-set folder or the merge folder; null when each file goes next to its own input.
/// </summary>
/// <param name="MergePath">The merged assembly's path, in <see cref="OutputMode.Merge"/> only.</param>
/// <param name="Hint">The sentence the page shows.</param>
/// <param name="Outputs">Every file the run writes for the included inputs, by the rule the hint and the run share.</param>
public record OutputPlan(OutputMode Mode, string? Directory, string? MergePath, string Hint, IReadOnlyList<PlannedOutput> Outputs)
{
    /// <summary>Where the run writes the output of <paramref name="inputPath"/>.</summary>
    /// <exception cref="InvalidOperationException">The plan has no output for that input.</exception>
    public string OutputPathFor(string inputPath) =>
        Outputs.FirstOrDefault(o => string.Equals(o.InputPath, inputPath, StringComparison.OrdinalIgnoreCase))?.OutputPath
        ?? throw new InvalidOperationException("The output plan has no output for that input.");
}

/// <summary>One file the run writes: the input it comes from and the path it is written to. A merge has one, from its first input.</summary>
public record PlannedOutput(string InputPath, string OutputPath);

/// <summary>An included row the run can protect (an assembly or a source file), with what the analysis learnt about it.</summary>
public record InputFile(string Id, string Path, FileKind Kind, bool FromSession, ProjectSettingsHints? Hints)
{
    public bool IsAssembly => Kind == FileKind.Assembly;
}

/// <summary>
/// Everything a run reads from the file list, taken in one read so the files and the plan made from them cannot come
/// from two different lists.
/// </summary>
/// <param name="Included">The rows a run protects, in list order.</param>
/// <param name="Skipped">The skipped rows, for the log.</param>
/// <param name="Plan">The output plan for <paramref name="Included"/>, by the rule the hint also uses.</param>
/// <param name="OutputFolderChosen">The user chose an output folder (otherwise the plan's folder is a default).</param>
/// <param name="SymbolMapPath">Where the symbol map is written, or null while the option is off.</param>
/// <param name="Collision">A planned output, or the symbol map at <paramref name="SymbolMapPath"/>, that would replace one of the listed files (the map: or one of the planned outputs); null when none would. A run does not start while it is set.</param>
public record RunInput(
    IReadOnlyList<InputFile> Included,
    IReadOnlyList<FileRow> Skipped,
    OutputPlan Plan,
    bool OutputFolderChosen,
    string? SymbolMapPath,
    OutputCollision? Collision = null);

/// <summary>What <see cref="InputSession.AddPaths"/> found beyond the files it added.</summary>
/// <param name="ConfigPath">The first <c>.json</c> among the paths: the configuration to load. Loading it is not the list's business.</param>
/// <param name="Added">How many rows were added.</param>
public record AddPathsResult(string? ConfigPath, int Added);

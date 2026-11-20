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
public record OutputPlan(OutputMode Mode, string? Directory, string? MergePath, string Hint);

/// <summary>An included row the run can protect (an assembly or a source file), with what the analysis learnt about it.</summary>
public record InputFile(string Id, string Path, FileKind Kind, bool FromSession, ProjectSettingsHints? Hints)
{
    public bool IsAssembly => Kind == FileKind.Assembly;
}

/// <summary>What <see cref="InputSession.AddPaths"/> found beyond the files it added.</summary>
/// <param name="ConfigPath">The first <c>.json</c> among the paths: the configuration to load. Loading it is not the list's business.</param>
/// <param name="Added">How many rows were added.</param>
public record AddPathsResult(string? ConfigPath, int Added);

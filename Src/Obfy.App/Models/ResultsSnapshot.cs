namespace Obfy.App.Models;

/// <summary>
/// Where the results stand. The page shows "Nothing to show yet" for <see cref="NotRun"/>, a progress state for
/// <see cref="Running"/>, and the statistics and tree for <see cref="Finished"/>; an empty finished run is not the same as no run.
/// </summary>
public enum ResultsState
{
    /// <summary>No run has started, or the results were cleared.</summary>
    NotRun,

    /// <summary>A run is in progress; the previous results are gone.</summary>
    Running,

    /// <summary>A run finished and its results are loaded.</summary>
    Finished
}

/// <summary>
/// Everything but the symbol tree and the decompiled text, which are paged and fetched on demand. Answers
/// <c>results.summary</c> and is pushed with <c>results.changed</c>.
/// </summary>
/// <param name="Revision">Rises with every change. A pushed event not newer than the revision the page holds is stale and is dropped.</param>
/// <param name="State">Before a run, during it, or after it.</param>
/// <param name="Groups">Counters, zeros left out.</param>
/// <param name="ElapsedMs">How long the run took.</param>
/// <param name="SymbolCount">Entries in the symbol map.</param>
/// <param name="HasReport">A report is available to export.</param>
/// <param name="HasPreview">A decompiled preview of <paramref name="SelectedPreviewPath"/> is loaded.</param>
/// <param name="PreviewPaths">Output assemblies that can be decompiled.</param>
/// <param name="SelectedPreviewPath">The assembly the preview shows, or null.</param>
/// <param name="PreviewError">Why the preview could not be shown, or null.</param>
public sealed record ResultsSnapshot(
    long Revision,
    ResultsState State,
    IReadOnlyList<StatisticGroup> Groups,
    long ElapsedMs,
    int SymbolCount,
    bool HasReport,
    bool HasPreview,
    IReadOnlyList<string> PreviewPaths,
    string? SelectedPreviewPath,
    string? PreviewError);

/// <summary>The decompiled C# of one output assembly, answering <c>preview.get</c>. Failures are in <paramref name="Error"/>, never thrown.</summary>
public sealed record PreviewResult(string? Path, string Text, string? Error)
{
    public bool HasPreview => Error is null && Text.Length > 0;
}

/// <summary>Answers an export: <paramref name="Exported"/> is false when the user cancelled the save dialog.</summary>
public sealed record ExportResult(bool Exported, string? Path);

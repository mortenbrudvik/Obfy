namespace Obfy.App.Models;

/// <summary>How a run turns the included files into output. Serialized by name. One rule, shared with the hint the page shows.</summary>
public enum RunMode
{
    /// <summary>Nothing is included: there is nothing to run.</summary>
    None,

    /// <summary>Each file is protected on its own.</summary>
    Single,

    /// <summary>Two or more C# files are compiled and protected as one unit.</summary>
    Sources,

    /// <summary>Two or more assemblies are merged into one, then protected.</summary>
    Merge,

    /// <summary>Two or more assemblies, or any from a solution, are protected together so shared names stay consistent.</summary>
    ClosedSet
}

/// <summary>Where the run stands. Serialized by name.</summary>
public enum RunStatus
{
    /// <summary>No run has started.</summary>
    Idle,

    /// <summary>A run is in progress.</summary>
    Running,

    /// <summary>The last run finished. <see cref="RunSnapshot.Failed"/> says how many files failed.</summary>
    Done,

    /// <summary>The last run was cancelled.</summary>
    Cancelled,

    /// <summary>The last run stopped on an unexpected error; <see cref="RunSnapshot.Message"/> says what.</summary>
    Faulted
}

/// <summary>
/// Everything the page needs to draw the run controls without knowing the rules: whether Obfuscate is available and,
/// when it is not, why; how the run will treat the files; and how it is going. Serialized by <c>JsonUtils</c> (camelCase,
/// enums as strings). Answers <c>run.get</c> and is pushed with <c>run.changed</c> and <c>run.completed</c>.
/// </summary>
/// <param name="Revision">Rises with every change. A pushed snapshot not newer than the one the page holds is stale and is dropped.</param>
/// <param name="CanStart">A run can start now. False while one is running, with nothing included, or with invalid settings.</param>
/// <param name="BlockedReason">Why <paramref name="CanStart"/> is false, in plain words; null when it is true.</param>
/// <param name="Mode">How the included files would be run (or are being run).</param>
/// <param name="ModeExplanation">One plain sentence saying what <paramref name="Mode"/> does.</param>
/// <param name="OutputHint">Where the output goes, from the same rule as the run.</param>
/// <param name="Progress">Overall progress, 0 to 100. Meaningless while <paramref name="Indeterminate"/>.</param>
/// <param name="Indeterminate">The run cannot report per-file progress (a merge, a closed set, or a single file).</param>
/// <param name="CurrentFile">The file being processed, or null.</param>
/// <param name="Completed">Files finished so far.</param>
/// <param name="Total">Files in the run.</param>
/// <param name="Failed">Files that failed, in the last run.</param>
/// <param name="Status">Idle, running, done, cancelled or faulted.</param>
/// <param name="Message">The status line: what is happening, or how the last run ended.</param>
public sealed record RunSnapshot(
    long Revision,
    bool CanStart,
    string? BlockedReason,
    RunMode Mode,
    string ModeExplanation,
    string OutputHint,
    double Progress,
    bool Indeterminate,
    string? CurrentFile,
    int Completed,
    int Total,
    int Failed,
    RunStatus Status,
    string Message);

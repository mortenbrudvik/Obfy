namespace Obfy.App.Models;

/// <summary>Where a row stands in a run. Serialized by name (<c>Pending</c>, <c>Skipped</c>, ...).</summary>
public enum FileStatus
{
    Pending,
    Processing,
    Success,
    Error,
    Skipped
}

/// <summary>What a row is, so the page can pick an icon without looking at the extension.</summary>
public enum FileKind
{
    /// <summary>A <c>.dll</c> or <c>.exe</c>.</summary>
    Assembly,

    /// <summary>A <c>.cs</c> file.</summary>
    Source,

    /// <summary>A project that has no built output (a skipped row from a solution or project drop).</summary>
    Project
}

/// <summary>How a row got into the list.</summary>
public enum FileOrigin
{
    /// <summary>Picked, dropped or given on the command line as itself.</summary>
    Added,

    /// <summary>Expanded from a dropped solution or project.</summary>
    Solution
}

/// <summary>How serious a <see cref="InputNotice"/> is. Serialized by name.</summary>
public enum NoticeLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// One row of the file list. The page refers to a row by <paramref name="Id"/> only; the id stays the same for the same
/// file for as long as the process runs.
/// </summary>
/// <param name="Id">Stable id derived from the file's full path; unique within the list.</param>
/// <param name="Name">File name.</param>
/// <param name="Path">Full path, for display.</param>
/// <param name="Size">Bytes, or 0 when the file is not there.</param>
/// <param name="SizeText">The size as the page shows it (<c>12.5 KB</c>).</param>
/// <param name="Included">False when the row is skipped or excluded; the page draws the include box from it.</param>
/// <param name="SkipReason">Why a skipped row is skipped. A row keeps the reason the analysis gave it.</param>
/// <param name="OutputPath">Where the protected file was written, after a run.</param>
/// <param name="Error">What went wrong, when <paramref name="Status"/> is Error.</param>
public record FileRow(
    string Id,
    string Name,
    string Path,
    FileOrigin Origin,
    FileKind Kind,
    long Size,
    string SizeText,
    bool Included,
    FileStatus Status,
    string? SkipReason,
    string? OutputPath,
    string? Error);

/// <summary>A message about the last thing done to the list (a skipped file, a refused solution). Replaced by the next change.</summary>
public record InputNotice(NoticeLevel Level, string Title, string Message);

/// <summary>
/// Everything the page needs to draw the file list and the output row without knowing the rules. Serialized by
/// <c>JsonUtils</c> (camelCase, enums as strings).
/// </summary>
/// <param name="Revision">Rises with every change. A pushed snapshot older than the one the page holds is stale and is dropped.</param>
/// <param name="Files">The rows, in the order they were added.</param>
/// <param name="HasIncludedFiles">At least one included row can be protected (an assembly or a source file).</param>
/// <param name="OutputFolder">The folder the user chose, or empty when output goes next to the inputs.</param>
/// <param name="OutputHint">Where a run writes with the chosen folder (or none), counting only included files.</param>
/// <param name="MergeOutputHint">The same, when the merge option is on. Equals <paramref name="OutputHint"/> when the included files cannot merge.</param>
/// <param name="OutputWarning">Set when the folder saved last time is gone, and was cleared.</param>
/// <param name="WriteSymbolMap">Whether a run writes the symbol map.</param>
/// <param name="SymbolMapPath">What the user typed for the map's path; empty for the default. Only editable while <paramref name="WriteSymbolMap"/> is on.</param>
/// <param name="ResolvedSymbolMapPath">The file a run would write, or null while the option is off. A relative path resolves against the output folder.</param>
/// <param name="Notices">What the last change has to say.</param>
public record InputSnapshot(
    long Revision,
    IReadOnlyList<FileRow> Files,
    bool HasIncludedFiles,
    string OutputFolder,
    string OutputHint,
    string MergeOutputHint,
    string? OutputWarning,
    bool WriteSymbolMap,
    string SymbolMapPath,
    string? ResolvedSymbolMapPath,
    IReadOnlyList<InputNotice> Notices);

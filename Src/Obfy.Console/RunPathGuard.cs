using Obfy.Core.Models;
using Obfy.Core.Utilities;

namespace Obfy.Console;

/// <summary>What a path the run must not replace is to the run.</summary>
internal enum PathRole
{
    /// <summary>A file the run reads: a listed input, or a project file or built output a solution session reads.</summary>
    Input,

    /// <summary>The <c>-c</c> configuration file.</summary>
    Config,

    /// <summary>A file the run writes: an output, the files packing adds beside it, its incremental cache file.</summary>
    Output
}

/// <summary>Files the run must not replace, and what they are to it.</summary>
internal sealed record ProtectedPaths(PathRole Role, IEnumerable<string?> Paths);

/// <summary>
/// A <c>--map</c> or <c>--report</c> path that would replace a file the run needs, and the sentence that says so.
/// </summary>
/// <param name="Option">The option, as typed: <c>--map</c> or <c>--report</c>.</param>
/// <param name="File">The full path the option names: the file the problem-matcher line is filed against.</param>
/// <param name="Message">One line: the option, the file, why, and the way out.</param>
internal sealed record PathCollision(string Option, string File, string Message);

/// <summary>
/// The data-loss guard of the command line. The symbol map and the report are written after the run, to a path the user
/// typed, and nothing else compared it with the files the run reads or writes: <c>obfy app.dll -o out --map app.dll</c>
/// replaced the input with JSON, and <c>--map out\app.dll</c> replaced the protected output. A path that names one of them
/// (or the other option's file, or the <c>-c</c> file) is refused before the run starts.
/// <para>
/// Paths are compared with <see cref="PathIdentity.SameFile"/>: full paths, ignoring case on Windows, with a <c>..</c>
/// segment, the <c>\\?\</c> forms and a link in the file's name or in a folder above it followed. It is the map and the
/// report that are guarded; an output folder equal to the input's folder is the documented in-place flow and is left alone.
/// </para>
/// </summary>
internal static class RunPathGuard
{
    private const string MapOption = "--map";
    private const string ReportOption = "--report";

    /// <summary>
    /// For each of <paramref name="map"/> and <paramref name="report"/> (null: not given), the first file among
    /// <paramref name="groups"/> it would replace, in the order the groups are listed. With
    /// <paramref name="compareMapWithReport"/> a report path that is the map's too is refused when nothing else is wrong
    /// with it. At most one collision per option. Never throws.
    /// </summary>
    public static IReadOnlyList<PathCollision> Find(
        FileInfo? map, FileInfo? report, IReadOnlyList<ProtectedPaths> groups, bool compareMapWithReport = false)
    {
        var found = new List<PathCollision>(2);
        if (map is null && report is null)
            return found;

        var candidates = groups
            .SelectMany(static group => group.Paths
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(path => (Path: path!, group.Role)))
            .ToList();

        if (FindFor(MapOption, "symbol map", map, candidates) is { } mapCollision)
            found.Add(mapCollision);

        if (FindFor(ReportOption, "report", report, candidates) is { } reportCollision)
            found.Add(reportCollision);
        else if (compareMapWithReport && map is not null && report is not null
                 && PathIdentity.SameFile(map.FullName, report.FullName))
        {
            found.Add(new PathCollision(
                ReportOption,
                report.FullName,
                $"{ReportOption} cannot be '{report.FullName}': {MapOption} writes the same file and the report would replace the symbol map. "
                + $"Choose another path for {ReportOption}."));
        }

        return found;
    }

    /// <summary>
    /// The same check against the paths the finished run wrote (the output of each result and its packed launcher), for
    /// the moment just before the map and the report are written: a file the plan did not know about is still not replaced.
    /// </summary>
    public static IReadOnlyList<PathCollision> FindForResults(
        FileInfo? map, FileInfo? report, IEnumerable<ObfuscationResult> results) =>
        map is null && report is null
            ? []
            : Find(map, report, [new ProtectedPaths(PathRole.Output, results.SelectMany(static r => new[] { r.OutputPath, r.PackedLauncherPath }))]);

    /// <summary>
    /// An assembly the run writes, and the files written for it: the packing companions when packing is on (all of them
    /// are listed, whether or not the assembly turns out to have an entry point) and its incremental cache file.
    /// </summary>
    public static IEnumerable<string> WithCompanions(string assemblyOutput, ObfySettings settings, bool cacheFile)
    {
        yield return assemblyOutput;
        foreach (var sidecar in PackingApplicator.SidecarPaths(assemblyOutput, settings.Packing))
            yield return sidecar;
        if (cacheFile)
            yield return IncrementalCache.CachePath(assemblyOutput);
    }

    private static PathCollision? FindFor(
        string option, string noun, FileInfo? file, List<(string Path, PathRole Role)> candidates)
    {
        if (file is null)
            return null;

        foreach (var (path, role) in candidates)
        {
            if (PathIdentity.SameFile(file.FullName, path))
                return new PathCollision(option, file.FullName, Describe(option, noun, file.FullName, role));
        }

        return null;
    }

    private static string Describe(string option, string noun, string path, PathRole role)
    {
        var why = role switch
        {
            PathRole.Input => $"it is an input file and the run would replace it with the {noun}",
            PathRole.Config => $"it is the configuration file given with -c and the run would replace it with the {noun}",
            _ => $"it is a file the run writes and the {noun} would replace it"
        };

        return $"{option} cannot be '{path}': {why}. Choose another path for {option}.";
    }
}

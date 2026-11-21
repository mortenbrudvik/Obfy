using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using dnlib.DotNet;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// File-backed cache so CI can skip obfuscation when the Obfy build, input bytes, and settings
/// are unchanged. The key covers the Obfy.Core build identity (version plus the module MVID, so a
/// hotfix that keeps the pinned version still misses), the signing key file and every dependency
/// DLL that embedding would pack, so a rotated key or a rebuilt sibling is a miss.
/// The caller computes the key once, before the run, from the settings the pipeline will use, and
/// removes the cache file before it overwrites the output (<see cref="Invalidate"/>).
/// A locked or corrupt <c>{output}.obfycache</c> is a miss, not a failed run.
/// The file is a small JSON document: the key and what a hit needs to say about the run that wrote the
/// output (<see cref="CachedRun"/>): the statistics and three counts. It holds numbers only. The symbol map,
/// the skipped items and the warnings name the original types, members, resources and paths, and people ship
/// their output folder, so none of them is ever written here. A file in any other layout (including the bare
/// hex key earlier builds wrote) is a miss: it has no numbers to return, and the key already contains the
/// Obfy.Core build identity, so it could only have come from a build that no longer exists.
/// A packing hit also requires the native sibling <c>{name}.runtimeconfig.json</c> (win-x64)
/// or the portable launcher files. The hex stored here is only the cache identity. It is not
/// the native overlay AES key.
/// </summary>
public static class IncrementalCache
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Version of the entry layout. Any other value is a miss.</summary>
    private const int EntryFormat = 2;

    private static readonly JsonSerializerOptions EntryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string CachePath(string outputPath) => outputPath + ".obfycache";

    /// <summary>
    /// Identifies the Obfy.Core build. The assembly version is pinned, so it alone would let a hotfix reuse
    /// the previous build's outputs. The MVID changes with every compiled change.
    /// </summary>
    internal static string BuildIdentity { get; } =
        (typeof(IncrementalCache).Assembly.GetName().Version?.ToString() ?? "0")
        + "+" + typeof(IncrementalCache).Module.ModuleVersionId.ToString("N");

    public static string ComputeKey(string inputPath, ObfySettings settings) =>
        ComputeKey(inputPath, settings, BuildIdentity);

    internal static string ComputeKey(string inputPath, ObfySettings settings, string buildIdentity)
    {
        var input = File.ReadAllBytes(inputPath);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var stamp = Encoding.UTF8.GetBytes("obfy-cache|" + buildIdentity + "|");
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(stamp);
        hasher.AppendData(input);
        hasher.AppendData(jsonBytes);
        AppendSigningKey(hasher, settings);
        AppendEmbeddedDependencies(hasher, inputPath, input, settings);
        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    private static void AppendSigningKey(IncrementalHash hasher, ObfySettings settings)
    {
        if (!settings.Signing.Enabled || string.IsNullOrWhiteSpace(settings.Signing.KeyFile))
            return;

        hasher.AppendData("|sign|"u8);
        if (!File.Exists(settings.Signing.KeyFile))
        {
            hasher.AppendData("missing"u8);
            return;
        }

        hasher.AppendData(File.ReadAllBytes(settings.Signing.KeyFile));
    }

    private static void AppendEmbeddedDependencies(IncrementalHash hasher, string inputPath, byte[] input, ObfySettings settings)
    {
        if (!settings.DependencyEmbedding.Enabled ||
            RuntimeProfileGating.BlocksAssemblyResolve(settings.RuntimeProfile))
            return;

        List<string> referenced;
        try
        {
            referenced = ReadAssemblyRefNames(input);
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidDataException)
        {
            return;
        }

        var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath));
        if (string.IsNullOrEmpty(inputDir))
            return;

        var selfName = Path.GetFileNameWithoutExtension(inputPath);
        foreach (var simple in referenced
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static name => name, StringComparer.Ordinal))
        {
            if (string.Equals(simple, selfName, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = simple + ".dll";
            if (!IsEmbeddedDependency(fileName, settings.DependencyEmbedding))
                continue;

            // The reference name comes from the input assembly, which is untrusted.
            var path = ResolveSibling(inputDir, fileName);
            if (path is null || !File.Exists(path))
                continue;

            hasher.AppendData(Encoding.UTF8.GetBytes("|dep|" + fileName + "|"));
            hasher.AppendData(File.ReadAllBytes(path));
        }
    }

    /// <summary>
    /// Returns the path of <paramref name="fileName"/> inside <paramref name="directory"/>, or null when the
    /// name is not a plain file name (separators, rooted or absolute paths, invalid characters).
    /// </summary>
    internal static string? ResolveSibling(string directory, string fileName)
    {
        if (string.IsNullOrEmpty(fileName) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(directory, fileName));
        var parent = Path.GetDirectoryName(full);
        var expected = Path.GetFullPath(directory);
        return parent is not null &&
               string.Equals(
                   Path.TrimEndingDirectorySeparator(parent),
                   Path.TrimEndingDirectorySeparator(expected),
                   StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
    }

    private static List<string> ReadAssemblyRefNames(byte[] input)
    {
        using var module = ModuleDefMD.Load(input);
        var names = new List<string>();
        foreach (var asmRef in module.GetAssemblyRefs())
        {
            var name = asmRef.Name;
            if (!UTF8String.IsNullOrEmpty(name))
                names.Add(name.String);
        }

        return names;
    }

    /// <summary>
    /// Same include/exclude rules as dependency embedding. A sibling DLL is part of the
    /// cache key only when embedding would pack it.
    /// </summary>
    private static bool IsEmbeddedDependency(string fileName, DependencyEmbeddingSettings settings)
    {
        var excludes = settings.ExcludePatterns ?? new List<string>();
        var includes = settings.IncludePatterns ?? new List<string>();
        if (excludes.Any(pattern => WildcardMatcher.IsMatch(fileName, pattern)))
            return false;
        return includes.Count == 0 || includes.Any(pattern => WildcardMatcher.IsMatch(fileName, pattern));
    }

    public static bool TryHit(string inputPath, string outputPath, ObfySettings settings)
    {
        try
        {
            return TryHit(outputPath, settings, ComputeKey(inputPath, settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Hit check against a key the caller already computed, so a run uses one key from start to finish.
    /// </summary>
    internal static bool TryHit(string outputPath, ObfySettings settings, string expectedKey) =>
        TryReadHit(outputPath, settings, expectedKey) is not null;

    /// <summary>
    /// The record of the run that wrote <paramref name="outputPath"/> when the cache is a hit for
    /// <paramref name="expectedKey"/>, otherwise null. Never throws: a missing, locked, corrupt or old-format file
    /// is a miss.
    /// </summary>
    internal static CachedRun? TryReadHit(string outputPath, ObfySettings settings, string expectedKey)
    {
        try
        {
            if (!File.Exists(outputPath) || !File.Exists(CachePath(outputPath)))
                return null;
            if (settings.Packing.Enabled)
            {
                if (settings.Packing.IsPortable)
                {
                    if (!File.Exists(ManagedLauncherPacker.LauncherPathFor(outputPath)) ||
                        !File.Exists(ManagedLauncherPacker.RuntimeConfigPathFor(outputPath)))
                        return null;
                }
                else if (!File.Exists(NativePacker.RuntimeConfigPathFor(outputPath)) ||
                         !NativePacker.LooksLikePackedHost(outputPath))
                    return null;
            }

            var entry = ParseEntry(File.ReadAllText(CachePath(outputPath)));
            return entry is not null && string.Equals(expectedKey, entry.Key, StringComparison.OrdinalIgnoreCase)
                ? entry.Run
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record Entry(string Key, CachedRun Run);

    /// <summary>The wire shape of the file. Numbers and the key, nothing else.</summary>
    private sealed class EntryFile
    {
        public int Format { get; set; }
        public string? Key { get; set; }
        public ObfuscationStatistics? Statistics { get; set; }
        public int SymbolMapEntries { get; set; }
        public int SkippedItems { get; set; }
        public int Warnings { get; set; }
    }

    private static Entry? ParseEntry(string text)
    {
        try
        {
            var file = JsonSerializer.Deserialize<EntryFile>(text, EntryJsonOptions);
            if (file is null ||
                file.Format != EntryFormat ||
                string.IsNullOrWhiteSpace(file.Key) ||
                file.Statistics is null ||
                file.SymbolMapEntries < 0 ||
                file.SkippedItems < 0 ||
                file.Warnings < 0)
            {
                return null;
            }

            return new Entry(file.Key.Trim(), new CachedRun(file.Statistics, file.SymbolMapEntries, file.SkippedItems, file.Warnings));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes an entry for an output the caller produced elsewhere. It carries no run record (zero statistics and
    /// counts), so a hit on it says nothing about the run. The obfuscation service writes through
    /// <see cref="TryWrite(string, string, CachedRun)"/> instead.
    /// </summary>
    public static bool TryWrite(string inputPath, string outputPath, ObfySettings settings)
    {
        try
        {
            return TryWrite(outputPath, ComputeKey(inputPath, settings), CachedRun.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool TryWrite(string outputPath, string key, CachedRun run)
    {
        try
        {
            var file = new EntryFile
            {
                Format = EntryFormat,
                Key = key,
                Statistics = run.Statistics,
                SymbolMapEntries = run.SymbolMapEntries,
                SkippedItems = run.SkippedItems,
                Warnings = run.Warnings
            };
            File.WriteAllText(CachePath(outputPath), JsonSerializer.Serialize(file, EntryJsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the cache file of an output that is about to be overwritten, so a failed or partial run can
    /// never leave a key that blesses the bad output. When the file cannot be deleted it is emptied (an empty
    /// file is a miss). Returns false only when the stale key may survive.
    /// </summary>
    internal static bool Invalidate(string outputPath)
    {
        var path = CachePath(outputPath);
        try
        {
            if (!File.Exists(path))
                return true;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.WriteAllText(path, string.Empty);
                return true;
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public static void Write(string inputPath, string outputPath, ObfySettings settings) =>
        TryWrite(inputPath, outputPath, settings);
}

/// <summary>
/// What the incremental cache keeps of the run that wrote an output: numbers only. The symbol map, the skipped
/// items and the warnings are never stored (they name original types, members, resources and paths), so the cache
/// keeps how many there were and a hit says so instead of inventing them.
/// </summary>
/// <param name="Statistics">The statistics of that run.</param>
/// <param name="SymbolMapEntries">How many entries its symbol map had. Zero means the map of a hit (empty) is exact.</param>
/// <param name="SkippedItems">How many items it skipped and left unobfuscated.</param>
/// <param name="Warnings">How many warnings it raised that a hit does not derive again (runtime-profile and packing lines are).</param>
internal sealed record CachedRun(ObfuscationStatistics Statistics, int SymbolMapEntries, int SkippedItems, int Warnings)
{
    /// <summary>A record with no information: zero statistics and counts.</summary>
    public static CachedRun Empty => new(new ObfuscationStatistics(), 0, 0, 0);
}

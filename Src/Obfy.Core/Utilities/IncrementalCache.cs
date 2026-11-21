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
/// The entry is written to a temporary file in the same folder and moved over the cache file, the way
/// <c>AssemblyProcessor.SaveAsync</c> writes outputs, so a crash in the middle of the write, or a second run on the
/// same output, never leaves a truncated entry, and an entry that was valid stays valid when the next write fails. A
/// failure to write is a <c>false</c> for the caller to report as a warning, never an exception.
/// The file is a small JSON document: the key, what a hit needs to say about the run that wrote the
/// output (<see cref="CachedRun"/>: the statistics and three counts) and a fingerprint (length and SHA-256) of
/// the output as it was when the cache was written, and of the sibling files a packed output needs. It holds
/// numbers and hashes only. The symbol map, the skipped items and the warnings name the original types,
/// members, resources and paths, and people ship their output folder, so none of them is ever written here.
/// A hit needs the files on disk to still match their fingerprint: the key proves the input, not that the
/// file at the output path is the one the cached run wrote (a build can copy the unprotected original over
/// it). A file in any other layout (including the bare hex key earlier builds wrote) is a miss: it has no
/// numbers to return, and the key already contains the Obfy.Core build identity, so it could only have come
/// from a build that no longer exists.
/// A packing hit also requires the native sibling <c>{name}.runtimeconfig.json</c> (win-x64)
/// or the portable launcher files, each matching its fingerprint. The hex stored here is only the cache
/// identity. It is not the native overlay AES key.
/// </summary>
public static class IncrementalCache
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Version of the entry layout. Any other value is a miss.</summary>
    private const int EntryFormat = 3;

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
            if (entry is null || !string.Equals(expectedKey, entry.Key, StringComparison.OrdinalIgnoreCase))
                return null;

            // The key says the input is the one the cached run read. These say the files are the ones it wrote.
            var sidecars = PackingApplicator.SidecarPaths(outputPath, settings.Packing).ToList();
            if (sidecars.Count != entry.Companions.Count || !entry.Output.Matches(outputPath))
                return null;
            for (var i = 0; i < sidecars.Count; i++)
            {
                if (!entry.Companions[i].Matches(sidecars[i]))
                    return null;
            }

            return entry.Run;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record Entry(string Key, CachedRun Run, FileFingerprint Output, List<FileFingerprint> Companions);

    /// <summary>The wire shape of the file. Numbers, hashes and the key, nothing else.</summary>
    private sealed class EntryFile
    {
        public int Format { get; set; }
        public string? Key { get; set; }
        public ObfuscationStatistics? Statistics { get; set; }
        public int SymbolMapEntries { get; set; }
        public int SkippedItems { get; set; }
        public int Warnings { get; set; }
        public FileFingerprint? Output { get; set; }
        public List<FileFingerprint>? Companions { get; set; }
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
                file.Warnings < 0 ||
                file.Output is not { IsWellFormed: true } ||
                file.Companions is null ||
                file.Companions.Exists(static c => c is not { IsWellFormed: true }))
            {
                return null;
            }

            return new Entry(
                file.Key.Trim(),
                new CachedRun(file.Statistics, file.SymbolMapEntries, file.SkippedItems, file.Warnings),
                file.Output,
                file.Companions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes an entry for an output the caller produced elsewhere, fingerprinting the file as it is now. It carries
    /// no run record (zero statistics and counts), so a hit on it says nothing about the run. Returns false when the
    /// output, or a file a packed output needs, is not there to fingerprint. The obfuscation service writes through
    /// <see cref="TryWrite(string, string, CachedRun, PackingSettings)"/> instead.
    /// </summary>
    public static bool TryWrite(string inputPath, string outputPath, ObfySettings settings)
    {
        try
        {
            return TryWrite(outputPath, ComputeKey(inputPath, settings), CachedRun.Empty, settings.Packing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the entry for an output that is final: after signing, the post-save patches and packing, because the
    /// fingerprint is of the files as they are on disk now. Returns false when the cache could not be written, or when
    /// a file it would vouch for is not there. <paramref name="writeText"/> is how the text reaches a file (it is handed
    /// the temporary file's path, never the cache file's); tests replace it to make the write fail halfway.
    /// </summary>
    internal static bool TryWrite(
        string outputPath, string key, CachedRun run, PackingSettings packing, Action<string, string>? writeText = null)
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
                Warnings = run.Warnings,
                Output = FileFingerprint.Of(outputPath),
                Companions = PackingApplicator.SidecarPaths(outputPath, packing).Select(FileFingerprint.Of).ToList()
            };
            WriteBesideThenReplace(
                CachePath(outputPath), JsonSerializer.Serialize(file, EntryJsonOptions), writeText ?? WriteText);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes <paramref name="text"/> to a temporary file beside <paramref name="cachePath"/> and moves it over the cache
    /// file in one step. The temporary file is removed on every path out, so a failure leaves the cache file as it was.
    /// </summary>
    private static void WriteBesideThenReplace(string cachePath, string text, Action<string, string> writeText)
    {
        var fullPath = Path.GetFullPath(cachePath);
        var tempPath = Path.Combine(
            Path.GetDirectoryName(fullPath) ?? string.Empty,
            $"{Path.GetFileName(fullPath)}.obfy-{Guid.NewGuid():N}.tmp");

        try
        {
            writeText(tempPath, text);
            if (OperatingSystem.IsWindows() && File.Exists(fullPath))
                File.Replace(tempPath, fullPath, destinationBackupFileName: null);
            else
                File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    private static void WriteText(string path, string text) => File.WriteAllText(path, text);

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file is only clutter; the caller has its own result to report.
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
/// <param name="Warnings">How many warnings it raised that a hit does not derive again. Runtime-profile gating and the
/// lines <see cref="PackingApplicator"/> adds for a packed output (the packed path, and the .dll advisory) are derived
/// from the settings and the output name, so a hit repeats them and they are not counted.</param>
internal sealed record CachedRun(ObfuscationStatistics Statistics, int SymbolMapEntries, int SkippedItems, int Warnings)
{
    /// <summary>A record with no information: zero statistics and counts.</summary>
    public static CachedRun Empty => new(new ObfuscationStatistics(), 0, 0, 0);
}

/// <summary>
/// Length and SHA-256 of a file. A hash names nothing, so it may be stored beside the output. Not secret: it only has
/// to tell the file the cached run wrote from any other file at the same path.
/// </summary>
internal sealed class FileFingerprint
{
    public long Length { get; set; }
    public string? Sha256 { get; set; }

    /// <summary>A stored value that can be compared: a length and a 64-digit hex hash.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsWellFormed =>
        Length >= 0 && Sha256 is { Length: 64 } hash && hash.All(static c => char.IsAsciiHexDigit(c));

    /// <summary>
    /// The fingerprint of the file as it is now. Throws IOException when it cannot be read. The file is opened for
    /// reading while others may hold it open for writing, or delete it: a scanner or an indexer with the output open
    /// has not changed it, and a hit must not turn into a rebuild that then cannot save. A file another process holds
    /// exclusively cannot be read, and that is a miss for the caller.
    /// </summary>
    public static FileFingerprint Of(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new FileFingerprint { Length = stream.Length, Sha256 = Convert.ToHexString(SHA256.HashData(stream)) };
    }

    /// <summary>True when the file at <paramref name="path"/> has this length and hash. A file that cannot be read does not match.</summary>
    public bool Matches(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != Length)
                return false;

            return string.Equals(Of(path).Sha256, Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

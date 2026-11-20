using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using dnlib.DotNet;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// File-backed cache so CI can skip obfuscation when Obfy version, input bytes, and settings
/// are unchanged. The key also covers the signing key file and every dependency DLL that
/// embedding would pack, so a rotated key or a rebuilt sibling is a miss.
/// A locked or corrupt <c>{output}.obfycache</c> is a miss, not a failed run.
/// A packing hit also requires the native sibling <c>{name}.runtimeconfig.json</c> (win-x64)
/// or the portable launcher files. <see cref="ComputeKey"/> hex is the native overlay AES-256
/// key and must remain 32 bytes.
/// </summary>
public static class IncrementalCache
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string CachePath(string outputPath) => outputPath + ".obfycache";

    public static string ComputeKey(string inputPath, ObfySettings settings)
    {
        var input = File.ReadAllBytes(inputPath);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var version = typeof(IncrementalCache).Assembly.GetName().Version?.ToString() ?? "0";
        var stamp = Encoding.UTF8.GetBytes("obfy-cache|" + version + "|");
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

            var path = Path.Combine(inputDir, fileName);
            if (!File.Exists(path))
                continue;

            hasher.AppendData(Encoding.UTF8.GetBytes("|dep|" + fileName + "|"));
            hasher.AppendData(File.ReadAllBytes(path));
        }
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

    /// <summary>
    /// 32-byte AES-256 key for the native overlay: hex-decoded <see cref="ComputeKey"/> with
    /// no second hash.
    /// </summary>
    public static byte[] ToAes256Key(string inputPath, ObfySettings settings) =>
        Convert.FromHexString(ComputeKey(inputPath, settings));

    public static bool TryHit(string inputPath, string outputPath, ObfySettings settings)
    {
        try
        {
            if (!File.Exists(outputPath) || !File.Exists(CachePath(outputPath)))
                return false;
            if (settings.Packing.Enabled)
            {
                if (settings.Packing.IsPortable)
                {
                    if (!File.Exists(ManagedLauncherPacker.LauncherPathFor(outputPath)) ||
                        !File.Exists(ManagedLauncherPacker.RuntimeConfigPathFor(outputPath)))
                        return false;
                }
                else if (!File.Exists(NativePacker.RuntimeConfigPathFor(outputPath)) ||
                         !NativePacker.LooksLikePackedHost(outputPath))
                    return false;
            }
            var expected = ComputeKey(inputPath, settings);
            var actual = File.ReadAllText(CachePath(outputPath)).Trim();
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool TryWrite(string inputPath, string outputPath, ObfySettings settings)
    {
        try
        {
            File.WriteAllText(CachePath(outputPath), ComputeKey(inputPath, settings));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Write(string inputPath, string outputPath, ObfySettings settings) =>
        TryWrite(inputPath, outputPath, settings);
}

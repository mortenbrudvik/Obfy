using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// File-backed cache so CI can skip obfuscation when Obfy version, input bytes, and settings
/// are unchanged. A locked or corrupt <c>{output}.obfycache</c> is a miss, not a failed run.
/// </summary>
public static class IncrementalCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
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
        var payload = new byte[stamp.Length + input.Length + jsonBytes.Length];
        Buffer.BlockCopy(stamp, 0, payload, 0, stamp.Length);
        Buffer.BlockCopy(input, 0, payload, stamp.Length, input.Length);
        Buffer.BlockCopy(jsonBytes, 0, payload, stamp.Length + input.Length, jsonBytes.Length);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    public static bool TryHit(string inputPath, string outputPath, ObfySettings settings)
    {
        try
        {
            if (!File.Exists(outputPath) || !File.Exists(CachePath(outputPath)))
                return false;
            if (settings.Packing.Enabled &&
                (!File.Exists(ManagedLauncherPacker.LauncherPathFor(outputPath)) ||
                 !File.Exists(ManagedLauncherPacker.RuntimeConfigPathFor(outputPath))))
                return false;
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

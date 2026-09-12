using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// File-backed cache so CI can skip obfuscation when input and settings are unchanged.
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
        var payload = new byte[input.Length + Encoding.UTF8.GetByteCount(json)];
        Buffer.BlockCopy(input, 0, payload, 0, input.Length);
        Encoding.UTF8.GetBytes(json, 0, json.Length, payload, input.Length);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    public static bool TryHit(string inputPath, string outputPath, ObfySettings settings)
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

    public static void Write(string inputPath, string outputPath, ObfySettings settings)
    {
        File.WriteAllText(CachePath(outputPath), ComputeKey(inputPath, settings));
    }
}

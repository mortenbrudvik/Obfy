using System;
using System.IO;
using System.Text;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Builds <c>obfy</c> CLI arguments and parses summary lines from CLI output.
/// </summary>
public static class CliArgumentBuilder
{
    public static string Build(
        string assemblyPath,
        string? outputPath,
        string? configPath,
        ObfuscationLevel? level = null,
        bool generateSymbolMap = false)
    {
        var sb = new StringBuilder();
        sb.Append($"\"{assemblyPath}\"");

        if (!string.IsNullOrEmpty(outputPath))
        {
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
                sb.Append($" -o \"{outputDir}\"");
        }

        if (!string.IsNullOrEmpty(configPath))
            sb.Append($" -c \"{configPath}\"");
        else if (level is not null)
            sb.Append($" -l {level.Value.ToString().ToLowerInvariant()}");

        if (generateSymbolMap)
        {
            var assemblyDir = Path.GetDirectoryName(assemblyPath);
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            if (!string.IsNullOrEmpty(assemblyDir) && !string.IsNullOrEmpty(assemblyName))
            {
                var mapPath = Path.Combine(assemblyDir, assemblyName + ".map.json");
                sb.Append($" --map \"{mapPath}\"");
            }
        }

        return sb.ToString();
    }

    public static ObfuscationStatistics ParseStatistics(string output)
    {
        var stats = new ObfuscationStatistics();

        foreach (var line in output.Split('\n'))
        {
            if (line.IndexOf("strings encrypted", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var parts = line.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var count))
                {
                    stats.StringsEncrypted = count;
                    stats.TotalTransformations += count;
                }
            }
            else if (line.IndexOf("symbols renamed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var parts = line.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var count))
                {
                    stats.SymbolsRenamed = count;
                    stats.TotalTransformations += count;
                }
            }
            else if (line.IndexOf("transformations", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var parts = line.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var count))
                    stats.TotalTransformations = count;
            }
        }

        return stats;
    }
}

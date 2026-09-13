using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Builds <c>obfy</c> CLI argv and parses summary lines from CLI output.
/// </summary>
public static class CliArgumentBuilder
{
    public static IReadOnlyList<string> Build(
        string assemblyPath,
        string? outputPath,
        string? configPath,
        ObfuscationLevel? level = null,
        bool generateSymbolMap = false)
    {
        var args = new List<string> { assemblyPath };

        if (!string.IsNullOrEmpty(outputPath))
        {
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                args.Add("-o");
                args.Add(outputDir);
            }
        }

        if (!string.IsNullOrEmpty(configPath))
        {
            args.Add("-c");
            args.Add(configPath!);
        }
        else if (level is not null)
        {
            args.Add("-l");
            args.Add(level.Value.ToString().ToLowerInvariant());
        }

        if (generateSymbolMap)
        {
            var assemblyDir = Path.GetDirectoryName(assemblyPath);
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            if (!string.IsNullOrEmpty(assemblyDir) && !string.IsNullOrEmpty(assemblyName))
            {
                args.Add("--map");
                args.Add(Path.Combine(assemblyDir, assemblyName + ".map.json"));
            }
        }

        return args;
    }

    public static string ToCommandLine(IReadOnlyList<string> args)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(Quote(args[i]));
        }

        return sb.ToString();
    }

    internal static string Quote(string value)
    {
        if (value.Length > 0 && value[0] == '-')
            return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
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

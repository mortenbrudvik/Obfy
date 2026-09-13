using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Builds <c>obfy</c> CLI argv and parses summary lines from CLI output.
/// </summary>
public static class CliArgumentBuilder
{
    private static readonly Regex LastInteger = new(@"-?\d+", RegexOptions.Compiled);

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
            var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
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
            if (string.IsNullOrEmpty(assemblyDir) || string.IsNullOrEmpty(assemblyName))
            {
                throw new ArgumentException(
                    "Cannot build --map path; assembly path must include a directory and file name.",
                    nameof(assemblyPath));
            }

            args.Add("--map");
            args.Add(Path.Combine(assemblyDir, assemblyName + ".map.json"));
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

    /// <summary>
    /// Parses CLI summary output. Accepts colon lines (<c>Strings encrypted: 4</c>) and
    /// Spectre table rows (<c>│ Strings Encrypted │ 42 │</c>).
    /// </summary>
    public static ObfuscationStatistics ParseStatistics(string output)
    {
        var stats = new ObfuscationStatistics();
        var sawExplicitTotal = false;

        foreach (var raw in output.Split('\n'))
        {
            var line = StripMarkup(raw);
            if (line.IndexOf("strings encrypted", StringComparison.OrdinalIgnoreCase) >= 0
                && TryLastInt(line, out var strings))
            {
                stats.StringsEncrypted = strings;
            }
            else if (line.IndexOf("symbols renamed", StringComparison.OrdinalIgnoreCase) >= 0
                && TryLastInt(line, out var symbols))
            {
                stats.SymbolsRenamed = symbols;
            }
            else if (line.IndexOf("transformations", StringComparison.OrdinalIgnoreCase) >= 0
                && TryLastInt(line, out var total))
            {
                stats.TotalTransformations = total;
                sawExplicitTotal = true;
            }
        }

        if (!sawExplicitTotal)
            stats.TotalTransformations = stats.StringsEncrypted + stats.SymbolsRenamed;

        return stats;
    }

    private static string StripMarkup(string line)
    {
        var result = line;
        while (true)
        {
            var start = result.IndexOf('[');
            if (start < 0) break;
            var end = result.IndexOf(']', start + 1);
            if (end < 0) break;
            result = result.Remove(start, end - start + 1);
        }
        return result;
    }

    private static bool TryLastInt(string line, out int value)
    {
        value = 0;
        Match? last = null;
        foreach (Match match in LastInteger.Matches(line))
            last = match;
        if (last == null)
            return false;
        if (!int.TryParse(last.Value, out value))
            return false;
        if (value < 0)
            value = 0;
        return true;
    }
}

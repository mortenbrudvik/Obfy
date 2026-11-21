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
    private static readonly Regex AnsiEscape = new("\u001b\\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

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

        // CommandLineToArgvW treats a backslash before a quote as an escape.
        // A trailing backslash must be doubled or "C:\" swallows the closing quote.
        var sb = new StringBuilder();
        sb.Append('"');
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', (slashes * 2) + 1);
                sb.Append('"');
                slashes = 0;
                continue;
            }

            if (slashes > 0)
            {
                sb.Append('\\', slashes);
                slashes = 0;
            }

            sb.Append(c);
        }

        if (slashes > 0)
            sb.Append('\\', slashes * 2);
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Parses CLI summary output. Accepts colon lines (<c>Strings encrypted: 4</c>) and the Spectre table
    /// the CLI prints (<c>│ Strings Encrypted │ 42 │</c>, or <c>| ... |</c> when stdout is redirected).
    /// The CLI reports renames as separate "Types Renamed" and "Methods Renamed" rows and never prints a
    /// "Symbols renamed" row, so <see cref="ObfuscationStatistics.SymbolsRenamed"/> is their sum.
    /// </summary>
    public static ObfuscationStatistics ParseStatistics(string output)
    {
        var stats = new ObfuscationStatistics();
        var sawExplicitTotal = false;

        foreach (var raw in output.Split('\n'))
        {
            var line = StripMarkup(StripAnsi(raw));
            if (line.IndexOf("strings encrypted", StringComparison.OrdinalIgnoreCase) >= 0
                && TryLastInt(line, out var strings))
            {
                stats.StringsEncrypted = strings;
            }
            else if (line.IndexOf("symbols renamed", StringComparison.OrdinalIgnoreCase) >= 0
                && TryLastInt(line, out var symbols))
            {
                stats.SymbolsRenamed += symbols;
            }
            else if ((line.IndexOf("types renamed", StringComparison.OrdinalIgnoreCase) >= 0
                      || line.IndexOf("methods renamed", StringComparison.OrdinalIgnoreCase) >= 0)
                && TryLastInt(line, out var renamed))
            {
                stats.SymbolsRenamed += renamed;
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

    /// <summary>
    /// Picks the text to show for a failed run. The CLI writes its errors to stdout ("Error: ..."), not stderr,
    /// so stderr is used when it has anything, then the CLI's own error lines, then the last line it printed.
    /// </summary>
    public static string ExtractErrorMessage(string? stdout, string? stderr, int exitCode)
    {
        var fromStderr = StripAnsi(stderr ?? string.Empty).Trim();
        if (fromStderr.Length > 0)
            return fromStderr;

        var errors = new List<string>();
        string? lastLine = null;
        foreach (var raw in (stdout ?? string.Empty).Split('\n'))
        {
            var line = StripAnsi(raw).Trim();
            if (line.Length == 0)
                continue;

            lastLine = line;
            if (line.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            {
                var text = line.Substring("Error:".Length).Trim();
                if (text.Length > 0)
                    errors.Add(text);
            }
        }

        if (errors.Count > 0)
            return string.Join(Environment.NewLine, errors);

        return lastLine ?? $"Process exited with code {exitCode}";
    }

    private static string StripAnsi(string text) => AnsiEscape.Replace(text, string.Empty);

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

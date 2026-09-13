using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Builds <c>obfy</c> CLI arguments and parses summary lines from CLI output.
/// </summary>
public static class CliArgumentBuilder
{
    private static readonly Regex LastInteger = new(@"-?\d+", RegexOptions.Compiled);

    public static string Build(
        string assemblyPath,
        string? outputPath,
        ObfySettings settings,
        bool generateSymbolMap = false)
    {
        var sb = new StringBuilder();
        sb.Append($"\"{assemblyPath}\"");

        if (!string.IsNullOrEmpty(outputPath))
        {
            var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(outputDir))
                sb.Append($" -o \"{outputDir}\"");
        }

        var level = MatchesPreset(settings) ? settings.Level : ObfuscationLevel.Custom;
        sb.Append($" -l {level.ToString().ToLowerInvariant()}");

        if (level == ObfuscationLevel.Custom)
        {
            if (!settings.StringEncryption) sb.Append(" --no-string-encryption");
            if (!settings.SymbolRenaming) sb.Append(" --no-symbol-renaming");
            if (settings.ControlFlow) sb.Append(" --control-flow");
            else sb.Append(" --no-control-flow");
            if (settings.AntiDebug) sb.Append(" --anti-debug");
            if (settings.AntiDump) sb.Append(" --anti-dump");
            if (settings.ReferenceProxy) sb.Append(" --reference-proxy");
            if (settings.AntiTamper) sb.Append(" --anti-tamper");
            if (settings.AntiDecompiler) sb.Append(" --anti-decompiler");
            if (settings.ConstantEncryption) sb.Append(" --encrypt-constants");
            if (settings.ResourceEncryption) sb.Append(" --encrypt-resources");
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

            var mapPath = Path.Combine(assemblyDir, assemblyName + ".map.json");
            sb.Append($" --map \"{mapPath}\"");
        }

        return sb.ToString();
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

    private static bool MatchesPreset(ObfySettings settings)
    {
        if (settings.Level == ObfuscationLevel.Custom)
            return false;

        var preset = ObfySettings.ForLevel(settings.Level);
        return settings.StringEncryption == preset.StringEncryption
            && settings.SymbolRenaming == preset.SymbolRenaming
            && settings.ControlFlow == preset.ControlFlow
            && settings.AntiDebug == preset.AntiDebug
            && settings.AntiDump == preset.AntiDump
            && settings.ReferenceProxy == preset.ReferenceProxy
            && settings.AntiTamper == preset.AntiTamper
            && settings.AntiDecompiler == preset.AntiDecompiler
            && settings.ConstantEncryption == preset.ConstantEncryption
            && settings.ResourceEncryption == preset.ResourceEncryption;
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

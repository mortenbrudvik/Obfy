using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// Result of an obfuscation operation
/// </summary>
public class ObfuscationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputPath { get; set; }
    public ObfuscationStatistics Statistics { get; set; } = new();
    public TimeSpan ElapsedTime { get; set; }

    public static ObfuscationResult Failure(string inputPath, string message, Exception? ex = null)
    {
        return new ObfuscationResult
        {
            Success = false,
            ErrorMessage = message,
            OutputPath = inputPath
        };
    }
}

/// <summary>
/// Statistics from obfuscation
/// </summary>
public class ObfuscationStatistics
{
    public int TotalTransformations { get; set; }
    public int StringsEncrypted { get; set; }
    public int SymbolsRenamed { get; set; }
}

/// <summary>
/// Settings for obfuscation level
/// </summary>
public enum ObfuscationLevel
{
    Minimal,
    Standard,
    Aggressive,
    Custom
}

/// <summary>
/// Obfuscation settings model (subset for VS extension)
/// </summary>
public class ObfySettings
{
    public ObfuscationLevel Level { get; set; } = ObfuscationLevel.Standard;
    public bool PostBuildEnabled { get; set; }
    public bool AntiDebug { get; set; }
    public bool AntiDump { get; set; }
    public bool ReferenceProxy { get; set; }
    public bool AntiTamper { get; set; }
    public bool AntiDecompiler { get; set; }
    public bool StringEncryption { get; set; } = true;
    public bool ControlFlow { get; set; }
    public bool SymbolRenaming { get; set; } = true;
    public bool ConstantEncryption { get; set; }
    public bool ResourceEncryption { get; set; }

    public static ObfySettings ForLevel(ObfuscationLevel level)
    {
        var settings = new ObfySettings { Level = level };

        switch (level)
        {
            case ObfuscationLevel.Minimal:
                settings.StringEncryption = false;
                settings.SymbolRenaming = true;
                settings.ControlFlow = false;
                settings.AntiDebug = false;
                settings.AntiDump = false;
                settings.ReferenceProxy = false;
                settings.AntiTamper = false;
                settings.AntiDecompiler = false;
                settings.ConstantEncryption = false;
                settings.ResourceEncryption = false;
                break;
            case ObfuscationLevel.Standard:
                settings.StringEncryption = true;
                settings.SymbolRenaming = true;
                settings.ControlFlow = false;
                settings.AntiDebug = false;
                settings.AntiDump = false;
                settings.ReferenceProxy = false;
                settings.AntiTamper = false;
                settings.AntiDecompiler = false;
                settings.ConstantEncryption = false;
                settings.ResourceEncryption = false;
                break;
            case ObfuscationLevel.Aggressive:
                settings.StringEncryption = true;
                settings.SymbolRenaming = true;
                settings.ControlFlow = true;
                settings.AntiDebug = true;
                settings.AntiDump = true;
                settings.ReferenceProxy = true;
                settings.AntiTamper = true;
                settings.AntiDecompiler = true;
                settings.ConstantEncryption = true;
                settings.ResourceEncryption = true;
                break;
        }

        return settings;
    }
}

/// <summary>
/// Wrapper that invokes the Obfy CLI to perform obfuscation
/// </summary>
public class ObfuscationServiceWrapper : IObfuscationServiceWrapper
{
    private readonly IOutputService _outputService;
    private string? _cliPath;

    public ObfuscationServiceWrapper(IOutputService outputService)
    {
        _outputService = outputService;
    }

    public async Task<ObfuscationResult> ObfuscateAsync(
        string assemblyPath,
        string? outputPath,
        ObfySettings settings,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Find the CLI executable
            var cliPath = FindCliPath();
            if (string.IsNullOrEmpty(cliPath))
            {
                return ObfuscationResult.Failure(assemblyPath,
                    "Obfy CLI not found. Please ensure Obfy is installed and in PATH.");
            }

            // Build command line arguments
            var args = BuildArguments(assemblyPath, outputPath, settings);

            _outputService.Info($"Executing: obfy {args}");

            // Run the CLI
            var result = await RunCliAsync(cliPath!, args, cancellationToken);

            stopwatch.Stop();
            result.ElapsedTime = stopwatch.Elapsed;

            if (result.Success)
            {
                result.OutputPath = outputPath ?? assemblyPath;
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return ObfuscationResult.Failure(assemblyPath, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _outputService.Error($"CLI execution failed: {ex.Message}");
            return ObfuscationResult.Failure(assemblyPath, ex.Message, ex);
        }
    }

    private string? FindCliPath()
    {
        if (_cliPath != null && File.Exists(_cliPath))
        {
            return _cliPath;
        }

        // Global dotnet tool install was removed; search it last as a fallback only.
        var possiblePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Obfy", "obfy.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Obfy", "obfy.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "obfy.exe"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _cliPath = path;
                return path;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var exePath = Path.Combine(dir, "obfy.exe");
            if (File.Exists(exePath))
            {
                _cliPath = exePath;
                return exePath;
            }
        }

        var dotnetTool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "obfy.exe");
        if (File.Exists(dotnetTool))
        {
            _cliPath = dotnetTool;
            return dotnetTool;
        }

        return null;
    }

    private static string BuildArguments(string assemblyPath, string? outputPath, ObfySettings settings)
    {
        var sb = new StringBuilder();

        // Input file (quoted for spaces)
        sb.Append($"\"{assemblyPath}\"");

        // Output directory
        if (!string.IsNullOrEmpty(outputPath))
        {
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                sb.Append($" -o \"{outputDir}\"");
            }
        }

        // Level
        sb.Append($" -l {settings.Level.ToString().ToLowerInvariant()}");

        // Individual toggles for custom level
        if (settings.Level == ObfuscationLevel.Custom)
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

        if (ObfyPackage.Options?.GenerateSymbolMap == true)
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

    private async Task<ObfuscationResult> RunCliAsync(string cliPath, string args, CancellationToken cancellationToken)
    {
        var result = new ObfuscationResult();
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
                _outputService.Info(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorBuilder.AppendLine(e.Data);
                _outputService.Error(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for process to complete
        await Task.Run(() =>
        {
            while (!process.WaitForExit(100))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Win32Exception)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    throw new OperationCanceledException();
                }
            }
        }, cancellationToken);

        result.Success = process.ExitCode == 0;

        if (!result.Success)
        {
            result.ErrorMessage = errorBuilder.Length > 0
                ? errorBuilder.ToString().Trim()
                : $"Process exited with code {process.ExitCode}";
        }
        else
        {
            // Try to parse statistics from output
            result.Statistics = ParseStatistics(outputBuilder.ToString());
        }

        return result;
    }

    private static ObfuscationStatistics ParseStatistics(string output)
    {
        var stats = new ObfuscationStatistics();

        // Simple parsing of CLI output for statistics
        // Example: "Strings encrypted: 42"
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("strings encrypted", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var count))
                {
                    stats.StringsEncrypted = count;
                    stats.TotalTransformations += count;
                }
            }
            else if (line.Contains("symbols renamed", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var count))
                {
                    stats.SymbolsRenamed = count;
                    stats.TotalTransformations += count;
                }
            }
            else if (line.Contains("transformations", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var count))
                {
                    stats.TotalTransformations = count;
                }
            }
        }

        return stats;
    }
}

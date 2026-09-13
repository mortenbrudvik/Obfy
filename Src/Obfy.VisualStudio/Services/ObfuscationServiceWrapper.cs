using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Obfy.VisualStudio;

namespace Obfy.VisualStudio.Services;

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
        CancellationToken cancellationToken = default,
        string? configPath = null)
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

            var generateMap = ObfyPackage.Options?.GenerateSymbolMap == true;
            var inPlace = !string.IsNullOrEmpty(outputPath) &&
                          string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(assemblyPath), StringComparison.OrdinalIgnoreCase);
            var cliOutput = outputPath;
            string? tempDir = null;
            if (inPlace)
            {
                tempDir = Path.Combine(Path.GetTempPath(), "obfy_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                cliOutput = Path.Combine(tempDir, Path.GetFileName(assemblyPath));
            }

            var args = CliArgumentBuilder.Build(
                assemblyPath,
                cliOutput,
                configPath,
                configPath is null ? settings.Level : null,
                generateMap);

            _outputService.Info($"Executing: obfy {args}");

            // Run the CLI
            var result = await RunCliAsync(cliPath!, args, cancellationToken);

            stopwatch.Stop();
            result.ElapsedTime = stopwatch.Elapsed;

            if (result.Success)
            {
                if (inPlace && cliOutput is not null && File.Exists(cliOutput))
                {
                    File.Copy(cliOutput, assemblyPath, overwrite: true);
                    TryDeleteDirectory(tempDir);
                }
                result.OutputPath = outputPath ?? assemblyPath;
            }
            else
            {
                TryDeleteDirectory(tempDir);
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
            result.Statistics = CliArgumentBuilder.ParseStatistics(outputBuilder.ToString());
        }

        return result;
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

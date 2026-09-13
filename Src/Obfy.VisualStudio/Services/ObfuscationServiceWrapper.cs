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
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var cliPath = FindCliPath();
            if (string.IsNullOrEmpty(cliPath))
            {
                return ObfuscationResult.Failure(assemblyPath,
                    "Obfy CLI not found. Please ensure Obfy is installed and in PATH.");
            }

            var generateMap = ObfyPackage.Options?.GenerateSymbolMap == true;
            var args = CliArgumentBuilder.Build(assemblyPath, outputPath, settings, generateMap);

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
            _outputService.Error($"CLI execution failed: {ex}");
            return ObfuscationResult.Failure(assemblyPath, ex.Message, ex);
        }
    }

    private string? FindCliPath()
    {
        if (_cliPath != null && File.Exists(_cliPath))
        {
            return _cliPath;
        }

        var found = ObfyCliLocator.Find(ObfyCliLocator.DefaultSearchDirectories());
        if (found != null)
            _cliPath = found;
        return found;
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

        using (cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Win32Exception ex)
            {
                _outputService.Warning($"Could not stop Obfy CLI: {ex.Message}");
            }
            catch (InvalidOperationException)
            {
            }
        }))
        {
            await Task.Run(() => process.WaitForExit(), CancellationToken.None).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

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
}

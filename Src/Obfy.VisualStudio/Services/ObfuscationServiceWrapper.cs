using System;
using System.Diagnostics;
using System.IO;
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
        string? tempDir = null;
        var keepTemp = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cliPath = FindCliPath();
            if (string.IsNullOrEmpty(cliPath))
            {
                return ObfuscationResult.Failure(assemblyPath,
                    "Obfy CLI not found. Please ensure Obfy is installed and in PATH.");
            }

            var generateMap = ObfyPackage.Options?.GenerateSymbolMap == true;
            var timeout = CliProcessRunner.ResolveTimeout(ObfyPackage.Options?.RunTimeoutMinutes);
            var inPlace = !string.IsNullOrEmpty(outputPath) &&
                          string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(assemblyPath), StringComparison.OrdinalIgnoreCase);
            var cliOutput = outputPath;
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
            var commandLine = CliArgumentBuilder.ToCommandLine(args);

            _outputService.Info($"Executing: obfy {commandLine}");

            var result = await RunCliAsync(cliPath!, commandLine, assemblyPath, timeout, cancellationToken);

            stopwatch.Stop();
            result.ElapsedTime = stopwatch.Elapsed;

            if (result.Success)
            {
                if (inPlace)
                {
                    try
                    {
                        InPlaceOutputCopy.CopyTempDirectoryToDestination(cliOutput!, assemblyPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        keepTemp = true;
                        _outputService.Error(
                            $"Failed to replace in-place output: {ex.Message}. Temp files kept at {tempDir}");
                        return ObfuscationResult.Failure(assemblyPath, ex.Message, ex);
                    }
                }

                result.OutputPath = outputPath ?? assemblyPath;
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return ObfuscationResult.Cancelled(assemblyPath);
        }
        catch (Exception ex)
        {
            _outputService.Error($"CLI execution failed: {ex}");
            return ObfuscationResult.Failure(assemblyPath, ex.Message, ex);
        }
        finally
        {
            if (!keepTemp)
                TryDeleteDirectory(tempDir);
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

    /// <summary>
    /// Runs the CLI until it exits, the token is cancelled or <paramref name="timeout"/> passes; the process tree is
    /// killed in the last two cases. See <see cref="CliProcessRunner"/>.
    /// </summary>
    private async Task<ObfuscationResult> RunCliAsync(
        string cliPath,
        string args,
        string assemblyPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var run = await CliProcessRunner
            .RunAsync(cliPath, args, timeout, _outputService.Info, _outputService.Error, cancellationToken)
            .ConfigureAwait(false);

        return run.ToObfuscationResult(assemblyPath, timeout);
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

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Autofac;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.ScenarioTests;

/// <summary>
/// Copy an SDK fixture, <c>dotnet build</c>, obfuscate with the real pipeline, run the result.
/// </summary>
internal static class ScenarioHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate Obfy.sln from {AppContext.BaseDirectory}");
    }

    public static string CopyToTemp(string sourceDirectory)
    {
        Directory.Exists(sourceDirectory).ShouldBeTrue(sourceDirectory);
        var dest = Path.Combine(Path.GetTempPath(), "obfy-scen-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(sourceDirectory, dest);
        CopyRepoGlobalJsonIfMissing(dest);
        return dest;
    }

    private static void CopyRepoGlobalJsonIfMissing(string dest)
    {
        var repoGlobalJson = Path.Combine(FindRepoRoot(), "global.json");
        var destGlobalJson = Path.Combine(dest, "global.json");
        if (File.Exists(repoGlobalJson) && !File.Exists(destGlobalJson))
            File.Copy(repoGlobalJson, destGlobalJson);
    }

    internal const int DefaultBuildTimeoutMs = 180_000;
    internal const int DefaultPublishTimeoutMs = 300_000;
    internal const int DefaultRunTimeoutMs = 60_000;
    internal const string TimeoutFactorVariable = "OBFY_SCENARIO_TIMEOUT_FACTOR";

    public static void DotnetBuild(
        string projectOrSln,
        string configuration = "Release",
        string extraArgs = "",
        int timeoutMs = DefaultBuildTimeoutMs)
    {
        var args = BuildMsBuildArgs($"build \"{projectOrSln}\" -c {configuration} --nologo", extraArgs);
        var result = RunProcess("dotnet", args, Path.GetDirectoryName(projectOrSln)!, timeoutMs);
        result.ExitCode.ShouldBe(0, result.StdOut + Environment.NewLine + result.StdErr);
    }

    public static ProcessResult DotnetPublish(string project, string extraArgs = "", int timeoutMs = DefaultPublishTimeoutMs)
    {
        var args = BuildMsBuildArgs($"publish \"{project}\" --nologo", extraArgs);
        return RunProcess("dotnet", args, Path.GetDirectoryName(project)!, timeoutMs);
    }

    private static string BuildMsBuildArgs(string baseArgs, string extraArgs) =>
        string.IsNullOrWhiteSpace(extraArgs)
            ? $"{baseArgs} -p:UseSharedCompilation=false"
            : $"{baseArgs} {extraArgs.Trim()} -p:UseSharedCompilation=false";

    public static ObfySettings LoadSettings(string jsonPath)
    {
        File.Exists(jsonPath).ShouldBeTrue(jsonPath);
        var json = File.ReadAllText(jsonPath);
        var settings = JsonSerializer.Deserialize<ObfySettings>(json, JsonOptions)
                       ?? throw new InvalidOperationException($"Config deserialized to null: {jsonPath}");
        settings.Level = ObfuscationLevel.Custom;
        return settings;
    }

    public static async Task<ObfuscationResult> ObfuscateAsync(string inputPath, string outputPath, ObfySettings settings)
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        await using var container = builder.Build();
        var service = container.Resolve<IObfuscationService>();

        var result = await service.ObfuscateAsync(inputPath, outputPath, settings);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        return result;
    }

    public static async Task ObfuscateClosedSetAsync(
        IReadOnlyList<string> assemblyPaths,
        string outputDirectory,
        ObfySettings settings,
        bool forcePreservePublic = false)
    {
        Directory.CreateDirectory(outputDirectory);
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        await using var container = builder.Build();
        var service = container.Resolve<IObfuscationService>();
        var inputs = assemblyPaths.Select(path => ClosedSetInput.From(path)).ToList();
        var result = await service.ObfuscateClosedSetAsync(inputs, outputDirectory, settings, forcePreservePublic);
        result.Success.ShouldBeTrue(result.ErrorMessage);
    }

    public static string StageBuildOutput(string buildOutputDirectory, string assemblyFileName)
    {
        Directory.Exists(buildOutputDirectory).ShouldBeTrue(buildOutputDirectory);
        var stage = Path.Combine(Path.GetDirectoryName(buildOutputDirectory)!, "obfy-stage-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(buildOutputDirectory, stage);
        return Path.Combine(stage, assemblyFileName);
    }

    public static ProcessResult RunDotnet(string assemblyPath, string extraArgs = "", int timeoutMs = DefaultRunTimeoutMs)
    {
        File.Exists(assemblyPath).ShouldBeTrue(assemblyPath);
        var args = string.IsNullOrWhiteSpace(extraArgs)
            ? $"\"{assemblyPath}\""
            : $"\"{assemblyPath}\" {extraArgs}";
        return RunProcess("dotnet", args, Path.GetDirectoryName(assemblyPath)!, timeoutMs);
    }

    private static readonly TimeSpan StreamDrainTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StreamDrainAfterKillTimeout = TimeSpan.FromSeconds(5);

    public static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory, int timeoutMs)
    {
        var start = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        SanitizeChildEnvironment(start.Environment, fileName, arguments);
        timeoutMs = ScaleTimeout(timeoutMs);

        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            TryKillTree(process);
            ObserveFault(stdoutTask);
            ObserveFault(stderrTask);
            throw new TimeoutException($"Timed out: {fileName} {arguments}");
        }

        // Wait up to 15s for stdout/stderr EOF; if a lingering child process still holds an
        // inherited pipe handle after the main process exits, kill the tree and drain for 5s.
        var drainTask = Task.WhenAll(stdoutTask, stderrTask);
        if (!WaitForTask(drainTask, StreamDrainTimeout))
        {
            TryKillTree(process);
            WaitForTask(drainTask, StreamDrainAfterKillTimeout);
        }

        ObserveFault(stdoutTask);
        ObserveFault(stderrTask);
        var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.GetAwaiter().GetResult() : string.Empty;
        var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.GetAwaiter().GetResult() : string.Empty;
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static void TryKillTree(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
    }

    private static bool WaitForTask(Task task, TimeSpan timeout)
    {
        try
        {
            return task.Wait(timeout);
        }
        catch (AggregateException)
        {
            return true;
        }
    }

    private static void ObserveFault(Task task)
    {
        if (task.IsFaulted)
        {
            _ = task.Exception;
        }
        else if (!task.IsCompleted)
        {
            _ = task.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    /// <summary>
    /// <c>OBFY_SCENARIO_TIMEOUT_FACTOR</c> from 2 to 10 stretches every scenario timeout.
    /// </summary>
    internal static int ScaleTimeout(int timeoutMs) =>
        ScaleTimeout(timeoutMs, Environment.GetEnvironmentVariable(TimeoutFactorVariable));

    internal static int ScaleTimeout(int timeoutMs, string? factor)
    {
        if (!int.TryParse(factor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var multiplier) ||
            multiplier <= 1)
            return timeoutMs;
        if (multiplier > 10)
            multiplier = 10;
        var scaled = (long)timeoutMs * multiplier;
        return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
    }

    private static readonly HashSet<string> DotnetParentMsBuildEnvVars = new(StringComparer.OrdinalIgnoreCase)
    {
        "MSBUILD_EXE_PATH",
        "MSBuildSDKsPath",
        "MSBuildExtensionsPath",
        "MSBuildUserExtensionsPath",
        "MSBuildLoadMicrosoftTargetsReadOnly",
        "MSBuildLoadReadOnly",
        "MSBUILDFAILONDRIVEENUMERATINGWILDCARD",
        "MSBUILDUSESERVER",
        "DOTNET_HOST_PATH",
        "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR",
        "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR",
        "DOTNET_MSBUILD_SDK_RESOLVER_ENABLE_LOG",
        "DOTNET_STARTUP_HOOKS"
    };

    /// <summary>
    /// Isolates child <c>dotnet</c> invocations from MSBuild and SDK-resolver variables inherited from a
    /// parent <c>dotnet test</c> host while keeping SDK root, NuGet cache, and toolchain variables intact.
    /// </summary>
    internal static void SanitizeChildEnvironment(
        IDictionary<string, string?> environment,
        string fileName,
        string arguments)
    {
        ArgumentNullException.ThrowIfNull(environment);

        environment["MSBUILDDISABLENODEREUSE"] = "1";
        environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";

        // vcvars sets Platform=x64, and an SDK-style project then writes to bin/x64/Release.
        // Drop that inherited value only for dotnet when the command does not set Platform itself.
        if (ShouldStripInheritedPlatform(fileName, arguments))
            RemoveEnvironmentKeysCaseInsensitive(environment, key => key.Equals("Platform", StringComparison.OrdinalIgnoreCase));

        if (IsDotnetExecutable(fileName))
            RemoveEnvironmentKeysCaseInsensitive(environment, DotnetParentMsBuildEnvVars.Contains);
    }

    private static void RemoveEnvironmentKeysCaseInsensitive(
        IDictionary<string, string?> environment,
        Func<string, bool> shouldRemove)
    {
        foreach (var key in environment.Keys.Where(shouldRemove).ToArray())
            environment.Remove(key);
    }

    private static bool IsDotnetExecutable(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldStripInheritedPlatform(string fileName, string arguments) =>
        IsDotnetExecutable(fileName) && !PlatformProperty.IsMatch(arguments);

    private static readonly Regex PlatformProperty = new(
        @"(?:^|\s)(?:--property:|-p:|/p:)Platform(?:=|\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(".vs", StringComparison.OrdinalIgnoreCase))
                continue;
            CopyDirectory(dir, Path.Combine(dest, name));
        }
    }

    public static void CopySidecars(string fromDir, string toDir, string? skipAssembly = null)
    {
        var destFull = Path.GetFullPath(toDir);
        var subdirs = Directory.GetDirectories(fromDir)
            .Where(d => !Path.GetFullPath(d).Equals(destFull, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Directory.CreateDirectory(toDir);
        foreach (var file in Directory.GetFiles(fromDir))
        {
            if (skipAssembly != null &&
                string.Equals(Path.GetFileName(file), skipAssembly, StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(toDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in subdirs)
        {
            var name = Path.GetFileName(dir);
            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(".vs", StringComparison.OrdinalIgnoreCase))
                continue;
            CopyDirectory(dir, Path.Combine(toDir, name));
        }
    }

    public static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
    }

    public static string FindObfyCli()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "obfy.dll");
        if (File.Exists(candidate))
            return candidate;

        var repo = FindRepoRoot();
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var path = Path.Combine(repo, "Src", "Obfy.Console", "bin", configuration, "net10.0", "obfy.dll");
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("obfy.dll not found. Build Obfy.Console first.");
    }

    public readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr);
}

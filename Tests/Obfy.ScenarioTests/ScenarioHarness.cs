using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autofac;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
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
        return dest;
    }

    public static void DotnetBuild(
        string projectOrSln,
        string configuration = "Release",
        string extraArgs = "",
        int timeoutMs = 60_000)
    {
        var args = $"build \"{projectOrSln}\" -c {configuration} --nologo";
        if (!string.IsNullOrWhiteSpace(extraArgs))
            args += " " + extraArgs;
        var result = RunProcess("dotnet", args, Path.GetDirectoryName(projectOrSln)!, timeoutMs);
        result.ExitCode.ShouldBe(0, result.StdOut + Environment.NewLine + result.StdErr);
    }

    public static ProcessResult DotnetPublish(string project, string extraArgs = "", int timeoutMs = 180_000)
    {
        var args = $"publish \"{project}\" --nologo {extraArgs}".Trim();
        return RunProcess("dotnet", args, Path.GetDirectoryName(project)!, timeoutMs);
    }

    public static ObfySettings LoadSettings(string jsonPath)
    {
        File.Exists(jsonPath).ShouldBeTrue(jsonPath);
        var json = File.ReadAllText(jsonPath);
        var settings = JsonSerializer.Deserialize<ObfySettings>(json, JsonOptions)
                       ?? throw new InvalidOperationException($"Config deserialized to null: {jsonPath}");
        settings.Level = ObfuscationLevel.Custom;
        return settings;
    }

    public static async Task ObfuscateAsync(string inputPath, string outputPath, ObfySettings settings)
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
    }

    public static string StageBuildOutput(string buildOutputDirectory, string assemblyFileName)
    {
        Directory.Exists(buildOutputDirectory).ShouldBeTrue(buildOutputDirectory);
        var stage = Path.Combine(Path.GetDirectoryName(buildOutputDirectory)!, "obfy-stage-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(buildOutputDirectory, stage);
        return Path.Combine(stage, assemblyFileName);
    }

    public static ProcessResult RunDotnet(string assemblyPath, string extraArgs = "", int timeoutMs = 20_000)
    {
        File.Exists(assemblyPath).ShouldBeTrue(assemblyPath);
        var args = string.IsNullOrWhiteSpace(extraArgs)
            ? $"\"{assemblyPath}\""
            : $"\"{assemblyPath}\" {extraArgs}";
        return RunProcess("dotnet", args, Path.GetDirectoryName(assemblyPath)!, timeoutMs);
    }

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
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";

        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"Timed out: {fileName} {arguments}");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

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

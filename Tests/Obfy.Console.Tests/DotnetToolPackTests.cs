using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.Console.Tests;

public class DotnetToolPackTests : IDisposable
{
    private static readonly HashSet<string> DotnetInheritedMsBuildEnvVars = new(StringComparer.OrdinalIgnoreCase)
    {
        "MSBUILD_EXE_PATH",
        "MSBuildSDKsPath",
        "MSBuildExtensionsPath",
        "MSBuildUserExtensionsPath",
        "MSBuildLoadMicrosoftTargetsReadOnly",
        "MSBuildLoadReadOnly",
        "DOTNET_HOST_PATH",
        "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR",
        "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR",
        "DOTNET_MSBUILD_SDK_RESOLVER_ENABLE_LOG",
        "DOTNET_STARTUP_HOOKS"
    };

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"ObfyToolPack_{Guid.NewGuid():N}");

    public DotnetToolPackTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Pack_ProducesInstallableDotnetToolNamedObfy()
    {
        var solutionDir = FindSolutionDirectory(AppContext.BaseDirectory);
        var csproj = Path.Combine(solutionDir, "Src", "Obfy.Console", "Obfy.Console.csproj");
        var packOut = Path.Combine(_tempDirectory, "nupkg");
        Directory.CreateDirectory(packOut);

        var pack = RunDotnet(solutionDir, ["pack", csproj, "-c", "Release", "-o", packOut, "--nologo"]);
        pack.ExitCode.ShouldBe(0, pack.Output);

        var nupkg = Directory.GetFiles(packOut, "*.nupkg")
            .Single(p => System.Text.RegularExpressions.Regex.IsMatch(
                Path.GetFileName(p),
                @"^Obfy\.\d",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        using (var zip = ZipFile.OpenRead(nupkg))
        {
            zip.GetEntry("tools/net10.0/any/obfy.dll").ShouldNotBeNull();
            zip.GetEntry("tools/net10.0/any/obfy.runtimeconfig.json").ShouldNotBeNull();

            var nuspecEntry = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            using var stream = nuspecEntry.Open();
            var nuspec = XDocument.Load(stream);
            XNamespace ns = nuspec.Root!.Name.Namespace;
            var packageType = nuspec.Root
                .Element(ns + "metadata")?
                .Element(ns + "packageTypes")?
                .Elements(ns + "packageType")
                .Select(e => (string?)e.Attribute("name"))
                .FirstOrDefault();
            packageType.ShouldBe("DotnetTool");
        }

        var version = Path.GetFileNameWithoutExtension(nupkg)["Obfy.".Length..];
        // The package version is the product version in version.json (no -p:Version on this pack).
        version.ShouldBe(RepoVersion.Read(), "the .nupkg file name carries the version from version.json");
        var toolPath = Path.Combine(_tempDirectory, "tool");
        Directory.CreateDirectory(toolPath);

        var tempNugetConfig = Path.Combine(_tempDirectory, "nuget.config");
        var escapedPackOut = SecurityElement.Escape(packOut)
            ?? throw new InvalidOperationException($"Failed to XML-escape path: {packOut}");
        File.WriteAllText(tempNugetConfig, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local-pack" value="{escapedPackOut}" />
              </packageSources>
            </configuration>
            """);

        var install = RunDotnet(_tempDirectory, [
            "tool", "install", "Obfy",
            "--tool-path", toolPath,
            "--configfile", tempNugetConfig,
            "--version", version
        ]);
        install.ExitCode.ShouldBe(0, install.Output);

        var shim = Path.Combine(toolPath, OperatingSystem.IsWindows() ? "obfy.exe" : "obfy");
        File.Exists(shim).ShouldBeTrue(shim);

        var help = Run(shim, ["--help"], toolPath);
        help.ExitCode.ShouldBe(0, help.Output);
        help.Output.ShouldContain("Obfy");
    }

    private static string FindSolutionDirectory(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate Obfy.sln from {startDirectory}");
    }

    private static (int ExitCode, string Output) RunDotnet(string workingDirectory, string[] args)
        => Run("dotnet", args, workingDirectory);

    private static (int ExitCode, string Output) Run(string fileName, string[] args, string workingDirectory)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Disable reusable MSBuild nodes and the MSBuild server so child processes do not outlive the command
        // and hold redirected pipes open.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        if (Path.GetFileNameWithoutExtension(fileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var key in start.Environment.Keys.Where(DotnetInheritedMsBuildEnvVars.Contains).ToArray())
                start.Environment.Remove(key);
        }

        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"{fileName} timed out");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        return (process.ExitCode, stdout + stderr);
    }
}

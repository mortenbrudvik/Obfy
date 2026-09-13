using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using Shouldly;

namespace Obfy.Console.Tests;

public class DotnetToolPackTests : IDisposable
{
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
        var toolPath = Path.Combine(_tempDirectory, "tool");
        Directory.CreateDirectory(toolPath);

        var install = RunDotnet(solutionDir, [
            "tool", "install", "Obfy",
            "--tool-path", toolPath,
            "--add-source", packOut,
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
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000).ShouldBeTrue($"{fileName} timed out");
        return (process.ExitCode, stdout + stderr);
    }
}

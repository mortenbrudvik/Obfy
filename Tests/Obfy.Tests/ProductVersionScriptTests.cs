using System.Diagnostics;
using System.Text.Json;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// build/Get-ProductVersion.ps1 turns version.json into <c>major.minor.patch</c> for the installer build and the
/// release-tag check. It must refuse a file that MSBuild, the MSIX script or the installer could not use.
/// </summary>
public class ProductVersionScriptTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ScriptPath = Path.Combine(RepoRoot, "build", "Get-ProductVersion.ps1");
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"ObfyProductVersion_{Guid.NewGuid():N}");

    public ProductVersionScriptTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void WithoutArguments_PrintsTheVersionInTheRepositoryVersionJson()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "version.json")));
        var root = document.RootElement;
        var expected = $"{root.GetProperty("major").GetInt32()}.{root.GetProperty("minor").GetInt32()}.{root.GetProperty("patch").GetInt32()}";

        var (exitCode, stdout, stderr) = Run();

        exitCode.ShouldBe(0, stderr);
        stdout.Trim().ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(VersionJsonCases.Accepted), MemberType = typeof(VersionJsonCases))]
    public void PrintsMajorMinorPatch(string json, string expected)
    {
        var (exitCode, stdout, stderr) = Run(WriteVersionFile(json));

        exitCode.ShouldBe(0, stderr);
        stdout.Trim().ShouldBe(expected);
    }

    // The same files Directory.Build.props rejects (DirectoryBuildPropsVersionTests): the two readers must agree.
    [Theory]
    [MemberData(nameof(VersionJsonCases.Rejected), MemberType = typeof(VersionJsonCases))]
    public void RefusesAFileThatIsNotExactlyTheThreeIntegerKeys(string json)
    {
        var (exitCode, stdout, stderr) = Run(WriteVersionFile(json));

        exitCode.ShouldNotBe(0);
        stdout.Trim().ShouldBeEmpty();
        stderr.ShouldContain("version.json");
    }

    [Fact]
    public void RefusesAMissingFile()
    {
        var (exitCode, stdout, stderr) = Run(Path.Combine(_scratch, "missing", "version.json"));

        exitCode.ShouldNotBe(0);
        stdout.Trim().ShouldBeEmpty();
        stderr.ShouldContain("version.json");
    }

    private string WriteVersionFile(string json)
    {
        var path = Path.Combine(_scratch, Guid.NewGuid().ToString("N"), "version.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string? versionFile = null)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "-NoProfile", "-File", ScriptPath })
            start.ArgumentList.Add(argument);
        if (versionFile is not null)
        {
            start.ArgumentList.Add("-VersionFile");
            start.ArgumentList.Add(versionFile);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start pwsh");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate Obfy.sln from {AppContext.BaseDirectory}");
    }
}

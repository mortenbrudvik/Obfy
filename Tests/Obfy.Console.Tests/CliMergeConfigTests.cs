using dnlib.DotNet;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// <c>--merge</c> and <c>--internalize</c> against the <c>assemblyMerge</c> section of a <c>-c</c> file: the flags
/// win only when they were typed, and <c>assemblyMerge.enabled</c> does what the docs say it does.
/// </summary>
public class CliMergeConfigTests : IDisposable
{
    private const string NoTransforms = """
        "level": "custom",
        "stringEncryption": { "enabled": false },
        "controlFlow": { "enabled": false },
        "symbolRenaming": { "enabled": false },
        "metadata": { "removeDebugInfo": false }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyMergeCfg_{Guid.NewGuid():N}");

    public CliMergeConfigTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private (string A, string B) CreatePair()
    {
        var a = ConsoleTestAssembly.Create(_root, "A.dll", typeName: "AType");
        var b = ConsoleTestAssembly.Create(_root, "B.dll", typeName: "BType");
        return (a, b);
    }

    private string WriteConfig(string assemblyMergeJson)
    {
        var path = Path.Combine(_root, $"obfy-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ " + NoTransforms + $", \"assemblyMerge\": {assemblyMergeJson} }}");
        return path;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string args)
    {
        using var capture = new AnsiConsoleCapture();
        var exitCode = await CommandLineTestHelpers.InvokeAsync(Program.CreateRootCommand(), args);
        return (exitCode, capture.Text);
    }

    private static bool? IsBTypePublic(string mergedPath)
    {
        using var module = ModuleDefMD.Load(mergedPath);
        return module.Types.FirstOrDefault(t => t.Name == "BType")?.IsPublic;
    }

    [Fact]
    public async Task MergeFlag_WithoutInternalizeFlag_KeepsConfigInternalizeFalse()
    {
        var (a, b) = CreatePair();
        var config = WriteConfig("""{ "internalize": false }""");
        var outputDir = Path.Combine(_root, "out");

        var (exitCode, _) = await RunAsync($"\"{a}\" \"{b}\" --merge -c \"{config}\" -o \"{outputDir}\" --no-logo");

        exitCode.ShouldBe(0);
        IsBTypePublic(Path.Combine(outputDir, "A.dll")).ShouldBe(true, "config internalize:false must not be overwritten by the flag's default");
    }

    [Fact]
    public async Task MergeFlag_WithoutConfig_InternalizesByDefault()
    {
        var (a, b) = CreatePair();
        var outputDir = Path.Combine(_root, "out");

        var (exitCode, _) = await RunAsync($"\"{a}\" \"{b}\" --merge -l minimal --no-symbol-renaming -o \"{outputDir}\" --no-logo");

        exitCode.ShouldBe(0);
        IsBTypePublic(Path.Combine(outputDir, "A.dll")).ShouldBe(false);
    }

    [Fact]
    public async Task ExplicitInternalizeTrue_OverridesConfigFalse()
    {
        var (a, b) = CreatePair();
        var config = WriteConfig("""{ "internalize": false }""");
        var outputDir = Path.Combine(_root, "out");

        var (exitCode, _) = await RunAsync(
            $"\"{a}\" \"{b}\" --merge --internalize true -c \"{config}\" -o \"{outputDir}\" --no-logo");

        exitCode.ShouldBe(0);
        IsBTypePublic(Path.Combine(outputDir, "A.dll")).ShouldBe(false);
    }

    [Fact]
    public async Task ExplicitInternalizeFalse_OverridesConfigTrue()
    {
        var (a, b) = CreatePair();
        var config = WriteConfig("""{ "internalize": true }""");
        var outputDir = Path.Combine(_root, "out");

        var (exitCode, _) = await RunAsync(
            $"\"{a}\" \"{b}\" --merge --internalize false -c \"{config}\" -o \"{outputDir}\" --no-logo");

        exitCode.ShouldBe(0);
        IsBTypePublic(Path.Combine(outputDir, "A.dll")).ShouldBe(true);
    }

    [Fact]
    public async Task ConfigMergeEnabled_WithoutMergeFlag_MergesIntoOneAssembly()
    {
        var (a, b) = CreatePair();
        var config = WriteConfig("""{ "enabled": true, "internalize": false }""");
        var outputDir = Path.Combine(_root, "out");

        var (exitCode, output) = await RunAsync($"\"{a}\" \"{b}\" -c \"{config}\" -o \"{outputDir}\" --no-logo");

        exitCode.ShouldBe(0);
        output.ShouldContain("Merging assemblies");
        File.Exists(Path.Combine(outputDir, "A.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "B.dll")).ShouldBeFalse("a merge writes one assembly, not a closed set");
        using var merged = ModuleDefMD.Load(Path.Combine(outputDir, "A.dll"));
        merged.Types.ShouldContain(t => t.Name == "AType");
        merged.Types.ShouldContain(t => t.Name == "BType");
    }

    [Fact]
    public async Task ConfigMergeDisabled_TwoAssemblies_StaysAClosedSet()
    {
        var (a, b) = CreatePair();
        var config = WriteConfig("""{ "enabled": false }""");
        var outputDir = Path.Combine(_root, "out");

        var (exitCode, _) = await RunAsync($"\"{a}\" \"{b}\" -c \"{config}\" -o \"{outputDir}\" --no-logo");

        exitCode.ShouldBe(0);
        File.Exists(Path.Combine(outputDir, "A.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "B.dll")).ShouldBeTrue();
    }

    [Fact]
    public async Task ConfigMergeEnabled_SingleInput_WarnsAndObfuscatesAlone()
    {
        var a = ConsoleTestAssembly.Create(_root, "A.dll", typeName: "AType");
        var config = WriteConfig("""{ "enabled": true }""");
        var outputDir = Path.Combine(_root, "out");

        var (exitCode, output) = await RunAsync($"\"{a}\" -c \"{config}\" -o \"{outputDir}\" --no-logo");

        exitCode.ShouldBe(0, "a shared config must not break a single-file run");
        output.ShouldContain("assemblyMerge.enabled");
        File.Exists(Path.Combine(outputDir, "A.dll")).ShouldBeTrue();
    }

    [Fact]
    public async Task MergeFlag_SingleInput_StillFails()
    {
        var a = ConsoleTestAssembly.Create(_root, "A.dll", typeName: "AType");

        var (exitCode, output) = await RunAsync($"\"{a}\" --merge --no-logo");

        exitCode.ShouldBe(1);
        output.ShouldContain("Merge requires at least two input assemblies");
    }
}

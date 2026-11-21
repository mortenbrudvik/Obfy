using dnlib.DotNet;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// Pins the documented MSBuild post-build step (<c>README.md</c>, <c>docs/Advanced.md</c>,
/// <c>examples/MsBuildIntegration</c>): <c>obfy $(TargetPath) -c obfy.json -o $(TargetDir)</c> writes the result over
/// the build output it just read. Refusing "output equals input" (review item) would break that workflow, so a change
/// that does must also change the docs, the example and <c>MsBuildIntegrationTests</c>, and decide this test.
/// </summary>
public class CliInPlaceOutputTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyInPlace_{Guid.NewGuid():N}");

    public CliInPlaceOutputTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task SingleFile_OutputFolderIsTheInputFolder_ReplacesTheInputInPlace()
    {
        var input = ConsoleTestAssembly.Create(_root, "App.dll", typeName: "KeepOrRename");
        var before = File.ReadAllBytes(input);

        using var capture = new AnsiConsoleCapture();
        var exitCode = await CommandLineTestHelpers.InvokeAsync(
            Program.CreateRootCommand(), $"\"{input}\" -o \"{_root}\" -l minimal --no-logo");

        exitCode.ShouldBe(0, capture.Text);
        File.ReadAllBytes(input).ShouldNotBe(before, "the output replaces the input");
        using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
        module.Types.ShouldNotContain(t => t.Name == "KeepOrRename");
        Directory.GetFiles(_root).Select(Path.GetFileName).ShouldBe(["App.dll"], "no stray temp or sibling files");
    }
}

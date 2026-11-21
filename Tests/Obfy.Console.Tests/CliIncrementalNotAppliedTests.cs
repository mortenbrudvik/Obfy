using System.Text.RegularExpressions;
using Shouldly;

namespace Obfy.Console.Tests;

/// <summary>
/// <c>--incremental</c> (or <c>incremental.enabled</c> in a <c>-c</c> file) only does something for a single assembly and a
/// merge. A closed set, a solution or project and C# source take no part in the cache, and the run says so once, as a
/// warning in the console and one warning line for the problem matcher, instead of silently ignoring the option.
/// </summary>
public class CliIncrementalNotAppliedTests : IDisposable
{
    private const string ClosedSetNote =
        "Incremental cache not used: it covers a single assembly or a merge, not a closed set " +
        "(a solution, a project, or several assemblies protected together), so every assembly was obfuscated.";

    private const string SourceNote =
        "Incremental cache not used: it covers a single assembly or a merge, not C# source " +
        "(a file, several files or a folder), so every file was obfuscated.";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyIncrementalNa_{Guid.NewGuid():N}");

    public CliIncrementalNotAppliedTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    public enum Shape { SeveralAssemblies, Solution, OneSourceFile, SeveralSourceFiles }

    public static IEnumerable<object[]> Shapes() => Enum.GetValues<Shape>().Select(static s => new object[] { s });

    private static async Task<(int Exit, string Text, IReadOnlyList<ProblemLine> Problems)> RunAsync(string args)
    {
        using var console = new AnsiConsoleCapture();
        using var capture = new ProblemLineCapture();
        var exit = await CommandLineTestHelpers.InvokeAsync(Program.CreateRootCommand(), args + " -l minimal --no-logo");
        return (exit, console.Text, capture.Problems);
    }

    private static int Occurrences(string text, string note) => Regex.Count(text, Regex.Escape(note));

    /// <summary>The command line (without the option under test) and the note it is expected to get.</summary>
    private (string Args, string Note) Setup(Shape shape)
    {
        var outDir = Path.Combine(_root, "out");
        switch (shape)
        {
            case Shape.SeveralAssemblies:
            {
                var a = ConsoleTestAssembly.Create(_root, "A.dll", "Alpha");
                var b = ConsoleTestAssembly.Create(_root, "B.dll", "Beta");
                return ($"\"{a}\" \"{b}\" -o \"{outDir}\"", ClosedSetNote);
            }
            case Shape.Solution:
            {
                var projectDir = Path.Combine(_root, "Lib");
                var builtDir = Path.Combine(projectDir, "bin", "Release", "net8.0");
                Directory.CreateDirectory(builtDir);
                File.WriteAllText(Path.Combine(projectDir, "Lib.csproj"), """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                      </PropertyGroup>
                    </Project>
                    """);
                ConsoleTestAssembly.Create(builtDir, "Lib.dll");
                var sln = Path.Combine(_root, "Lib.sln");
                File.WriteAllText(sln, """
                    Microsoft Visual Studio Solution File, Format Version 12.00
                    Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Lib", "Lib\Lib.csproj", "{22222222-2222-2222-2222-222222222222}"
                    EndProject
                    """);
                return ($"\"{sln}\" -o \"{outDir}\"", ClosedSetNote);
            }
            case Shape.OneSourceFile:
            {
                var x = Path.Combine(_root, "X.cs");
                File.WriteAllText(x, "class X { string S() => \"a string that is long enough\"; }");
                return ($"\"{x}\" -o \"{outDir}\"", SourceNote);
            }
            case Shape.SeveralSourceFiles:
            {
                var x = Path.Combine(_root, "X.cs");
                var y = Path.Combine(_root, "Y.cs");
                File.WriteAllText(x, "class X { string S() => \"a string that is long enough\"; }");
                File.WriteAllText(y, "class Y { string S() => \"another string that is long\"; }");
                return ($"\"{x}\" \"{y}\" -o \"{outDir}\"", SourceNote);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task WithTheFlag_TheRunSaysOnce_ThatTheCacheIsNotUsed(Shape shape)
    {
        var (args, note) = Setup(shape);

        var (exit, text, problems) = await RunAsync($"{args} --incremental");

        exit.ShouldBe(0, text);
        Occurrences(text, note).ShouldBe(1, text);
        var line = problems.Where(static p => p.Message.StartsWith("Incremental cache not used", StringComparison.Ordinal))
            .ShouldHaveSingleItem(text);
        line.Severity.ShouldBe("warning");
        line.Message.ShouldBe(note);
        text.ShouldNotContain("Incremental: reused cached output");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task WithTheOptionInAConfigFile_TheRunSaysOnce_ThatTheCacheIsNotUsed(Shape shape)
    {
        var (args, note) = Setup(shape);
        var config = Path.Combine(_root, "obfy.json");
        File.WriteAllText(config, """{ "level": "Custom", "incremental": { "enabled": true } }""");

        var (exit, text, problems) = await RunAsync($"{args} -c \"{config}\"");

        exit.ShouldBe(0, text);
        Occurrences(text, note).ShouldBe(1, text);
        problems.Count(static p => p.Message.StartsWith("Incremental cache not used", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task WithoutIncrementalMode_TheRunSaysNothingAboutTheCache(Shape shape)
    {
        var (args, _) = Setup(shape);

        var (exit, text, problems) = await RunAsync(args);

        exit.ShouldBe(0, text);
        text.ShouldNotContain("Incremental cache");
        problems.ShouldNotContain(static p => p.Message.Contains("Incremental", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SingleAssembly_WithTheFlag_UsesTheCache_AndNeverSaysItIsNotCovered()
    {
        var app = ConsoleTestAssembly.Create(_root, "App.dll");
        var args = $"\"{app}\" -o \"{Path.Combine(_root, "out")}\" --incremental";

        var (_, first, _) = await RunAsync(args);
        var (exit, second, problems) = await RunAsync(args);

        exit.ShouldBe(0, second);
        first.ShouldNotContain("Incremental cache not used");
        second.ShouldContain("Incremental: reused cached output");
        second.ShouldNotContain("Incremental cache not used");
        problems.ShouldNotContain(static p => p.Message.StartsWith("Incremental cache not used", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Merge_WithTheFlag_NeverSaysItIsNotCovered()
    {
        var a = ConsoleTestAssembly.Create(_root, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_root, "B.dll", "Beta");

        var (exit, text, problems) = await RunAsync($"\"{a}\" \"{b}\" --merge -o \"{Path.Combine(_root, "out")}\" --incremental");

        exit.ShouldBe(0, text);
        text.ShouldNotContain("Incremental cache not used");
        problems.ShouldNotContain(static p => p.Message.StartsWith("Incremental cache not used", StringComparison.Ordinal));
    }
}

using dnlib.DotNet;
using Obfy.Console;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Console.Tests;

public class SolutionInputTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"ObfySln_{Guid.NewGuid():N}");

    public SolutionInputTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Parse_SlnPath_IsAcceptedAsInput()
    {
        var sln = Path.Combine("src", "App.sln");
        var parseResult = Program.CreateRootCommand().Parse($"\"{sln}\" -o out");
        parseResult.Errors.ShouldBeEmpty();
        parseResult.GetRequiredValue(Program.InputArgument)[0].Name.ShouldBe("App.sln");
    }

    [Fact]
    public void IsSolutionOrProject_RecognizesExtensions()
    {
        Program.IsSolutionOrProject("a.sln").ShouldBeTrue();
        Program.IsSolutionOrProject("a.slnx").ShouldBeTrue();
        Program.IsSolutionOrProject("a.csproj").ShouldBeTrue();
        Program.IsSolutionOrProject("a.dll").ShouldBeFalse();
    }

    [Fact]
    public void ShouldUseLooseClosedSet_TwoExistingDlls_IsTrue()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll");

        Program.ShouldUseLooseClosedSet([new FileInfo(a), new FileInfo(b)]).ShouldBeTrue();
    }

    [Fact]
    public void ShouldUseLooseClosedSet_SingleDll_IsFalse()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");

        Program.ShouldUseLooseClosedSet([new FileInfo(a)]).ShouldBeFalse();
    }

    [Fact]
    public void ShouldUseLooseClosedSet_TwoDllsPlusSource_IsTrue()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll");
        var cs = Path.Combine(_tempDirectory, "Extra.cs");
        File.WriteAllText(cs, "class Extra {}");

        Program.ShouldUseLooseClosedSet([new FileInfo(a), new FileInfo(b), new FileInfo(cs)]).ShouldBeTrue();
    }

    [Fact]
    public void ShouldUseLooseClosedSet_TwoDllsPlusMissing_IsTrue()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll");
        var missing = Path.Combine(_tempDirectory, "NoSuch.dll");

        Program.ShouldUseLooseClosedSet(
            [new FileInfo(a), new FileInfo(b), new FileInfo(missing)]).ShouldBeTrue();
    }

    [Fact]
    public void Invoke_TwoDlls_DryRun_ReturnsExitCode0()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{a}\" \"{b}\" --dry-run --no-logo", out _);

        exitCode.ShouldBe(0);
        Directory.Exists(Path.Combine(Path.GetDirectoryName(a)!, "obfy-out")).ShouldBeFalse();
    }

    [Fact]
    public void Invoke_TwoDlls_WritesBothOutputs_AndKeepsUnreferencedPublicTypes()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll", "Beta");
        var outputDir = Path.Combine(_tempDirectory, "two-dll-out");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{a}\" \"{b}\" -o \"{outputDir}\" -l minimal --no-logo", out _);

        exitCode.ShouldBe(0);
        File.Exists(Path.Combine(outputDir, "A.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "B.dll")).ShouldBeTrue();
        using var module = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(outputDir, "A.dll")));
        module.GetTypes().ShouldContain(t => t.Name == "Alpha");
    }

    [Fact]
    public void Invoke_TwoDllsPlusMissing_DryRun_ReturnsExitCode1()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll");
        var missing = Path.Combine(_tempDirectory, "NoSuch.dll");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{a}\" \"{b}\" \"{missing}\" --dry-run --no-logo", out _);

        exitCode.ShouldBe(1);
    }

    [Fact]
    public void Invoke_TwoDllsPlusMissing_ReturnsExitCode1()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll");
        var missing = Path.Combine(_tempDirectory, "NoSuch.dll");
        var outputDir = Path.Combine(_tempDirectory, "missing-third-out");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{a}\" \"{b}\" \"{missing}\" -o \"{outputDir}\" -l minimal --no-logo", out _);

        exitCode.ShouldBe(1);
        Directory.Exists(outputDir).ShouldBeFalse();
    }

    [Fact]
    public void Invoke_TwoDllsPlusCorrupt_ReturnsExitCode1()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll", "Beta");
        var corrupt = Path.Combine(_tempDirectory, "Corrupt.dll");
        File.WriteAllText(corrupt, "not an assembly");
        var outputDir = Path.Combine(_tempDirectory, "corrupt-out");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{a}\" \"{b}\" \"{corrupt}\" -o \"{outputDir}\" -l minimal --no-logo", out _);

        exitCode.ShouldBe(1);
    }

    [Fact]
    public void Invoke_TwoDllsPlusSource_WritesBothDlls()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll", "Beta");
        var cs = Path.Combine(_tempDirectory, "Extra.cs");
        File.WriteAllText(cs, "class Extra {}");
        var outputDir = Path.Combine(_tempDirectory, "dll-plus-cs-out");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{a}\" \"{b}\" \"{cs}\" -o \"{outputDir}\" -l minimal --no-logo", out _);

        exitCode.ShouldBe(0);
        File.Exists(Path.Combine(outputDir, "A.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "B.dll")).ShouldBeTrue();
        using var module = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(outputDir, "A.dll")));
        module.GetTypes().ShouldContain(t => t.Name == "Alpha");
    }

    [Fact]
    public void Invoke_TwoDlls_WithMerge_WritesSingleMergedOutput()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll");
        var outputDir = Path.Combine(_tempDirectory, "merge-out");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{a}\" \"{b}\" --merge -o \"{outputDir}\" -l minimal --no-logo", out _);

        exitCode.ShouldBe(0);
        File.Exists(Path.Combine(outputDir, "A.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "B.dll")).ShouldBeFalse();
    }

    [Fact]
    public void Invoke_TwoAssemblies_AppAndLib_RenamesLibPublicType_AndRunStillReturnsHi()
    {
        var (libPath, appPath) = ConsoleTestAssembly.CreateClosedSetPair(_tempDirectory);
        var outputDir = Path.Combine(_tempDirectory, "app-lib-out");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{appPath}\" \"{libPath}\" -o \"{outputDir}\" -l minimal --no-logo", out _);

        exitCode.ShouldBe(0);
        var outLib = Path.Combine(outputDir, "Lib.dll");
        var outApp = Path.Combine(outputDir, "App.exe");
        File.Exists(outLib).ShouldBeTrue();
        File.Exists(outApp).ShouldBeTrue();
        using (var libModule = ModuleDefMD.Load(File.ReadAllBytes(outLib)))
            libModule.GetTypes().ShouldNotContain(t => t.Name == "Greeter");
        ConsoleTestAssembly.InvokeProgramRun(outApp, outLib).ShouldBe("hi");
    }

    [Fact]
    public void Invoke_TwoAssemblies_PreservePublic_KeepsGreeterName()
    {
        var (libPath, appPath) = ConsoleTestAssembly.CreateClosedSetPair(_tempDirectory);
        var outputDir = Path.Combine(_tempDirectory, "preserve-out");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command,
            $"\"{appPath}\" \"{libPath}\" -o \"{outputDir}\" -l minimal --preserve-public --no-logo",
            out _);

        exitCode.ShouldBe(0);
        using var libModule = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(outputDir, "Lib.dll")));
        libModule.GetTypes().ShouldContain(t => t.Name == "Greeter");
        ConsoleTestAssembly.InvokeProgramRun(
            Path.Combine(outputDir, "App.exe"),
            Path.Combine(outputDir, "Lib.dll")).ShouldBe("hi");
    }

    [Fact]
    public void Invoke_TestsOnlySolution_DryRun_ReturnsExitCode2()
    {
        var sln = WriteTestsOnlySolution();
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(command, $"\"{sln}\" --dry-run --no-logo", out _);

        exitCode.ShouldBe(2);
    }

    [Fact]
    public void Invoke_TwoSolutionFiles_ReturnsExitCode1()
    {
        var sln1 = Path.Combine(_tempDirectory, "A.sln");
        var sln2 = Path.Combine(_tempDirectory, "B.sln");
        File.WriteAllText(sln1, "Microsoft Visual Studio Solution File, Format Version 12.00");
        File.WriteAllText(sln2, "Microsoft Visual Studio Solution File, Format Version 12.00");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(command, $"\"{sln1}\" \"{sln2}\" --no-logo", out _);

        exitCode.ShouldBe(1);
    }

    [Fact]
    public void Invoke_ExtraCsWithTestsOnlySolution_DryRun_ReturnsExitCode2()
    {
        var sln = WriteTestsOnlySolution();
        var csPath = Path.Combine(_tempDirectory, "Extra.cs");
        File.WriteAllText(csPath, "class Extra {}");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{sln}\" \"{csPath}\" --dry-run --no-logo", out _);

        exitCode.ShouldBe(2);
    }

    [Fact]
    public void Invoke_ExtraDllWithTestsOnlySolution_DryRun_ReturnsExitCode0()
    {
        var sln = WriteTestsOnlySolution();
        var dll = ConsoleTestAssembly.Create(_tempDirectory, "Extra.dll");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{sln}\" \"{dll}\" --dry-run --no-logo", out _);

        exitCode.ShouldBe(0);
    }

    [Fact]
    public void Invoke_IncludedProject_DryRun_ReturnsExitCode0()
    {
        var sln = WriteLibrarySolutionWithBuiltOutput();
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(command, $"\"{sln}\" --dry-run --no-logo", out _);

        exitCode.ShouldBe(0);
        Directory.Exists(Path.Combine(_tempDirectory, "obfy-out")).ShouldBeFalse();
    }

    [Fact]
    public void Invoke_MissingOutput_DefaultsToObfyOut()
    {
        var sln = WriteLibrarySolutionWithBuiltOutput();
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(command, $"\"{sln}\" --no-logo -l minimal", out _);

        exitCode.ShouldBe(0);
        var outputDir = Path.Combine(_tempDirectory, "obfy-out");
        Directory.Exists(outputDir).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "Lib.dll")).ShouldBeTrue();
    }

    [Fact]
    public void Help_InputArgument_MentionsSolutionFiles()
    {
        CommandLineTestHelpers.Invoke(Program.CreateRootCommand(), "--help", out var output);

        output.ShouldContain(".sln");
        output.ShouldContain(".slnx");
        output.ShouldContain("project files");
    }

    [Fact]
    public void Invoke_MissingExtraDll_TestsOnlySolution_DryRun_ReturnsExitCode1()
    {
        var sln = WriteTestsOnlySolution();
        var missing = Path.Combine(_tempDirectory, "NoSuch.dll");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{sln}\" \"{missing}\" --dry-run --no-logo", out _);

        exitCode.ShouldBe(1);
    }

    [Fact]
    public void Invoke_TwoDllsPlusCorrupt_EmitsProblemMatcherLineForTheCorruptFile()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll", "Beta");
        var corrupt = Path.Combine(_tempDirectory, "Corrupt.dll");
        File.WriteAllText(corrupt, "not an assembly");
        var outputDir = Path.Combine(_tempDirectory, "corrupt-problem-out");
        using var problems = new ProblemLineCapture();

        var exitCode = CommandLineTestHelpers.Invoke(
            Program.CreateRootCommand(), $"\"{a}\" \"{b}\" \"{corrupt}\" -o \"{outputDir}\" -l minimal --no-logo", out _);

        exitCode.ShouldBe(1);
        var problem = problems.Problems.Where(p => p.File == corrupt).ShouldHaveSingleItem();
        problem.Severity.ShouldBe("error");
        problem.Message.ShouldStartWith("Failed to load assembly:");
    }

    [Fact]
    public void Invoke_SolutionWithCorruptOutput_EmitsProblemMatcherLineForTheCorruptFile()
    {
        var sln = WriteLibrarySolutionWithBuiltOutput();
        var corrupt = Path.Combine(_tempDirectory, "Lib", "bin", "Release", "net8.0", "Lib.dll");
        File.WriteAllText(corrupt, "not an assembly");
        var outputDir = Path.Combine(_tempDirectory, "sln-corrupt-problem-out");
        using var problems = new ProblemLineCapture();

        var exitCode = CommandLineTestHelpers.Invoke(
            Program.CreateRootCommand(), $"\"{sln}\" -o \"{outputDir}\" -l minimal --no-logo", out _);

        exitCode.ShouldBe(1);
        var problem = problems.Problems.ShouldHaveSingleItem();
        problem.File.ShouldBe(corrupt, StringCompareShould.IgnoreCase);
        problem.Severity.ShouldBe("error");
        problem.Message.ShouldStartWith("Failed to load assembly:");
    }

    [Fact]
    public void Invoke_SolutionPlusMissingExtraDll_EmitsProblemMatcherLineForTheMissingFile()
    {
        var sln = WriteTestsOnlySolution();
        var missing = Path.Combine(_tempDirectory, "NoSuch.dll");
        using var problems = new ProblemLineCapture();

        var exitCode = CommandLineTestHelpers.Invoke(
            Program.CreateRootCommand(), $"\"{sln}\" \"{missing}\" --dry-run --no-logo", out _);

        exitCode.ShouldBe(1);
        var problem = problems.Problems.ShouldHaveSingleItem();
        problem.File.ShouldBe(missing);
        problem.Severity.ShouldBe("error");
        problem.Message.ShouldBe("File not found");
    }

    [Fact]
    public void Invoke_TwoDllsPlusMissing_EmitsProblemMatcherLineForTheMissingFile()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll");
        var missing = Path.Combine(_tempDirectory, "NoSuch.dll");
        using var problems = new ProblemLineCapture();

        var exitCode = CommandLineTestHelpers.Invoke(
            Program.CreateRootCommand(), $"\"{a}\" \"{b}\" \"{missing}\" --dry-run --no-logo", out _);

        exitCode.ShouldBe(1);
        var problem = problems.Problems.ShouldHaveSingleItem();
        problem.File.ShouldBe(missing);
        problem.Severity.ShouldBe("error");
        problem.Message.ShouldBe("File not found");
    }

    [Fact]
    public void FormatLibraryMode_ForcePreservePublic_YesOnlyWhenIncluded()
    {
        Program.FormatLibraryMode(included: true, hintPreservePublic: false, forcePreservePublic: true)
            .ShouldBe("Yes");
        Program.FormatLibraryMode(included: true, hintPreservePublic: false, forcePreservePublic: false)
            .ShouldBe("No");
        Program.FormatLibraryMode(included: true, hintPreservePublic: true, forcePreservePublic: false)
            .ShouldBe("Yes");
        Program.FormatLibraryMode(included: false, hintPreservePublic: false, forcePreservePublic: true)
            .ShouldBe("No");
    }

    [Fact]
    public void Invoke_SingleIncluded_WithMerge_UsesClosedSet()
    {
        var sln = WriteLibrarySolutionWithBuiltOutput();
        var outputDir = Path.Combine(_tempDirectory, "single-merge-out");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{sln}\" --merge --no-logo -l minimal -o \"{outputDir}\"", out _);

        exitCode.ShouldBe(0);
        File.Exists(Path.Combine(outputDir, "Lib.dll")).ShouldBeTrue();
        Directory.GetFiles(outputDir, "*.merged*").ShouldBeEmpty();
    }

    [Fact]
    public void Invoke_EmptySln_ReturnsExitCode1()
    {
        var sln = Path.Combine(_tempDirectory, "Empty.sln");
        File.WriteAllText(sln, "Microsoft Visual Studio Solution File, Format Version 12.00");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(command, $"\"{sln}\" --dry-run --no-logo", out _);

        exitCode.ShouldBe(1);
    }

    [Fact]
    public void FormatLibraryMode_ExtraUnknownUntilLoad_PrintsAfterLoad()
    {
        Program.FormatLibraryMode(included: true, hintPreservePublic: false, forcePreservePublic: false, extraUnknownUntilLoad: true)
            .ShouldBe("After load");
        Program.FormatLibraryMode(included: true, hintPreservePublic: false, forcePreservePublic: true, extraUnknownUntilLoad: true)
            .ShouldBe("Yes");
    }

    [Fact]
    public void Invoke_TwoIncluded_WithMerge_WritesSingleMergedOutput()
    {
        var sln = WriteLibrarySolutionWithBuiltOutput();
        var extra = ConsoleTestAssembly.Create(_tempDirectory, "Extra.dll", "ExtraType");
        var outputDir = Path.Combine(_tempDirectory, "merge-out");
        var command = Program.CreateRootCommand();

        var exitCode = CommandLineTestHelpers.Invoke(
            command, $"\"{sln}\" \"{extra}\" --merge --no-logo -l minimal -o \"{outputDir}\"", out _);

        exitCode.ShouldBe(0);
        File.Exists(Path.Combine(outputDir, "Lib.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "Extra.dll")).ShouldBeFalse();
    }

    // ---- Failures that name no file still reach the Problems panel: one error line, against the solution or the first input ----

    private static (int Exit, string Text, IReadOnlyList<ProblemLine> Problems) RunCapturing(string args)
    {
        using var console = new AnsiConsoleCapture();
        using var capture = new ProblemLineCapture();
        var exit = CommandLineTestHelpers.Invoke(Program.CreateRootCommand(), args, out _);
        return (exit, console.Text, capture.Problems);
    }

    private static void ShouldBeOneErrorLine(
        IReadOnlyList<ProblemLine> problems, string file, string messageStart, string text)
    {
        var problem = problems.ShouldHaveSingleItem(text);
        problem.File.ShouldBe(file, StringCompareShould.IgnoreCase);
        problem.Severity.ShouldBe("error");
        problem.Message.ShouldStartWith(messageStart);
        text.ShouldContain(messageStart, Case.Sensitive, "the red text stays");
    }

    [Fact]
    public void Invoke_SolutionWithNoBuiltOutputs_EmitsOneProblemLineAgainstTheSolution()
    {
        var sln = WriteTestsOnlySolution();

        var (exitCode, text, problems) = RunCapturing($"\"{sln}\" --no-logo");

        exitCode.ShouldBe(2);
        ShouldBeOneErrorLine(problems, sln, "No built outputs found. Build the ship set (Release) and retry.", text);
    }

    [Fact]
    public void Invoke_ProjectWithNoBuiltOutputs_EmitsOneProblemLineAgainstTheProject()
    {
        WriteTestsOnlySolution();
        var project = Path.Combine(_tempDirectory, "Foo.Tests", "Foo.Tests.csproj");

        var (exitCode, text, problems) = RunCapturing($"\"{project}\" --no-logo");

        exitCode.ShouldBe(2);
        ShouldBeOneErrorLine(problems, project, "No built outputs found. Build the ship set (Release) and retry.", text);
    }

    [Fact]
    public void Invoke_TwoSolutionFiles_EmitsOneProblemLineAgainstTheFirstSolution()
    {
        var sln1 = Path.Combine(_tempDirectory, "A.sln");
        var sln2 = Path.Combine(_tempDirectory, "B.sln");
        File.WriteAllText(sln1, "Microsoft Visual Studio Solution File, Format Version 12.00");
        File.WriteAllText(sln2, "Microsoft Visual Studio Solution File, Format Version 12.00");

        var (exitCode, text, problems) = RunCapturing($"\"{sln1}\" \"{sln2}\" --no-logo");

        exitCode.ShouldBe(1);
        ShouldBeOneErrorLine(problems, sln1, "Only one solution file can be obfuscated per run.", text);
    }

    [Fact]
    public void Invoke_MergeWithOneAssembly_EmitsOneProblemLineAgainstTheInput()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");

        var (exitCode, text, problems) = RunCapturing($"\"{a}\" --merge --no-logo");

        exitCode.ShouldBe(1);
        ShouldBeOneErrorLine(problems, a, "Merge requires at least two input assemblies.", text);
    }

    [Fact]
    public async Task NoAssembliesFound_EmitsOneProblemLineAgainstTheFirstInput_WhenNoAssemblyWasListed()
    {
        // Unreachable from the command line (a loose closed set needs two existing assemblies), so the method is called
        // directly: nothing it lists is an assembly, and nothing has printed a line for that yet.
        var x = Path.Combine(_tempDirectory, "X.cs");
        var y = Path.Combine(_tempDirectory, "Y.cs");
        File.WriteAllText(x, "class X {}");
        File.WriteAllText(y, "class Y {}");
        using var console = new AnsiConsoleCapture();
        using var capture = new ProblemLineCapture();

        var exitCode = await Program.RunLooseClosedSetAsync(
            [new FileInfo(x), new FileInfo(y)], null, new ObfySettings(), null, null, dryRun: false, preservePublic: false,
            null!, null!, CancellationToken.None);

        exitCode.ShouldBe(2);
        ShouldBeOneErrorLine(capture.Problems, x, "No assemblies found.", console.Text);
    }

    [Fact]
    public async Task NoAssembliesFound_AddsNoLine_WhenEveryListedAssemblyIsMissing()
    {
        var missingA = Path.Combine(_tempDirectory, "NoSuchA.dll");
        var missingB = Path.Combine(_tempDirectory, "NoSuchB.dll");
        using var console = new AnsiConsoleCapture();
        using var capture = new ProblemLineCapture();

        var exitCode = await Program.RunLooseClosedSetAsync(
            [new FileInfo(missingA), new FileInfo(missingB)], null, new ObfySettings(), null, null, dryRun: false, preservePublic: false,
            null!, null!, CancellationToken.None);

        exitCode.ShouldBe(2);
        capture.Problems.Select(static p => (p.File, p.Message)).ShouldBe(
            [(missingA, "File not found"), (missingB, "File not found")], "the per-file lines are enough");
        console.Text.ShouldContain("No assemblies found.");
    }

    [Fact]
    public void Invoke_TwoDllsPlusMissing_KeepsTheRedSentence_AndAddsNoLineBesideTheMissingFile()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll");
        var missing = Path.Combine(_tempDirectory, "NoSuch.dll");

        var (exitCode, text, problems) = RunCapturing($"\"{a}\" \"{b}\" \"{missing}\" --no-logo");

        exitCode.ShouldBe(1);
        text.ShouldContain("Closed-set obfuscation requires every listed assembly to exist.");
        problems.Select(static p => (p.File, p.Message)).ShouldBe([(missing, "File not found")]);
    }

    [Fact]
    public void Invoke_TwoDllsPlusCorrupt_EmitsOnlyThePerFileLine_NotASecondForTheStoppedRun()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll", "Beta");
        var corrupt = Path.Combine(_tempDirectory, "Corrupt.dll");
        File.WriteAllText(corrupt, "not an assembly");

        var (exitCode, text, problems) = RunCapturing(
            $"\"{a}\" \"{b}\" \"{corrupt}\" -o \"{Path.Combine(_tempDirectory, "o")}\" -l minimal --no-logo");

        exitCode.ShouldBe(1);
        text.ShouldContain("Closed-set obfuscation stopped because 1 assembly(ies) failed to load.");
        var problem = problems.ShouldHaveSingleItem(text);
        problem.File.ShouldBe(corrupt);
        problem.Message.ShouldStartWith("Failed to load assembly:");
    }

    [Fact]
    public void Invoke_TwoAlreadyProtectedAssemblies_EmitsOneProblemLineAgainstTheFirstInput()
    {
        var (a, b) = ProtectedPair();

        var (exitCode, text, problems) = RunCapturing(
            $"\"{a}\" \"{b}\" -o \"{Path.Combine(_tempDirectory, "again-out")}\" -l minimal --no-logo");

        exitCode.ShouldBe(1);
        ShouldBeOneErrorLine(problems, a, "Closed-set obfuscation stopped: ", text);
        problems[0].Message.ShouldContain("already protected");
    }

    [Fact]
    public void Invoke_SolutionWhoseBuiltOutputIsAlreadyProtected_EmitsOneProblemLineAgainstTheSolution()
    {
        var (protectedLib, _) = ProtectedPair();
        var sln = WriteLibrarySolutionWithBuiltOutput();
        File.Copy(protectedLib, Path.Combine(_tempDirectory, "Lib", "bin", "Release", "net8.0", "Lib.dll"), overwrite: true);

        var (exitCode, text, problems) = RunCapturing(
            $"\"{sln}\" -o \"{Path.Combine(_tempDirectory, "again-out")}\" -l minimal --no-logo");

        exitCode.ShouldBe(1);
        ShouldBeOneErrorLine(problems, sln, "Closed-set obfuscation stopped: ", text);
        problems[0].Message.ShouldContain("already protected");
    }

    [Fact]
    public void Invoke_TwoAssembliesWithTheSameOutputPath_EmitsOneProblemLineAgainstTheFirstInput()
    {
        var one = Path.Combine(_tempDirectory, "one", "sub");
        var two = Path.Combine(_tempDirectory, "two", "sub");
        Directory.CreateDirectory(one);
        Directory.CreateDirectory(two);
        var a = ConsoleTestAssembly.Create(one, "A.dll");
        var b = ConsoleTestAssembly.Create(two, "A.dll");

        var (exitCode, text, problems) = RunCapturing(
            $"\"{a}\" \"{b}\" -o \"{Path.Combine(_tempDirectory, "collide-out")}\" -l minimal --no-logo");

        exitCode.ShouldBe(1);
        ShouldBeOneErrorLine(problems, a, "Output path collision", text);
    }

    [Fact]
    public void Invoke_ClosedSetWithAMissingSigningKey_EmitsOneProblemLineAgainstTheFirstInput()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll", "Beta");
        var config = WriteConfig("""{ "signing": { "enabled": true, "keyFile": "no-such-key.snk" } }""");

        var (exitCode, text, problems) = RunCapturing(
            $"\"{a}\" \"{b}\" -c \"{config}\" -o \"{Path.Combine(_tempDirectory, "sign-out")}\" --no-logo");

        exitCode.ShouldBe(1);
        ShouldBeOneErrorLine(problems, a, "Failed to load the signing key:", text);
    }

    [Fact]
    public void Invoke_SolutionWithAMissingSigningKey_EmitsOneProblemLineAgainstTheSolution()
    {
        var sln = WriteLibrarySolutionWithBuiltOutput();
        var config = WriteConfig("""{ "signing": { "enabled": true, "keyFile": "no-such-key.snk" } }""");

        var (exitCode, text, problems) = RunCapturing(
            $"\"{sln}\" -c \"{config}\" -o \"{Path.Combine(_tempDirectory, "sign-out")}\" --no-logo");

        exitCode.ShouldBe(1);
        ShouldBeOneErrorLine(problems, sln, "Failed to load the signing key:", text);
    }

    [Fact]
    public void Invoke_ClosedSetWithInvalidSettings_EmitsOneProblemLineAgainstTheFirstInput()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll", "Beta");
        var config = WriteConfig("""{ "stringEncryption": { "enabled": true, "minStringLength": 0 } }""");

        var (exitCode, text, problems) = RunCapturing(
            $"\"{a}\" \"{b}\" -c \"{config}\" -o \"{Path.Combine(_tempDirectory, "invalid-out")}\" --no-logo");

        exitCode.ShouldBe(1);
        ShouldBeOneErrorLine(problems, a, "Invalid settings:", text);
    }

    [Fact]
    public void Invoke_SolutionWithInvalidSettings_EmitsOneProblemLineAgainstTheSolution()
    {
        var sln = WriteLibrarySolutionWithBuiltOutput();
        var config = WriteConfig("""{ "stringEncryption": { "enabled": true, "minStringLength": 0 } }""");

        var (exitCode, text, problems) = RunCapturing(
            $"\"{sln}\" -c \"{config}\" -o \"{Path.Combine(_tempDirectory, "invalid-out")}\" --no-logo");

        exitCode.ShouldBe(1);
        ShouldBeOneErrorLine(problems, sln, "Invalid settings:", text);
    }

    private string WriteConfig(string json)
    {
        var path = Path.Combine(_tempDirectory, "obfy.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>Two assemblies run through Obfy with string encryption, so each carries the runtime helper types a second run refuses.</summary>
    private (string A, string B) ProtectedPair()
    {
        var a = ConsoleTestAssembly.Create(_tempDirectory, "A.dll", "Alpha");
        var b = ConsoleTestAssembly.Create(_tempDirectory, "B.dll", "Beta");
        var folder = Path.Combine(_tempDirectory, "protected");
        CommandLineTestHelpers.Invoke(
            Program.CreateRootCommand(), $"\"{a}\" \"{b}\" -o \"{folder}\" -l minimal --string-encrypt --no-logo", out _)
            .ShouldBe(0);
        return (Path.Combine(folder, "A.dll"), Path.Combine(folder, "B.dll"));
    }

    private string WriteTestsOnlySolution()
    {
        var projectDir = Path.Combine(_tempDirectory, "Foo.Tests");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "Foo.Tests.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
            </Project>
            """);

        var sln = Path.Combine(_tempDirectory, "Foo.sln");
        File.WriteAllText(sln, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Foo.Tests", "Foo.Tests\Foo.Tests.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            """);
        return sln;
    }

    private string WriteLibrarySolutionWithBuiltOutput()
    {
        var projectDir = Path.Combine(_tempDirectory, "Lib");
        var outputDir = Path.Combine(projectDir, "bin", "Release", "net8.0");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(projectDir, "Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        ConsoleTestAssembly.Create(outputDir, "Lib.dll");

        var sln = Path.Combine(_tempDirectory, "Lib.sln");
        File.WriteAllText(sln, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Lib", "Lib\Lib.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            """);
        return sln;
    }
}

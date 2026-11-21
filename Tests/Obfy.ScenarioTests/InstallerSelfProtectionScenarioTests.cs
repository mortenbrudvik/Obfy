using dnlib.DotNet;
using Shouldly;

namespace Obfy.ScenarioTests;

/// <summary>
/// The installer protects each publish folder as one closed set
/// (<c>build/self-obfuscation.json</c>, public names not preserved). Doing the same assemblies one at a
/// time renames a public type in the library and leaves the application calling the old name, so the
/// application cannot start. These tests run the result.
/// </summary>
public class InstallerSelfProtectionScenarioTests
{
    [Fact]
    public async Task ClosedSet_WithInstallerSettings_RenamesTheLibraryAndStillRuns()
    {
        var copy = Stage();
        try
        {
            var settings = InstallerSettings();
            var stageDir = StageDirectory(copy);
            var obfDir = Path.Combine(stageDir, "closed");
            ScenarioHarness.CopySidecars(stageDir, obfDir);

            await ScenarioHarness.ObfuscateClosedSetAsync(
                [Path.Combine(stageDir, "ConsoleLib.dll"), Path.Combine(stageDir, "ConsoleApp.dll")],
                obfDir,
                settings);

            using (var lib = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(obfDir, "ConsoleLib.dll"))))
            {
                lib.Types.ShouldNotContain(t => t.Name == "Calculator");
            }

            var run = ScenarioHarness.RunDotnet(Path.Combine(obfDir, "ConsoleApp.dll"), "--smoke");
            run.ExitCode.ShouldBe(0, run.StdOut + run.StdErr);
            run.StdOut.ShouldContain("SMOKE:5");
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }

    [Fact]
    public async Task OneAssemblyAtATime_WithInstallerSettings_FailsToLoadTheLibraryType()
    {
        var copy = Stage();
        try
        {
            var settings = InstallerSettings();
            var stageDir = StageDirectory(copy);
            var obfDir = Path.Combine(stageDir, "separate");
            ScenarioHarness.CopySidecars(stageDir, obfDir);

            await ScenarioHarness.ObfuscateAsync(
                Path.Combine(stageDir, "ConsoleLib.dll"),
                Path.Combine(obfDir, "ConsoleLib.dll"),
                settings);
            await ScenarioHarness.ObfuscateAsync(
                Path.Combine(stageDir, "ConsoleApp.dll"),
                Path.Combine(obfDir, "ConsoleApp.dll"),
                settings);

            var run = ScenarioHarness.RunDotnet(Path.Combine(obfDir, "ConsoleApp.dll"), "--smoke");
            run.ExitCode.ShouldNotBe(0);
            (run.StdOut + run.StdErr).ShouldContain("TypeLoadException");
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }

    private static Obfy.Core.Models.ObfySettings InstallerSettings()
    {
        var settings = ScenarioHarness.LoadSettings(
            Path.Combine(ScenarioHarness.FindRepoRoot(), "build", "self-obfuscation.json"));
        settings.StringEncryption.Enabled.ShouldBeTrue();
        settings.ControlFlow.Enabled.ShouldBeTrue();
        settings.SymbolRenaming.Enabled.ShouldBeTrue();
        settings.SymbolRenaming.PreservePublicApi.ShouldBeFalse();
        settings.SymbolRenaming.PreserveXaml.ShouldBeTrue();
        settings.Metadata.RemoveDebugInfo.ShouldBeTrue();
        settings.Exclusions.Namespaces.ShouldContain("Obfy.Core.Models");
        settings.Exclusions.Namespaces.ShouldContain("Obfy.App.Models");
        settings.Exclusions.Types.ShouldContain("<>*");
        return settings;
    }

    private static string Stage()
    {
        var fixture = Path.Combine(
            ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "ConsoleSolution");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        ScenarioHarness.DotnetBuild(Path.Combine(copy, "App", "ConsoleApp.csproj"));
        return copy;
    }

    private static string StageDirectory(string copy)
    {
        var appDir = Path.Combine(copy, "App", "bin", "Release", "net10.0");
        var staged = ScenarioHarness.StageBuildOutput(appDir, "ConsoleApp.dll");
        return Path.GetDirectoryName(staged)!;
    }
}

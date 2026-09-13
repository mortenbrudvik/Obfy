using dnlib.DotNet;
using Shouldly;

namespace Obfy.ScenarioTests;

public class ConsoleSolutionTests
{
    [Fact]
    public async Task ConsoleAppAndLibrary_PreservePublicApiOnLib_AndRun()
    {
        var fixture = Path.Combine(ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "ConsoleSolution");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            var appProj = Path.Combine(copy, "App", "ConsoleApp.csproj");
            File.Exists(appProj).ShouldBeTrue(appProj);
            ScenarioHarness.DotnetBuild(appProj);

            var appDir = Path.Combine(copy, "App", "bin", "Release", "net10.0");
            var staged = ScenarioHarness.StageBuildOutput(appDir, "ConsoleApp.dll");
            var stageDir = Path.GetDirectoryName(staged)!;
            var obfDir = Path.Combine(stageDir, "obf");
            ScenarioHarness.CopySidecars(stageDir, obfDir);

            await ScenarioHarness.ObfuscateAsync(
                Path.Combine(stageDir, "ConsoleLib.dll"),
                Path.Combine(obfDir, "ConsoleLib.dll"),
                ScenarioHarness.LoadSettings(Path.Combine(copy, "Lib", "obfy.json")));
            await ScenarioHarness.ObfuscateAsync(
                Path.Combine(stageDir, "ConsoleApp.dll"),
                Path.Combine(obfDir, "ConsoleApp.dll"),
                ScenarioHarness.LoadSettings(Path.Combine(copy, "App", "obfy.json")));

            using (var lib = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(obfDir, "ConsoleLib.dll"))))
            {
                lib.Types.ShouldContain(t => t.Name == "Calculator");
                lib.Types.ShouldContain(t => t.Methods.Any(m => m.Name == "Add"));
                lib.Types.ShouldNotContain(t => t.Methods.Any(m => m.Name == "Secret"));
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
}

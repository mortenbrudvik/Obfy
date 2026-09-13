using Shouldly;

namespace Obfy.ScenarioTests;

public class WinFormsAppTests
{
    [Fact]
    public async Task WinFormsApp_FormClickHandler_RunsAfterObfuscation()
    {
        var fixture = Path.Combine(ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "WinFormsApp");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            ScenarioHarness.DotnetBuild(Path.Combine(copy, "WinFormsApp.csproj"));

            var builtDir = Path.Combine(copy, "bin", "Release", "net10.0-windows");
            var staged = ScenarioHarness.StageBuildOutput(builtDir, "WinFormsApp.dll");
            var obfDir = Path.Combine(Path.GetDirectoryName(staged)!, "obf");
            var output = Path.Combine(obfDir, "WinFormsApp.dll");
            ScenarioHarness.CopySidecars(Path.GetDirectoryName(staged)!, obfDir, "WinFormsApp.dll");
            await ScenarioHarness.ObfuscateAsync(
                staged,
                output,
                ScenarioHarness.LoadSettings(Path.Combine(copy, "obfy.json")));

            var run = ScenarioHarness.RunDotnet(output, "--smoke");
            var smokeFile = Path.Combine(obfDir, "smoke.txt");
            var smoke = File.Exists(smokeFile) ? File.ReadAllText(smokeFile) : run.StdOut;
            run.ExitCode.ShouldBe(0, run.StdOut + run.StdErr + smoke);
            smoke.ShouldContain("SMOKE:ready");
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }
}

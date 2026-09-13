using dnlib.DotNet;
using Shouldly;

namespace Obfy.ScenarioTests;

public class WpfSolutionTests
{
    [Fact]
    public async Task WpfSolution_AppAndLibrary_BothObfuscatedAndRun()
    {
        var fixture = Path.Combine(ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "WpfSolution");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            var appProj = Path.Combine(copy, "App", "WpfApp.csproj");
            ScenarioHarness.DotnetBuild(appProj);

            var appDir = Path.Combine(copy, "App", "bin", "Release", "net10.0-windows");
            var libBuilt = Path.Combine(copy, "Lib", "bin", "Release", "net10.0", "WpfLib.dll");
            File.Exists(libBuilt).ShouldBeTrue(libBuilt);

            var stagedApp = ScenarioHarness.StageBuildOutput(appDir, "WpfApp.dll");
            var stageDir = Path.GetDirectoryName(stagedApp)!;
            var obfDir = Path.Combine(stageDir, "obf");
            Directory.CreateDirectory(obfDir);
            CopySidecars(stageDir, obfDir, skip: null);

            var libSettings = ScenarioHarness.LoadSettings(Path.Combine(copy, "Lib", "obfy.json"));
            var appSettings = ScenarioHarness.LoadSettings(Path.Combine(copy, "App", "obfy.json"));

            await ScenarioHarness.ObfuscateAsync(
                Path.Combine(stageDir, "WpfLib.dll"),
                Path.Combine(obfDir, "WpfLib.dll"),
                libSettings);
            await ScenarioHarness.ObfuscateAsync(
                Path.Combine(stageDir, "WpfApp.dll"),
                Path.Combine(obfDir, "WpfApp.dll"),
                appSettings);

            using (var lib = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(obfDir, "WpfLib.dll"))))
            {
                lib.Types.ShouldContain(t => t.Name == "Greeter");
                lib.Types.ShouldContain(t => t.Methods.Any(m => m.Name == "Hello"));
            }

            var run = ScenarioHarness.RunDotnet(Path.Combine(obfDir, "WpfApp.dll"), "--smoke");
            run.ExitCode.ShouldBe(0, run.StdOut + run.StdErr);
            run.StdOut.ShouldContain("SMOKE:from-lib");
        }
        finally
        {
            TryDelete(copy);
        }
    }

    private static void CopySidecars(string fromDir, string toDir, string? skip)
    {
        Directory.CreateDirectory(toDir);
        foreach (var file in Directory.GetFiles(fromDir))
        {
            if (skip != null && string.Equals(Path.GetFileName(file), skip, StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(toDir, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
    }
}

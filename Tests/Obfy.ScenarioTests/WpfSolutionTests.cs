using dnlib.DotNet;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services.Solution;
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
            CopySidecars(stageDir, obfDir);

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

    [Fact]
    public async Task WpfSolution_SolutionAnalyzerAndClosedSet_WithoutManualExclusion_PreservesMainWindowAndRuns()
    {
        var fixture = Path.Combine(ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "WpfSolution");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            File.WriteAllText(Path.Combine(copy, "App", "DashboardModel.cs"), """
                namespace WpfApp;

                public sealed class DashboardModel
                {
                    public string Caption { get; } = "should-be-renamed";
                }
                """);

            var sln = Path.Combine(copy, "WpfSolution.sln");
            ScenarioHarness.DotnetBuild(sln);

            var session = new SolutionAnalyzer().Analyze(sln);
            var appEntry = session.Included.Single(e => e.ProjectName == "WpfApp");
            var libEntry = session.Included.Single(e => e.ProjectName == "WpfLib");

            appEntry.Hints.PreserveXaml.ShouldBeTrue();
            appEntry.Hints.XamlTypes.ShouldContain("WpfApp.MainWindow");
            appEntry.Hints.XamlTypes.ShouldContain("WpfApp.App");
            libEntry.Hints.PreserveXaml.ShouldBeFalse();
            libEntry.Hints.PreservePublicApi.ShouldBeFalse();

            var appOutDir = Path.GetDirectoryName(appEntry.OutputPath!)!;
            var obfDir = Path.Combine(copy, "obf-closed");
            CopySidecars(appOutDir, obfDir, "WpfApp.dll", "WpfLib.dll");

            var settings = ScenarioHarness.LoadSettings(Path.Combine(copy, "App", "obfy.json"));
            settings.Exclusions.Types.Clear();
            settings.SymbolRenaming.PreserveXaml = false;

            var inputs = session.Included.Select(ClosedSetInput.FromIncluded).ToList();
            var result = await ScenarioHarness.ObfuscateClosedSetAsync(inputs, obfDir, settings);
            result.ModuleResults.ShouldAllBe(module =>
                !module.Warnings.Any(w => w.Contains("preserveXaml", StringComparison.Ordinal)));

            using (var app = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(obfDir, "WpfApp.dll"))))
            {
                app.Types.ShouldContain(t => t.Name == "MainWindow" && t.Namespace == "WpfApp");
                app.Types.ShouldContain(t => t.Name == "App" && t.Namespace == "WpfApp");
                app.Types.ShouldContain(t => t.Properties.Any(p => p.Name == "Title"));
                app.Types.ShouldNotContain(t => t.Name == "DashboardModel");
            }

            using (var lib = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(obfDir, "WpfLib.dll"))))
            {
                lib.Types.ShouldNotContain(t => t.Name == "Greeter");
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

    private static void CopySidecars(string fromDir, string toDir, params string[] skipAssemblies)
    {
        Directory.CreateDirectory(toDir);
        foreach (var file in Directory.GetFiles(fromDir))
        {
            var fileName = Path.GetFileName(file);
            if (skipAssemblies.Any(s => string.Equals(fileName, s, StringComparison.OrdinalIgnoreCase)))
                continue;
            File.Copy(file, Path.Combine(toDir, fileName), overwrite: true);
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
    }
}

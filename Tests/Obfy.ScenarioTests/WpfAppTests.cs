using dnlib.DotNet;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.ScenarioTests;

public class WpfAppTests
{
    [Fact]
    public Task WpfApp_PreserveXaml_WindowConstructsAndBindingSurvives() =>
        RunWpfAppScenarioAsync();

    [Fact]
    public Task WpfApp_PreserveXaml_WithoutManualMainWindowExclusion_PreservesMainWindowAndRuns() =>
        RunWpfAppScenarioAsync(settings =>
        {
            settings.Exclusions.Types.Clear();
            settings.SymbolRenaming.PreserveXaml.ShouldBeTrue();
        });

    private static async Task RunWpfAppScenarioAsync(Action<ObfySettings>? configureSettings = null)
    {
        var fixture = Path.Combine(ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "WpfApp");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            var csproj = Path.Combine(copy, "WpfApp.csproj");
            ScenarioHarness.DotnetBuild(csproj);

            var builtDir = Path.Combine(copy, "bin", "Release", "net10.0-windows");
            var staged = ScenarioHarness.StageBuildOutput(builtDir, "WpfApp.dll");
            var settings = ScenarioHarness.LoadSettings(Path.Combine(copy, "obfy.json"));
            configureSettings?.Invoke(settings);

            var obfDir = Path.Combine(Path.GetDirectoryName(staged)!, "obf");
            var output = Path.Combine(obfDir, "WpfApp.dll");
            CopySidecars(Path.GetDirectoryName(staged)!, obfDir, "WpfApp.dll");
            var result = await ScenarioHarness.ObfuscateAsync(staged, output, settings);
            result.Warnings.ShouldNotContain(w => w.Contains("preserveXaml", StringComparison.Ordinal));

            using (var module = ModuleDefMD.Load(File.ReadAllBytes(output)))
            {
                module.Types.ShouldContain(t => t.Name == "MainWindow" && t.Namespace == "WpfApp");
                module.Types.ShouldContain(t => t.Name == "App" && t.Namespace == "WpfApp");
                module.Types.ShouldContain(t => t.Properties.Any(p => p.Name == "Title"));
                module.Types.ShouldNotContain(t => t.Name == "DashboardModel");
            }

            var run = ScenarioHarness.RunDotnet(output, "--smoke");
            run.ExitCode.ShouldBe(0, run.StdOut + run.StdErr);
            run.StdOut.ShouldContain("SMOKE:Hello from binding");
        }
        finally
        {
            TryDelete(copy);
        }
    }

    private static void CopySidecars(string fromDir, string toDir, string skipAssembly)
    {
        Directory.CreateDirectory(toDir);
        foreach (var file in Directory.GetFiles(fromDir))
        {
            if (string.Equals(Path.GetFileName(file), skipAssembly, StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(toDir, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
    }
}

using dnlib.DotNet;
using Shouldly;

namespace Obfy.ScenarioTests;

public class WpfAppTests
{
    [Fact]
    public async Task WpfApp_PreserveXaml_WindowConstructsAndBindingSurvives()
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
            var obfDir = Path.Combine(Path.GetDirectoryName(staged)!, "obf");
            var output = Path.Combine(obfDir, "WpfApp.dll");
            CopySidecars(Path.GetDirectoryName(staged)!, obfDir, "WpfApp.dll");
            await ScenarioHarness.ObfuscateAsync(staged, output, settings);

            using (var module = ModuleDefMD.Load(File.ReadAllBytes(output)))
            {
                module.Types.ShouldContain(t => t.Name == "MainWindow");
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

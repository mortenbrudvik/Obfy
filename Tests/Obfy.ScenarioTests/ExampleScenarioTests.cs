using System.Runtime.Loader;
using dnlib.DotNet;
using Shouldly;

namespace Obfy.ScenarioTests;

public class ExampleScenarioTests
{
    [Fact]
    public async Task BasicConsoleApp_Example_BuildsObfuscatesAndRuns()
    {
        var repo = ScenarioHarness.FindRepoRoot();
        var source = Path.Combine(repo, "examples", "BasicConsoleApp");
        var copy = ScenarioHarness.CopyToTemp(source);
        try
        {
            var csproj = Path.Combine(copy, "BasicConsoleApp.csproj");
            ScenarioHarness.DotnetBuild(csproj);

            var built = Path.Combine(copy, "bin", "Release", "net10.0", "BasicConsoleApp.dll");
            File.Exists(built).ShouldBeTrue(built);

            var staged = ScenarioHarness.StageBuildOutput(Path.GetDirectoryName(built)!, "BasicConsoleApp.dll");
            var settings = ScenarioHarness.LoadSettings(Path.Combine(copy, "obfy.json"));
            var output = Path.Combine(Path.GetDirectoryName(staged)!, "obf", "BasicConsoleApp.dll");
            CopySidecars(Path.GetDirectoryName(staged)!, Path.GetDirectoryName(output)!, "BasicConsoleApp.dll");
            await ScenarioHarness.ObfuscateAsync(staged, output, settings);

            var run = ScenarioHarness.RunDotnet(output);
            run.ExitCode.ShouldBe(0, run.StdOut + run.StdErr);
            run.StdOut.ShouldContain("Welcome to the application");
        }
        finally
        {
            TryDelete(copy);
        }
    }

    [Fact]
    public async Task LibraryWithPublicApi_Example_PreservesCalculatorAndRunsAdd()
    {
        var repo = ScenarioHarness.FindRepoRoot();
        var source = Path.Combine(repo, "examples", "LibraryWithPublicApi");
        var copy = ScenarioHarness.CopyToTemp(source);
        try
        {
            var csproj = Path.Combine(copy, "LibraryWithPublicApi.csproj");
            ScenarioHarness.DotnetBuild(csproj);

            var built = Path.Combine(copy, "bin", "Release", "net10.0", "LibraryWithPublicApi.dll");
            File.Exists(built).ShouldBeTrue(built);

            var staged = ScenarioHarness.StageBuildOutput(Path.GetDirectoryName(built)!, "LibraryWithPublicApi.dll");
            var settings = ScenarioHarness.LoadSettings(Path.Combine(copy, "obfy.json"));
            var output = Path.Combine(Path.GetDirectoryName(staged)!, "obf", "LibraryWithPublicApi.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await ScenarioHarness.ObfuscateAsync(staged, output, settings);

            using var module = ModuleDefMD.Load(File.ReadAllBytes(output));
            module.Types.ShouldContain(t => t.Name == "Calculator");
            module.Types.ShouldNotContain(t => t.Name == "InternalHelper");

            var alc = new AssemblyLoadContext("lib-scen-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                var asm = alc.LoadFromAssemblyPath(output);
                var calculator = asm.GetType("MyLibrary.Calculator");
                calculator.ShouldNotBeNull();
                var add = calculator!.GetMethod("Add");
                add.ShouldNotBeNull();
                var instance = Activator.CreateInstance(calculator);
                add!.Invoke(instance, [2, 3]).ShouldBe(5);
            }
            finally
            {
                alc.Unload();
            }
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

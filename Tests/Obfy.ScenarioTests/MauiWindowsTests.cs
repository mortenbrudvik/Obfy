using System.Text.RegularExpressions;
using dnlib.DotNet;
using Shouldly;

namespace Obfy.ScenarioTests;

[Trait("Category", "Platform")]
public class MauiWindowsTests
{
    [PlatformFact("maui")]
    public async Task MauiWindows_PreserveXaml_ObfuscatesBuiltOutput()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-maui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var created = ScenarioHarness.RunProcess(
                "dotnet",
                $"new maui -n MauiSmoke -o \"{dir}\" --force",
                dir,
                60_000);
            created.ExitCode.ShouldBe(0, created.StdOut + created.StdErr);

            var csproj = Directory.GetFiles(dir, "MauiSmoke.csproj", SearchOption.AllDirectories).FirstOrDefault();
            csproj.ShouldNotBeNull();
            var projectDir = Path.GetDirectoryName(csproj)!;
            var text = File.ReadAllText(csproj!);
            var tfmMatch = Regex.Match(text, @"net[0-9.]+-windows[0-9.]*");
            tfmMatch.Success.ShouldBeTrue("No windows TFM in maui template");
            var tfm = tfmMatch.Value;

            File.WriteAllText(Path.Combine(projectDir, "Secret.cs"), """
                namespace MauiSmoke;
                public static class Secret
                {
                    public static string Get() => "maui-secret";
                }
                """);

            var build = ScenarioHarness.RunProcess(
                "dotnet",
                $"build \"{csproj}\" -c Release -f {tfm} --nologo",
                projectDir,
                300_000);
            build.ExitCode.ShouldBe(0, build.StdOut + build.StdErr);

            var dll = Directory.GetFiles(projectDir, "MauiSmoke.dll", SearchOption.AllDirectories)
                .FirstOrDefault(p =>
                    p.Contains("Release", StringComparison.OrdinalIgnoreCase) &&
                    p.Contains("windows", StringComparison.OrdinalIgnoreCase));
            dll.ShouldNotBeNull();

            var settings = ScenarioHarness.LoadSettings(
                Path.Combine(ScenarioHarness.FindRepoRoot(), "examples", "maui", "obfy.json"));
            settings.SymbolRenaming.PreserveXaml.ShouldBeTrue();
            settings.Protection.MethodEncryption.ShouldBeFalse();

            var output = Path.Combine(Path.GetDirectoryName(dll)!, "MauiSmoke.obf.dll");
            await ScenarioHarness.ObfuscateAsync(dll!, output, settings);

            using var module = ModuleDefMD.Load(File.ReadAllBytes(output));
            module.Types.ShouldNotContain(t => t.Name == "Secret");
            var il = string.Join(" ", module.Types.SelectMany(t => t.Methods)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions.Select(i => i.Operand?.ToString() ?? "")));
            il.ShouldNotContain("maui-secret");
        }
        finally
        {
            ScenarioHarness.TryDelete(dir);
        }
    }
}

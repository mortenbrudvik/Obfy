using System.Runtime.Loader;
using dnlib.DotNet;
using Shouldly;

namespace Obfy.ScenarioTests;

public class UnitySampleTests
{
    [Fact]
    public async Task UnityIl2CppProfile_OnStubAssembly_PreservesEngineTypes_AndRuns()
    {
        var fixture = Path.Combine(ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "UnitySample");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            var csproj = Path.Combine(copy, "UnitySample.csproj");
            ScenarioHarness.DotnetBuild(csproj);

            var built = Path.Combine(copy, "bin", "Release", "net10.0", "UnitySample.dll");
            File.Exists(built).ShouldBeTrue(built);

            var settings = ScenarioHarness.LoadSettings(
                Path.Combine(ScenarioHarness.FindRepoRoot(), "examples", "unity", "obfy.json"));
            // Exclusions match type.Name (not FullName). Keep the invoke target findable.
            settings.Exclusions.Types.Add("GameEntry");
            settings.RuntimeProfile.ShouldBe(Obfy.Core.Models.RuntimeProfile.UnityIl2Cpp);

            var output = Path.Combine(copy, "UnitySample.obf.dll");
            await ScenarioHarness.ObfuscateAsync(built, output, settings);

            using (var module = ModuleDefMD.Load(File.ReadAllBytes(output)))
            {
                module.Types.ShouldContain(t => t.Name == "MonoBehaviour" && t.Namespace == "UnityEngine");
                module.Types.ShouldContain(t => t.Name == "GameEntry");
                module.Types.ShouldNotContain(t => t.Name == "Player");
                var il = string.Join(" ", module.Types.SelectMany(t => t.Methods)
                    .Where(m => m.HasBody)
                    .SelectMany(m => m.Body.Instructions.Select(i => i.Operand?.ToString() ?? "")));
                il.ShouldNotContain("unity-secret");
            }

            var alc = new AssemblyLoadContext("unity-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                var asm = alc.LoadFromAssemblyPath(output);
                var entry = asm.GetTypes().FirstOrDefault(t => t.Name == "GameEntry");
                entry.ShouldNotBeNull();
                entry!.GetMethod("Ping")!.Invoke(null, null).ShouldBe("unity-secret");
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }
}

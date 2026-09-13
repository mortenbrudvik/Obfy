using System.Runtime.Loader;
using System.Security.Cryptography;
using dnlib.DotNet;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.ScenarioTests;

public class SatelliteTests
{
    [Fact]
    public void DefaultExclude_SkipsSatelliteAssemblyFileName()
    {
        WildcardMatcher.IsMatch("SatelliteLib.resources.dll", "*.resources.dll").ShouldBeTrue();
        WildcardMatcher.IsMatch("SatelliteLib.dll", "*.resources.dll").ShouldBeFalse();
    }

    [Fact]
    public async Task SatelliteResourcesDll_StaysUnobfuscated_AndParentStillLoads()
    {
        var fixture = Path.Combine(ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "SatelliteLib");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            ScenarioHarness.DotnetBuild(Path.Combine(copy, "SatelliteLib.csproj"));

            var builtDir = Path.Combine(copy, "bin", "Release", "net10.0");
            var satellite = Path.Combine(builtDir, "de", "SatelliteLib.resources.dll");
            File.Exists(satellite).ShouldBeTrue(satellite);
            var originalHash = SHA256.HashData(File.ReadAllBytes(satellite));

            var staged = ScenarioHarness.StageBuildOutput(builtDir, "SatelliteLib.dll");
            var stageDir = Path.GetDirectoryName(staged)!;
            var obfDir = Path.Combine(stageDir, "obf");
            var output = Path.Combine(obfDir, "SatelliteLib.dll");
            ScenarioHarness.CopySidecars(stageDir, obfDir, "SatelliteLib.dll");
            await ScenarioHarness.ObfuscateAsync(
                staged,
                output,
                ScenarioHarness.LoadSettings(Path.Combine(copy, "obfy.json")));

            var outputSatellite = Path.Combine(obfDir, "de", "SatelliteLib.resources.dll");
            File.Exists(outputSatellite).ShouldBeTrue(outputSatellite);
            SHA256.HashData(File.ReadAllBytes(outputSatellite)).ShouldBe(originalHash);

            using (var module = ModuleDefMD.Load(File.ReadAllBytes(output)))
            {
                module.Resources.Any(r =>
                    r.Name.String.Contains("Obfy.Embedded", StringComparison.OrdinalIgnoreCase) &&
                    r.Name.String.Contains("resources.dll", StringComparison.OrdinalIgnoreCase))
                    .ShouldBeFalse();
            }

            var alc = new AssemblyLoadContext("sat-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                var asm = alc.LoadFromAssemblyPath(output);
                var greeter = asm.GetType("SatelliteLib.Greeter");
                greeter.ShouldNotBeNull();
                var hello = greeter!.GetMethod("Hello");
                hello.ShouldNotBeNull();
                hello!.Invoke(null, ["en"]).ShouldBe("Hello");
                hello.Invoke(null, ["de"]).ShouldBe("Hallo");
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

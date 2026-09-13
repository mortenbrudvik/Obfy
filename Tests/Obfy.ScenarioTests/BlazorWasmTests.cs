using System.Runtime.Loader;
using dnlib.DotNet;
using Shouldly;

namespace Obfy.ScenarioTests;

[Trait("Category", "Platform")]
public class BlazorWasmTests
{
    [PlatformFact(PlatformWorkload.Blazor)]
    public async Task PublishedFrameworkDll_ObfuscatesWithBlazorWasmProfile_AndLoads()
    {
        var fixture = Path.Combine(ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "BlazorWasmApp");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            var csproj = Path.Combine(copy, "BlazorWasmApp.csproj");
            var publish = ScenarioHarness.DotnetPublish(csproj, "-c Release");
            publish.ExitCode.ShouldBe(0, publish.StdOut + publish.StdErr);

            var frameworkDir = Directory.GetDirectories(Path.Combine(copy, "bin"), "_framework", SearchOption.AllDirectories)
                .FirstOrDefault();
            frameworkDir.ShouldNotBeNull("_framework not found after publish");
            var appDll = Directory.GetFiles(frameworkDir!, "BlazorWasmApp*.dll")
                .FirstOrDefault(p => !p.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));
            appDll.ShouldNotBeNull($"BlazorWasmApp*.dll not in {frameworkDir}");

            var settings = ScenarioHarness.LoadSettings(
                Path.Combine(ScenarioHarness.FindRepoRoot(), "examples", "blazor", "obfy.json"));
            // Exclusions match type.Name. Probe calls Secret so we can invoke after Secret is renamed.
            settings.Exclusions.Types.Add("Probe");
            var output = Path.Combine(frameworkDir, "BlazorWasmApp.obf.dll");
            await ScenarioHarness.ObfuscateAsync(appDll, output, settings);

            settings.RuntimeProfile.ShouldBe(Obfy.Core.Models.RuntimeProfile.BlazorWasm);
            using (var module = ModuleDefMD.Load(File.ReadAllBytes(output)))
            {
                module.Types.ShouldContain(t => t.Name == "Probe");
                module.Types.ShouldNotContain(t => t.Name == "Secret");
                var il = string.Join(" ", module.Types.SelectMany(t => t.Methods)
                    .Where(m => m.HasBody)
                    .SelectMany(m => m.Body.Instructions.Select(i => i.Operand?.ToString() ?? "")));
                il.ShouldNotContain("blazor-secret");
            }

            File.Copy(output, appDll, overwrite: true);
            var alc = new AssemblyLoadContext("blazor-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            alc.Resolving += (_, name) =>
            {
                var path = FindFrameworkAssembly(frameworkDir, name.Name);
                return path != null ? alc.LoadFromAssemblyPath(path) : null;
            };
            try
            {
                var asm = alc.LoadFromAssemblyPath(appDll);
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }

                var probe = types.FirstOrDefault(t => t.Name == "Probe");
                probe.ShouldNotBeNull();
                var ping = probe!.GetMethod("Ping", Type.EmptyTypes);
                ping!.Invoke(null, null).ShouldBe("blazor-secret");
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

    private static string? FindFrameworkAssembly(string frameworkDir, string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        var exact = Path.Combine(frameworkDir, assemblyName + ".dll");
        if (File.Exists(exact))
            return exact;

        foreach (var file in Directory.GetFiles(frameworkDir, "*.dll"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (stem.Equals(assemblyName, StringComparison.OrdinalIgnoreCase))
                return file;
            if (!stem.StartsWith(assemblyName + ".", StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = stem[(assemblyName.Length + 1)..];
            if (!rest.Contains('.'))
                return file;
        }

        return null;
    }
}

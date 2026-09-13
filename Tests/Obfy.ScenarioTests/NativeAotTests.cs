using dnlib.DotNet;
using Shouldly;

namespace Obfy.ScenarioTests;

[Trait("Category", "Platform")]
public class NativeAotTests
{
    [PlatformFact]
    public async Task ObfuscateBeforePublishAot_NativeExeRuns()
    {
        var fixture = Path.Combine(ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "NativeAotApp");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            var csproj = Path.Combine(copy, "NativeAotApp.csproj");
            var rid = PlatformWorkloads.CurrentWindowsRid();
            ScenarioHarness.DotnetBuild(csproj, extraArgs: $"-r {rid} -p:PublishAot=false", timeoutMs: 120_000);

            var builtDir = Path.Combine(copy, "bin", "Release", "net10.0", rid);
            if (!Directory.Exists(builtDir))
                builtDir = Path.Combine(copy, "bin", "Release", "net10.0");
            var builtDll = Path.Combine(builtDir, "NativeAotApp.dll");
            File.Exists(builtDll).ShouldBeTrue(builtDll);

            var settings = ScenarioHarness.LoadSettings(Path.Combine(copy, "obfy.json"));
            settings.RuntimeProfile.ShouldBe(Obfy.Core.Models.RuntimeProfile.NativeAot);
            await ScenarioHarness.ObfuscateAsync(builtDll, builtDll, settings);

            using (var module = ModuleDefMD.Load(File.ReadAllBytes(builtDll)))
            {
                var il = string.Join(" ", module.Types.SelectMany(t => t.Methods)
                    .Where(m => m.HasBody)
                    .SelectMany(m => m.Body.Instructions.Select(i => i.Operand?.ToString() ?? "")));
                il.ShouldNotContain("aot-secret");
            }

            // ILC reads IntermediateAssembly under obj/. Stamp every copy so incremental
            // publish does not recompile from source and undo the obfuscation.
            var stamp = DateTime.UtcNow.AddMinutes(2);
            foreach (var dll in Directory.GetFiles(copy, "NativeAotApp.dll", SearchOption.AllDirectories))
            {
                if (!string.Equals(dll, builtDll, StringComparison.OrdinalIgnoreCase))
                    File.Copy(builtDll, dll, overwrite: true);
                File.SetLastWriteTimeUtc(dll, stamp);
            }

            var publish = ScenarioHarness.DotnetPublish(
                csproj,
                $"-c Release -r {rid} -p:PublishAot=true",
                timeoutMs: 360_000);
            publish.ExitCode.ShouldBe(0,
                "NativeAOT publish failed. First run restores Microsoft.DotNet.ILCompiler from NuGet.\n"
                + publish.StdOut + publish.StdErr);
            publish.StdOut.ShouldContain("Generating native code");

            var exe = Directory.GetFiles(Path.Combine(copy, "bin"), "NativeAotApp.exe", SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Contains("publish", StringComparison.OrdinalIgnoreCase))
                ?? Directory.GetFiles(Path.Combine(copy, "bin"), "NativeAotApp.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(p => p.Contains("native", StringComparison.OrdinalIgnoreCase));
            exe.ShouldNotBeNull("NativeAOT publish did not produce NativeAotApp.exe");
            // Managed apphost is ~150 KB; invariant-globalization NativeAOT hello-world is ~0.9–1.2 MB.
            new FileInfo(exe!).Length.ShouldBeGreaterThan(400_000,
                "publish output looks like a managed apphost, not a NativeAOT binary");

            var exeBytes = File.ReadAllBytes(exe!);
            ContainsText(exeBytes, "aot-secret").ShouldBeFalse(
                "plaintext aot-secret in the native exe; ILC likely recompiled unobfuscated IL");

            var run = ScenarioHarness.RunProcess(exe!, "", Path.GetDirectoryName(exe)!, 20_000);
            run.ExitCode.ShouldBe(0, run.StdOut + run.StdErr);
            run.StdOut.ShouldContain("SMOKE:10");
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }

    private static bool ContainsText(byte[] haystack, string needle)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(needle);
        var utf16 = System.Text.Encoding.Unicode.GetBytes(needle);
        return haystack.AsSpan().IndexOf(utf8) >= 0 || haystack.AsSpan().IndexOf(utf16) >= 0;
    }
}

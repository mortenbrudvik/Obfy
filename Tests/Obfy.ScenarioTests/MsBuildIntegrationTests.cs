using dnlib.DotNet;
using Shouldly;

namespace Obfy.ScenarioTests;

public class MsBuildIntegrationTests
{
    [Fact]
    public void MsBuildAfterBuild_ObfuscatesReleaseOutput_AndAppRuns()
    {
        var repo = ScenarioHarness.FindRepoRoot();
        var source = Path.Combine(repo, "examples", "MsBuildIntegration");
        var copy = ScenarioHarness.CopyToTemp(source);
        try
        {
            var obfyDll = ScenarioHarness.FindObfyCli();
            var csprojPath = Path.Combine(copy, "MsBuildIntegration.csproj");
            var csproj = File.ReadAllText(csprojPath);
            const string originalExec =
                """<Exec Command="obfy &quot;$(TargetPath)&quot; -c obfy.json -o &quot;$(TargetDir)&quot;" />""";
            const string patchedExec =
                """<Exec Command="dotnet exec &quot;$(ObfyDll)&quot; &quot;$(TargetPath)&quot; -c obfy.json -o &quot;$([System.IO.Path]::GetDirectoryName($(TargetPath)))&quot; --no-logo" />""";
            csproj.Contains(originalExec).ShouldBeTrue("example AfterBuild Exec command changed");
            File.WriteAllText(csprojPath, csproj.Replace(originalExec, patchedExec, StringComparison.Ordinal));

            ScenarioHarness.DotnetBuild(csprojPath, extraArgs: $"-p:ObfyDll=\"{obfyDll}\"");

            var dll = Path.Combine(copy, "bin", "Release", "net10.0", "MsBuildIntegration.dll");
            File.Exists(dll).ShouldBeTrue(dll);
            var ascii = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(dll));
            ascii.ShouldNotContain("api-secret-key-12345");

            using var module = ModuleDefMD.Load(File.ReadAllBytes(dll));
            module.Types.ShouldNotContain(t => t.Name == "DataProcessor");
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }
}

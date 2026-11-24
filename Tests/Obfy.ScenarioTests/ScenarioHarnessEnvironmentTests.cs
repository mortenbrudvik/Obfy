using Shouldly;

namespace Obfy.ScenarioTests;

public class ScenarioHarnessEnvironmentTests
{
    private static readonly string[] StrippedDotnetVars =
    [
        "MSBUILD_EXE_PATH",
        "MSBuildSDKsPath",
        "MSBuildExtensionsPath",
        "MSBuildUserExtensionsPath",
        "MSBuildLoadMicrosoftTargetsReadOnly",
        "MSBuildLoadReadOnly",
        "MSBUILDFAILONDRIVEENUMERATINGWILDCARD",
        "MSBUILDUSESERVER",
        "DOTNET_HOST_PATH",
        "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR",
        "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR",
        "DOTNET_MSBUILD_SDK_RESOLVER_ENABLE_LOG",
        "DOTNET_STARTUP_HOOKS"
    ];

    private static readonly (string Key, string Value)[] PreservedToolchainVars =
    [
        ("DOTNET_ROOT", @"C:\Program Files\dotnet"),
        ("NUGET_PACKAGES", @"C:\Users\runner\.nuget\packages"),
        ("PATH", @"C:\Program Files\dotnet;C:\Windows\System32"),
        ("VSINSTALLDIR", @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\"),
        ("VCINSTALLDIR", @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\"),
        ("VCTargetsPath", @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Microsoft\VC\v170\"),
        ("VisualStudioVersion", "17.0")
    ];

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet.exe")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    public void SanitizeChildEnvironment_StripsInheritedMsBuildAndResolverVarsForDotnet(string fileName)
    {
        var env = CreateSampleEnvironment();

        ScenarioHarness.SanitizeChildEnvironment(env, fileName, "build app.csproj -c Release");

        env["MSBUILDDISABLENODEREUSE"].ShouldBe("1");
        env["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"].ShouldBe("1");
        env.ShouldNotContainKey("Platform");

        foreach (var stripped in StrippedDotnetVars)
            env.ShouldNotContainKey(stripped);

        foreach (var (key, value) in PreservedToolchainVars)
            env[key].ShouldBe(value);
    }

    [Fact]
    public void SanitizeChildEnvironment_KeepsPlatformWhenSpecifiedOnDotnet_WhileStillStrippingMsBuildVars()
    {
        var env = CreateSampleEnvironment();

        ScenarioHarness.SanitizeChildEnvironment(env, "dotnet", "build app.csproj -p:Platform=x64");

        env["Platform"].ShouldBe("x64");
        foreach (var stripped in StrippedDotnetVars)
            env.ShouldNotContainKey(stripped);
        foreach (var (key, value) in PreservedToolchainVars)
            env[key].ShouldBe(value);
    }

    [Fact]
    public void SanitizeChildEnvironment_PreservesInheritedVarsForNonDotnetExecutables()
    {
        var env = CreateSampleEnvironment();

        ScenarioHarness.SanitizeChildEnvironment(env, "HelloConsole.exe", "--smoke");

        env["MSBUILDDISABLENODEREUSE"].ShouldBe("1");
        env["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"].ShouldBe("1");
        env["Platform"].ShouldBe("x64");

        foreach (var stripped in StrippedDotnetVars)
            env.ShouldContainKey(stripped);
        foreach (var (key, value) in PreservedToolchainVars)
            env[key].ShouldBe(value);
    }

    [Fact]
    public void CopyToTemp_CopiesRepoGlobalJsonIntoTempFixtureFolder()
    {
        var repoRoot = ScenarioHarness.FindRepoRoot();
        var fixture = Path.Combine(repoRoot, "Tests", "Obfy.ScenarioTests", "Fixtures", "HelloConsole");

        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            var copiedGlobalJson = Path.Combine(copy, "global.json");
            File.Exists(copiedGlobalJson).ShouldBeTrue(copiedGlobalJson);
            File.ReadAllText(copiedGlobalJson).ShouldBe(
                File.ReadAllText(Path.Combine(repoRoot, "global.json")));
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }

    [Fact]
    public void CopyToTemp_DoesNotOverwriteExistingFixtureGlobalJson()
    {
        var source = Path.Combine(Path.GetTempPath(), "obfy-src-global-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        const string customGlobalJson = """{ "sdk": { "version": "10.0.200" } }""";
        File.WriteAllText(Path.Combine(source, "global.json"), customGlobalJson);

        string? copy = null;
        try
        {
            copy = ScenarioHarness.CopyToTemp(source);
            File.ReadAllText(Path.Combine(copy, "global.json")).ShouldBe(customGlobalJson);
        }
        finally
        {
            if (copy != null)
                ScenarioHarness.TryDelete(copy);
            ScenarioHarness.TryDelete(source);
        }
    }

    private static Dictionary<string, string?> CreateSampleEnvironment()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Platform"] = "x64",
            ["MSBUILDDISABLENODEREUSE"] = "0",
            ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "0"
        };

        foreach (var key in StrippedDotnetVars)
            env[key] = "inherited-value";

        foreach (var (key, value) in PreservedToolchainVars)
            env[key] = value;

        return env;
    }
}

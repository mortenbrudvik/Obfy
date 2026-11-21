using System.Runtime.InteropServices;
using dnlib.DotNet;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.ScenarioTests;

/// <summary>
/// An SDK-built console app (core library reference <c>System.Runtime</c>, or <c>mscorlib</c> on .NET Framework, as
/// <c>dotnet new console</c> produces) is protected and then run. The framework types the injected code names must
/// resolve at run time through the target framework's own assemblies, and those differ per target: the
/// <c>System.Runtime</c> facade of net8 does not carry <c>DllNotFoundException</c> or <c>Marshal</c>, that of net6
/// does not carry <c>RuntimeInformation</c> or the hashing types, that of net5 not even <c>File</c>, and the
/// <c>System.Security.Cryptography</c> assembly does not exist before .NET 7. A reference to a type the scoped
/// assembly does not carry fails with a <see cref="TypeLoadException"/> or <see cref="FileNotFoundException"/> before
/// user code runs. <c>EndToEndObfuscationTests</c> compiles against the runtime's implementation assemblies, whose
/// core library is <c>System.Private.CoreLib</c>, so it cannot see this.
/// </summary>
/// <remarks>
/// Each target framework has one theory with the same cases: the Aggressive preset, and AntiDebug, AntiDump,
/// MethodEncryption, AntiTamper, AES string encryption and Virtualization each alone, then Virtualization together
/// with symbol renaming (which renames the interpreter and everything it reaches), so a failure names the technique;
/// plus the <c>RichConsole</c> fixture (embedded resource, resx, a sibling assembly) with every technique at once,
/// renaming included. A target is skipped, visibly, on a machine without its runtime or reference assemblies.
/// </remarks>
public sealed class ProtectedConsoleStartupTests : IClassFixture<BuiltFixtures>
{
    private const string ExpectedHello = "Hello, World!\nSum:163";

    private readonly BuiltFixtures _built;

    public ProtectedConsoleStartupTests(BuiltFixtures built) => _built = built;

    public static TheoryData<string> Protections => new()
    {
        "Aggressive",
        "AntiDebug",
        "AntiDump",
        "MethodEncryption",
        "AntiTamper",
        "StringsAes",
        "Virtualization",
        "VirtualizationWithRenaming"
    };

    [SdkRuntimeTheory("net10.0", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net10_HelloConsole_StartsAndPrints(string protection) => AssertHelloStartsAsync("net10.0", protection);

    [SdkRuntimeTheory("net9.0", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net9_HelloConsole_StartsAndPrints(string protection) => AssertHelloStartsAsync("net9.0", protection);

    [SdkRuntimeTheory("net8.0", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net8_HelloConsole_StartsAndPrints(string protection) => AssertHelloStartsAsync("net8.0", protection);

    [SdkRuntimeTheory("net7.0", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net7_HelloConsole_StartsAndPrints(string protection) => AssertHelloStartsAsync("net7.0", protection);

    [SdkRuntimeTheory("net6.0", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net6_HelloConsole_StartsAndPrints(string protection) => AssertHelloStartsAsync("net6.0", protection);

    [SdkRuntimeTheory("net5.0", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net5_HelloConsole_StartsAndPrints(string protection) => AssertHelloStartsAsync("net5.0", protection);

    [SdkRuntimeTheory("netcoreapp3.1", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task NetCoreApp31_HelloConsole_StartsAndPrints(string protection) => AssertHelloStartsAsync("netcoreapp3.1", protection);

    [SdkRuntimeTheory("net48", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net48_HelloConsole_StartsAndPrints(string protection) => AssertHelloStartsAsync("net48", protection);

    [SdkRuntimeTheory("net461", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net461_HelloConsole_StartsAndPrints(string protection) => AssertHelloStartsAsync("net461", protection);

    [SdkRuntimeFact("net10.0", windowsOnly: true)]
    public Task Net10_RichConsole_WithEveryTechnique_RunsLikeTheOriginal() => AssertRichRunsLikeTheOriginalAsync("net10.0");

    [SdkRuntimeFact("net9.0", windowsOnly: true)]
    public Task Net9_RichConsole_WithEveryTechnique_RunsLikeTheOriginal() => AssertRichRunsLikeTheOriginalAsync("net9.0");

    [SdkRuntimeFact("net8.0", windowsOnly: true)]
    public Task Net8_RichConsole_WithEveryTechnique_RunsLikeTheOriginal() => AssertRichRunsLikeTheOriginalAsync("net8.0");

    [SdkRuntimeFact("net7.0", windowsOnly: true)]
    public Task Net7_RichConsole_WithEveryTechnique_RunsLikeTheOriginal() => AssertRichRunsLikeTheOriginalAsync("net7.0");

    [SdkRuntimeFact("net6.0", windowsOnly: true)]
    public Task Net6_RichConsole_WithEveryTechnique_RunsLikeTheOriginal() => AssertRichRunsLikeTheOriginalAsync("net6.0");

    [SdkRuntimeFact("net5.0", windowsOnly: true)]
    public Task Net5_RichConsole_WithEveryTechnique_RunsLikeTheOriginal() => AssertRichRunsLikeTheOriginalAsync("net5.0");

    [SdkRuntimeFact("netcoreapp3.1", windowsOnly: true)]
    public Task NetCoreApp31_RichConsole_WithEveryTechnique_RunsLikeTheOriginal() => AssertRichRunsLikeTheOriginalAsync("netcoreapp3.1");

    [SdkRuntimeFact("net48", windowsOnly: true)]
    public Task Net48_RichConsole_WithEveryTechnique_RunsLikeTheOriginal() => AssertRichRunsLikeTheOriginalAsync("net48");

    [SdkRuntimeFact("net461", windowsOnly: true)]
    public Task Net461_RichConsole_WithEveryTechnique_RunsLikeTheOriginal() => AssertRichRunsLikeTheOriginalAsync("net461");

    private async Task AssertHelloStartsAsync(string targetFramework, string protection)
    {
        var (staged, _, output) = Stage(_built.Hello(targetFramework), "HelloConsole", targetFramework);

        var result = await ScenarioHarness.ObfuscateAsync(staged, output, SettingsFor(protection));
        if (protection.StartsWith("Virtualization", StringComparison.Ordinal))
        {
            result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(
                0, $"{protection} virtualized nothing, so this run would prove nothing about it");
        }

        var marker = InjectedTypeFor(protection);
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(output)))
        {
            if (marker is not null)
            {
                module.Types.ShouldContain(
                    t => t.Namespace == "Obfy.Runtime" && t.Name == marker,
                    $"{protection} did not inject {marker}, so this run would prove nothing about it");
            }
            else if (protection == "AntiTamper" || protection == "StringsAes")
            {
                module.Types.ShouldContain(
                    t => t.Namespace == "Obfy.Runtime",
                    $"{protection} did not inject its helper, so this run would prove nothing about it");
            }
        }

        var run = Run(output);
        run.ExitCode.ShouldBe(0, $"{protection} on {targetFramework}:{Environment.NewLine}{run.StdOut}{run.StdErr}");
        run.StdOut.ReplaceLineEndings("\n").Trim().ShouldBe(ExpectedHello, $"{protection} on {targetFramework}");

        // Anti-dump must do what it says, not just start: it overwrites the first byte of MiniDumpWriteDump with a
        // ret. (A failed hook is swallowed at run time, so the app starts either way.) ARM64 is skipped by design.
        if (protection is "AntiDump" or "Aggressive")
        {
            var expected = RuntimeInformation.ProcessArchitecture is Architecture.X86 or Architecture.X64 ? "patched" : "intact";
            var probe = Run(output, "--probe-dump");
            probe.ExitCode.ShouldBe(0, $"{protection} on {targetFramework}:{Environment.NewLine}{probe.StdOut}{probe.StdErr}");
            probe.StdOut.ShouldContain("dump-hook:" + expected, customMessage: $"{protection} on {targetFramework}");
        }
    }

    private async Task AssertRichRunsLikeTheOriginalAsync(string targetFramework)
    {
        var buildOutput = _built.Rich(targetFramework);
        var original = Run(Path.Combine(buildOutput, AssemblyFileName("RichConsole", targetFramework)));
        original.ExitCode.ShouldBe(0, $"the unprotected fixture on {targetFramework}:{Environment.NewLine}{original.StdOut}{original.StdErr}");
        original.StdOut.ShouldContain("virtual!");

        // The Aggressive preset with the options a preset leaves alone: embedding the sibling, AES, a watermark.
        var preset = new ObfySettings { Level = ObfuscationLevel.Aggressive };
        EmbedSiblingAndUseAes(preset);
        await AssertRunsLikeTheOriginalAsync(buildOutput, targetFramework, "Aggressive", preset, original);

        // Everything the preset leaves out as well: the VM and proxies for external calls, with symbol renaming on.
        var everything = new ObfySettings { Level = ObfuscationLevel.Custom };
        everything.StringEncryption.Enabled = true;
        everything.ConstantEncryption.Enabled = true;
        everything.ResourceEncryption.Enabled = true;
        everything.ControlFlow.Enabled = true;
        everything.SymbolRenaming.Enabled = true;
        everything.Protection.AntiDebug = true;
        everything.Protection.AntiDump = true;
        everything.Protection.AntiTamper.Enabled = true;
        everything.Protection.AntiDecompiler.Enabled = true;
        everything.Protection.MethodEncryption = true;
        everything.Protection.ReferenceProxy = true;
        everything.Protection.ProxyExternalCalls = true;
        everything.Virtualization.Enabled = true;
        EmbedSiblingAndUseAes(everything);
        await AssertRunsLikeTheOriginalAsync(buildOutput, targetFramework, "Everything", everything, original);
    }

    private static void EmbedSiblingAndUseAes(ObfySettings settings)
    {
        settings.Watermark.Enabled = true;
        settings.Watermark.Id = "framework-targets";
        settings.DependencyEmbedding.Enabled = true;
        settings.DependencyEmbedding.IncludePatterns = new List<string> { "SiblingLib.dll" };
        settings.DependencyEmbedding.ExcludePatterns = new List<string>();
        settings.StringEncryption.Algorithm = EncryptionAlgorithm.Aes256;
        settings.ResourceEncryption.Algorithm = EncryptionAlgorithm.Aes256;
    }

    private static async Task AssertRunsLikeTheOriginalAsync(
        string buildOutput,
        string targetFramework,
        string label,
        ObfySettings settings,
        ScenarioHarness.ProcessResult original)
    {
        var (staged, obfDir, output) = Stage(buildOutput, "RichConsole", targetFramework);
        await ScenarioHarness.ObfuscateAsync(staged, output, settings);

        if (settings.Virtualization.Enabled)
        {
            // With symbol renaming on, the interpreter has no recognizable name. It is the one type that keeps its
            // byte tables (code, opcode map, key, ...) as several initialized static blobs; the other helpers have one.
            using var module = ModuleDefMD.Load(File.ReadAllBytes(output));
            module.GetTypes().Max(t => t.Fields.Count(f => f.HasFieldRVA)).ShouldBeGreaterThanOrEqualTo(
                3, $"RichConsole ({label}) did not get the VM, so the run would prove nothing about it");
        }

        // The embedded sibling must be what the app loads: take the loose copy away, and the dependency manifest that
        // lists it (the host of .NET Core 3.1 and 5 refuses to start when a listed file is missing).
        File.Delete(Path.Combine(obfDir, "SiblingLib.dll"));
        foreach (var manifest in Directory.GetFiles(obfDir, "*.deps.json"))
            File.Delete(manifest);

        var run = Run(output);
        run.ExitCode.ShouldBe(0, $"RichConsole ({label}) on {targetFramework}:{Environment.NewLine}{run.StdOut}{run.StdErr}");
        run.StdOut.ReplaceLineEndings("\n").ShouldBe(
            original.StdOut.ReplaceLineEndings("\n"), $"RichConsole ({label}) on {targetFramework}");
    }

    private static (string Staged, string ObfDir, string Output) Stage(
        string buildOutput, string assemblyName, string targetFramework)
    {
        var fileName = AssemblyFileName(assemblyName, targetFramework);
        var staged = ScenarioHarness.StageBuildOutput(buildOutput, fileName);
        var stageDir = Path.GetDirectoryName(staged)!;
        var obfDir = Path.Combine(stageDir, "obf");
        ScenarioHarness.CopySidecars(stageDir, obfDir, fileName);
        return (staged, obfDir, Path.Combine(obfDir, fileName));
    }

    internal static string AssemblyFileName(string assemblyName, string targetFramework) =>
        assemblyName + (InstalledRuntimes.IsNetFramework(targetFramework) ? ".exe" : ".dll");

    /// <summary>.NET Framework apps are executables the framework runs; modern .NET apps are dlls for <c>dotnet</c>.</summary>
    private static ScenarioHarness.ProcessResult Run(string assemblyPath, string arguments = "") =>
        assemblyPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? ScenarioHarness.RunProcess(assemblyPath, arguments, Path.GetDirectoryName(assemblyPath)!, ScenarioHarness.DefaultRunTimeoutMs)
            : ScenarioHarness.RunDotnet(assemblyPath, arguments);

    private static ObfySettings SettingsFor(string protection) => protection switch
    {
        "Aggressive" => new ObfySettings { Level = ObfuscationLevel.Aggressive },
        "AntiDebug" => Only(s => s.Protection.AntiDebug = true),
        "AntiDump" => Only(s => s.Protection.AntiDump = true),
        "MethodEncryption" => Only(s => s.Protection.MethodEncryption = true),
        "AntiTamper" => Only(s => s.Protection.AntiTamper.Enabled = true),
        "StringsAes" => Only(s =>
        {
            s.StringEncryption.Enabled = true;
            s.StringEncryption.Algorithm = EncryptionAlgorithm.Aes256;
        }),
        "Virtualization" => Only(s => s.Virtualization.Enabled = true),
        "VirtualizationWithRenaming" => Only(s =>
        {
            s.Virtualization.Enabled = true;
            s.SymbolRenaming.Enabled = true;
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(protection), protection, null)
    };

    /// <summary>Custom level with every default-on technique off, then just the one the caller turns on.</summary>
    private static ObfySettings Only(Action<ObfySettings> enable)
    {
        var settings = new ObfySettings { Level = ObfuscationLevel.Custom };
        settings.StringEncryption.Enabled = false;
        settings.SymbolRenaming.Enabled = false;
        settings.Metadata.RemoveDebugInfo = false;
        settings.Metadata.RemoveAttributes = false;
        enable(settings);
        return settings;
    }

    /// <summary>
    /// The type the technique injects (symbol renaming is off for the single-technique cases; with it on, the
    /// interpreter is renamed, so <c>VirtualizationWithRenaming</c> is checked through the statistics instead).
    /// </summary>
    private static string? InjectedTypeFor(string protection) => protection switch
    {
        "AntiDebug" => "<AntiDebug>",
        "AntiDump" => "<AntiDump>",
        "MethodEncryption" => "<MethodCrypt>",
        "Virtualization" => "Vm",
        _ => null
    };
}

/// <summary>Builds each fixture once per target framework and removes the copies when the class is done.</summary>
public sealed class BuiltFixtures : IDisposable
{
    private readonly Dictionary<string, string> _outputs = new();
    private readonly List<string> _copies = new();
    private readonly object _gate = new();

    public string Hello(string targetFramework) => BuildOutput("HelloConsole", targetFramework);

    public string Rich(string targetFramework) => BuildOutput("RichConsole", targetFramework);

    private string BuildOutput(string fixtureName, string targetFramework)
    {
        lock (_gate)
        {
            var key = fixtureName + "/" + targetFramework;
            if (_outputs.TryGetValue(key, out var existing))
                return existing;

            var fixture = Path.Combine(
                ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", fixtureName);
            var copy = ScenarioHarness.CopyToTemp(fixture);
            _copies.Add(copy);

            ScenarioHarness.DotnetBuild(
                Path.Combine(copy, fixtureName + ".csproj"),
                extraArgs: $"-p:FixtureTfm={targetFramework}");

            var output = Path.Combine(copy, "bin", "Release", targetFramework);
            File.Exists(Path.Combine(output, ProtectedConsoleStartupTests.AssemblyFileName(fixtureName, targetFramework)))
                .ShouldBeTrue(output);
            return _outputs[key] = output;
        }
    }

    public void Dispose()
    {
        foreach (var copy in _copies)
            ScenarioHarness.TryDelete(copy);
    }
}

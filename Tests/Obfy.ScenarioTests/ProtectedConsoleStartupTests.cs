using dnlib.DotNet;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.ScenarioTests;

/// <summary>
/// An SDK-built console app (core library reference <c>System.Runtime</c>, as <c>dotnet new console</c> produces)
/// is protected and then run. The framework types the injected code names must resolve at run time through the
/// target framework's own assemblies: the runtime's <c>System.Runtime</c> facade does not forward
/// <c>DllNotFoundException</c> or <c>Marshal</c> (they live in <c>System.Runtime.InteropServices</c>), so a
/// reference to them through the core library fails with a <see cref="TypeLoadException"/> before user code runs.
/// <c>EndToEndObfuscationTests</c> compiles against the runtime's implementation assemblies, whose core library is
/// <c>System.Private.CoreLib</c>, so it cannot see this.
/// </summary>
/// <remarks>
/// Every theory below runs the same cases: the Aggressive preset, and AntiDebug, AntiDump, MethodEncryption and
/// Virtualization each alone, then Virtualization together with symbol renaming (which renames the interpreter and
/// everything it reaches), so a failure names the technique. net8.0 and net9.0 are skipped (visibly) on a machine
/// without those runtimes.
/// </remarks>
public sealed class ProtectedConsoleStartupTests : IClassFixture<ProtectedConsoleStartupTests.BuiltHelloConsole>
{
    private const string AssemblyFileName = "HelloConsole.dll";
    private const string ExpectedOutput = "Hello, World!\nSum:163";

    private readonly BuiltHelloConsole _built;

    public ProtectedConsoleStartupTests(BuiltHelloConsole built) => _built = built;

    public static TheoryData<string> Protections => new()
    {
        "Aggressive",
        "AntiDebug",
        "AntiDump",
        "MethodEncryption",
        "Virtualization",
        "VirtualizationWithRenaming"
    };

    [SdkRuntimeTheory("net10.0", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net10_ProtectedApp_StartsAndPrints(string protection) => AssertStartsAsync("net10.0", protection);

    [SdkRuntimeTheory("net9.0", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net9_ProtectedApp_StartsAndPrints(string protection) => AssertStartsAsync("net9.0", protection);

    [SdkRuntimeTheory("net8.0", windowsOnly: true)]
    [MemberData(nameof(Protections))]
    public Task Net8_ProtectedApp_StartsAndPrints(string protection) => AssertStartsAsync("net8.0", protection);

    private async Task AssertStartsAsync(string targetFramework, string protection)
    {
        var buildOutput = _built.BuildOutput(targetFramework);
        var staged = ScenarioHarness.StageBuildOutput(buildOutput, AssemblyFileName);
        var stageDir = Path.GetDirectoryName(staged)!;
        var obfDir = Path.Combine(stageDir, "obf");
        ScenarioHarness.CopySidecars(stageDir, obfDir, AssemblyFileName);
        var output = Path.Combine(obfDir, AssemblyFileName);

        var result = await ScenarioHarness.ObfuscateAsync(staged, output, SettingsFor(protection));
        if (protection.StartsWith("Virtualization", StringComparison.Ordinal))
        {
            result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(
                0, $"{protection} virtualized nothing, so this run would prove nothing about it");
        }

        var marker = InjectedTypeFor(protection);
        if (marker is not null)
        {
            using var module = ModuleDefMD.Load(File.ReadAllBytes(output));
            module.Types.ShouldContain(
                t => t.Namespace == "Obfy.Runtime" && t.Name == marker,
                $"{protection} did not inject {marker}, so this run would prove nothing about it");
        }

        var run = ScenarioHarness.RunDotnet(output);
        run.ExitCode.ShouldBe(0, $"{protection} on {targetFramework}:{Environment.NewLine}{run.StdOut}{run.StdErr}");
        run.StdOut.ReplaceLineEndings("\n").Trim().ShouldBe(ExpectedOutput, $"{protection} on {targetFramework}");
    }

    private static ObfySettings SettingsFor(string protection) => protection switch
    {
        "Aggressive" => new ObfySettings { Level = ObfuscationLevel.Aggressive },
        "AntiDebug" => Only(s => s.Protection.AntiDebug = true),
        "AntiDump" => Only(s => s.Protection.AntiDump = true),
        "MethodEncryption" => Only(s => s.Protection.MethodEncryption = true),
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

    /// <summary>Builds the fixture once per target framework and removes the copies when the class is done.</summary>
    public sealed class BuiltHelloConsole : IDisposable
    {
        private readonly Dictionary<string, string> _buildOutputs = new();
        private readonly List<string> _copies = new();
        private readonly object _gate = new();

        public string BuildOutput(string targetFramework)
        {
            lock (_gate)
            {
                if (_buildOutputs.TryGetValue(targetFramework, out var existing))
                    return existing;

                var fixture = Path.Combine(
                    ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "HelloConsole");
                var copy = ScenarioHarness.CopyToTemp(fixture);
                _copies.Add(copy);

                ScenarioHarness.DotnetBuild(
                    Path.Combine(copy, "HelloConsole.csproj"),
                    extraArgs: $"-p:FixtureTfm={targetFramework}");

                var output = Path.Combine(copy, "bin", "Release", targetFramework);
                File.Exists(Path.Combine(output, AssemblyFileName)).ShouldBeTrue(output);
                return _buildOutputs[targetFramework] = output;
            }
        }

        public void Dispose()
        {
            foreach (var copy in _copies)
                ScenarioHarness.TryDelete(copy);
        }
    }
}

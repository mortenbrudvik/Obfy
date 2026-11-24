using System.Runtime.InteropServices;
using System.Text;
using dnlib.DotNet;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.ScenarioTests;

/// <summary>
/// Runtime scenario verification for publish modes (<c>PublishSingleFile</c>, <c>PublishTrimmed</c>,
/// self-contained <c>FrameworkReferences</c> resolution) and sequential short-name reuse across
/// separate runs vs closed-set obfuscation.
/// </summary>
public sealed class PublishModesScenarioTests
{
    private const string ExpectedHello = "Hello, World!\nSum:163";

    [SdkRuntimeFact("net10.0", windowsOnly: true)]
    public async Task PublishSingleFile_ObfuscatedBeforePublish_WithAntiTamper_StartsAndRuns()
    {
        var copy = CopyHelloConsole();
        try
        {
            var csproj = Path.Combine(copy, "HelloConsole.csproj");
            var rid = PlatformWorkloads.CurrentWindowsRid();
            ScenarioHarness.DotnetBuild(
                csproj,
                extraArgs: $"-r {rid} -p:SelfContained=false -p:PublishSingleFile=true",
                timeoutMs: 90_000);

            var intermediateDll = Path.Combine(copy, "obj", "Release", "net10.0", rid, "HelloConsole.dll");
            File.Exists(intermediateDll).ShouldBeTrue(intermediateDll);

            var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
            settings.Level = ObfuscationLevel.Custom;
            settings.Protection.AntiTamper.Enabled = true;
            // Single-file bundles load embedded assemblies with Marshal.GetHINSTANCE == -1,
            // so in-memory PE method-IL decryption returns early at startup.
            settings.Protection.MethodEncryption = false;
            settings.Packing.Enabled = false;
            settings.SymbolRenaming.PreservePublicApi = false;

            await ScenarioHarness.ObfuscateAsync(intermediateDll, intermediateDll, settings);
            File.SetLastWriteTimeUtc(intermediateDll, DateTime.UtcNow.AddMinutes(1));
            CopyToBinIfPresent(copy, rid, "HelloConsole.dll", intermediateDll);

            using (var module = ModuleDefMD.Load(File.ReadAllBytes(intermediateDll)))
            {
                var entryType = module.EntryPoint?.DeclaringType;
                entryType.ShouldNotBeNull();
                entryType!.Methods.ShouldNotContain(m => m.Name == "Sum" || m.Name == "DumpHookPatched");
                HasMethodCall(module, "get_ProcessPath").ShouldBeTrue(
                    "Expected AntiTamper single-file ProcessPath check to be injected.");
                HasPlaintextString(module, "Hello, World!").ShouldBeFalse();
            }

            var publish = ScenarioHarness.DotnetPublish(
                csproj,
                $"-c Release -r {rid} -p:PublishSingleFile=true -p:SelfContained=false --no-build",
                timeoutMs: 120_000);
            publish.ExitCode.ShouldBe(0, publish.StdOut + publish.StdErr);

            var publishDir = Path.Combine(copy, "bin", "Release", "net10.0", rid, "publish");
            var exe = Path.Combine(publishDir, "HelloConsole.exe");
            File.Exists(exe).ShouldBeTrue(exe);
            File.Exists(Path.Combine(publishDir, "HelloConsole.dll")).ShouldBeFalse(
                "PublishSingleFile should bundle HelloConsole.dll inside the single-file executable");
            ContainsText(File.ReadAllBytes(exe), "Hello, World!").ShouldBeFalse(
                "Plaintext string found in single-file exe; bundle likely skipped obfuscated IL");

            AssertHelloRunsCleanly(exe, publishDir, "PublishSingleFile");
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }

    [SdkRuntimeFact("net10.0", windowsOnly: true)]
    public async Task PublishTrimmed_ObfuscatedBeforeTrim_WithInjectedFrameworkReferences_RunsCleanly()
    {
        var copy = CopyHelloConsole();
        try
        {
            var csproj = Path.Combine(copy, "HelloConsole.csproj");
            var rid = PlatformWorkloads.CurrentWindowsRid();
            ScenarioHarness.DotnetBuild(
                csproj,
                extraArgs: $"-r {rid} -p:SelfContained=true -p:PublishTrimmed=true -p:DebugType=none",
                timeoutMs: 90_000);

            var intermediateDll = Path.Combine(copy, "obj", "Release", "net10.0", rid, "HelloConsole.dll");
            File.Exists(intermediateDll).ShouldBeTrue(intermediateDll);

            // AntiTamper and MethodEncryption are off before ILLink because the trimmer rewrites
            // the assembly into linked/, changing its file bytes and method RVAs.
            var settings = CreateFrameworkReferencesSettings(antiTamper: false);
            await ScenarioHarness.ObfuscateAsync(intermediateDll, intermediateDll, settings);
            File.SetLastWriteTimeUtc(intermediateDll, DateTime.UtcNow.AddMinutes(1));
            CopyToBinIfPresent(copy, rid, "HelloConsole.dll", intermediateDll);

            using (var module = ModuleDefMD.Load(File.ReadAllBytes(intermediateDll)))
            {
                var refs = module.GetAssemblyRefs().Select(a => a.Name.String).ToList();
                refs.ShouldContain(
                    "System.Security.Cryptography",
                    "Expected FrameworkReferences to synthesize System.Security.Cryptography before trimming.");
                refs.ShouldContain(
                    "System.Runtime.InteropServices",
                    "Expected FrameworkReferences to keep/synthesize System.Runtime.InteropServices before trimming.");
            }

            var publish = ScenarioHarness.DotnetPublish(
                csproj,
                $"-c Release -r {rid} -p:SelfContained=true -p:PublishTrimmed=true -p:DebugType=none --no-build",
                timeoutMs: 120_000);
            publish.ExitCode.ShouldBe(0, publish.StdOut + publish.StdErr);

            var publishDir = Path.Combine(copy, "bin", "Release", "net10.0", rid, "publish");
            var exe = Path.Combine(publishDir, "HelloConsole.exe");
            var publishedDll = Path.Combine(publishDir, "HelloConsole.dll");
            File.Exists(exe).ShouldBeTrue(exe);
            File.Exists(publishedDll).ShouldBeTrue(publishedDll);
            File.Exists(Path.Combine(publishDir, "System.Security.Cryptography.dll")).ShouldBeTrue(
                "ILLink should preserve System.Security.Cryptography referenced by Obfy's injected helpers");
            // System.Runtime.InteropServices is a type-forwarding facade to System.Private.CoreLib on .NET 10,
            // so ILLink trims the facade itself while keeping Marshal/RuntimeInformation working in System.Private.CoreLib.
            File.Exists(Path.Combine(publishDir, "System.Xml.Linq.dll")).ShouldBeFalse(
                "Unused framework assemblies should be trimmed by PublishTrimmed=true");

            using (var trimmedModule = ModuleDefMD.Load(File.ReadAllBytes(publishedDll)))
            {
                var entryType = trimmedModule.EntryPoint?.DeclaringType;
                entryType.ShouldNotBeNull();
                entryType!.Methods.ShouldNotContain(m => m.Name == "Sum" || m.Name == "DumpHookPatched");
                HasPlaintextString(trimmedModule, "Hello, World!").ShouldBeFalse();
            }

            AssertHelloRunsCleanly(exe, publishDir, "PublishTrimmed");
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }

    [SdkRuntimeFact("net10.0", windowsOnly: true)]
    public async Task PublishedSelfContainedApp_ObfuscatedWithFrameworkReferences_RunsCleanly()
    {
        var copy = CopyHelloConsole();
        try
        {
            var csproj = Path.Combine(copy, "HelloConsole.csproj");
            var rid = PlatformWorkloads.CurrentWindowsRid();
            var publish = ScenarioHarness.DotnetPublish(csproj, $"-c Release -r {rid} -p:SelfContained=true");
            publish.ExitCode.ShouldBe(0, publish.StdOut + publish.StdErr);

            var publishDir = Path.Combine(copy, "bin", "Release", "net10.0", rid, "publish");
            var publishedDll = Path.Combine(publishDir, "HelloConsole.dll");
            var exe = Path.Combine(publishDir, "HelloConsole.exe");
            File.Exists(publishedDll).ShouldBeTrue(publishedDll);
            File.Exists(exe).ShouldBeTrue(exe);

            var settings = CreateFrameworkReferencesSettings(antiTamper: true);
            await ScenarioHarness.ObfuscateAsync(publishedDll, publishedDll, settings);

            using (var module = ModuleDefMD.Load(File.ReadAllBytes(publishedDll)))
            {
                var refs = module.GetAssemblyRefs().Select(a => a.Name.String).ToList();
                refs.ShouldContain("System.Security.Cryptography");
                refs.ShouldContain("System.Runtime.InteropServices");
                foreach (var assemblyRef in refs)
                {
                    File.Exists(Path.Combine(publishDir, assemblyRef + ".dll")).ShouldBeTrue(
                        $"Referenced framework assembly '{assemblyRef}.dll' missing from {publishDir}");
                }

                HasMethodCall(module, "get_ProcessPath").ShouldBeTrue();
            }

            AssertHelloRunsCleanly(exe, publishDir, "PublishedSelfContained");
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }

    [Fact]
    public async Task SeparateSingleFileRuns_ReuseSequentialShortNames_WhileClosedSetSharesThem()
    {
        var fixture = Path.Combine(
            ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "ConsoleSolution");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            var appProj = Path.Combine(copy, "App", "ConsoleApp.csproj");
            ScenarioHarness.DotnetBuild(appProj);

            var appDir = Path.Combine(copy, "App", "bin", "Release", "net10.0");
            var staged = ScenarioHarness.StageBuildOutput(appDir, "ConsoleApp.dll");
            var stageDir = Path.GetDirectoryName(staged)!;
            var stageRoot = Path.GetDirectoryName(stageDir)!;
            var sepDir = Path.Combine(stageRoot, "separate");
            var closedDir = Path.Combine(stageRoot, "closed");
            ScenarioHarness.CopySidecars(stageDir, sepDir);
            ScenarioHarness.CopySidecars(stageDir, closedDir);

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                ControlFlow = { Enabled = false },
                ConstantEncryption = { Enabled = false },
                ResourceEncryption = { Enabled = false },
                SymbolRenaming =
                {
                    Enabled = true,
                    Mode = NamingMode.Sequential,
                    PreservePublicApi = false,
                    RenameTypes = true,
                    RenameMethods = true,
                    RenameFields = true,
                    RenameProperties = true,
                    RenameEvents = true,
                    RenameNamespaces = true,
                    RenameParameters = true
                },
                Metadata =
                {
                    RemoveDebugInfo = false,
                    RemoveAttributes = false,
                    StripDocumentation = false
                }
            };

            var stagedLib = Path.Combine(stageDir, "ConsoleLib.dll");
            var stagedApp = Path.Combine(stageDir, "ConsoleApp.dll");

            var sepLibResult = await ScenarioHarness.ObfuscateAsync(
                stagedLib, Path.Combine(sepDir, "ConsoleLib.dll"), settings);
            var sepAppResult = await ScenarioHarness.ObfuscateAsync(
                stagedApp, Path.Combine(sepDir, "ConsoleApp.dll"), settings);

            sepLibResult.SymbolMap.Values.ShouldContain("a");
            sepAppResult.SymbolMap.Values.ShouldContain("a");

            var sepLibTypes = RenamedTypeNames(Path.Combine(sepDir, "ConsoleLib.dll"));
            var sepAppTypes = RenamedTypeNames(Path.Combine(sepDir, "ConsoleApp.dll"));
            sepLibTypes.Intersect(sepAppTypes, StringComparer.Ordinal).ShouldContain("a");

            await ScenarioHarness.ObfuscateClosedSetAsync([stagedLib, stagedApp], closedDir, settings);

            var closedLibPath = Path.Combine(closedDir, "ConsoleLib.dll");
            var closedAppPath = Path.Combine(closedDir, "ConsoleApp.dll");
            var closedLibTypes = RenamedTypeNames(closedLibPath);
            var closedAppTypes = RenamedTypeNames(closedAppPath);

            closedLibTypes.ShouldContain("a");
            closedLibTypes.ShouldNotContain("Calculator");
            closedAppTypes.ShouldNotContain("a");
            closedLibTypes.Intersect(closedAppTypes, StringComparer.Ordinal).ShouldBeEmpty();

            using (var appModule = ModuleDefMD.Load(File.ReadAllBytes(closedAppPath)))
            {
                appModule.GetTypeRefs().ShouldNotContain(tr => tr.Name == "Calculator");
                appModule.GetMemberRefs().ShouldNotContain(mr => mr.Name == "Add");
            }

            var run = ScenarioHarness.RunDotnet(closedAppPath, "--smoke");
            run.ExitCode.ShouldBe(0, run.StdOut + run.StdErr);
            run.StdOut.ShouldContain("SMOKE:5");
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }

    private static string CopyHelloConsole()
    {
        var fixture = Path.Combine(
            ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "HelloConsole");
        return ScenarioHarness.CopyToTemp(fixture);
    }

    private static void CopyToBinIfPresent(string projectCopy, string rid, string fileName, string sourcePath)
    {
        var binPath = Path.Combine(projectCopy, "bin", "Release", "net10.0", rid, fileName);
        if (File.Exists(binPath))
            File.Copy(sourcePath, binPath, overwrite: true);
    }

    private static ObfySettings CreateFrameworkReferencesSettings(bool antiTamper)
    {
        var settings = new ObfySettings { Level = ObfuscationLevel.Custom };
        settings.StringEncryption.Enabled = true;
        settings.StringEncryption.Algorithm = EncryptionAlgorithm.Aes256;
        settings.ConstantEncryption.Enabled = true;
        settings.ConstantEncryption.Algorithm = EncryptionAlgorithm.Aes256;
        settings.ControlFlow.Enabled = true;
        settings.ControlFlow.Mode = ControlFlowMode.Switch;
        settings.ControlFlow.Intensity = 80;
        settings.SymbolRenaming.Enabled = true;
        settings.SymbolRenaming.PreservePublicApi = false;
        settings.Protection.AntiDebug = true;
        settings.Protection.AntiDump = true;
        settings.Protection.AntiTamper.Enabled = antiTamper;
        settings.Protection.AntiDecompiler.Enabled = true;
        settings.Protection.ReferenceProxy = true;
        settings.Protection.MethodEncryption = false;
        settings.Metadata.RemoveDebugInfo = true;
        settings.Metadata.RemoveAttributes = true;
        return settings;
    }

    private static void AssertHelloRunsCleanly(string exePath, string workingDirectory, string label)
    {
        var run = ScenarioHarness.RunProcess(exePath, "", workingDirectory, ScenarioHarness.DefaultRunTimeoutMs);
        run.ExitCode.ShouldBe(0, $"{label}:{Environment.NewLine}{run.StdOut}{run.StdErr}");
        run.StdOut.ReplaceLineEndings("\n").Trim().ShouldBe(ExpectedHello, label);

        var expectedHook = RuntimeInformation.ProcessArchitecture is Architecture.X86 or Architecture.X64
            ? "patched"
            : "intact";
        var probe = ScenarioHarness.RunProcess(
            exePath, "--probe-dump", workingDirectory, ScenarioHarness.DefaultRunTimeoutMs);
        probe.ExitCode.ShouldBe(0, $"{label} --probe-dump:{Environment.NewLine}{probe.StdOut}{probe.StdErr}");
        probe.StdOut.ShouldContain("dump-hook:" + expectedHook, customMessage: label);
    }

    private static HashSet<string> RenamedTypeNames(string assemblyPath)
    {
        using var module = ModuleDefMD.Load(File.ReadAllBytes(assemblyPath));
        var entryDeclaringType = module.EntryPoint?.DeclaringType;
        return module.GetTypes()
            .Where(t => !t.IsGlobalModuleType && t != entryDeclaringType)
            .Select(t => t.Name.String)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasMethodCall(ModuleDefMD module, string methodName) =>
        module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Any(i => i.Operand is IMethod m && m.Name == methodName);

    private static bool HasPlaintextString(ModuleDefMD module, string value) =>
        module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Any(i => i.Operand is string s && s.Contains(value, StringComparison.Ordinal));

    private static bool ContainsText(byte[] haystack, string needle)
    {
        var utf8 = Encoding.UTF8.GetBytes(needle);
        var utf16 = Encoding.Unicode.GetBytes(needle);
        return haystack.AsSpan().IndexOf(utf8) >= 0 || haystack.AsSpan().IndexOf(utf16) >= 0;
    }
}

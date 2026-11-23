using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autofac;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Obfuscators.Source;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Obfy.Core.Services.Solution;
using Obfy.Core.Utilities;
using Obfy.Core.Virtualization;
using Obfy.Tests.CoreServices;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Regression tests for Wave 2A fixes across core engine, packing, privacy, reporting,
/// virtualization, PE encryption, and symbol renaming edge cases.
/// </summary>
public class ReviewFixesWave2CoreTests : IDisposable
{
    private readonly string _dir = CoreServicesTestSupport.NewTempDirectory("obfy-wave2a");

    public void Dispose() => CoreServicesTestSupport.DeleteQuietly(_dir);

    #region B-40: nameof(...) with SourceSymbolRenamer

    [Fact]
    public async Task SourceSymbolRenamer_NameofExpression_ReturnsObfuscatedName()
    {
        const string source = """
            using System.Reflection;

            public static class ReflectionEntry
            {
                public static string GetNames() =>
                    $"{nameof(SecretWidget)}|{nameof(Holder.ComputeSecret)}|{nameof(Holder.ExcludedMember)}";
            }

            internal class SecretWidget
            {
            }

            internal static class Holder
            {
                internal static int ComputeSecret() => 42;

                [Obfuscation(Feature = "renaming", Exclude = true)]
                internal static int ExcludedMember() => 99;
            }
            """;

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            SymbolRenaming = { Enabled = true, PreservePublicApi = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForSourceCode(SourceRvTestSupport.CreateCompilation(source), settings);
        var renamer = new SourceSymbolRenamer(new NameGenerator(), NullLogger<SourceSymbolRenamer>.Instance);

        var result = await renamer.ObfuscateAsync(context);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        SourceRvTestSupport.GetErrors(context.Compilation!).ShouldBeEmpty(
            SourceRvTestSupport.ErrorText(SourceRvTestSupport.GetErrors(context.Compilation!)));

        var output = (string)SourceRvTestSupport.Invoke(context.Compilation!, "ReflectionEntry", "GetNames")!;
        var parts = output.Split('|');
        parts.Length.ShouldBe(3);

        var obfuscatedTypeName = context.SymbolMap.Single(kv => kv.Key.EndsWith("SecretWidget", StringComparison.Ordinal)).Value;
        var obfuscatedMethodName = context.SymbolMap.Single(kv => kv.Key.Contains("ComputeSecret", StringComparison.Ordinal)).Value;

        parts[0].ShouldBe(obfuscatedTypeName);
        parts[0].ShouldNotBe("SecretWidget");
        parts[1].ShouldBe(obfuscatedMethodName);
        parts[1].ShouldNotBe("ComputeSecret");
        parts[2].ShouldBe("ExcludedMember");
    }

    #endregion

    #region B-11: Output assembly network-reference scan

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Obfuscate_AggressiveAndVirtualization_InjectsZeroNetworkReferences(bool enableVirtualization)
    {
        const string source = """
            public class Calculator
            {
                public string Label() => "Hello Obfy World";

                public int Compute(int x, int y)
                {
                    var sum = x + y + 12345;
                    if (sum > 100)
                        return sum * 2;
                    return sum + 7;
                }

                public double Scale(double v) => v * 3.14159;
            }
            """;

        var inputPath = CompileWithResource(source, _dir, enableVirtualization ? "CleanAggVm" : "CleanAgg");
        var outputPath = Path.Combine(_dir, (enableVirtualization ? "CleanAggVm" : "CleanAgg") + ".obf.dll");

        var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
        if (enableVirtualization)
        {
            settings.Level = ObfuscationLevel.Custom;
            settings.Virtualization.Enabled = true;
        }

        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var result = await service.ObfuscateAsync(inputPath, outputPath, settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        if (enableVirtualization)
            result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0);

        using var module = ModuleDefMD.Load(File.ReadAllBytes(outputPath));
        foreach (var asmRef in module.GetAssemblyRefs())
            asmRef.Name.String.ShouldNotStartWith("System.Net", Case.Insensitive);

        foreach (var typeRef in module.GetTypeRefs())
            AssertNotNetworkName(typeRef.FullName);

        foreach (var memberRef in module.GetMemberRefs())
            AssertNotNetworkName(memberRef.DeclaringType?.FullName ?? memberRef.FullName);

        static void AssertNotNetworkName(string name)
        {
            name.ShouldNotStartWith("System.Net", Case.Insensitive);
            name.ShouldNotContain("HttpClient", Case.Insensitive);
            name.ShouldNotContain("WebClient", Case.Insensitive);
            name.ShouldNotContain("TcpClient", Case.Insensitive);
            name.ShouldNotContain("WebRequest", Case.Insensitive);
            name.ShouldNotContain("Socket", Case.Insensitive);
        }
    }

    #endregion

    #region B-63 #1: Self-reflection after renaming

    [Fact]
    public async Task SymbolRenaming_SelfReflectionGetMethod_FailsUnlessExcluded()
    {
        const string source = """
            using System.Reflection;

            public static class Target
            {
                private static int Secret() => 42;

                [Obfuscation(Feature = "renaming", Exclude = true)]
                private static int ExcludedSecret() => 99;

                public static bool HasOriginalSecret() =>
                    typeof(Target).GetMethod("Secret", BindingFlags.NonPublic | BindingFlags.Static) != null;

                public static int InvokeExcludedSecret() =>
                    (int)typeof(Target).GetMethod("ExcludedSecret", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
            }
            """;

        var inputPath = CoreServicesTestSupport.CompileLibrary(_dir, "ReflectSelf", source);
        var outputPath = Path.Combine(_dir, "ReflectSelf.obf.dll");

        var settings = CoreServicesTestSupport.AllOff();
        settings.SymbolRenaming.Enabled = true;
        settings.SymbolRenaming.PreservePublicApi = true;
        settings.SymbolRenaming.Mode = NamingMode.Sequential;

        using var container = CoreServicesTestSupport.BuildContainer();
        var result = await container.Resolve<IObfuscationService>().ObfuscateAsync(inputPath, outputPath, settings);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        InvokeInIsolatedContext(inputPath, "Target", "HasOriginalSecret").ShouldBe(true);
        InvokeInIsolatedContext(inputPath, "Target", "InvokeExcludedSecret").ShouldBe(99);

        InvokeInIsolatedContext(outputPath, "Target", "HasOriginalSecret").ShouldBe(false);
        InvokeInIsolatedContext(outputPath, "Target", "InvokeExcludedSecret").ShouldBe(99);
    }

    #endregion

    #region B-30: Packer Trace cleanup

    [Fact]
    public void NativePacker_And_ManagedLauncherPacker_TraceDoesNotLeakPathsOrExceptionMessage()
    {
        var messages = new List<string>();
        var listener = new CapturingTraceListener(messages);
        Trace.Listeners.Add(listener);
        try
        {
            var nativeInput = CoreServicesTestSupport.CompileExe(
                _dir,
                "TraceNativeApp",
                """
                [assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v8.0")]
                public static class Program { public static int Main() => 0; }
                """);
            var nativeOutput = Path.Combine(_dir, "TraceNativeApp.obf.exe");
            File.Copy(nativeInput, nativeOutput);

            var runtimeConfigTemp = NativePacker.RuntimeConfigPathFor(nativeOutput) + ".obfytmp";
            File.WriteAllText(runtimeConfigTemp, "lock");
            File.SetAttributes(runtimeConfigTemp, System.IO.FileAttributes.ReadOnly);
            try
            {
                var settings = new ObfySettings { Level = ObfuscationLevel.Custom, Packing = { Enabled = true } };
                Should.Throw<InvalidOperationException>(() => NativePacker.Pack(nativeOutput, settings, nativeInput));
            }
            finally
            {
                if (File.Exists(runtimeConfigTemp))
                    File.SetAttributes(runtimeConfigTemp, System.IO.FileAttributes.Normal);
            }

            var portableInput = CoreServicesTestSupport.CompileExe(
                _dir,
                "TracePortableApp",
                "public static class Program { public static int Main() => 0; }");
            var portableOutput = Path.Combine(_dir, "TracePortableApp.obf.exe");
            File.Copy(portableInput, portableOutput);

            var portableConfig = ManagedLauncherPacker.RuntimeConfigPathFor(portableOutput);
            File.WriteAllText(portableConfig, "lock");
            File.SetAttributes(portableConfig, System.IO.FileAttributes.ReadOnly);
            try
            {
                Should.Throw<UnauthorizedAccessException>(() => ManagedLauncherPacker.Pack(portableOutput, portableInput));
            }
            finally
            {
                if (File.Exists(portableConfig))
                    File.SetAttributes(portableConfig, System.IO.FileAttributes.Normal);
            }
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        var packerTraces = messages
            .Where(m => m.Contains("NativePacker:", StringComparison.Ordinal) ||
                        m.Contains("ManagedLauncherPacker:", StringComparison.Ordinal))
            .ToList();

        if (OperatingSystem.IsWindows())
            packerTraces.ShouldNotBeEmpty();

        foreach (var line in packerTraces)
        {
            line.ShouldNotContain(_dir, Case.Insensitive);
            line.ShouldNotContain("TraceNativeApp", Case.Insensitive);
            line.ShouldNotContain("TracePortableApp", Case.Insensitive);
            line.ShouldNotContain("Access to the path", Case.Insensitive);
        }
    }

    #endregion

    #region B-105 & B-18: Portable launcher STAThread, async exception type, and strong-name signing

    [Fact]
    public void PortablePack_StaThreadMain_SetsStaApartmentOnWindows()
    {
        const string source = """
            using System;
            using System.Threading;

            public static class Program
            {
                [STAThread]
                public static int Main() =>
                    Thread.CurrentThread.GetApartmentState() == ApartmentState.STA ? 42 : 7;
            }
            """;

        var input = CoreServicesTestSupport.CompileExe(_dir, "StaApp", source);
        var output = Path.Combine(_dir, "StaApp.obf.exe");
        File.Copy(input, output);

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            Packing = { Enabled = true, Rid = "portable" }
        };
        PackingApplicator.Pack(output, settings, input, new List<string>());

        var (exitCode, _, stderr) = RunDotnet(ManagedLauncherPacker.LauncherPathFor(output));
        var expected = OperatingSystem.IsWindows() ? 42 : 7;
        exitCode.ShouldBe(expected, stderr);
    }

    [Fact]
    public void PortablePack_AsyncTaskMainException_PreservesExceptionTypeInStderr()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;

            public static class Program
            {
                public static async Task Main()
                {
                    await Task.Yield();
                    throw new InvalidOperationException("async-boom");
                }
            }
            """;

        var input = CoreServicesTestSupport.CompileExe(_dir, "AsyncFailApp", source);
        var output = Path.Combine(_dir, "AsyncFailApp.obf.exe");
        File.Copy(input, output);

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            Packing = { Enabled = true, Rid = "portable" }
        };
        PackingApplicator.Pack(output, settings, input, new List<string>());

        var (exitCode, _, stderr) = RunDotnet(ManagedLauncherPacker.LauncherPathFor(output));
        exitCode.ShouldBe(1);
        stderr.ShouldContain(nameof(InvalidOperationException));
        stderr.ShouldContain("async-boom");
    }

    [Fact]
    public async Task PortablePack_WithSigning_SignsLauncher_AndRuns()
    {
        var snkPath = Path.Combine(_dir, "portable.snk");
        WriteCrossPlatformSnk(snkPath);

        var input = CoreServicesTestSupport.CompileExe(
            _dir,
            "SignedPortableApp",
            "public static class Program { public static int Main() => 21; }");
        var output = Path.Combine(_dir, "SignedPortableApp.obf.exe");

        var settings = CoreServicesTestSupport.AllOff();
        settings.Signing.Enabled = true;
        settings.Signing.KeyFile = snkPath;
        settings.Packing.Enabled = true;
        settings.Packing.Rid = "portable";

        using var container = CoreServicesTestSupport.BuildContainer();
        var result = await container.Resolve<IObfuscationService>().ObfuscateAsync(input, output, settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.PackedLauncherPath.ShouldNotBeNull();
        File.Exists(result.PackedLauncherPath).ShouldBeTrue();

        using (var launcher = ModuleDefMD.Load(File.ReadAllBytes(result.PackedLauncherPath)))
        {
            launcher.IsStrongNameSigned.ShouldBeTrue();
        }

        var (exitCode, _, stderr) = RunDotnet(result.PackedLauncherPath!);
        exitCode.ShouldBe(21, stderr);
    }

    [Fact]
    public async Task ClosedSet_PortablePack_WithSigning_SignsLauncher()
    {
        var snkPath = Path.Combine(_dir, "closed.snk");
        WriteCrossPlatformSnk(snkPath);

        var libPath = CoreServicesTestSupport.CompileLibrary(
            _dir,
            "ClosedLib",
            "public static class Helper { public static int Value() => 33; }");
        var appPath = CoreServicesTestSupport.CompileExe(
            _dir,
            "ClosedApp",
            "public static class Program { public static int Main() => Helper.Value(); }",
            libPath);
        var outDir = Path.Combine(_dir, "closed-out");

        var settings = CoreServicesTestSupport.AllOff();
        settings.Signing.Enabled = true;
        settings.Signing.KeyFile = snkPath;
        settings.Packing.Enabled = true;
        settings.Packing.Rid = "portable";

        using var container = CoreServicesTestSupport.BuildContainer();
        var result = await container.Resolve<IObfuscationService>().ObfuscateClosedSetAsync(
            [ClosedSetInput.From(appPath), ClosedSetInput.From(libPath)],
            outDir,
            settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        var launcherPath = ManagedLauncherPacker.LauncherPathFor(Path.Combine(outDir, "ClosedApp.exe"));
        File.Exists(launcherPath).ShouldBeTrue();

        using (var launcher = ModuleDefMD.Load(File.ReadAllBytes(launcherPath)))
        {
            launcher.IsStrongNameSigned.ShouldBeTrue();
        }

        var (exitCode, _, stderr) = RunDotnet(launcherPath);
        exitCode.ShouldBe(33, stderr);
    }

    [Fact]
    public async Task NativePack_WithSigning_DoesNotAttemptToSignNativeStub()
    {
        var snkPath = Path.Combine(_dir, "native.snk");
        WriteCrossPlatformSnk(snkPath);

        var input = CoreServicesTestSupport.CompileExe(
            _dir,
            "SignedNativeApp",
            """
            [assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v8.0")]
            public static class Program { public static int Main() => 5; }
            """);
        var output = Path.Combine(_dir, "SignedNativeApp.obf.exe");

        var settings = CoreServicesTestSupport.AllOff();
        settings.Signing.Enabled = true;
        settings.Signing.KeyFile = snkPath;
        settings.Packing.Enabled = true;
        settings.Packing.Rid = "win-x64";

        using var container = CoreServicesTestSupport.BuildContainer();
        var result = await container.Resolve<IObfuscationService>().ObfuscateAsync(input, output, settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        NativePacker.LooksLikePackedHost(output).ShouldBeTrue();
    }

    #endregion

    #region B-95: MergeAndObfuscateAsync dependency embedding and incremental cache

    [Fact]
    public async Task MergeAndObfuscate_WithDependencyEmbedding_EmbedsNonMergedSiblingDll()
    {
        var inputDir = Path.Combine(_dir, "in");
        var outDir = Path.Combine(_dir, "out");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outDir);

        var depPath = CoreServicesTestSupport.CompileLibrary(
            inputDir,
            "SiblingDep",
            "public static class DepBox { public static int Number() => 77; }");
        var libPath = CoreServicesTestSupport.CompileLibrary(
            inputDir,
            "MergedLib",
            "public static class LibBox { public static int Bonus() => 3; }");
        var appPath = CoreServicesTestSupport.CompileLibrary(
            inputDir,
            "MainPart",
            "public static class EntryPart { public static int Run() => DepBox.Number() + LibBox.Bonus(); }",
            depPath,
            libPath);
        var outputPath = Path.Combine(outDir, "Combined.dll");

        var settings = CoreServicesTestSupport.AllOff();
        settings.DependencyEmbedding.Enabled = true;

        using var container = CoreServicesTestSupport.BuildContainer();
        var result = await container.Resolve<IObfuscationService>()
            .MergeAndObfuscateAsync([appPath, libPath], outputPath, settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.AssembliesEmbedded.ShouldBe(1);

        using (var module = ModuleDefMD.Load(File.ReadAllBytes(outputPath)))
        {
            module.Resources.Any(r => r.Name == "Obfy.Embedded.SiblingDep.dll").ShouldBeTrue();
        }

        // outDir contains ONLY Combined.dll (no SiblingDep.dll), proving the embedded dependency resolves.
        InvokeInIsolatedContext(outputPath, "EntryPart", "Run").ShouldBe(80);
    }

    [Fact]
    public async Task MergeAndObfuscate_IncrementalCache_SkipsMergeOnHit_AndInvalidatesWhenSiblingDepChanges()
    {
        var inputDir = Path.Combine(_dir, "inc-in");
        var outDir = Path.Combine(_dir, "inc-out");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outDir);

        var depPath = CoreServicesTestSupport.CompileLibrary(
            inputDir,
            "DynDep",
            "public static class DynBox { public static int Val() => 10; }");
        var libPath = CoreServicesTestSupport.CompileLibrary(
            inputDir,
            "PartB",
            "public static class PartB { public static int Val() => 5; }");
        var appPath = CoreServicesTestSupport.CompileExe(
            inputDir,
            "PartA",
            "public static class Program { public static int Main() => DynBox.Val() + PartB.Val(); }",
            depPath,
            libPath);
        var outputPath = Path.Combine(outDir, "MergedInc.dll");

        var settings = CoreServicesTestSupport.AllOff();
        settings.DependencyEmbedding.Enabled = true;
        settings.Incremental.Enabled = true;

        using var container = CoreServicesTestSupport.BuildContainer();
        var realService = container.Resolve<IObfuscationService>();

        var first = await realService.MergeAndObfuscateAsync([appPath, libPath], outputPath, settings);
        first.Success.ShouldBeTrue(first.ErrorMessage);
        first.FromCache.ShouldBeFalse();
        RunManagedAssembly(outputPath).ShouldBe(15);

        // Second run with a failing merger proves cache hit happens BEFORE ILRepack runs.
        var failingMerger = new Mock<IAssemblyMerger>();
        failingMerger
            .Setup(m => m.MergeAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<AssemblyMergeSettings>(),
                It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("ILRepack should not run on an incremental merge cache hit."));
        var cachedService = new ObfuscationService(
            container.Resolve<IAssemblyProcessor>(),
            container.Resolve<ISourceProcessor>(),
            container.Resolve<IObfuscationPipeline>(),
            failingMerger.Object,
            NullLogger<ObfuscationService>.Instance,
            container.Resolve<IClosedSetProcessor>());

        var second = await cachedService.MergeAndObfuscateAsync([appPath, libPath], outputPath, settings);
        second.Success.ShouldBeTrue(second.ErrorMessage);
        second.FromCache.ShouldBeTrue();
        failingMerger.Verify(
            m => m.MergeAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<AssemblyMergeSettings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Updating the non-merged sibling DLL invalidates the merge cache key.
        CoreServicesTestSupport.CompileLibrary(
            inputDir,
            "DynDep",
            "public static class DynBox { public static int Val() => 25; }");

        var third = await realService.MergeAndObfuscateAsync([appPath, libPath], outputPath, settings);
        third.Success.ShouldBeTrue(third.ErrorMessage);
        third.FromCache.ShouldBeFalse();
        RunManagedAssembly(outputPath).ShouldBe(30);
    }

    #endregion

    #region B-111: Validate null sections/lists, non-contiguous .cs batch, SettingsSummary, Html formatting

    [Fact]
    public void ObfySettings_Validate_NullNestedSection_ThrowsValidationException()
    {
        var settings = JsonSerializer.Deserialize<ObfySettings>(
            """{ "protection": null }""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Should.Throw<ValidationException>(() => settings.Validate());
    }

    [Fact]
    public void ObfySettings_Validate_NullLists_CoalescesToEmptyLists()
    {
        var settings = JsonSerializer.Deserialize<ObfySettings>(
            """
            {
              "exclusions": { "namespaces": null, "types": null, "methods": null, "attributes": null },
              "resourceEncryption": { "includePatterns": null, "excludePatterns": null },
              "assemblyMerge": { "searchDirectories": null, "excludePatterns": null }
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        settings.Validate();

        settings.Exclusions.Namespaces.ShouldNotBeNull().ShouldBeEmpty();
        settings.Exclusions.Types.ShouldNotBeNull().ShouldBeEmpty();
        settings.Exclusions.Methods.ShouldNotBeNull().ShouldBeEmpty();
        settings.Exclusions.Attributes.ShouldNotBeNull().ShouldBeEmpty();
        settings.ResourceEncryption.IncludePatterns.ShouldNotBeNull().ShouldBeEmpty();
        settings.ResourceEncryption.ExcludePatterns.ShouldNotBeNull().ShouldBeEmpty();
        settings.AssemblyMerge.SearchDirectories.ShouldNotBeNull().ShouldBeEmpty();
        settings.AssemblyMerge.ExcludePatterns.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public async Task ObfuscateBatch_NonContiguousCSharpFiles_CompileTogetherInOneCompilation()
    {
        var srcDir = Path.Combine(_dir, "src");
        var outDir = Path.Combine(_dir, "batch-out");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(outDir);

        var aCs = Path.Combine(srcDir, "A.cs");
        var bCs = Path.Combine(srcDir, "B.cs");
        await File.WriteAllTextAsync(aCs, "public static class Entry { public static int Call() => SharedHelper.GetNumber(); }");
        await File.WriteAllTextAsync(bCs, "internal static class SharedHelper { internal static int GetNumber() => 123; }");
        var dll = CoreServicesTestSupport.CompileLibrary(srcDir, "PlainLib", "public class Plain { }");

        var settings = CoreServicesTestSupport.AllOff();
        settings.SymbolRenaming.Enabled = true;
        settings.SymbolRenaming.PreservePublicApi = true;
        settings.SymbolRenaming.Mode = NamingMode.Sequential;

        using var container = CoreServicesTestSupport.BuildContainer();
        var results = await container.Resolve<IObfuscationService>()
            .ObfuscateBatchAsync([aCs, dll, bCs], outDir, settings);

        results.Count.ShouldBe(2);
        results.ShouldAllBe(r => r.Success, string.Join("; ", results.Select(r => r.ErrorMessage)));

        var outA = File.ReadAllText(Path.Combine(outDir, "A.cs"));
        var outB = File.ReadAllText(Path.Combine(outDir, "B.cs"));
        outA.ShouldNotContain("SharedHelper");
        outB.ShouldNotContain("SharedHelper");

        var compiled = SourceRvTestSupport.CreateCompilation([("A.cs", outA), ("B.cs", outB)]);
        SourceRvTestSupport.GetErrors(compiled).ShouldBeEmpty(
            SourceRvTestSupport.ErrorText(SourceRvTestSupport.GetErrors(compiled)));
        SourceRvTestSupport.Invoke(compiled, "Entry", "Call").ShouldBe(123);
    }

    [Fact]
    public async Task ReportService_And_HtmlReportGenerator_PopulateAllFlags_FormatHours_AndHandleLongMinValue()
    {
        var settings = CoreServicesTestSupport.AllOff();
        settings.Protection.AntiTamper.Enabled = true;
        settings.Protection.AntiDump = true;
        settings.Protection.AntiDecompiler.Enabled = true;
        settings.Protection.ReferenceProxy = true;
        settings.Protection.MethodEncryption = true;
        settings.DependencyEmbedding.Enabled = true;
        settings.Virtualization.Enabled = true;
        settings.Packing.Enabled = true;
        settings.Signing.Enabled = true;
        settings.Signing.KeyFile = "key.snk";
        settings.Watermark.Enabled = true;
        settings.Watermark.Id = "build-1";

        var reportService = new ReportService(
            NullLogger<ReportService>.Instance,
            new HtmlReportGenerator(NullLogger<HtmlReportGenerator>.Instance),
            new JsonReportGenerator(NullLogger<JsonReportGenerator>.Instance));

        var result = ObfuscationResult.Successful(
            new ObfuscationStatistics(),
            elapsedTime: new TimeSpan(1, 5, 3).Add(TimeSpan.FromMilliseconds(456)));

        var built = reportService.BuildReport(result, settings);

        built.SettingsUsed.AntiTamperEnabled.ShouldBeTrue();
        built.SettingsUsed.AntiDumpEnabled.ShouldBeTrue();
        built.SettingsUsed.AntiDecompilerEnabled.ShouldBeTrue();
        built.SettingsUsed.ReferenceProxyEnabled.ShouldBeTrue();
        built.SettingsUsed.MethodEncryptionEnabled.ShouldBeTrue();
        built.SettingsUsed.DependencyEmbeddingEnabled.ShouldBeTrue();
        built.SettingsUsed.VirtualizationEnabled.ShouldBeTrue();
        built.SettingsUsed.PackingEnabled.ShouldBeTrue();
        built.SettingsUsed.SigningEnabled.ShouldBeTrue();
        built.SettingsUsed.WatermarkEnabled.ShouldBeTrue();

        var extremeReport = new ObfuscationReport
        {
            Metadata = built.Metadata,
            FileInfo = new FileInfoSection
            {
                InputPath = "in.dll",
                OutputPath = "out.dll",
                InputSizeBytes = long.MaxValue,
                OutputSizeBytes = -1 // SizeDifferenceBytes == long.MinValue
            },
            SettingsUsed = built.SettingsUsed
        };

        var html = await new HtmlReportGenerator(NullLogger<HtmlReportGenerator>.Instance)
            .GenerateToStringAsync(extremeReport);

        html.ShouldContain("01:05:03.456");
        html.ShouldContain("Anti-Tamper Protection");
        html.ShouldContain("Anti-Dump Protection");
        html.ShouldContain("Anti-Decompiler Protection");
        html.ShouldContain("Reference Proxy");
        html.ShouldContain("Method Encryption");
        html.ShouldContain("Dependency Embedding");
        html.ShouldContain("Virtualization");
        html.ShouldContain("Packing");
        html.ShouldContain("Strong-Name Signing");
        html.ShouldContain("Watermark");
    }

    #endregion

    #region B-112: VmEncoder volatile & ref structs, MethodBodyPeEncryptor bounds

    [Fact]
    public void VmEncoder_VolatilePrefix_SkipsWithUnsupportedOpcode()
    {
        const string source = """
            public static class VolatileHolder
            {
                private static volatile int _flag;
                public static int ReadFlag() => _flag;
            }
            """;

        var dllPath = CoreServicesTestSupport.CompileLibrary(_dir, "VolLib", source);
        using var module = ModuleDefMD.Load(File.ReadAllBytes(dllPath));
        var method = module.Types.Single(t => t.Name == "VolatileHolder").Methods.Single(m => m.Name == "ReadFlag");

        method.Body.Instructions.ShouldContain(i => i.OpCode.Code == Code.Volatile);

        var ok = VmEncoder.TryEncode(
            method,
            new Dictionary<MethodDef, int>(),
            new VmMemberTables(),
            out _,
            out var skipReason);

        ok.ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.UnsupportedOpcode);
    }

    [Theory]
    [InlineData("System.Text.Json", "Utf8JsonReader", false)]
    [InlineData("System.Buffers", "SequenceReader`1", true)]
    [InlineData("System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", false)]
    [InlineData("System", "TypedReference", false)]
    [InlineData("System", "ArgIterator", false)]
    [InlineData("System", "RuntimeArgumentHandle", false)]
    [InlineData("System.MemoryExtensions", "SpanSplitEnumerator`1", true)]
    public void VmEncoder_CommonBclRefStructs_SkippedInCalleeSignatureWhenUnresolved(
        string ns, string name, bool genericArityOne)
    {
        var module = new ModuleDefUser("T", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var callerType = new TypeDefUser("CallerType", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(callerType);

        var extAsm = new AssemblyRefUser("ExternalBcl", new Version(8, 0, 0, 0));
        ITypeDefOrRef refStructType;
        if (ns == "System.MemoryExtensions")
        {
            var outer = new TypeRefUser(module, "System", "MemoryExtensions", extAsm);
            refStructType = new TypeRefUser(module, string.Empty, name, outer);
        }
        else
        {
            refStructType = new TypeRefUser(module, ns, name, extAsm);
        }

        TypeSig sig = genericArityOne
            ? new GenericInstSig(new ValueTypeSig(refStructType), module.CorLibTypes.Char)
            : new ValueTypeSig(refStructType);

        var helper = new TypeRefUser(module, "System", "Helper", extAsm);
        var callee = new MemberRefUser(module, "Consume", MethodSig.CreateStatic(module.CorLibTypes.Void, sig), helper);

        var caller = new MethodDefUser(
            "CallIt",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            dnlib.DotNet.MethodImplAttributes.IL,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static)
        {
            Body = new CilBody()
        };
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Call, callee));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        callerType.Methods.Add(caller);

        var ok = VmEncoder.TryEncode(
            caller,
            new Dictionary<MethodDef, int>(),
            new VmMemberTables(),
            out _,
            out var skipReason);

        ok.ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.ByRef);
    }

    [Fact]
    public void VmEncoder_UnresolvedRegularValueTypeInCalleeSignature_RemainsEligible()
    {
        var module = new ModuleDefUser("T", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var callerType = new TypeDefUser("CallerType", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(callerType);

        var extAsm = new AssemblyRefUser("ExternalBcl", new Version(8, 0, 0, 0));
        var dateTimeRef = new TypeRefUser(module, "System", "DateTime", extAsm);
        var helper = new TypeRefUser(module, "System", "Clock", extAsm);
        var callee = new MemberRefUser(
            module,
            "Now",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, new ValueTypeSig(dateTimeRef)),
            helper);

        var caller = new MethodDefUser(
            "CallIt",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            dnlib.DotNet.MethodImplAttributes.IL,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static)
        {
            Body = new CilBody()
        };
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Call, callee));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        callerType.Methods.Add(caller);

        var ok = VmEncoder.TryEncode(
            caller,
            new Dictionary<MethodDef, int>(),
            new VmMemberTables(),
            out _,
            out var skipReason);

        ok.ShouldBeTrue(skipReason);
    }

    [Fact]
    public async Task MethodBodyPeEncryptor_DuplicateMagicMarker_ThrowsInvalidOperationException()
    {
        var inputPath = CoreServicesTestSupport.CompileLibrary(
            _dir,
            "MethodEncTarget",
            """
            public static class Worker
            {
                public static int Add(int a, int b) => a + b + 10;
            }
            """);

        // Write two copies of the magic marker into a valid PE so PatchBlob detects the duplicate.
        var peBytes = await File.ReadAllBytesAsync(inputPath);
        var dupPath = Path.Combine(_dir, "DuplicateMagic.dll");
        var magicLen = MethodEncryptionMetadata.Magic.Length;
        var slotSize = magicLen + MethodEncryptionMetadata.HeaderBytes + MethodEncryptionMetadata.EntryBytes;
        var extended = new byte[peBytes.Length + slotSize * 2];
        Buffer.BlockCopy(peBytes, 0, extended, 0, peBytes.Length);
        MethodEncryptionMetadata.Magic.CopyTo(extended.AsSpan(peBytes.Length));
        MethodEncryptionMetadata.Magic.CopyTo(extended.AsSpan(peBytes.Length + slotSize));
        await File.WriteAllBytesAsync(dupPath, extended);

        using var loaded = ModuleDefMD.Load(File.ReadAllBytes(dupPath));
        var method = loaded.Types.Single(t => t.Name == "Worker").Methods.Single(m => m.Name == "Add");
        var metadata = new MethodEncryptionMetadata([new EncryptedMethodBody(method, 0x5A)]);

        var ex = Should.Throw<InvalidOperationException>(() => MethodBodyPeEncryptor.Encrypt(dupPath, metadata));
        ex.Message.ShouldContain("more than once");
    }

    [Theory]
    [InlineData("Span`1")]
    [InlineData("ReadOnlySpan`1")]
    public void VmEncoder_SpanEnumeratorNestedRefStruct_SkippedAsByRefWhenUnresolved(string spanName)
    {
        var module = new ModuleDefUser("T", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var callerType = new TypeDefUser("CallerType", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(callerType);

        var extAsm = new AssemblyRefUser("ExternalBcl", new Version(8, 0, 0, 0));
        var spanType = new TypeRefUser(module, "System", spanName, extAsm);
        var enumType = new TypeRefUser(module, string.Empty, "Enumerator", spanType);
        var sig = new GenericInstSig(new ValueTypeSig(enumType), module.CorLibTypes.Char);

        var helper = new TypeRefUser(module, "System", "Helper", extAsm);
        var callee = new MemberRefUser(module, "Consume", MethodSig.CreateStatic(module.CorLibTypes.Void, sig), helper);

        var caller = new MethodDefUser(
            "CallIt",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            dnlib.DotNet.MethodImplAttributes.IL,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static)
        {
            Body = new CilBody()
        };
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Call, callee));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        callerType.Methods.Add(caller);

        var ok = VmEncoder.TryEncode(
            caller,
            new Dictionary<MethodDef, int>(),
            new VmMemberTables(),
            out _,
            out var skipReason);

        ok.ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.ByRef);
    }

    [Fact]
    public void VmEncoder_CollidingUserClassesAndNonRefStructs_AreNotFalselySkippedAsByRef()
    {
        const string source = """
            namespace MyApp
            {
                public class Utf8JsonReader { }

                public class Span<T> { }

                public struct NonRefSpan<T>
                {
                    public T Value;
                }

                public static class Helper
                {
                    public static int TakeClass(Utf8JsonReader reader) => reader != null ? 1 : 0;

                    public static int TakeGenericClass(Span<int> items) => items != null ? 1 : 0;

                    public static void TakeResolvedNonRefStruct(NonRefSpan<int> item) { }
                }

                public static class Caller
                {
                    public static int CallNewClass() =>
                        Helper.TakeClass(new Utf8JsonReader());

                    public static int CallClassParams(Utf8JsonReader reader, Span<int> items) =>
                        Helper.TakeClass(reader) + Helper.TakeGenericClass(items);
                }
            }
            """;

        var dllPath = CoreServicesTestSupport.CompileLibrary(_dir, "CollidingNames", source);
        using var module = ModuleDefMD.Load(File.ReadAllBytes(dllPath));
        var callerType = module.Types.Single(t => t.Name == "Caller");

        foreach (var methodName in new[] { "CallNewClass", "CallClassParams" })
        {
            var method = callerType.Methods.Single(m => m.Name == methodName);
            var ok = VmEncoder.TryEncode(
                method,
                new Dictionary<MethodDef, int>(),
                new VmMemberTables(),
                out _,
                out var skipReason);

            ok.ShouldBeTrue($"{methodName} was skipped: {skipReason}");
        }

        // A resolved non-ref struct sharing a colliding short name is NOT treated as ByRef.
        var helperType = module.Types.Single(t => t.Name == "Helper");
        var structType = new TypeDefUser(
            "MyApp",
            "Span`1",
            module.CorLibTypes.GetTypeRef("System", "ValueType"));
        structType.Attributes = dnlib.DotNet.TypeAttributes.Public | dnlib.DotNet.TypeAttributes.Sealed | dnlib.DotNet.TypeAttributes.SequentialLayout;
        module.Types.Add(structType);
        var structMethod = new MethodDefUser(
            "TakeNonRefSpanStruct",
            MethodSig.CreateStatic(module.CorLibTypes.Void, new GenericInstSig(new ValueTypeSig(structType), module.CorLibTypes.Int32)),
            dnlib.DotNet.MethodImplAttributes.IL,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static)
        {
            Body = new CilBody()
        };
        structMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        helperType.Methods.Add(structMethod);

        var structOk = VmEncoder.TryEncode(
            structMethod,
            new Dictionary<MethodDef, int>(),
            new VmMemberTables(),
            out _,
            out var structReason);

        structOk.ShouldBeFalse();
        structReason.ShouldBe(VmSkipReasons.NonPrimitiveValuetype);

        // An unresolved ClassSig sharing a known ref-struct name is a class, not a ref struct.
        var extAsm = new AssemblyRefUser("ExternalUnresolved", new Version(8, 0, 0, 0));
        var extClassRef = new TypeRefUser(module, "System.Text.Json", "Utf8JsonReader", extAsm);
        var extClassMethod = new MethodDefUser(
            "TakeUnresolvedClass",
            MethodSig.CreateStatic(module.CorLibTypes.Void, new ClassSig(extClassRef)),
            dnlib.DotNet.MethodImplAttributes.IL,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static)
        {
            Body = new CilBody()
        };
        extClassMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        helperType.Methods.Add(extClassMethod);

        var extClassOk = VmEncoder.TryEncode(
            extClassMethod,
            new Dictionary<MethodDef, int>(),
            new VmMemberTables(),
            out _,
            out var extClassReason);

        extClassOk.ShouldBeTrue(extClassReason);
    }

    [Fact]
    public async Task ObfySettings_NullNestedSection_ReturnsValidationErrorInsteadOfNullReferenceException()
    {
        var settings = JsonSerializer.Deserialize<ObfySettings>(
            """{ "protection": null }""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var validationEx = Should.Throw<ValidationException>(() => settings.ApplyLevel());
        validationEx.Message.ShouldBe("Settings section 'Protection' cannot be null.");

        var inputPath = CoreServicesTestSupport.CompileLibrary(
            _dir,
            "NullSectionTarget",
            "public static class Target { public static int Ping() => 1; }");
        var outputPath = Path.Combine(_dir, "NullSectionTarget.obf.dll");

        using var container = CoreServicesTestSupport.BuildContainer();
        var serviceResult = await container.Resolve<IObfuscationService>().ObfuscateAsync(inputPath, outputPath, settings);
        serviceResult.Success.ShouldBeFalse();
        serviceResult.ErrorMessage.ShouldBe("Invalid settings: Settings section 'Protection' cannot be null.");

        var closedSetResult = await container.Resolve<IClosedSetProcessor>().ExecuteAsync(
            [ClosedSetInput.From(inputPath)],
            Path.Combine(_dir, "closed-out"),
            settings);
        closedSetResult.Success.ShouldBeFalse();
        closedSetResult.ErrorMessage.ShouldBe("Invalid settings: Settings section 'Protection' cannot be null.");
    }

    [Fact]
    public void IncrementalCache_ComputeMergeKey_InvalidatesWhenInputFileRenamed()
    {
        var dir1 = Path.Combine(_dir, "rename-a");
        var dir2 = Path.Combine(_dir, "rename-b");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        var app1 = CoreServicesTestSupport.CompileLibrary(dir1, "AppOne", "public class A { public int X() => 1; }");
        var lib1 = CoreServicesTestSupport.CompileLibrary(dir1, "LibOne", "public class B { public int Y() => 2; }");

        var app2 = Path.Combine(dir2, "AppOne.dll");
        var lib2Renamed = Path.Combine(dir2, "Excluded.dll");
        File.Copy(app1, app2);
        File.Copy(lib1, lib2Renamed);

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            AssemblyMerge = { Enabled = true, ExcludePatterns = ["Excluded.dll"] }
        };

        var key1 = IncrementalCache.ComputeMergeKey([app1, lib1], settings);
        var key2 = IncrementalCache.ComputeMergeKey([app2, lib2Renamed], settings);

        key2.ShouldNotBe(key1);
    }

    #endregion

    #region Helpers

    private static string CompileWithResource(string source, string dir, string name)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            name,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(dir, name + ".dll");
        var resource = new ResourceDescription(
            "sample.txt",
            () => new MemoryStream("resource-payload-content"u8.ToArray()),
            isPublic: true);

        using var fs = File.Create(path);
        var emit = compilation.Emit(fs, manifestResources: [resource]);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static object? InvokeInIsolatedContext(string assemblyPath, string typeName, string methodName, params object?[] args)
    {
        var alc = new AssemblyLoadContext("wave2a-probe-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(assemblyPath);
            var type = asm.GetType(typeName)!;
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
            return method.Invoke(null, args);
        }
        finally
        {
            alc.Unload();
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunDotnet(string launcherPath)
    {
        var start = new ProcessStartInfo("dotnet", $"\"{launcherPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000).ShouldBeTrue("launcher timed out");
        return (process.ExitCode, stdout, stderr);
    }

    private int RunManagedAssembly(string assemblyPath)
    {
        var runtimeConfig = Path.Combine(_dir, "probe.runtimeconfig.json");
        if (!File.Exists(runtimeConfig))
        {
            var major = Environment.Version.Major;
            File.WriteAllText(runtimeConfig, $$"""
                {
                  "runtimeOptions": {
                    "tfm": "net{{major}}.0",
                    "rollForward": "LatestMinor",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "{{major}}.0.0"
                    }
                  }
                }
                """);
        }

        var start = new ProcessStartInfo("dotnet", $"exec --runtimeconfig \"{runtimeConfig}\" \"{assemblyPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000).ShouldBeTrue("managed probe timed out: " + stderr);
        return process.ExitCode;
    }

    private static void WriteCrossPlatformSnk(string path)
    {
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(includePrivateParameters: true);
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((byte)0x07);
        writer.Write((byte)0x02);
        writer.Write((ushort)0);
        writer.Write(0x00002400u);
        writer.Write(0x32415352u);
        writer.Write(2048u);

        uint pubExp = 0;
        foreach (var b in p.Exponent!)
            pubExp = (pubExp << 8) | b;
        writer.Write(pubExp);

        WriteReversedPadded(writer, p.Modulus!, 256);
        WriteReversedPadded(writer, p.P!, 128);
        WriteReversedPadded(writer, p.Q!, 128);
        WriteReversedPadded(writer, p.DP!, 128);
        WriteReversedPadded(writer, p.DQ!, 128);
        WriteReversedPadded(writer, p.InverseQ!, 128);
        WriteReversedPadded(writer, p.D!, 256);

        File.WriteAllBytes(path, ms.ToArray());

        static void WriteReversedPadded(BinaryWriter w, byte[] bigEndian, int length)
        {
            var le = new byte[length];
            for (var i = 0; i < bigEndian.Length; i++)
                le[i] = bigEndian[bigEndian.Length - 1 - i];
            w.Write(le);
        }
    }

    private sealed class CapturingTraceListener(List<string> sink) : TraceListener
    {
        public override void Write(string? message)
        {
            if (message is not null)
                sink.Add(message);
        }

        public override void WriteLine(string? message)
        {
            if (message is not null)
                sink.Add(message);
        }
    }

    #endregion
}

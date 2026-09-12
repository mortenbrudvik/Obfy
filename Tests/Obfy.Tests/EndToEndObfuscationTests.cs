using System.Buffers.Binary;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Autofac;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// End-to-end tests that obfuscate a real compiled assembly (referencing the actual runtime, so its
/// core-library reference is System.Runtime / System.Private.CoreLib as in a shipped app) and then
/// load and run the result. These catch failures that unit tests built on an mscorlib test module miss
/// — most importantly, references to framework types (crypto, ...) that do not resolve through the
/// core-library facade at runtime.
/// </summary>
public class EndToEndObfuscationTests
{
    private static string CompileToAssembly(string source, string dir, string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static List<int> ReadMethodEncryptionBlobKeys(string pePath)
    {
        var pe = File.ReadAllBytes(pePath);
        var magic = MethodEncryptionMetadata.Magic;
        var max = pe.Length - (magic.Length + MethodEncryptionMetadata.HeaderBytes);
        for (var i = 0; i <= max; i++)
        {
            var match = true;
            for (var j = 0; j < magic.Length; j++)
            {
                if (pe[i + j] != magic[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;

            var count = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(i + MethodEncryptionMetadata.MagicLength));
            var keys = new List<int>(count);
            var offset = i + MethodEncryptionMetadata.MagicLength + MethodEncryptionMetadata.HeaderBytes;
            for (var n = 0; n < count; n++)
            {
                keys.Add(BinaryPrimitives.ReadInt32LittleEndian(
                    pe.AsSpan(offset + MethodEncryptionMetadata.KeyOffset)));
                offset += MethodEncryptionMetadata.EntryBytes;
            }

            return keys;
        }

        throw new InvalidOperationException("Method-encryption blob was not found.");
    }

    private static string CompileToAssemblyWithRef(string source, string dir, string assemblyName, string referencePath)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(referencePath));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static object? LoadAndInvoke(string assemblyPath, string typeName, string methodName)
    {
        var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(assemblyPath);
            var type = asm.GetType(typeName);
            type.ShouldNotBeNull();
            var method = type!.GetMethod(methodName);
            method.ShouldNotBeNull();
            return method!.Invoke(null, null);
        }
        finally
        {
            alc.Unload();
        }
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public async Task StringEncryption_RunsOnRealAssembly(EncryptionAlgorithm algorithm)
    {
        // A real assembly references System.Runtime, which does not host the crypto types. Referencing
        // Aes through the corlib facade throws TypeLoadException at runtime; this catches that.
        const string source = "public static class Lib { public static string Get() => \"IntegrationSecretString\"; }";

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "StrLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = true, Algorithm = algorithm, MinStringLength = 3 }
            };
            var context = PipelineContext.ForAssembly(module, settings);
            var obfuscator = new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object);

            (await obfuscator.ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "StrLib.obf.dll");
            module.Write(output);

            LoadAndInvoke(output, "Lib", "Get").ShouldBe("IntegrationSecretString");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public async Task StringEncryption_WithHardenedHelpers_RunsOnRealAssembly(EncryptionAlgorithm algorithm)
    {
        const string source = "public static class Lib { public static string Get() => \"IntegrationSecretString\"; }";

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-hh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "StrHelpLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = true, Algorithm = algorithm, MinStringLength = 3 },
                ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 10 },
                Protection = { ReferenceProxy = true }
            };
            var context = PipelineContext.ForAssembly(module, settings);

            (await new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();
            (await new ControlFlowObfuscator(new Mock<ILogger<ControlFlowObfuscator>>().Object)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();
            (await new ReferenceProxyObfuscator(new Mock<ILogger<ReferenceProxyObfuscator>>().Object)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "StrHelpLib.obf.dll");
            module.Write(output);

            LoadAndInvoke(output, "Lib", "Get").ShouldBe("IntegrationSecretString");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ReferenceProxy_ExternalCalls_RunOnRealAssembly()
    {
        const string source = "public static class Lib { public static int Get() => \"hello\".Length; }";
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "ExtProxyLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
            var settings = new ObfySettings
            {
                Protection = { ReferenceProxy = true, ProxyExternalCalls = true }
            };
            var context = PipelineContext.ForAssembly(module, settings);
            (await new ReferenceProxyObfuscator(new Mock<ILogger<ReferenceProxyObfuscator>>().Object)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "ExtProxyLib.obf.dll");
            module.Write(output);
            LoadAndInvoke(output, "Lib", "Get").ShouldBe(5);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task DependencyEmbedding_LoadsMissingSiblingAssembly()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-emb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dep = CompileToAssembly("public static class Dep { public static int N => 4; }", dir, "Dep");
            var libSource = """
                public static class Lib
                {
                    public static int Get() => Dep.N + 1;
                }
                """;
            var lib = CompileToAssemblyWithRef(libSource, dir, "EmbLib", dep);

            var output = Path.Combine(dir, "EmbLib.obf.dll");
            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();
            var service = container.Resolve<IObfuscationService>();
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                SymbolRenaming = { Enabled = false, PreservePublicApi = true },
                DependencyEmbedding = { Enabled = true }
            };

            var result = await service.ObfuscateAsync(lib, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0);

            File.Delete(dep);
            LoadAndInvoke(output, "Lib", "Get").ShouldBe(5);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task AntiTamper_VerifyPassesOnUntamperedRealAssembly()
    {
        // Exercises the full anti-tamper chain on a real assembly: inject -> AssemblyProcessor.SaveAsync
        // (writes, then patches the integrity hash) -> load and run. The runtime Verify() in the module
        // initializer must NOT falsely trip, and references SHA256, which is not in the corlib facade.
        const string source = "public static class Lib { public static int Get() => 4242; }";

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "TamperLib");
            var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                Protection = { AntiTamper = { Enabled = true, CheckModuleInitializer = true } }
            };
            var context = PipelineContext.ForAssembly(module, settings);

            var obfuscator = new AntiTamperObfuscator(new Mock<ILogger<AntiTamperObfuscator>>().Object);
            (await obfuscator.ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "TamperLib.obf.dll");
            var processor = new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object);
            await processor.SaveAsync(context, output); // writes and patches the integrity hash

            // If Verify() threw a TypeLoadException or falsely detected tampering, this would fail.
            LoadAndInvoke(output, "Lib", "Get").ShouldBe(4242);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task SymbolRenaming_RewritesReferences_AndRunsOnRealAssembly()
    {
        // Renames private members and rewrites every reference; the public API is preserved so the
        // result can be invoked by its original name and must still compute correctly.
        const string source = """
            public static class Calc
            {
                private static int Factor = 3;
                private static int Triple(int x) => x * Factor;
                public static int Run() => Triple(14) + 1;
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "RenameLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                SymbolRenaming =
                {
                    Enabled = true,
                    RenameMethods = true,
                    RenameFields = true,
                    PreservePublicApi = true,
                    Mode = NamingMode.Sequential
                }
            };
            var context = PipelineContext.ForAssembly(module, settings);
            var renamer = new SymbolRenamingObfuscator(new NameGenerator(), new Mock<ILogger<SymbolRenamingObfuscator>>().Object);

            var result = await renamer.ObfuscateAsync(context);
            result.Success.ShouldBeTrue();
            (result.Statistics.MethodsRenamed + result.Statistics.FieldsRenamed).ShouldBeGreaterThan(0);

            var output = Path.Combine(dir, "RenameLib.obf.dll");
            module.Write(output);

            LoadAndInvoke(output, "Calc", "Run").ShouldBe((14 * 3) + 1);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task AntiDebug_UndebuggedRun_Proceeds_OnRealAssembly()
    {
        // With no debugger attached, the injected Check() is a no-op and the method runs normally.
        // (Under a debugger the injected code calls Environment.Exit — tests run without one.)
        const string source = "public static class Lib { public static int Get() => 7; }";

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "AntiDebugLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                Protection = { AntiDebug = true }
            };
            var context = PipelineContext.ForAssembly(module, settings);

            var obfuscator = new AntiDebugObfuscator(new Mock<ILogger<AntiDebugObfuscator>>().Object);
            (await obfuscator.ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "AntiDebugLib.obf.dll");
            module.Write(output);

            LoadAndInvoke(output, "Lib", "Get").ShouldBe(7);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task AntiDebug_ScatteredChecks_UndebuggedRun_Proceeds()
    {
        const string source = """
            public static class Lib
            {
                public static int Get() => Helper() + 1;
                static int Helper() => 6;
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-ad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "AntiDebugScatterLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                Protection = { AntiDebug = true }
            };
            var context = PipelineContext.ForAssembly(module, settings);
            (await new AntiDebugObfuscator(new Mock<ILogger<AntiDebugObfuscator>>().Object)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "AntiDebugScatterLib.obf.dll");
            module.Write(output);

            LoadAndInvoke(output, "Lib", "Get").ShouldBe(7);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task MethodEncryption_RoundTripsOnRealAssembly()
    {
        const string source = """
            public static class Lib
            {
                public static int Get()
                {
                    int x = 7;
                    x = x + 35;
                    return x;
                }
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-mc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "McLib");
            var output = Path.Combine(dir, "McLib.obf.dll");

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();

            var service = container.Resolve<IObfuscationService>();
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                SymbolRenaming = { Enabled = false, PreservePublicApi = true },
                Protection = { MethodEncryption = true }
            };

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0);

            LoadAndInvoke(output, "Lib", "Get").ShouldBe(42);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task MethodEncryption_TwoMethods_RoundTripWithDistinctKeys()
    {
        const string source = """
            public static class Lib
            {
                public static int Get() => Helper() + 1;
                static int Helper()
                {
                    int x = 7;
                    x = x + 34;
                    return x;
                }
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-mc2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "Mc2Lib");
            var output = Path.Combine(dir, "Mc2Lib.obf.dll");

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();

            var service = container.Resolve<IObfuscationService>();
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                SymbolRenaming = { Enabled = false, PreservePublicApi = true },
                Protection = { MethodEncryption = true }
            };

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Statistics.ProtectionsApplied.ShouldBeGreaterThanOrEqualTo(2);
            result.Statistics.MethodsEncrypted.ShouldBeGreaterThanOrEqualTo(2);

            var keys = ReadMethodEncryptionBlobKeys(output);
            keys.Count.ShouldBeGreaterThanOrEqualTo(2);
            keys.Distinct().Count().ShouldBe(keys.Count);

            LoadAndInvoke(output, "Lib", "Get").ShouldBe(42);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Aggressive_FullPipeline_RunsOnRealAssembly()
    {
        // Individual technique tests would not catch interactions such as switch flattening after
        // decrypt-call insertion, or metadata/anti-decompiler making AES/SHA256 unloadable.
        const string source = """
            public static class Lib
            {
                public static int Run()
                {
                    var s = "hello-world-secret";
                    long n = 1234567890123L;
                    int x = s.Length;
                    x = x + (int)(n % 100);
                    x = x * 2;
                    return x;
                }
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-agg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "AggLib");
            var output = Path.Combine(dir, "AggLib.obf.dll");

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();

            var service = container.Resolve<IObfuscationService>();
            var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
            settings.SymbolRenaming.PreservePublicApi = true;

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Statistics.StringsEncrypted.ShouldBeGreaterThan(0);
            result.Statistics.ConstantsEncrypted.ShouldBeGreaterThan(0);

            LoadAndInvoke(output, "Lib", "Run").ShouldBe(82);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task NativeAotProfile_DisablesPeProtections_AndStillRuns()
    {
        const string source = "public static class Lib { public static int Get() => 9; }";
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-aot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "AotLib");
            var output = Path.Combine(dir, "AotLib.obf.dll");

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();

            var service = container.Resolve<IObfuscationService>();
            var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
            settings.SymbolRenaming.PreservePublicApi = true;
            settings.RuntimeProfile = RuntimeProfile.NativeAot;

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Warnings.ShouldContain(w => w.Contains("Method encryption disabled", StringComparison.OrdinalIgnoreCase));
            result.Warnings.ShouldContain(w => w.Contains("Anti-dump disabled", StringComparison.OrdinalIgnoreCase));

            using var loaded = ModuleDefMD.Load(File.ReadAllBytes(output));
            loaded.Types.ShouldNotContain(t => t.Name == "<MethodCrypt>");
            loaded.Types.ShouldNotContain(t => t.Name == "<AntiDump>");

            LoadAndInvoke(output, "Lib", "Get").ShouldBe(9);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task MethodEncryptionAndAntiTamper_WithSnk_StillRunsAndStaysSigned()
    {
        const string source = """
            public static class Lib
            {
                public static int Get()
                {
                    int x = 7;
                    x = x + 35;
                    return x;
                }
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-sign-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var snkPath = Path.Combine(dir, "test.snk");
            WriteSnk(snkPath);

            var input = CompileToAssembly(source, dir, "SignMcLib");
            var output = Path.Combine(dir, "SignMcLib.obf.dll");

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();

            var service = container.Resolve<IObfuscationService>();
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                SymbolRenaming = { Enabled = false, PreservePublicApi = true },
                Protection =
                {
                    MethodEncryption = true,
                    AntiTamper = { Enabled = true }
                },
                Signing = { Enabled = true, KeyFile = snkPath }
            };

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using var loaded = ModuleDefMD.Load(File.ReadAllBytes(output));
            loaded.IsStrongNameSigned.ShouldBeTrue();
            loaded.GetTypes().ShouldContain(t => t.Name == "<MethodCrypt>");
            loaded.GetTypes().ShouldContain(t => t.Name == "<AntiTamper>");

            LoadAndInvoke(output, "Lib", "Get").ShouldBe(42);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task CompiledObfuscationAttribute_FeatureRenaming_DoesNotSkipStrings()
    {
        const string source = """
            using System.Reflection;
            [Obfuscation(Exclude = true, Feature = "renaming")]
            public static class Lib
            {
                public static string Get() => "EncryptThisSecretString";
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-attr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "AttrLib");
            var output = Path.Combine(dir, "AttrLib.obf.dll");

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();

            var service = container.Resolve<IObfuscationService>();
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = true, MinStringLength = 3 },
                SymbolRenaming = { Enabled = true, RenameTypes = true, PreservePublicApi = false, Mode = NamingMode.Sequential }
            };

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using var loaded = ModuleDefMD.Load(File.ReadAllBytes(output));
            loaded.Types.ShouldContain(t => t.Name == "Lib");
            var lib = loaded.Types.Single(t => t.Name == "Lib");
            var get = lib.Methods.Single(m => m.Name == "Get");
            get.Body.Instructions.ShouldNotContain(i =>
                i.OpCode.Code == dnlib.DotNet.Emit.Code.Ldstr && (string)i.Operand! == "EncryptThisSecretString");

            LoadAndInvoke(output, "Lib", "Get").ShouldBe("EncryptThisSecretString");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static void WriteSnk(string path)
    {
#pragma warning disable SYSLIB0028, CA1416
        var cspParams = new CspParameters { KeyNumber = (int)KeyNumber.Signature };
        using var csp = new RSACryptoServiceProvider(1024, cspParams);
        File.WriteAllBytes(path, csp.ExportCspBlob(includePrivateParameters: true));
#pragma warning restore SYSLIB0028, CA1416
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public async Task ConstantEncryption_Aes_RunsOnRealAssembly(EncryptionAlgorithm algorithm)
    {
        // mscorlib test modules mask TypeLoadException for Aes/SHA256; a real System.Runtime
        // assembly is the setup that used to crash at load.
        const string source = "public static class Lib { public static long Get() => 9876543210L; }";

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-const-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "ConstLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                ConstantEncryption =
                {
                    Enabled = true,
                    Algorithm = algorithm,
                    EncryptIntegers = false,
                    EncryptLongs = true,
                    LongThreshold = 0
                }
            };
            var context = PipelineContext.ForAssembly(module, settings);
            var obfuscator = new ConstantEncryptionObfuscator(new Mock<ILogger<ConstantEncryptionObfuscator>>().Object);

            (await obfuscator.ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "ConstLib.obf.dll");
            module.Write(output);

            LoadAndInvoke(output, "Lib", "Get").ShouldBe(9876543210L);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}

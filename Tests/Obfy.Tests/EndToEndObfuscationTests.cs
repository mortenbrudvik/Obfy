using System.Buffers.Binary;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Autofac;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
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
    private static string CompileToAssembly(
        string source, string dir, string assemblyName, OptimizationLevel optimization = OptimizationLevel.Debug)
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
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: optimization));

        var path = Path.Combine(dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static string CompileToExe(string source, string dir, string assemblyName)
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
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var path = Path.Combine(dir, assemblyName + ".exe");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static string CompileToExeWithRef(string source, string dir, string assemblyName, string referencePath)
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
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var path = Path.Combine(dir, assemblyName + ".exe");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static IObfuscationService CreateService()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build().Resolve<IObfuscationService>();
    }

    private static ObfySettings PackingSettings() => new()
    {
        Level = ObfuscationLevel.Custom,
        StringEncryption = { Enabled = false },
        SymbolRenaming = { Enabled = false, PreservePublicApi = true },
        Packing = { Enabled = true }
    };

    private static int RunLauncher(string launcher, string extraArgs = "", string? workingDirectory = null)
        => RunLauncherCapture(launcher, extraArgs, workingDirectory).ExitCode;

    private static (int ExitCode, string StdErr) RunLauncherCapture(
        string launcher, string extraArgs = "", string? workingDirectory = null)
    {
        var args = string.IsNullOrEmpty(extraArgs) ? $"\"{launcher}\"" : $"\"{launcher}\" {extraArgs}";
        var start = new System.Diagnostics.ProcessStartInfo("dotnet", args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        if (!string.IsNullOrEmpty(workingDirectory))
            start.WorkingDirectory = workingDirectory;
        using var process = System.Diagnostics.Process.Start(start);
        process.ShouldNotBeNull();
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit(15000).ShouldBeTrue("launcher timed out");
        var stderr = stderrTask.GetAwaiter().GetResult();
        stdoutTask.GetAwaiter().GetResult();
        return (process.ExitCode, stderr);
    }

    private static void WriteRuntimeConfig(string assemblyPath)
    {
        var json = """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "10.0.0"
                }
              }
            }
            """;
        File.WriteAllText(Path.ChangeExtension(assemblyPath, ".runtimeconfig.json"), json);
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* ignore */ }
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

    private static bool CallsExecute(MethodDef method) =>
        method.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "Execute");

    private static void AssertVmStub(ModuleDef module, string typeName, string methodName)
    {
        var method = module.Types.First(t => t.Name == typeName).FindMethod(methodName);
        method.ShouldNotBeNull();
        CallsExecute(method!).ShouldBeTrue($"{typeName}.{methodName} should call Execute");
    }

    private static void AssertNotVmStub(ModuleDef module, string typeName, string methodName)
    {
        var method = module.Types.First(t => t.Name == typeName).FindMethod(methodName);
        method.ShouldNotBeNull();
        CallsExecute(method!).ShouldBeFalse($"{typeName}.{methodName} should not call Execute");
    }

    private static object? LoadAndInvoke(string assemblyPath, string typeName, string methodName, params object[] args)
    {
        var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(assemblyPath);
            var type = asm.GetType(typeName);
            type.ShouldNotBeNull();
            var method = type!.GetMethod(methodName);
            method.ShouldNotBeNull();
            return method!.Invoke(null, args.Length == 0 ? null : args);
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public async Task Incremental_ReusesCachedOutput()
    {
        const string source = "public static class Lib { public static int Get() => 3; }";
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-inc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "IncLib");
            var output = Path.Combine(dir, "IncLib.obf.dll");
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
                Incremental = { Enabled = true }
            };

            var first = await service.ObfuscateAsync(input, output, settings);
            first.Success.ShouldBeTrue(first.ErrorMessage);
            first.Warnings.ShouldNotContain(w => w.Contains("Incremental: reused"));
            var stamp = File.GetLastWriteTimeUtc(output);

            await Task.Delay(50);
            var second = await service.ObfuscateAsync(input, output, settings);
            second.Success.ShouldBeTrue(second.ErrorMessage);
            second.Warnings.ShouldContain(w => w.Contains("Incremental: reused cached output"));
            File.GetLastWriteTimeUtc(output).ShouldBe(stamp);
            LoadAndInvoke(output, "Lib", "Get").ShouldBe(3);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Virtualization_RunsSimpleArithmeticOnRealAssembly()
    {
        const string source = "public static class Lib { public static int Add(int a, int b) => a + b; }";
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-vm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "VmLib");
            var output = Path.Combine(dir, "VmLib.obf.dll");
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
                Virtualization = { Enabled = true }
            };

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0);

            using (var loaded = ModuleDefMD.Load(File.ReadAllBytes(output)))
                loaded.Types.ShouldContain(t => t.Name == "<Vm>");

            LoadAndInvoke(output, "Lib", "Add", 2, 3).ShouldBe(5);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Virtualization_RunsLocalsAndBranchesOnRealAssembly()
    {
        const string source = """
            public static class Lib
            {
                public static int Scale(int a, int b)
                {
                    int x = a + 1;
                    return x * b;
                }

                public static int Mix(int a, int b)
                {
                    int x = a + 1;
                    int y = b + 2;
                    return x * y;
                }

                public static int Five(int a)
                {
                    int a0 = a, a1 = a0 + 1, a2 = a1 + 1, a3 = a2 + 1, a4 = a3 + 1;
                    return a0 + a1 + a2 + a3 + a4;
                }

                public static int Pick(int a, int b)
                {
                    if (a > 0)
                        return a + b;
                    return a - b;
                }

                public static int Eq(int a, int b) => a == b ? 1 : 0;
                public static int Ne(int a, int b) => a != b ? 1 : 0;
                public static int Lt(int a, int b) => a < b ? 1 : 0;
                public static int Le(int a, int b) => a <= b ? 1 : 0;
                public static int Gt(int a, int b) => a > b ? 1 : 0;
                public static int Ge(int a, int b) => a >= b ? 1 : 0;

                public static int IfNe(int a, int b)
                {
                    if (a != 0)
                        return a + b;
                    return b;
                }

                public static int Div(int a, int b) => a / b;

                public static int Const() => 40 + 2;
            }
            """;
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-vm2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "Vm2Lib");
            var output = Path.Combine(dir, "Vm2Lib.obf.dll");
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
                Virtualization = { Enabled = true }
            };

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using (var loaded = ModuleDefMD.Load(File.ReadAllBytes(output)))
            {
                loaded.Types.ShouldContain(t => t.Name == "<Vm>");
                AssertVmStub(loaded, "Lib", "Scale");
                AssertVmStub(loaded, "Lib", "Mix");
                AssertVmStub(loaded, "Lib", "Five");
                AssertVmStub(loaded, "Lib", "Pick");
                AssertVmStub(loaded, "Lib", "Eq");
                AssertVmStub(loaded, "Lib", "Lt");
                AssertVmStub(loaded, "Lib", "Le");
                AssertVmStub(loaded, "Lib", "Gt");
                AssertVmStub(loaded, "Lib", "Ge");
                AssertVmStub(loaded, "Lib", "Ne");
                AssertVmStub(loaded, "Lib", "Const");
                AssertNotVmStub(loaded, "Lib", "Div");
            }

            LoadAndInvoke(output, "Lib", "Scale", 2, 3).ShouldBe(9);
            LoadAndInvoke(output, "Lib", "Mix", 2, 3).ShouldBe(15);
            LoadAndInvoke(output, "Lib", "Five", 1).ShouldBe(15);
            LoadAndInvoke(output, "Lib", "Pick", 4, 1).ShouldBe(5);
            LoadAndInvoke(output, "Lib", "Pick", -3, 1).ShouldBe(-4);
            LoadAndInvoke(output, "Lib", "Pick", 0, 1).ShouldBe(-1);
            LoadAndInvoke(output, "Lib", "Eq", 2, 2).ShouldBe(1);
            LoadAndInvoke(output, "Lib", "Eq", 2, 3).ShouldBe(0);
            LoadAndInvoke(output, "Lib", "Ne", 2, 3).ShouldBe(1);
            LoadAndInvoke(output, "Lib", "Ne", 2, 2).ShouldBe(0);
            LoadAndInvoke(output, "Lib", "Lt", 1, 2).ShouldBe(1);
            LoadAndInvoke(output, "Lib", "Lt", 2, 2).ShouldBe(0);
            LoadAndInvoke(output, "Lib", "Le", 2, 2).ShouldBe(1);
            LoadAndInvoke(output, "Lib", "Le", 3, 2).ShouldBe(0);
            LoadAndInvoke(output, "Lib", "Gt", 3, 2).ShouldBe(1);
            LoadAndInvoke(output, "Lib", "Gt", 2, 2).ShouldBe(0);
            LoadAndInvoke(output, "Lib", "Ge", 2, 2).ShouldBe(1);
            LoadAndInvoke(output, "Lib", "Ge", 1, 2).ShouldBe(0);
            LoadAndInvoke(output, "Lib", "IfNe", -3, 1).ShouldBe(-2);
            LoadAndInvoke(output, "Lib", "IfNe", 0, 1).ShouldBe(1);
            LoadAndInvoke(output, "Lib", "IfNe", 4, 1).ShouldBe(5);
            LoadAndInvoke(output, "Lib", "Div", 8, 2).ShouldBe(4);
            LoadAndInvoke(output, "Lib", "Const").ShouldBe(42);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Virtualization_RunsReleaseBrtrueOnRealAssembly()
    {
        const string source = """
            public static class Lib
            {
                public static int IfZero(int a)
                {
                    if (a == 0)
                        return 1;
                    return 0;
                }
            }
            """;
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-vm3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "Vm3Lib", OptimizationLevel.Release);
            var output = Path.Combine(dir, "Vm3Lib.obf.dll");
            var service = CreateService();
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                SymbolRenaming = { Enabled = false, PreservePublicApi = true },
                Virtualization = { Enabled = true }
            };

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using (var loaded = ModuleDefMD.Load(File.ReadAllBytes(input)))
            {
                var method = loaded.Types.First(t => t.Name == "Lib").FindMethod("IfZero");
                method!.Body.Instructions.Any(i =>
                    i.OpCode.Code is Code.Brtrue or Code.Brtrue_S)
                    .ShouldBeTrue("Release if (a == 0) should emit brtrue");
            }

            using (var loaded = ModuleDefMD.Load(File.ReadAllBytes(output)))
                AssertVmStub(loaded, "Lib", "IfZero");

            LoadAndInvoke(output, "Lib", "IfZero", 0).ShouldBe(1);
            LoadAndInvoke(output, "Lib", "IfZero", 4).ShouldBe(0);
            LoadAndInvoke(output, "Lib", "IfZero", -1).ShouldBe(0);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Packing_ProducesRunnableLauncher()
    {
        const string source = "public static class Program { public static int Main() => 11; }";
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToExe(source, dir, "PackApp");
            var output = Path.Combine(dir, "PackApp.obf.exe");
            var service = CreateService();
            var settings = PackingSettings();

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Warnings.ShouldContain(w => w.Contains("Packed launcher:"));
            result.PackedLauncherPath.ShouldNotBeNull();

            File.Exists(output).ShouldBeTrue();
            var launcher = ManagedLauncherPacker.LauncherPathFor(output);
            File.Exists(launcher).ShouldBeTrue();
            File.Exists(ManagedLauncherPacker.RuntimeConfigPathFor(output)).ShouldBeTrue();
            result.PackedLauncherPath.ShouldBe(launcher);

            RunLauncher(launcher).ShouldBe(11);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Packing_LibraryWithoutEntryPoint_FailsWithEntryPointError()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-pack-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly("public static class Lib { public static int Get() => 1; }", dir, "Lib");
            var output = Path.Combine(dir, "Lib.obf.dll");
            var result = await CreateService().ObfuscateAsync(input, output, PackingSettings());

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldNotBeNull();
            result.ErrorMessage.ShouldContain("Packing failed");
            result.ErrorMessage.ShouldContain("entry point");
            File.Exists(output).ShouldBeTrue();
            File.Exists(ManagedLauncherPacker.LauncherPathFor(output)).ShouldBeFalse();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Packing_WhenPackFails_DoesNotLeaveSuccessfulIncrementalCache()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-pack-inc-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly("public static class Lib { public static int Get() => 1; }", dir, "Lib");
            var output = Path.Combine(dir, "Lib.obf.dll");
            var settings = PackingSettings();
            settings.Incremental.Enabled = true;
            var service = CreateService();

            var first = await service.ObfuscateAsync(input, output, settings);
            first.Success.ShouldBeFalse();

            var second = await service.ObfuscateAsync(input, output, settings);
            second.Success.ShouldBeFalse();
            second.Warnings.ShouldNotContain(w => w.Contains("Incremental: reused cached output"));
            File.Exists(ManagedLauncherPacker.LauncherPathFor(output)).ShouldBeFalse();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task IncrementalHit_WithPackingEnabled_StillProducesLauncher()
    {
        const string source = "public static class Program { public static int Main() => 11; }";
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-pack-inc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToExe(source, dir, "PackApp");
            var output = Path.Combine(dir, "PackApp.obf.exe");
            var settings = PackingSettings();
            settings.Incremental.Enabled = true;
            var service = CreateService();

            var first = await service.ObfuscateAsync(input, output, settings);
            first.Success.ShouldBeTrue(first.ErrorMessage);
            var launcher = ManagedLauncherPacker.LauncherPathFor(output);
            File.Exists(launcher).ShouldBeTrue();
            File.Delete(launcher);
            File.Delete(ManagedLauncherPacker.RuntimeConfigPathFor(output));

            var second = await service.ObfuscateAsync(input, output, settings);
            second.Success.ShouldBeTrue(second.ErrorMessage);
            second.Warnings.ShouldNotContain(w => w.Contains("Incremental: reused cached output"));
            File.Exists(launcher).ShouldBeTrue();
            RunLauncher(launcher).ShouldBe(11);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Packing_ForwardsMainArgs()
    {
        const string source = "public static class Program { public static int Main(string[] args) => args.Length == 1 && args[0] == \"ping\" ? 7 : 1; }";
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-pack-args-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToExe(source, dir, "ArgsApp");
            var output = Path.Combine(dir, "ArgsApp.obf.exe");
            var result = await CreateService().ObfuscateAsync(input, output, PackingSettings());
            result.Success.ShouldBeTrue(result.ErrorMessage);
            RunLauncher(ManagedLauncherPacker.LauncherPathFor(output), "ping").ShouldBe(7);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Packing_WaitsForAsyncTaskMain()
    {
        const string source = """
            public static class Program
            {
                public static async System.Threading.Tasks.Task<int> Main()
                {
                    await System.Threading.Tasks.Task.Yield();
                    return 9;
                }
            }
            """;
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-pack-async-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToExe(source, dir, "AsyncApp");
            var output = Path.Combine(dir, "AsyncApp.obf.exe");
            var result = await CreateService().ObfuscateAsync(input, output, PackingSettings());
            result.Success.ShouldBeTrue(result.ErrorMessage);
            RunLauncher(ManagedLauncherPacker.LauncherPathFor(output)).ShouldBe(9);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Packing_ResolvesSiblingAssemblies()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-pack-sib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var lib = CompileToAssembly("public static class Lib { public static int Get() => 13; }", dir, "Lib");
            var input = CompileToExeWithRef("public static class Program { public static int Main() => Lib.Get(); }", dir, "SibApp", lib);
            var output = Path.Combine(dir, "SibApp.obf.exe");

            var result = await CreateService().ObfuscateAsync(input, output, PackingSettings());
            result.Success.ShouldBeTrue(result.ErrorMessage);
            var cwd = Path.Combine(dir, "cwd");
            Directory.CreateDirectory(cwd);
            RunLauncher(ManagedLauncherPacker.LauncherPathFor(output), workingDirectory: cwd).ShouldBe(13);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Packing_WithAntiTamper_Runs()
    {
        const string source = "public static class Program { public static int Main() => 11; }";
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-pack-tamper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToExe(source, dir, "TamperApp");
            var output = Path.Combine(dir, "TamperApp.obf.exe");
            var settings = PackingSettings();
            settings.Protection.AntiTamper.Enabled = true;

            var result = await CreateService().ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            RunLauncher(ManagedLauncherPacker.LauncherPathFor(output)).ShouldBe(11);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Packing_SourceInput_SkipsWithWarning()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-pack-src-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "App.cs");
            await File.WriteAllTextAsync(input, "class C { static void Main() {} }");
            var output = Path.Combine(dir, "App.obf.cs");
            var result = await CreateService().ObfuscateAsync(input, output, PackingSettings());
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Warnings.ShouldContain(w => w.Contains("Packing skipped"));
            result.PackedLauncherPath.ShouldBeNull();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void AssemblyPreview_DecompilesCompiledAssembly()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-prev-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dll = CompileToAssembly("public static class Lib { public static int Get() => 4; }", dir, "PrevLib");
            var text = AssemblyPreview.Decompile(dll);
            text.ShouldContain("class Lib");
            text.ShouldContain("Get");
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void AssemblyPreview_TruncatesLongOutput()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-prev-trunc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dll = CompileToAssembly("public static class Lib { public static int Get() => 4; }", dir, "PrevLib");
            var text = AssemblyPreview.Decompile(dll, maxChars: 32);
            text.Length.ShouldBe(32 + "\n/* truncated */".Length);
            text.ShouldEndWith("/* truncated */");
        }
        finally
        {
            TryDeleteDir(dir);
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
    public async Task ReferenceProxy_ConstrainedForeachAndValueTypeToString_RunOnRealAssembly()
    {
        const string source = """
            public static class Lib
            {
                public static int Get()
                {
                    var list = new System.Collections.Generic.List<int>();
                    list.Add(1);
                    list.Add(2);
                    var n = 0;
                    foreach (var x in list) n += x;
                    return n + 42.ToString().Length;
                }
            }
            """;
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-cns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "ConstrainedProxyLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
            var settings = new ObfySettings
            {
                Protection = { ReferenceProxy = true, ProxyExternalCalls = true }
            };
            var context = PipelineContext.ForAssembly(module, settings);
            (await new ReferenceProxyObfuscator(new Mock<ILogger<ReferenceProxyObfuscator>>().Object)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "ConstrainedProxyLib.obf.dll");
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
            result.Statistics.AssembliesEmbedded.ShouldBeGreaterThan(0);

            File.Delete(dep);
            LoadAndInvoke(output, "Lib", "Get").ShouldBe(5);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task DependencyEmbedding_WithResourceEncryption_LoadsMissingSiblingAssembly()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-embenc-{Guid.NewGuid():N}");
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
            var lib = CompileToAssemblyWithRef(libSource, dir, "EmbEncLib", dep);

            var output = Path.Combine(dir, "EmbEncLib.obf.dll");
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
                ResourceEncryption = { Enabled = true },
                DependencyEmbedding = { Enabled = true }
            };

            var result = await service.ObfuscateAsync(lib, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Statistics.AssembliesEmbedded.ShouldBeGreaterThan(0);

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
    public async Task AntiTamper_TamperedPe_ChildProcessExitsNonZero()
    {
        const string source = "public static class Lib { public static int Get() => 7; }";
        const string runnerSource = """
            using System.Reflection;
            using System.Runtime.Loader;
            public static class Program
            {
                public static int Main(string[] args)
                {
                    var alc = new AssemblyLoadContext("tamper");
                    var asm = alc.LoadFromAssemblyPath(args[0]);
                    var lib = asm.GetTypes()[0];
                    foreach (var t in asm.GetTypes())
                        if (t.Name == "Lib") lib = t;
                    return (int)lib.GetMethod("Get")!.Invoke(null, null)!;
                }
            }
            """;
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-tamper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "TamperLib");
            var runner = CompileToExe(runnerSource, dir, "TamperRunner");
            WriteRuntimeConfig(runner);

            var module = ModuleDefMD.Load(File.ReadAllBytes(input));
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                Protection = { AntiTamper = { Enabled = true, CheckModuleInitializer = true } }
            };
            var context = PipelineContext.ForAssembly(module, settings);
            var injected = await new AntiTamperObfuscator(new Mock<ILogger<AntiTamperObfuscator>>().Object)
                .ObfuscateAsync(context);
            injected.Success.ShouldBeTrue();
            injected.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0);

            var output = Path.Combine(dir, "TamperLib.obf.dll");
            await new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object)
                .SaveAsync(context, output);

            RunLauncher(runner, $"\"{output}\"").ShouldBe(7);

            var bytes = File.ReadAllBytes(output);
            var hashOffset = AssemblyHashComputer.FindHashOffset(bytes);
            hashOffset.ShouldBeGreaterThan(0);
            bytes[hashOffset] ^= 0xFF;
            var storedAfter = bytes.AsSpan(hashOffset, AssemblyHashComputer.HashSize).ToArray();
            AssemblyHashComputer.ComputeIntegrityHash(bytes, hashOffset).ShouldNotBe(storedAfter);

            var tampered = Path.Combine(dir, "TamperLib.tampered.dll");
            File.WriteAllBytes(tampered, bytes);

            var (exit, stderr) = RunLauncherCapture(runner, $"\"{tampered}\"");
            exit.ShouldNotBe(0, "FailFast must not return success");
            exit.ShouldNotBe(7, "anti-tamper Verify should FailFast after the stored hash is flipped");
            stderr.ShouldContain("Obfy anti-tamper: assembly integrity check failed");
        }
        finally
        {
            TryDeleteDir(dir);
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
            settings.DependencyEmbedding.Enabled = true;

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Warnings.ShouldContain(w => w.Contains("Method encryption disabled", StringComparison.OrdinalIgnoreCase));
            result.Warnings.ShouldContain(w => w.Contains("Anti-dump disabled", StringComparison.OrdinalIgnoreCase));
            result.Warnings.ShouldContain(w => w.Contains("Dependency embedding disabled", StringComparison.OrdinalIgnoreCase));

            using var loaded = ModuleDefMD.Load(File.ReadAllBytes(output));
            loaded.Types.ShouldNotContain(t => t.Name == "<MethodCrypt>");
            loaded.Types.ShouldNotContain(t => t.Name == "<AntiDump>");
            loaded.Types.ShouldNotContain(t => t.Name == "<Embed>");
            result.Warnings.ShouldContain(w => w.Contains("kernel32", StringComparison.OrdinalIgnoreCase));
            var antiDebug = loaded.Types.FirstOrDefault(t => t.Methods.Any(m =>
                m.HasBody && m.Body.Instructions.Any(i =>
                    i.Operand is IMethod im && im.Name == "get_IsAttached")));
            antiDebug.ShouldNotBeNull();
            antiDebug!.Methods.ShouldNotContain(m => m.IsPinvokeImpl);

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

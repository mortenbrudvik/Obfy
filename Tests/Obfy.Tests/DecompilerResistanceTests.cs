using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Autofac;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
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
using Obfy.Core.Services.Reporting;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// QT-06: decompiler-resistance fixtures. These lock protection quality:
/// multiple decrypt entry points and non-straight-line helper IL, anti-debug
/// survives NOP-ing one user call site, reports show method-encryption skip counts,
/// and a native-packed EXE is rejected by dnlib as a managed module.
/// </summary>
public class DecompilerResistanceTests
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

    [Fact]
    public async Task IlSpy_DoesNotShowTrivialStringDecryptor()
    {
        const string secret = "IntegrationSecretString";
        const string source = """
            public static class Lib
            {
                public static string Get() => "IntegrationSecretString";
                public static string Get2() => "AnotherSecretValue!!";
                public static string Get3() => "ThirdSecretLiteral!!!";
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-qt06-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "DecoyLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = true, MinStringLength = 3 },
                ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 },
                Protection = { ReferenceProxy = true },
                SymbolRenaming = { Enabled = false }
            };
            var context = PipelineContext.ForAssembly(module, settings);

            (await new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();
            (await new ControlFlowObfuscator(new Mock<ILogger<ControlFlowObfuscator>>().Object)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();
            (await new ReferenceProxyObfuscator(new Mock<ILogger<ReferenceProxyObfuscator>>().Object)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "DecoyLib.obf.dll");
            module.Write(output);

            LoadAndInvoke(output, "Lib", "Get").ShouldBe(secret);
            LoadAndInvoke(output, "Lib", "Get2").ShouldBe("AnotherSecretValue!!");
            LoadAndInvoke(output, "Lib", "Get3").ShouldBe("ThirdSecretLiteral!!!");

            var csharp = new CSharpDecompiler(output, new DecompilerSettings()).DecompileWholeModuleAsString();
            csharp.ShouldNotContain(secret);
            csharp.ShouldContain("Decrypt2");
            (csharp.Contains("goto ", StringComparison.Ordinal) ||
             csharp.Contains("switch (", StringComparison.Ordinal) ||
             csharp.Contains("TickCount", StringComparison.Ordinal))
                .ShouldBeTrue("decryptor/helpers should not decompile as straight-line C#");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task AntiDebug_SurvivesNopOfOneCallSite()
    {
        const string source = """
            public static class Lib
            {
                public static int Get() => Helper() + 1;
                public static int Helper() => 6;
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-qt06-ad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "AdNopLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                Protection = { AntiDebug = true }
            };
            var context = PipelineContext.ForAssembly(module, settings);
            (await new AntiDebugObfuscator(new Mock<ILogger<AntiDebugObfuscator>>().Object)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();

            var check = module.Types.First(t => t.Name == "<AntiDebug>").FindMethod("Check")!;
            var callSites = module.GetTypes()
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody && !m.IsStaticConstructor && m.DeclaringType.Name != "<AntiDebug>")
                .SelectMany(m => m.Body.Instructions.Select(i => (Method: m, Instr: i)))
                .Where(x => x.Instr.OpCode == OpCodes.Call && x.Instr.Operand is IMethod called &&
                            (called == check || called.Name == "Check" && called.DeclaringType?.Name == "<AntiDebug>"))
                .ToList();
            callSites.Count.ShouldBeGreaterThan(1);

            var victim = callSites[0];
            victim.Instr.OpCode = OpCodes.Nop;
            victim.Instr.Operand = null;

            var remainingUser = module.GetTypes()
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody && !m.IsStaticConstructor && m.DeclaringType.Name != "<AntiDebug>")
                .SelectMany(m => m.Body.Instructions)
                .Count(i => i.OpCode == OpCodes.Call && i.Operand is IMethod called &&
                            (called == check || called.Name == "Check" && called.DeclaringType?.Name == "<AntiDebug>"));
            remainingUser.ShouldBe(callSites.Count - 1);
            remainingUser.ShouldBeGreaterThan(0);

            var cctor = module.GlobalType.FindStaticConstructor();
            cctor.ShouldNotBeNull();
            cctor!.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Call && i.Operand is IMethod called &&
                (called == check || called.Name == "Check" && called.DeclaringType?.Name == "<AntiDebug>"))
                .ShouldBeTrue("module initializer must still call Check");

            var output = Path.Combine(dir, "AdNopLib.obf.dll");
            module.Write(output);
            LoadAndInvoke(output, "Lib", "Get").ShouldBe(7);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Report_ShowsMethodEncryptionGenericSkipCounts()
    {
        var module = new ModuleDefUser("SkipLib", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var assembly = new AssemblyDefUser("SkipLib", new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);

        var generic = new TypeDefUser("NS", "Box`1", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        generic.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T"));
        module.Types.Add(generic);
        for (var i = 0; i < 4; i++)
        {
            var method = new MethodDefUser(
                "G" + i,
                MethodSig.CreateStatic(module.CorLibTypes.Int32),
                MethodAttributes.Public | MethodAttributes.Static);
            var body = new CilBody();
            for (var n = 0; n < 8; n++)
                body.Instructions.Add(Instruction.Create(OpCodes.Nop));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            method.Body = body;
            generic.Methods.Add(method);
        }

        var concrete = new TypeDefUser("NS", "Work", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(concrete);
        var go = new MethodDefUser(
            "Go",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            MethodAttributes.Public | MethodAttributes.Static);
        var goBody = new CilBody();
        for (var n = 0; n < 8; n++)
            goBody.Instructions.Add(Instruction.Create(OpCodes.Nop));
        goBody.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        goBody.Instructions.Add(Instruction.Create(OpCodes.Ret));
        go.Body = goBody;
        concrete.Methods.Add(go);

        var settings = new ObfySettings { Protection = { MethodEncryption = true } };
        var context = PipelineContext.ForAssembly(module, settings);
        var encryptResult = await new MethodEncryptionObfuscator(new Mock<ILogger<MethodEncryptionObfuscator>>().Object)
            .ObfuscateAsync(context);
        encryptResult.Success.ShouldBeTrue();
        context.SkippedItems.Count(s => s.Reason == SkipReason.GenericMethod).ShouldBe(4);

        var result = ObfuscationResult.Successful(
            encryptResult.Statistics,
            skippedItems: context.SkippedItems.ToList(),
            warnings: context.Warnings.ToList());

        var report = new ReportService(
            new Mock<ILogger<ReportService>>().Object,
            new HtmlReportGenerator(new Mock<ILogger<HtmlReportGenerator>>().Object),
            new JsonReportGenerator(new Mock<ILogger<JsonReportGenerator>>().Object))
            .BuildReport(result, settings);

        report.Warnings.ShouldContain(w =>
            w.Category == WarningCategory.SkippedItem &&
            w.Message.Contains("4 items skipped") &&
            w.Message.Contains("generic method (IL encryption)"));
    }

    [Fact]
    public async Task NativePackedExe_CannotBeLoadedAsManagedModule()
    {
        const string source = "public static class Program { public static int Main() => 11; }";
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-qt06-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToExe(source, dir, "NativePackApp");
            var output = Path.Combine(dir, "NativePackApp.obf.exe");
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                SymbolRenaming = { Enabled = false, PreservePublicApi = true },
                Packing = { Enabled = true }
            };

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();
            var service = container.Resolve<IObfuscationService>();

            var result = await service.ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            Should.Throw<BadImageFormatException>(() => ModuleDefMD.Load(output));
            using var pe = new PEReader(new MemoryStream(File.ReadAllBytes(output)));
            pe.PEHeaders.CorHeader.ShouldBeNull();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}

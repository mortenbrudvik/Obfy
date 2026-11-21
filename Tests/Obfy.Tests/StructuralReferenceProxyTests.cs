using System.Runtime.Loader;
using Autofac;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Reference proxies are static <c>calli</c> trampolines built with the default managed calling
/// convention, so they cannot stand in for a vararg callee, and the nested trampoline holder is
/// an injected helper like the module-level one.
/// </summary>
public class StructuralReferenceProxyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"obfy-refproxy-{Guid.NewGuid():N}");

    public StructuralReferenceProxyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task InModuleVarargCall_IsLeftAsDirectCall()
    {
        const string source = """
            using System;
            public static class Lib
            {
                public static int Count(__arglist)
                {
                    var it = new ArgIterator(__arglist);
                    return it.GetRemainingCount();
                }

                public static int Plain(int a, int b) => a + b;

                public static int Run() => Count(__arglist(1, 2, 3)) + Plain(1, 1) + new Inst().Count(__arglist(7, 8));
            }

            public class Inst
            {
                public int Count(__arglist)
                {
                    var it = new ArgIterator(__arglist);
                    return it.GetRemainingCount();
                }
            }
            """;
        var input = Compile("VarargLib", source);
        var output = Path.Combine(_dir, "VarargLib.obf.dll");

        using var container = BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var result = await service.ObfuscateAsync(input, output, ReferenceProxyOnlySettings());
        result.Success.ShouldBeTrue(result.ErrorMessage);

        using (var module = ModuleDefMD.Load(output))
        {
            var run = module.Types.Single(t => t.Name == "Lib").FindMethod("Run")!;
            var calls = run.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Call && i.Operand is IMethod)
                .Select(i => (IMethod)i.Operand!)
                .ToList();

            var seen = string.Join("; ", calls.Select(c => $"{c.DeclaringType.Name}::{c.Name}"));

            // The vararg callee keeps its direct call site (call-site signature with sentinel).
            calls.ShouldContain(
                c => IsVarArg(c) && c.DeclaringType.Name == "Lib",
                $"static vararg call must stay direct; Run calls: {seen}");
            calls.ShouldContain(
                c => IsVarArg(c) && c.DeclaringType.Name == "Inst",
                $"instance vararg call must stay direct; Run calls: {seen}");
            // The ordinary in-module call is still proxied, so the pass ran.
            calls.ShouldContain(c => c.DeclaringType.Name == "<RefProxy>", $"Run calls: {seen}");
        }

        if (OperatingSystem.IsWindows())
            InvokeRun(output).ShouldBe(7);
    }

    [Fact]
    public async Task NestedProxyType_IsRegisteredAsInjectedHelper()
    {
        var module = new ModuleDefUser("TestAssembly", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        new AssemblyDefUser("TestAssembly", new Version(1, 0, 0, 0)).Modules.Add(module);
        var type = new TypeDefUser("TestNamespace", "Calc", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NotPublic | TypeAttributes.Class
        };
        module.Types.Add(type);

        var target = new MethodDefUser(
            "Secret",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static)
        {
            Body = new CilBody()
        };
        target.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(target);

        var caller = new MethodDefUser(
            "Run",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static)
        {
            Body = new CilBody()
        };
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Call, target));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(caller);

        var obfuscator = new ReferenceProxyObfuscator(NullLogger<ReferenceProxyObfuscator>.Instance);
        var context = PipelineContext.ForAssembly(module, new ObfySettings { Protection = { ReferenceProxy = true } });

        (await obfuscator.ObfuscateAsync(context)).Success.ShouldBeTrue();

        var nested = type.NestedTypes.Single(t => t.Name == "<RefProxy>");
        nested.Methods.ShouldNotBeEmpty();
        context.InjectedHelpers.Keys.ShouldContain(nested);
    }

    private static bool IsVarArg(IMethod method) =>
        (method.MethodSig.CallingConvention & CallingConvention.Mask) == CallingConvention.VarArg;

    private static ObfySettings ReferenceProxyOnlySettings() => new()
    {
        Level = ObfuscationLevel.Custom,
        StringEncryption = { Enabled = false },
        ControlFlow = { Enabled = false },
        ConstantEncryption = { Enabled = false },
        ResourceEncryption = { Enabled = false },
        SymbolRenaming = { Enabled = false },
        Metadata = { RemoveDebugInfo = false, RemoveAttributes = false, StripDocumentation = false },
        Protection =
        {
            AntiDebug = false,
            AntiTamper = { Enabled = false },
            AntiDecompiler = { Enabled = false },
            AntiDump = false,
            ReferenceProxy = true,
            MethodEncryption = false
        }
    };

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build();
    }

    private string Compile(string assemblyName, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(_dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static int InvokeRun(string assemblyPath)
    {
        var alc = new AssemblyLoadContext($"rp-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(assemblyPath);
            var run = asm.GetType("Lib")!.GetMethod("Run", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            return (int)run.Invoke(null, null)!;
        }
        finally
        {
            alc.Unload();
        }
    }
}

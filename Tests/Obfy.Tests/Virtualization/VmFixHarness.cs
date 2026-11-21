using System.Reflection;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Virtualization;
using Shouldly;

namespace Obfy.Tests.Virtualization;

/// <summary>
/// Shared plumbing for the VM regression tests: compile C# (or take a dnlib module), encode the
/// named methods, import the VM, write the stubs and invoke through a collectible load context.
/// </summary>
internal static class VmFixHarness
{
    /// <summary>Compiles <paramref name="source"/> and loads it as a dnlib module.</summary>
    public static ModuleDefMD Compile(string source, OptimizationLevel optimization)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            "VmFixLib",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: optimization));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return ModuleDefMD.Load(stream.ToArray());
    }

    /// <summary>
    /// Compiles <paramref name="mainSource"/> against a library built from <paramref name="dependencySource"/>,
    /// then points the reference at an assembly that does not exist: an optional dependency that is
    /// absent at run time. Every member of the dependency is unresolvable when the module runs.
    /// </summary>
    public static ModuleDefMD CompileWithMissingDependency(string dependencySource, string mainSource)
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release);

        using var depStream = new MemoryStream();
        var depEmit = CSharpCompilation.Create("VmFixDep", new[] { CSharpSyntaxTree.ParseText(dependencySource) }, platform, options)
            .Emit(depStream);
        depEmit.Success.ShouldBeTrue(string.Join(Environment.NewLine, depEmit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var references = platform.Concat(new[] { MetadataReference.CreateFromImage(depStream.ToArray()) });
        using var mainStream = new MemoryStream();
        var mainEmit = CSharpCompilation.Create("VmFixMain", new[] { CSharpSyntaxTree.ParseText(mainSource) }, references, options)
            .Emit(mainStream);
        mainEmit.Success.ShouldBeTrue(string.Join(Environment.NewLine, mainEmit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var module = ModuleDefMD.Load(mainStream.ToArray());
        var reference = module.GetAssemblyRefs().Single(r => r.Name == "VmFixDep");
        reference.Name = "VmFixDepIsNotInstalled";
        return module;
    }

    public static MethodDef Find(ModuleDef module, string typeName, string methodName)
    {
        var type = module.Types.FirstOrDefault(t => t.Name == typeName);
        type.ShouldNotBeNull($"type '{typeName}' was not found");
        var method = type!.FindMethod(methodName);
        method.ShouldNotBeNull($"method '{typeName}.{methodName}' was not found");
        return method!;
    }

    public static bool ContainsCode(MethodDef method, params Code[] codes) =>
        method.Body.Instructions.Any(i => codes.Contains(i.OpCode.Code));

    /// <summary>
    /// Virtualizes exactly the named static methods of <paramref name="typeName"/> (each must
    /// encode) and runs <paramref name="invoke"/> against the written assembly.
    /// </summary>
    public static object? VirtualizeAndRun(
        ModuleDef module,
        string typeName,
        string[] methodNames,
        Func<Assembly, object?> invoke)
    {
        var methods = methodNames.Select(n => Find(module, typeName, n)).ToList();
        var ids = new Dictionary<MethodDef, int>();
        for (var i = 0; i < methods.Count; i++)
            ids[methods[i]] = i;

        var tables = new VmMemberTables();
        var blobs = new byte[methods.Count][];
        var starts = new int[methods.Count];
        var offset = 0;
        for (var i = 0; i < methods.Count; i++)
        {
            VmEncoder.TryEncode(methods[i], ids, tables, out blobs[i], out var reason)
                .ShouldBeTrue($"expected '{methods[i].Name}' to encode, skipReason={reason}");
            starts[i] = offset;
            offset += blobs[i].Length;
        }

        var code = new byte[offset];
        offset = 0;
        foreach (var blob in blobs)
        {
            Buffer.BlockCopy(blob, 0, code, offset, blob.Length);
            offset += blob.Length;
        }

        var identity = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var returnTypes = methods.Select(m => Strip(m.MethodSig.RetType).ToTypeDefOrRef()).ToList();
        var argumentCounts = methods.Select(m => m.Parameters.Count).ToArray();
        var argumentTypes = methods
            .SelectMany(m => m.Parameters)
            .Select(p => Strip(p.Type).ToTypeDefOrRef())
            .ToList();
        var context = PipelineContext.ForAssembly(module, new ObfySettings());
        var vmType = VmImporter.Import(
            context, code, starts, identity, new byte[8],
            tables.Methods, tables.Fields, tables.Types, returnTypes, argumentTypes, argumentCounts);
        var run = vmType.FindMethod("Run");
        run.ShouldNotBeNull();
        for (var i = 0; i < methods.Count; i++)
            VmImporter.WriteStub(methods[i], run!, i);

        return VmExecuteHarness.Invoke(module, invoke);
    }

    /// <summary>Compiles, virtualizes one static method and runs it once per argument set.</summary>
    public static object?[] RunMany(
        string source,
        OptimizationLevel optimization,
        string typeName,
        string methodName,
        params object?[][] argSets)
    {
        using var module = Compile(source, optimization);
        var result = VirtualizeAndRun(module, typeName, new[] { methodName }, assembly =>
        {
            var method = VmExecuteHarness.GetType(assembly, typeName).GetMethod(methodName)
                ?? throw new InvalidOperationException($"Method '{typeName}.{methodName}' was not found.");
            var results = new object?[argSets.Length];
            for (var i = 0; i < argSets.Length; i++)
                results[i] = method.Invoke(null, argSets[i]);
            return results;
        });
        return (object?[])result!;
    }

    /// <summary>
    /// Runs the same static method un-virtualized, as the CLR oracle for what the VM must return.
    /// </summary>
    public static object?[] RunNative(
        string source,
        OptimizationLevel optimization,
        string typeName,
        string methodName,
        params object?[][] argSets)
    {
        using var module = Compile(source, optimization);
        return (object?[])VmExecuteHarness.Invoke(module, assembly =>
        {
            var method = VmExecuteHarness.GetType(assembly, typeName).GetMethod(methodName)
                ?? throw new InvalidOperationException($"Method '{typeName}.{methodName}' was not found.");
            var results = new object?[argSets.Length];
            for (var i = 0; i < argSets.Length; i++)
                results[i] = method.Invoke(null, argSets[i]);
            return results;
        })!;
    }

    /// <summary>Compiles, virtualizes one static method and runs it once.</summary>
    public static object? RunOnce(
        string source,
        OptimizationLevel optimization,
        string typeName,
        string methodName,
        params object?[] args) =>
        RunMany(source, optimization, typeName, methodName, args)[0];

    public static TypeSig Strip(TypeSig sig) => sig.RemovePinnedAndModifiers() ?? sig;
}

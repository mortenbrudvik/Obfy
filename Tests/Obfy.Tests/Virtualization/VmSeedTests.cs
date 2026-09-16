using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Virtualization;
using Shouldly;

namespace Obfy.Tests.Virtualization;

public class VmSeedTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obfy-vm-seed-" + Guid.NewGuid().ToString("N"));

    public VmSeedTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void CreateOpMap_IsPermutationOfVmOps()
    {
        var map = VmSeed.CreateOpMap(new byte[32]);
        map.Length.ShouldBe(256);
        var used = map.Where(b => b != 0xFF).OrderBy(b => b).ToArray();
        used.ShouldBe(Enumerable.Range(1, 77).Select(i => (byte)i).ToArray());
    }

    [Fact]
    public void Compute_SameInputAndSettings_SameSeed()
    {
        var path = Path.Combine(_dir, "same.dll");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var settings = new ObfySettings { Level = ObfuscationLevel.Custom };
        var a = PipelineContext.ForAssembly(new ModuleDefUser("t"), settings);
        a.InputPath = path;
        var b = PipelineContext.ForAssembly(new ModuleDefUser("t"), settings);
        b.InputPath = path;

        VmSeed.Compute(a).SequenceEqual(VmSeed.Compute(b)).ShouldBeTrue();
    }

    [Fact]
    public void Compute_DifferentInput_DifferentOpMap()
    {
        var pathA = Path.Combine(_dir, "a.dll");
        var pathB = Path.Combine(_dir, "b.dll");
        File.WriteAllBytes(pathA, [1]);
        File.WriteAllBytes(pathB, [2]);
        var settings = new ObfySettings { Level = ObfuscationLevel.Custom };
        var ctxA = PipelineContext.ForAssembly(new ModuleDefUser("a"), settings);
        ctxA.InputPath = pathA;
        var ctxB = PipelineContext.ForAssembly(new ModuleDefUser("b"), settings);
        ctxB.InputPath = pathB;

        var mapA = VmSeed.CreateOpMap(VmSeed.Compute(ctxA));
        var mapB = VmSeed.CreateOpMap(VmSeed.Compute(ctxB));
        mapA.SequenceEqual(mapB).ShouldBeFalse();
    }

    [Fact]
    public void Apply_ThenRun_StillAdds()
    {
        EncodeApplyImportInvoke(
            "public static class Lib { public static int Add(int a, int b) => a + b; }",
            "Lib", "Add", 2, 3).ShouldBe(5);
    }

    [Fact]
    public void Apply_ThenRun_StillReturnsHi()
    {
        EncodeApplyImportInvoke(
            "public static class Lib { public static string Hi() => \"hi\"; }",
            "Lib", "Hi").ShouldBe("hi");
    }

    [Fact]
    public void Apply_ThenRun_StillBranches()
    {
        EncodeApplyImportInvoke(
            "public static class Lib { public static int Pick(int a, int b) => a < b ? 1 : 0; }",
            "Lib", "Pick", 1, 2).ShouldBe(1);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    private static object? EncodeApplyImportInvoke(
        string source, string typeName, string methodName, params object[] args)
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-vm-seed-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir);
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
            var type = module.Types.FirstOrDefault(t => t.Name == typeName);
            type.ShouldNotBeNull($"type '{typeName}' was not found");
            var method = type!.FindMethod(methodName);
            method.ShouldNotBeNull($"method '{typeName}.{methodName}' was not found");
            var tables = new VmMemberTables();
            VmEncoder.TryEncode(method!, new Dictionary<MethodDef, int>(), tables, out var code, out var skipReason)
                .ShouldBeTrue($"expected encode to succeed, skipReason={skipReason}");
            skipReason.ShouldBeNull();

            var seed = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
            var opMap = VmSeed.CreateOpMap(seed);
            var xorKey = VmSeed.CreateXorKey(seed);
            opMap.SequenceEqual(Enumerable.Range(0, 256).Select(i => (byte)i)).ShouldBeFalse();
            xorKey.SequenceEqual(new byte[8]).ShouldBeFalse();
            var original = code.ToArray();
            VmSeed.Apply(code, opMap, xorKey);
            code.SequenceEqual(original).ShouldBeFalse();

            var context = PipelineContext.ForAssembly(module, new ObfySettings());
            var vmType = VmImporter.Import(
                context,
                code,
                starts: new[] { 0 },
                opMap: opMap,
                xorKey: xorKey,
                methods: tables.Methods,
                fields: tables.Fields,
                types: tables.Types,
                returnTypes: new[] { method!.MethodSig.RetType.ToTypeDefOrRef() });
            var run = vmType.FindMethod("Run");
            run.ShouldNotBeNull();
            VmImporter.WriteStub(method, run!, id: 0);
            return VmExecuteHarness.Invoke(module, typeName, methodName, args);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static string CompileToAssembly(string source, string dir)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            "VmSeedLib",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));

        var path = Path.Combine(dir, "VmSeedLib.dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }
}

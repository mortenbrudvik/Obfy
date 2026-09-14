using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Virtualization;
using Shouldly;

namespace Obfy.Tests.Virtualization;

public class VmRuntimeExecuteTests
{
    [Fact]
    public void Run_Add_ReturnsSum()
    {
        var result = EncodeImportInvoke(
            "public static class Lib { public static int Add(int a, int b) => a + b; }",
            "Lib", "Add", 2, 3);
        result.ShouldBe(5);
    }

    [Fact]
    public void Run_Ldstr_ReturnsString()
    {
        EncodeImportInvoke(
            "public static class Lib { public static string Hi() => \"hi\"; }",
            "Lib", "Hi").ShouldBe("hi");
    }

    [Fact]
    public void Run_SignedAndUnsignedBranches()
    {
        EncodeImportInvoke("public static class Lib { public static int Pick(int a, int b) => a < b ? 1 : 0; }",
            "Lib", "Pick", 1, 2).ShouldBe(1);
    }

    [Fact]
    public void Run_UnsignedCompare()
    {
        EncodeImportInvoke(
            "public static class Lib { public static int UnGt(uint a, uint b) => a > b ? 1 : 0; }",
            "Lib", "UnGt", 0u, uint.MaxValue).ShouldBe(0);
    }

    [Fact]
    public void Run_BoolReturn_DoesNotThrow()
    {
        EncodeImportInvoke("public static class Lib { public static bool Gt(int a, int b) => a > b; }",
            "Lib", "Gt", 3, 1).ShouldBe(true);
    }

    [Fact]
    public void Run_UintReturn_DoesNotThrow()
    {
        EncodeImportInvoke("public static class Lib { public static uint Id(uint x) => x; }",
            "Lib", "Id", 7u).ShouldBe(7u);
    }

    [Fact]
    public void Run_CharReturn_DoesNotThrow()
    {
        EncodeImportInvoke("public static class Lib { public static char Id(char c) => c; }",
            "Lib", "Id", 'A').ShouldBe('A');
    }

    [Fact]
    public void Run_UlongReturn_DoesNotThrow()
    {
        EncodeImportInvoke("public static class Lib { public static ulong Id(ulong x) => x; }",
            "Lib", "Id", 9UL).ShouldBe(9UL);
    }

    [Fact]
    public void Run_LongAdd_ReturnsSum()
    {
        EncodeImportInvoke(
            "public static class Lib { public static long Add(long a, long b) => a + b; }",
            "Lib", "Add", 10L, 20L).ShouldBe(30L);
    }

    [Fact]
    public void Run_DoubleMul_ReturnsProduct()
    {
        EncodeImportInvoke(
            "public static class Lib { public static double Mul(double a, double b) => a * b; }",
            "Lib", "Mul", 1.5d, 4d).ShouldBe(6d);
    }

    [Fact]
    public void Run_LdlocStloc_Temp()
    {
        EncodeImportInvoke(
            "public static class Lib { public static int Scale(int a, int b) { int x = a + 1; return x * b; } }",
            "Lib", "Scale", 2, 3).ShouldBe(9);
    }

    [Fact]
    public void Run_BrtrueBrfalse_IfZero()
    {
        EncodeImportInvoke(
            "public static class Lib { public static int IfZero(int a) { if (a == 0) return 1; return 0; } }",
            "Lib", "IfZero", new object[] { 0 }, OptimizationLevel.Release).ShouldBe(1);
        EncodeImportInvoke(
            "public static class Lib { public static int IfZero(int a) { if (a == 0) return 1; return 0; } }",
            "Lib", "IfZero", new object[] { 4 }, OptimizationLevel.Release).ShouldBe(0);
    }

    private static object? EncodeImportInvoke(
        string source,
        string typeName,
        string methodName,
        params object[] args) =>
        EncodeImportInvoke(source, typeName, methodName, args, OptimizationLevel.Debug);

    private static object? EncodeImportInvoke(
        string source,
        string typeName,
        string methodName,
        object[] args,
        OptimizationLevel optimization)
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-vm-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "VmExecLib", optimization);
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
            var method = FindMethod(module, typeName, methodName);
            var tables = new VmMemberTables();
            VmEncoder.TryEncode(method, new Dictionary<MethodDef, int>(), tables, out var code, out var skipReason)
                .ShouldBeTrue($"expected encode to succeed, skipReason={skipReason}");
            skipReason.ShouldBeNull();

            var identity = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            var retType = method.MethodSig.RetType.ToTypeDefOrRef();
            var context = PipelineContext.ForAssembly(module, new ObfySettings());
            var vmType = VmImporter.Import(
                context,
                code,
                starts: new[] { 0 },
                opMap: identity,
                xorKey: new byte[8],
                methods: tables.Methods,
                fields: tables.Fields,
                types: tables.Types,
                returnTypes: new[] { retType });
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

    private static MethodDef FindMethod(ModuleDef module, string typeName, string methodName)
    {
        var type = module.Types.FirstOrDefault(t => t.Name == typeName);
        type.ShouldNotBeNull($"type '{typeName}' was not found");
        var method = type!.FindMethod(methodName);
        method.ShouldNotBeNull($"method '{typeName}.{methodName}' was not found");
        return method!;
    }

    private static string CompileToAssembly(
        string source, string dir, string assemblyName, OptimizationLevel optimization)
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
}

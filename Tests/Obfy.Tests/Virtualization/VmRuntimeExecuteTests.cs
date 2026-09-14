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

    [Fact]
    public void Run_NewobjAndInstanceField()
    {
        const string src = """
            public class Box {
                public int N;
                public static int Go(int n) { var b = new Box(); b.N = n; return b.N; }
            }
            """;
        EncodeImportInvoke(src, "Box", "Go", 9).ShouldBe(9);
    }

    [Fact]
    public void Run_Callvirt_UsesVirtualDispatch()
    {
        const string src = """
            public class A { public virtual int V() => 1; }
            public class B : A { public override int V() => 2; }
            public static class Lib { public static int Hit(A a) => a.V(); }
            """;
        EncodeImportInvoke(src, "Lib", "Hit", assembly =>
        {
            var instance = VmExecuteHarness.CreateInstance(assembly, "B");
            var hit = VmExecuteHarness.GetType(assembly, "Lib").GetMethod("Hit")
                ?? throw new InvalidOperationException("Method 'Lib.Hit' was not found.");
            return hit.Invoke(null, new[] { instance });
        }).ShouldBe(2);
    }

    [Fact]
    public void Run_StaticField_GetSet()
    {
        EncodeImportInvoke(
            "public static class Lib { public static int N; public static int Go(int n) { N = n; return N; } }",
            "Lib", "Go", 11).ShouldBe(11);
    }

    [Fact]
    public void Run_BoxEnum_ReturnsColorRed()
    {
        const string src = """
            public enum Color { Red = 1 }
            public static class Lib { public static object BoxEnum() => Color.Red; }
            """;
        AssertEnum(EncodeImportInvoke(src, "Lib", "BoxEnum"), "Color", "Red", 1);
    }

    [Fact]
    public void Run_InstanceAndStaticEnumFields_RoundTrip()
    {
        const string src = """
            public enum Status { Open = 2 }
            public class Holder {
                public Status Inst;
                public static Status Stat;
                public static object InstanceGo() { var h = new Holder(); h.Inst = Status.Open; return h.Inst; }
                public static object StaticGo() { Stat = Status.Open; return Stat; }
            }
            """;
        AssertEnum(EncodeImportInvoke(src, "Holder", "InstanceGo"), "Status", "Open", 2);
        AssertEnum(EncodeImportInvoke(src, "Holder", "StaticGo"), "Status", "Open", 2);
    }

    [Fact]
    public void Run_CallEnum_PassesToNonVirtualizedCallee()
    {
        const string src = """
            public enum Color { Red = 1 }
            public static class Lib {
                public static int Take(Color c) => (int)c;
                public static int CallEnum() => Take(Color.Red);
            }
            """;
        EncodeImportInvoke(src, "Lib", "CallEnum").ShouldBe(1);
    }

    [Fact]
    public void Run_Call_NonVirtualizedHelper()
    {
        EncodeImportInvoke(
            "public static class Lib { public static string Join(string a, string b) => string.Concat(a, b); }",
            "Lib", "Join", "ab", "cd").ShouldBe("abcd");
    }

    [Fact]
    public void Run_InstanceThis_GetN()
    {
        const string src = """
            public class Box {
                public int N;
                public int GetN() => N;
            }
            """;
        EncodeImportInvoke(src, "Box", "GetN", assembly =>
        {
            var boxType = VmExecuteHarness.GetType(assembly, "Box");
            var instance = VmExecuteHarness.CreateInstance(assembly, "Box");
            boxType.GetField("N")!.SetValue(instance, 4);
            var getN = boxType.GetMethod("GetN")
                ?? throw new InvalidOperationException("Method 'Box.GetN' was not found.");
            return getN.Invoke(instance, null);
        }).ShouldBe(4);
    }

    [Fact]
    public void Run_CallVm_StaticACallsStaticB()
    {
        const string src = """
            public static class Lib {
                public static int Inner(int x) => x + 1;
                public static int Outer(int x) => Inner(x) * 2;
            }
            """;
        EncodeImportInvokeBoth(src, "Lib", "Outer", poisonCalleeStub: false, 3).ShouldBe(8);
    }

    [Fact]
    public void Run_CallVm_DoesNotEnterStubForCallee()
    {
        const string src = """
            public static class Lib {
                public static int Inner(int x) => x + 1;
                public static int Outer(int x) => Inner(x) * 2;
            }
            """;
        EncodeImportInvokeBoth(src, "Lib", "Outer", poisonCalleeStub: true, 3).ShouldBe(8);
    }

    [Fact]
    public void Run_ArrayBoxCastThrow()
    {
        const string src = """
            public static class Lib {
                public static int Go() {
                    var a = new int[2];
                    a[1] = 4;
                    object o = 5;
                    int n = (int)o;
                    return a[1] + n;
                }
            }
            """;
        EncodeImportInvoke(src, "Lib", "Go").ShouldBe(9);
    }

    [Fact]
    public void Run_Throw_Propagates()
    {
        const string src = "public static class Lib { public static int Boom() { throw new System.InvalidOperationException(\"x\"); } }";
        Should.Throw<InvalidOperationException>(() => EncodeImportInvoke(src, "Lib", "Boom"))
            .Message.ShouldBe("x");
    }

    [Fact]
    public void Apply_ThenRun_StillAdds()
    {
        EncodeImportInvoke(
            "public static class Lib { public static int Add(int a, int b) => a + b; }",
            "Lib", "Add", new object[] { 2, 3 }, OptimizationLevel.Debug, invoke: null, applySeed: true)
            .ShouldBe(5);
    }

    [Fact]
    public void Apply_ThenRun_StillReturnsHi()
    {
        EncodeImportInvoke(
            "public static class Lib { public static string Hi() => \"hi\"; }",
            "Lib", "Hi", Array.Empty<object>(), OptimizationLevel.Debug, invoke: null, applySeed: true)
            .ShouldBe("hi");
    }

    [Fact]
    public void Apply_ThenRun_StillBranches()
    {
        EncodeImportInvoke(
            "public static class Lib { public static int Pick(int a, int b) => a < b ? 1 : 0; }",
            "Lib", "Pick", new object[] { 1, 2 }, OptimizationLevel.Debug, invoke: null, applySeed: true)
            .ShouldBe(1);
    }

    private static object? EncodeImportInvoke(
        string source,
        string typeName,
        string methodName,
        params object[] args) =>
        EncodeImportInvoke(source, typeName, methodName, args, OptimizationLevel.Debug, invoke: null);

    private static object? EncodeImportInvoke(
        string source,
        string typeName,
        string methodName,
        object[] args,
        OptimizationLevel optimization) =>
        EncodeImportInvoke(source, typeName, methodName, args, optimization, invoke: null);

    private static object? EncodeImportInvoke(
        string source,
        string typeName,
        string methodName,
        Func<Assembly, object?> invoke) =>
        EncodeImportInvoke(source, typeName, methodName, Array.Empty<object>(), OptimizationLevel.Debug, invoke);

    private static object? EncodeImportInvoke(
        string source,
        string typeName,
        string methodName,
        object[] args,
        OptimizationLevel optimization,
        Func<Assembly, object?>? invoke) =>
        EncodeImportInvoke(source, typeName, methodName, args, optimization, invoke, applySeed: false);

    private static object? EncodeImportInvoke(
        string source,
        string typeName,
        string methodName,
        object[] args,
        OptimizationLevel optimization,
        Func<Assembly, object?>? invoke,
        bool applySeed)
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
            var opMap = identity;
            var xorKey = new byte[8];
            if (applySeed)
            {
                var seed = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
                opMap = VmSeed.CreateOpMap(seed);
                xorKey = VmSeed.CreateXorKey(seed);
                opMap.SequenceEqual(identity).ShouldBeFalse();
                xorKey.SequenceEqual(new byte[8]).ShouldBeFalse();
                VmSeed.Apply(code, opMap, xorKey);
            }

            var retType = method.MethodSig.RetType.ToTypeDefOrRef();
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
                returnTypes: new[] { retType });
            var run = vmType.FindMethod("Run");
            run.ShouldNotBeNull();
            VmImporter.WriteStub(method, run!, id: 0);
            if (invoke is not null)
                return VmExecuteHarness.Invoke(module, invoke);
            return VmExecuteHarness.Invoke(module, typeName, methodName, args);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static object? EncodeImportInvokeBoth(
        string source,
        string typeName,
        string entryMethod,
        bool poisonCalleeStub,
        params object[] args)
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-vm-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "VmExecLib", OptimizationLevel.Debug);
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
            var type = module.Types.FirstOrDefault(t => t.Name == typeName);
            type.ShouldNotBeNull($"type '{typeName}' was not found");
            var selected = VmEncoder.Select(type!.Methods, maxMethods: 256);
            selected.Count.ShouldBeGreaterThanOrEqualTo(2);

            var ids = new Dictionary<MethodDef, int>();
            for (var i = 0; i < selected.Count; i++)
                ids[selected[i]] = i;

            var tables = new VmMemberTables();
            var blobs = new byte[selected.Count][];
            var starts = new int[selected.Count];
            var returnTypes = new ITypeDefOrRef[selected.Count];
            var offset = 0;
            for (var i = 0; i < selected.Count; i++)
            {
                VmEncoder.TryEncode(selected[i], ids, tables, out blobs[i], out var skipReason)
                    .ShouldBeTrue($"expected encode of '{selected[i].Name}' to succeed, skipReason={skipReason}");
                skipReason.ShouldBeNull();
                starts[i] = offset;
                offset += blobs[i].Length;
                returnTypes[i] = selected[i].MethodSig.RetType.ToTypeDefOrRef();
            }

            var entryIndex = -1;
            for (var i = 0; i < selected.Count; i++)
            {
                if (selected[i].Name == entryMethod)
                    entryIndex = i;
            }

            entryIndex.ShouldBeGreaterThanOrEqualTo(0, $"entry method '{entryMethod}' was not selected");
            blobs[entryIndex].ShouldContain((byte)VmOp.CallVm);

            var code = new byte[offset];
            offset = 0;
            for (var i = 0; i < blobs.Length; i++)
            {
                Buffer.BlockCopy(blobs[i], 0, code, offset, blobs[i].Length);
                offset += blobs[i].Length;
            }

            var identity = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            var context = PipelineContext.ForAssembly(module, new ObfySettings());
            var vmType = VmImporter.Import(
                context,
                code,
                starts,
                opMap: identity,
                xorKey: new byte[8],
                methods: tables.Methods,
                fields: tables.Fields,
                types: tables.Types,
                returnTypes: returnTypes);
            var run = vmType.FindMethod("Run");
            run.ShouldNotBeNull();
            for (var i = 0; i < selected.Count; i++)
                VmImporter.WriteStub(selected[i], run!, id: i);

            if (poisonCalleeStub)
            {
                foreach (var method in selected)
                {
                    if (method.Name != entryMethod)
                        ReplaceStubWithThrow(method);
                }
            }

            return VmExecuteHarness.Invoke(module, typeName, entryMethod, args);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static void ReplaceStubWithThrow(MethodDef method)
    {
        var module = method.Module
            ?? throw new InvalidOperationException("Poison stub method has no module.");
        var ex = new TypeRefUser(module, "System", "InvalidOperationException", module.CorLibTypes.AssemblyRef);
        var ctor = new MemberRefUser(
            module,
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String),
            ex);
        var body = new CilBody { MaxStack = 8 };
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "Inner stub invoked"));
        body.Instructions.Add(Instruction.Create(OpCodes.Newobj, ctor));
        body.Instructions.Add(Instruction.Create(OpCodes.Throw));
        body.UpdateInstructionOffsets();
        method.Body = body;
    }

    private static void AssertEnum(object? value, string typeName, string name, int underlying)
    {
        value.ShouldNotBeNull();
        value!.GetType().Name.ShouldBe(typeName);
        value.ToString().ShouldBe(name);
        Convert.ToInt32(value).ShouldBe(underlying);
        value.ShouldBe(Enum.ToObject(value.GetType(), underlying));
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

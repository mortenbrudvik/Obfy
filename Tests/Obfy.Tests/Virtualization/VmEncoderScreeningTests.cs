using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Obfy.Core.Virtualization;
using Shouldly;

namespace Obfy.Tests.Virtualization;

/// <summary>
/// The encoder is a whitelist: a construct the VM cannot run with the CLR's semantics must make
/// the method be skipped, never be encoded and fail (or silently differ) at run time.
/// </summary>
public class VmEncoderScreeningTests
{
    private static readonly IReadOnlyDictionary<MethodDef, int> EmptyIds =
        new Dictionary<MethodDef, int>();

    // --- Nullable<T> operands (finding 2) -------------------------------------------------

    [Fact]
    public void NullableField_LoadedAndBoxed_Skips()
    {
        const string source = """
            public class Holder {
                public int? Nf = 5;
                public object Get() => Nf;
            }
            """;
        AssertSkips(source, "Holder", "Get", VmSkipReasons.NonPrimitiveValuetype);
    }

    [Fact]
    public void UnboxAnyNullable_Skips()
    {
        // unbox.any int? then box int?: null must stay null, a value must round-trip.
        const string source = "public static class Lib { public static object Pass(object o) => (int?)o; }";
        AssertSkips(source, "Lib", "Pass", VmSkipReasons.NonPrimitiveValuetype);
    }

    [Fact]
    public void IsinstNullable_Skips()
    {
        const string source = "public static class Lib { public static bool IsInt(object o) => o is int?; }";
        AssertSkips(source, "Lib", "IsInt", VmSkipReasons.NonPrimitiveValuetype);
    }

    [Fact]
    public void CalleeReturningNullable_Skips()
    {
        const string source = """
            public static class Lib {
                public static int? Source() => 7;
                public static void Sink(int? value) { }
                public static void Pipe() { Sink(Source()); }
            }
            """;
        AssertSkips(source, "Lib", "Pipe", VmSkipReasons.NonPrimitiveValuetype);
    }

    [Fact]
    public void NullableUnrelatedToTheMethod_StillEncodes()
    {
        // The screening must not reject ordinary reference-typed operands.
        const string source = """
            public class Holder {
                public string Name = "x";
                public object Get() => Name;
            }
            public static class Lib { public static bool IsStr(object o) => o is string; }
            """;
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        foreach (var (type, name) in new[] { ("Holder", "Get"), ("Lib", "IsStr") })
        {
            VmEncoder.TryEncode(VmFixHarness.Find(module, type, name), EmptyIds, new VmMemberTables(), out _, out var reason)
                .ShouldBeTrue($"{type}.{name}: {reason}");
        }
    }

    // --- Callee signatures (finding 5) ----------------------------------------------------

    [Fact]
    public void CallWithByRefLikeSignature_Skips()
    {
        // int.Parse(s.AsSpan()): MethodBase.Invoke cannot pass or return a ref struct.
        const string source = """
            using System;
            public static class Lib { public static int Parse(string s) => int.Parse(s.AsSpan()); }
            """;
        AssertSkips(source, "Lib", "Parse", VmSkipReasons.ByRef);
    }

    // --- Lone surrogate string literals (finding 3) ---------------------------------------

    [Fact]
    public void LdstrWithLoneSurrogate_Skips()
    {
        const string source = "public static class Lib { public static string Lone() => \"a\\uD800b\"; }";
        AssertSkips(source, "Lib", "Lone", VmSkipReasons.InvalidStringLiteral);
    }

    [Fact]
    public void LdstrWithSurrogatePair_RoundTripsThroughTheVm()
    {
        const string source = "public static class Lib { public static string Emoji() => \"x\\U0001F600y\"; }";
        VmFixHarness.RunOnce(source, OptimizationLevel.Release, "Lib", "Emoji").ShouldBe("x\U0001F600y");

        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        VmEncoder.TryEncode(VmFixHarness.Find(module, "Lib", "Emoji"), EmptyIds, new VmMemberTables(), out var code, out _)
            .ShouldBeTrue();
        Encoding.UTF8.GetString(code, 3, BitConverter.ToUInt16(code, 1)).ShouldBe("x\U0001F600y");
    }

    // --- Precise static initialization (finding 9) ----------------------------------------

    [Fact]
    public void CallVm_ToStaticMethodOfPreciseInitType_GoesThroughTheStub()
    {
        const string source = """
            public static class Dep {
                static Dep() { }
                public static int Inner(int x) => x + 1;
            }
            public static class Lib { public static int Outer(int x) => Dep.Inner(x) * 2; }
            """;
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        var inner = VmFixHarness.Find(module, "Dep", "Inner");
        var outer = VmFixHarness.Find(module, "Lib", "Outer");
        var ids = new Dictionary<MethodDef, int> { [inner] = 0 };

        VmEncoder.TryEncode(outer, ids, new VmMemberTables(), out var code, out var reason).ShouldBeTrue(reason);
        code.ShouldNotContain((byte)VmOp.CallVm,
            "a CallVm would skip the stub and so never run Dep's precise-init static constructor");
    }

    [Fact]
    public void CallVm_ToStaticMethodOfBeforeFieldInitType_StillUsed()
    {
        const string source = """
            public static class Dep {
                public static int Seed = 3;
                public static int Inner(int x) => x + 1;
            }
            public static class Lib { public static int Outer(int x) => Dep.Inner(x) * 2; }
            """;
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        var inner = VmFixHarness.Find(module, "Dep", "Inner");
        var outer = VmFixHarness.Find(module, "Lib", "Outer");
        var ids = new Dictionary<MethodDef, int> { [inner] = 0 };

        VmEncoder.TryEncode(outer, ids, new VmMemberTables(), out var code, out _).ShouldBeTrue();
        code.ShouldContain((byte)VmOp.CallVm);
    }

    [Fact]
    public void CallVm_WithinOneType_StillUsedEvenWithStaticConstructor()
    {
        const string source = """
            public static class Lib {
                static Lib() { }
                public static int Inner(int x) => x + 1;
                public static int Outer(int x) => Inner(x) * 2;
            }
            """;
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        var inner = VmFixHarness.Find(module, "Lib", "Inner");
        var outer = VmFixHarness.Find(module, "Lib", "Outer");
        var ids = new Dictionary<MethodDef, int> { [inner] = 0 };

        VmEncoder.TryEncode(outer, ids, new VmMemberTables(), out var code, out _).ShouldBeTrue();
        code.ShouldContain((byte)VmOp.CallVm, "Outer's own stub already ran the type initializer");
    }

    [Fact]
    public void PreciseInitStaticConstructor_StillRunsWhenOuterCallsInner()
    {
        const string source = """
            public static class Log { public static string S = ""; }
            public static class Dep {
                static Dep() { Log.S += "cctor;"; }
                public static int Inner(int x) => x + 1;
            }
            public static class Lib {
                public static int Outer(int x) => Dep.Inner(x) * 2;
                public static string Trace() => Log.S;
            }
            """;
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        var dep = module.Types.First(t => t.Name == "Dep");
        var lib = module.Types.First(t => t.Name == "Lib");
        var selected = new[] { dep.FindMethod("Inner")!, lib.FindMethod("Outer")! };
        selected.Length.ShouldBe(2);

        // Virtualize both across types: Outer -> Inner must still trigger Dep's .cctor.
        var result = VirtualizeAcrossTypes(module, selected, assembly =>
        {
            var libType = VmExecuteHarness.GetType(assembly, "Lib");
            libType.GetMethod("Outer")!.Invoke(null, new object[] { 3 }).ShouldBe(8);
            return libType.GetMethod("Trace")!.Invoke(null, null);
        });
        result.ShouldBe("cctor;");
    }

    private static object? VirtualizeAcrossTypes(ModuleDef module, MethodDef[] methods, Func<System.Reflection.Assembly, object?> invoke)
    {
        var ids = new Dictionary<MethodDef, int>();
        for (var i = 0; i < methods.Length; i++)
            ids[methods[i]] = i;

        var tables = new VmMemberTables();
        var blobs = new byte[methods.Length][];
        var starts = new int[methods.Length];
        var offset = 0;
        for (var i = 0; i < methods.Length; i++)
        {
            VmEncoder.TryEncode(methods[i], ids, tables, out blobs[i], out var reason)
                .ShouldBeTrue($"{methods[i].Name}: {reason}");
            starts[i] = offset;
            offset += blobs[i].Length;
        }

        var code = blobs.SelectMany(b => b).ToArray();
        var identity = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var context = Obfy.Core.Pipeline.PipelineContext.ForAssembly(module, new Obfy.Core.Models.ObfySettings());
        var vmType = VmImporter.Import(
            context, code, starts, identity, new byte[8],
            tables.Methods, tables.Fields, tables.Types,
            methods.Select(m => VmFixHarness.Strip(m.MethodSig.RetType).ToTypeDefOrRef()).ToList(),
            methods.SelectMany(m => m.Parameters).Select(p => VmFixHarness.Strip(p.Type).ToTypeDefOrRef()).ToList(),
            methods.Select(m => m.Parameters.Count).ToArray());
        var run = vmType.FindMethod("Run")!;
        for (var i = 0; i < methods.Length; i++)
            VmImporter.WriteStub(methods[i], run, i);
        return VmExecuteHarness.Invoke(module, invoke);
    }

    private static void AssertSkips(string source, string typeName, string methodName, string expectedReason)
    {
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        var method = VmFixHarness.Find(module, typeName, methodName);
        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var reason)
            .ShouldBeFalse($"{typeName}.{methodName} must be skipped");
        reason.ShouldBe(expectedReason);
    }
}

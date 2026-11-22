using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Autofac;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Obfy.Core.Services.Solution;
using Obfy.Core.Utilities;
using Obfy.Core.Virtualization;
using Obfy.Tests.CoreServices;
using Obfy.Tests.Virtualization;
using Settings.Core.Validation;
using Shouldly;

namespace Obfy.Tests;

public class ReviewFixesCoreAndVmTests : IDisposable
{
    private readonly string _dir = CoreServicesTestSupport.NewTempDirectory("obfy-review-1b");

    public void Dispose() => CoreServicesTestSupport.DeleteQuietly(_dir);

    #region VM R4/R8 Widening

    [Fact]
    public void Vm_FloatToDoubleWidening_MatchesClrAcrossReturnOpsComparisonsAndCalls()
    {
        const string source = """
            public static class Lib
            {
                public static double WidenCSharp(float f) => f;
                public static double AddMixedCSharp(float f, double d) => f + d;
                public static bool LessMixedCSharp(float f, double d) => f < d;

                public static double RawReturnR4AsDouble(float f) => 0.0;
                public static float RawNarrowR8AsFloat(double d) => 0.0f;
                public static double RawAddR4AndR8(float f, double d) => 0.0;
                public static double RawAddR8AndR4(double d, float f) => 0.0;
                public static bool RawLessR4AndR8(float f, double d) => false;
                public static bool RawEqualR4AndR8(float f, double d) => false;
                public static bool RawBleR4AndR8(float f, double d) => false;
                public static bool RawBgeUnR8AndR4(double d, float f) => false;

                public static double DoubleCallee(double d) => d + 1.0;
                public static double CallVmPassR4ToDouble(float f) => 0.0;

                public static double InnerReturnR4AsDouble(float f) => 0.0;
                public static double CallVmReceiveR4AsDouble(float f) => InnerReturnR4AsDouble(f) + 2.0;
            }
            """;

        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);

        // Return R4 without conv.r8 from a double-returning method
        ReplaceBody(module, "Lib", "RawReturnR4AsDouble", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        // Return R8 without conv.r4 from a float-returning method
        ReplaceBody(module, "Lib", "RawNarrowR8AsFloat", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        // Raw R4 + R8 and R8 + R4 without conv.r8
        ReplaceBody(module, "Lib", "RawAddR4AndR8", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });
        ReplaceBody(module, "Lib", "RawAddR8AndR4", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        // Raw R4 < R8 and R4 == R8
        ReplaceBody(module, "Lib", "RawLessR4AndR8", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Clt));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });
        ReplaceBody(module, "Lib", "RawEqualR4AndR8", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Ceq));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        // Raw ble (R4, R8) and bge.un (R8, R4)
        ReplaceBody(module, "Lib", "RawBleR4AndR8", body =>
        {
            var retTrue = Instruction.Create(OpCodes.Ldc_I4_1);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Ble, retTrue));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            body.Instructions.Add(retTrue);
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });
        ReplaceBody(module, "Lib", "RawBgeUnR8AndR4", body =>
        {
            var retTrue = Instruction.Create(OpCodes.Ldc_I4_1);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Bge_Un, retTrue));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            body.Instructions.Add(retTrue);
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        // CallVm passing R4 into double parameter, and CallVm receiving R4 from double-returning method
        var doubleCallee = VmFixHarness.Find(module, "Lib", "DoubleCallee");
        ReplaceBody(module, "Lib", "CallVmPassR4ToDouble", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Call, doubleCallee));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });
        ReplaceBody(module, "Lib", "InnerReturnR4AsDouble", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        var methods = new[]
        {
            "WidenCSharp", "AddMixedCSharp", "LessMixedCSharp",
            "RawReturnR4AsDouble", "RawNarrowR8AsFloat",
            "RawAddR4AndR8", "RawAddR8AndR4",
            "RawLessR4AndR8", "RawEqualR4AndR8",
            "RawBleR4AndR8", "RawBgeUnR8AndR4",
            "DoubleCallee", "CallVmPassR4ToDouble",
            "InnerReturnR4AsDouble", "CallVmReceiveR4AsDouble"
        };

        VmFixHarness.VirtualizeAndRun(module, "Lib", methods, assembly =>
        {
            var lib = VmExecuteHarness.GetType(assembly, "Lib");
            object? Call(string name, params object?[] args) => lib.GetMethod(name)!.Invoke(null, args);

            ((double)Call("WidenCSharp", 1.5f)!).ShouldBe(1.5d);
            ((double)Call("AddMixedCSharp", 1.5f, 2.25d)!).ShouldBe(3.75d);
            ((bool)Call("LessMixedCSharp", 1.5f, 2.0d)!).ShouldBeTrue();
            ((bool)Call("LessMixedCSharp", 2.5f, 2.0d)!).ShouldBeFalse();

            ((double)Call("RawReturnR4AsDouble", 1.5f)!).ShouldBe(1.5d);
            ((double)Call("RawReturnR4AsDouble", -3.25f)!).ShouldBe(-3.25d);
            ((float)Call("RawNarrowR8AsFloat", 1.5d)!).ShouldBe(1.5f);

            ((double)Call("RawAddR4AndR8", 1.5f, 2.25d)!).ShouldBe(3.75d);
            ((double)Call("RawAddR8AndR4", 2.25d, 1.5f)!).ShouldBe(3.75d);

            ((bool)Call("RawLessR4AndR8", 1.5f, 2.0d)!).ShouldBeTrue();
            ((bool)Call("RawLessR4AndR8", 2.5f, 2.0d)!).ShouldBeFalse();
            ((bool)Call("RawLessR4AndR8", float.NaN, 2.0d)!).ShouldBeFalse();

            ((bool)Call("RawEqualR4AndR8", 1.5f, 1.5d)!).ShouldBeTrue();
            ((bool)Call("RawEqualR4AndR8", 1.5f, 2.5d)!).ShouldBeFalse();

            ((bool)Call("RawBleR4AndR8", 1.5f, 1.5d)!).ShouldBeTrue();
            ((bool)Call("RawBleR4AndR8", float.NaN, 1.5d)!).ShouldBeFalse();
            ((bool)Call("RawBgeUnR8AndR4", 2.0d, 1.5f)!).ShouldBeTrue();
            ((bool)Call("RawBgeUnR8AndR4", double.NaN, 1.5f)!).ShouldBeTrue();

            ((double)Call("CallVmPassR4ToDouble", 1.5f)!).ShouldBe(2.5d);
            ((double)Call("CallVmReceiveR4AsDouble", 1.5f)!).ShouldBe(3.5d);
            return null;
        });
    }

    [Fact]
    public void Vm_StlocAndStarg_CoerceFloatSlots_AndPreserveFloat64Precision()
    {
        const string source = """
            public static class Lib
            {
                public static double DoubleLocalFromFloatCSharp(float f, float tiny)
                {
                    double d = f;
                    return d + tiny;
                }

                public static double RawStoreR4InDoubleLocal(float f, float tiny) => 0.0;
                public static double RawStoreR8InFloatLocal(double d) => 0.0;
                public static double RawStoreR4InDoubleArg(double d, float f, float tiny) => 0.0;
                public static double RawStoreR8InFloatArg(float f, double d) => 0.0;
            }
            """;

        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);

        ReplaceBody(module, "Lib", "RawStoreR4InDoubleLocal", body =>
        {
            body.InitLocals = true;
            body.Variables.Add(new Local(module.CorLibTypes.Double));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Stloc_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        ReplaceBody(module, "Lib", "RawStoreR8InFloatLocal", body =>
        {
            body.InitLocals = true;
            body.Variables.Add(new Local(module.CorLibTypes.Single));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Stloc_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        var storeR4Arg = VmFixHarness.Find(module, "Lib", "RawStoreR4InDoubleArg");
        ReplaceBody(module, "Lib", "RawStoreR4InDoubleArg", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Starg_S, storeR4Arg.Parameters[0]));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        var storeR8Arg = VmFixHarness.Find(module, "Lib", "RawStoreR8InFloatArg");
        ReplaceBody(module, "Lib", "RawStoreR8InFloatArg", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Starg_S, storeR8Arg.Parameters[0]));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        var methods = new[]
        {
            "DoubleLocalFromFloatCSharp",
            "RawStoreR4InDoubleLocal",
            "RawStoreR8InFloatLocal",
            "RawStoreR4InDoubleArg",
            "RawStoreR8InFloatArg"
        };

        VmFixHarness.VirtualizeAndRun(module, "Lib", methods, assembly =>
        {
            var lib = VmExecuteHarness.GetType(assembly, "Lib");
            object? Call(string name, params object?[] args) => lib.GetMethod(name)!.Invoke(null, args);

            const float one = 1.0f;
            const float tiny = 1e-8f;
            var expectedWidened = (double)one + (double)tiny;
            expectedWidened.ShouldBeGreaterThan(1.0d); // float32 addition would round to 1.0f

            ((double)Call("DoubleLocalFromFloatCSharp", one, tiny)!).ShouldBe(expectedWidened);
            ((double)Call("RawStoreR4InDoubleLocal", one, tiny)!).ShouldBe(expectedWidened);
            ((double)Call("RawStoreR4InDoubleArg", 0.0d, one, tiny)!).ShouldBe(expectedWidened);

            const double precise = 1.0d + 1e-10d;
            var expectedNarrowed = (double)(float)precise;
            expectedNarrowed.ShouldBe(1.0d);

            ((double)Call("RawStoreR8InFloatLocal", precise)!).ShouldBe(expectedNarrowed);
            ((double)Call("RawStoreR8InFloatArg", 0.0f, precise)!).ShouldBe(expectedNarrowed);
            return null;
        });
    }

    #endregion

    #region VmEncoder & VmMemberTables

    [Fact]
    public async Task VmEncoder_SkipsIntPtrMethodAndField_AndLeavesCallerRunnable()
    {
        const string source = """
            using System;
            public static class PtrDep
            {
                public static IntPtr Field = IntPtr.Zero;
                public static IntPtr Make() => Field;
                public static void Take(IntPtr p) { }
            }
            public static class Lib
            {
                public static int CallPtr() { PtrDep.Take(PtrDep.Make()); return 7; }
                public static void CopyPtrField() { PtrDep.Field = PtrDep.Field; }
                public static int Plain(int x) => x + 10;
            }
            """;

        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        var emptyIds = new Dictionary<MethodDef, int>();

        VmEncoder.TryEncode(
            VmFixHarness.Find(module, "Lib", "CallPtr"),
            emptyIds,
            new VmMemberTables(),
            out _,
            out var callSkipReason).ShouldBeFalse();
        callSkipReason.ShouldBe(VmSkipReasons.NonPrimitiveValuetype);

        VmEncoder.TryEncode(
            VmFixHarness.Find(module, "Lib", "CopyPtrField"),
            emptyIds,
            new VmMemberTables(),
            out _,
            out var fieldSkipReason).ShouldBeFalse();
        fieldSkipReason.ShouldBe(VmSkipReasons.NonPrimitiveValuetype);

        // Full VirtualizationObfuscator skips IntPtr methods cleanly and virtualizes Plain
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            Virtualization = { Enabled = true }
        });
        var obf = new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object);
        var obfResult = await obf.ObfuscateAsync(context);
        obfResult.Success.ShouldBeTrue(obfResult.ErrorMessage);

        VmExecuteHarness.Invoke(module, assembly =>
        {
            var lib = VmExecuteHarness.GetType(assembly, "Lib");
            ((int)lib.GetMethod("CallPtr")!.Invoke(null, null)!).ShouldBe(7);
            ((int)lib.GetMethod("Plain")!.Invoke(null, new object[] { 5 })!).ShouldBe(15);
            return null;
        });
    }

    [Fact]
    public void VmEncoder_SkipsVarArgCall()
    {
        var module = new ModuleDefUser("VarArgMod", Guid.NewGuid(), new AssemblyRefUser("mscorlib", new Version(4, 0, 0, 0)));
        var type = new TypeDefUser("Ns", "Caller", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(type);

        var varargCallee = new MemberRefUser(
            module,
            "VarArgLog",
            new MethodSig(
                CallingConvention.VarArg,
                0,
                module.CorLibTypes.Void,
                new TypeSig[] { module.CorLibTypes.String },
                new TypeSig[] { module.CorLibTypes.Int32 }),
            type);

        var method = new MethodDefUser(
            "InvokeVarArg",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            dnlib.DotNet.MethodImplAttributes.IL,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static)
        {
            Body = new CilBody()
        };
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "fmt"));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, varargCallee));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        VmEncoder.TryEncode(method, new Dictionary<MethodDef, int>(), new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.UnsupportedOpcode);
    }

    [Fact]
    public void VmEncoder_MethodEndingInUnconditionalBranch_EncodesAndExecutes()
    {
        const string source = """
            public static class Lib
            {
                public static int LoopCountdown(int n) => 0;
            }
            """;

        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        var method = VmFixHarness.Find(module, "Lib", "LoopCountdown");
        var loopBody = new CilBody { MaxStack = 8 };
        var top = Instruction.Create(OpCodes.Ldarg_0);
        var step = Instruction.Create(OpCodes.Ldarg_0);

        loopBody.Instructions.Add(top);
        loopBody.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        loopBody.Instructions.Add(Instruction.Create(OpCodes.Bgt_S, step));
        loopBody.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 42));
        loopBody.Instructions.Add(Instruction.Create(OpCodes.Ret));
        loopBody.Instructions.Add(step);
        loopBody.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        loopBody.Instructions.Add(Instruction.Create(OpCodes.Sub));
        loopBody.Instructions.Add(Instruction.Create(OpCodes.Starg_S, method.Parameters[0]));
        loopBody.Instructions.Add(Instruction.Create(OpCodes.Br_S, top));
        method.Body = loopBody;

        VmEncoder.TryEncode(method, new Dictionary<MethodDef, int>(), new VmMemberTables(), out var encoded, out var reason)
            .ShouldBeTrue(reason);
        encoded.ShouldNotBeEmpty();

        var result = VmFixHarness.VirtualizeAndRun(module, "Lib", new[] { "LoopCountdown" }, assembly =>
            VmExecuteHarness.GetType(assembly, "Lib").GetMethod("LoopCountdown")!.Invoke(null, new object[] { 4 }));
        result.ShouldBe(42);
    }

    [Fact]
    public void VmMemberTables_SameFullNameFromDifferentAssemblies_GetDistinctSlots()
    {
        var module = new ModuleDefUser("Host", Guid.NewGuid(), new AssemblyRefUser("System.Runtime", new Version(8, 0, 0, 0)));
        var asmA = new AssemblyRefUser("AssemblyA", new Version(1, 0, 0, 0));
        var asmB = new AssemblyRefUser("AssemblyB", new Version(1, 0, 0, 0));

        var typeA = new TypeRefUser(module, "Shared", "Widget", asmA);
        var typeA2 = new TypeRefUser(module, "Shared", "Widget", asmA);
        var typeB = new TypeRefUser(module, "Shared", "Widget", asmB);

        var tables = new VmMemberTables();
        var slotTypeA = tables.AddType(typeA);
        var slotTypeA2 = tables.AddType(typeA2);
        var slotTypeB = tables.AddType(typeB);

        slotTypeA2.ShouldBe(slotTypeA);
        slotTypeB.ShouldNotBe(slotTypeA);

        var sig = MethodSig.CreateStatic(module.CorLibTypes.Int32);
        var methodA = new MemberRefUser(module, "Get", sig, typeA);
        var methodB = new MemberRefUser(module, "Get", sig, typeB);
        tables.AddMethod(methodA).ShouldNotBe(tables.AddMethod(methodB));

        var fieldSig = new FieldSig(module.CorLibTypes.Int32);
        var fieldA = new MemberRefUser(module, "Tag", fieldSig, typeA);
        var fieldB = new MemberRefUser(module, "Tag", fieldSig, typeB);
        tables.AddField(fieldA).ShouldNotBe(tables.AddField(fieldB));
    }

    #endregion

    #region ClosedSetProcessor & SolutionAnalyzer

    [Fact]
    public async Task ClosedSetProcessor_PackingEnabled_DoesNotThrowOnDisposedModule()
    {
        var libPath = CoreServicesTestSupport.CompileLibrary(_dir, "PackLib", """
            namespace PackLib;
            public static class Msg
            {
                public static string Text() => "ok";
            }
            """);
        var appPath = CoreServicesTestSupport.CompileExe(_dir, "PackApp", """
            public static class Program
            {
                public static void Main() { }
            }
            """, libPath);

        var outDir = Path.Combine(_dir, "packed-closed-out");
        var settings = CoreServicesTestSupport.AllOff();
        settings.Packing.Enabled = true;

        using var container = CoreServicesTestSupport.BuildContainer();
        var processor = container.Resolve<IClosedSetProcessor>();
        var result = await processor.ExecuteAsync(
            [ClosedSetInput.From(libPath), ClosedSetInput.From(appPath)],
            outDir,
            settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.ModuleResults.ShouldContain(r => r.PackedLauncherPath == Path.Combine(outDir, "PackApp.exe"));
        result.ModuleResults.ShouldContain(r =>
            r.OutputPath == Path.Combine(outDir, "PackLib.dll")
            && r.Warnings.Any(w => w.Contains("Packing skipped", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ClosedSetProcessor_TransitiveEntryPointReachability_RenamesTwoHopLibrary()
    {
        var libB = CoreServicesTestSupport.CompileLibrary(_dir, "LibB", """
            namespace Chain.B;
            public class LeafService
            {
                public static string Ping() => "pong";
            }
            """);
        var libA = CoreServicesTestSupport.CompileLibrary(_dir, "LibA", """
            namespace Chain.A;
            public class MidService
            {
                public static string Forward() => Chain.B.LeafService.Ping();
            }
            """, libB);
        var app = CoreServicesTestSupport.CompileExe(_dir, "App", """
            public static class Program
            {
                public static void Main() { }
                public static string Run() => Chain.A.MidService.Forward();
            }
            """, libA);
        var extra = CoreServicesTestSupport.CompileLibrary(_dir, "Extra", """
            namespace Chain.Extra;
            public class UnreferencedApi
            {
                public static string Untouched() => "x";
            }
            """);

        var settings = CoreServicesTestSupport.AllOff();
        settings.SymbolRenaming.Enabled = true;
        settings.SymbolRenaming.PreservePublicApi = false;
        settings.SymbolRenaming.Mode = NamingMode.Sequential;

        var outDir = Path.Combine(_dir, "transitive-out");
        using var container = CoreServicesTestSupport.BuildContainer();
        var result = await container.Resolve<IClosedSetProcessor>().ExecuteAsync(
            [
                new ClosedSetInput { AssemblyPath = libB, Hints = new ProjectSettingsHints { PreservePublicApi = true } },
                new ClosedSetInput { AssemblyPath = libA, Hints = new ProjectSettingsHints { PreservePublicApi = true } },
                new ClosedSetInput { AssemblyPath = app, Hints = new ProjectSettingsHints() },
                new ClosedSetInput { AssemblyPath = extra, Hints = new ProjectSettingsHints { PreservePublicApi = false } }
            ],
            outDir,
            settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);

        var outLibB = Path.Combine(outDir, "LibB.dll");
        var outLibA = Path.Combine(outDir, "LibA.dll");
        var outApp = Path.Combine(outDir, "App.exe");
        var outExtra = Path.Combine(outDir, "Extra.dll");

        using (var modB = ModuleDefMD.Load(await File.ReadAllBytesAsync(outLibB)))
            modB.GetTypes().ShouldNotContain(t => t.Name == "LeafService");
        using (var modA = ModuleDefMD.Load(await File.ReadAllBytesAsync(outLibA)))
            modA.GetTypes().ShouldNotContain(t => t.Name == "MidService");
        using (var modExtra = ModuleDefMD.Load(await File.ReadAllBytesAsync(outExtra)))
            modExtra.GetTypes().ShouldContain(t => t.Name == "UnreferencedApi");

        var alc = new AssemblyLoadContext($"transitive-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            alc.LoadFromAssemblyPath(outLibB);
            alc.LoadFromAssemblyPath(outLibA);
            var appAsm = alc.LoadFromAssemblyPath(outApp);
            var run = appAsm.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Single(m => m.ReturnType == typeof(string) && m.GetParameters().Length == 0 && m.Name != "Main");
            ((string)run.Invoke(null, null)!).ShouldBe("pong");
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public void SolutionAnalyzer_TransitiveProjectReferences_ClearsPreservePublicApiOnTwoHopLibrary()
    {
        var slnDir = Path.Combine(_dir, "sln");
        Directory.CreateDirectory(slnDir);

        WriteFile(Path.Combine(slnDir, @"App\App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\LibA\LibA.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile(Path.Combine(slnDir, @"LibA\LibA.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\LibB\LibB.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile(Path.Combine(slnDir, @"LibB\LibB.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        WriteFile(Path.Combine(slnDir, @"Extra\Extra.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        WriteFile(Path.Combine(slnDir, @"App\bin\Release\net8.0\App.dll"), "");
        WriteFile(Path.Combine(slnDir, @"LibA\bin\Release\net8.0\LibA.dll"), "");
        WriteFile(Path.Combine(slnDir, @"LibB\bin\Release\net8.0\LibB.dll"), "");
        WriteFile(Path.Combine(slnDir, @"Extra\bin\Release\net8.0\Extra.dll"), "");

        var slnPath = Path.Combine(slnDir, "Chain.slnx");
        WriteFile(slnPath, """
            <Solution>
              <Project Path="App/App.csproj" />
              <Project Path="LibA/LibA.csproj" />
              <Project Path="LibB/LibB.csproj" />
              <Project Path="Extra/Extra.csproj" />
            </Solution>
            """);

        var session = new SolutionAnalyzer().Analyze(slnPath);
        session.Entries.Single(e => e.ProjectName == "LibA").Hints.PreservePublicApi.ShouldBeFalse();
        session.Entries.Single(e => e.ProjectName == "LibB").Hints.PreservePublicApi.ShouldBeFalse();
        session.Entries.Single(e => e.ProjectName == "Extra").Hints.PreservePublicApi.ShouldBeTrue();
    }

    [Fact]
    public async Task ClosedSetProcessor_WithSigning_RewritesInternalsVisibleToPublicKeyForMembers()
    {
        var oldSnkPath = CoreServicesTestSupport.WriteSnk(Path.Combine(_dir, "old.snk"));
        var newSnkPath = CoreServicesTestSupport.WriteSnk(Path.Combine(_dir, "new.snk"));
        var oldKey = new StrongNameKey(oldSnkPath);
        var newKey = new StrongNameKey(newSnkPath);
        var newHex = BitConverter.ToString(newKey.PublicKey).Replace("-", "").ToLowerInvariant();
        var oldHex = BitConverter.ToString(oldKey.PublicKey).Replace("-", "").ToLowerInvariant();

        var libPath = CoreServicesTestSupport.CompileLibrary(_dir, "FriendTarget", """
            using System.Runtime.CompilerServices;
            [assembly: InternalsVisibleTo("FriendCaller")]
            [assembly: InternalsVisibleTo("ExternalFriend")]
            public static class InternalApi
            {
                internal static int Secret() => 42;
            }
            """);
        var appPath = CoreServicesTestSupport.CompileExe(_dir, "FriendCaller", """
            public static class Program
            {
                public static void Main() { }
                public static int Run() => InternalApi.Secret();
            }
            """, libPath);

        // Simulate pre-signed input pair carrying the old public key in InternalsVisibleTo
        using (var mod = ModuleDefMD.Load(await File.ReadAllBytesAsync(libPath)))
        {
            var ivt = mod.Assembly.CustomAttributes.First(a =>
                a.TypeFullName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute"
                && a.ConstructorArguments[0].Value?.ToString() == "FriendCaller");
            var arg = ivt.ConstructorArguments[0];
            ivt.ConstructorArguments[0] = new CAArgument(arg.Type, new UTF8String($"FriendCaller, PublicKey={oldHex}"));
            mod.Assembly.PublicKey = new PublicKey(oldKey.PublicKey);
            mod.Assembly.HasPublicKey = true;
            mod.Write(libPath, new ModuleWriterOptions(mod) { StrongNameKey = oldKey });
        }

        var settings = CoreServicesTestSupport.AllOff();
        settings.Signing.Enabled = true;
        settings.Signing.KeyFile = newSnkPath;

        var outDir = Path.Combine(_dir, "signed-ivt-out");
        using var container = CoreServicesTestSupport.BuildContainer();
        var result = await container.Resolve<IClosedSetProcessor>().ExecuteAsync(
            [ClosedSetInput.From(libPath), ClosedSetInput.From(appPath)],
            outDir,
            settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);

        var outLib = Path.Combine(outDir, "FriendTarget.dll");
        var outApp = Path.Combine(outDir, "FriendCaller.exe");

        using (var outMod = ModuleDefMD.Load(await File.ReadAllBytesAsync(outLib)))
        {
            var ivts = outMod.Assembly.CustomAttributes
                .Where(a => a.TypeFullName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
                .Select(a => a.ConstructorArguments[0].Value?.ToString())
                .ToList();
            ivts.ShouldContain($"FriendCaller, PublicKey={newHex}");
            ivts.ShouldContain("ExternalFriend");
        }

        var alc = new AssemblyLoadContext($"ivt-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            alc.LoadFromAssemblyPath(outLib);
            var appAsm = alc.LoadFromAssemblyPath(outApp);
            var run = appAsm.GetType("Program")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            ((int)run.Invoke(null, null)!).ShouldBe(42);
        }
        finally
        {
            alc.Unload();
        }
    }

    #endregion

    #region ReportService & HtmlReportGenerator

    [Fact]
    public void ReportService_BuildCombinedReport_DeduplicatesSymbolsAndWarnings_AndAggregatesFileSizes()
    {
        var in1 = Path.Combine(_dir, "in1.dll");
        var in2 = Path.Combine(_dir, "in2.dll");
        var out1 = Path.Combine(_dir, "out1.dll");
        var out2 = Path.Combine(_dir, "out2.dll");
        File.WriteAllBytes(in1, new byte[100]);
        File.WriteAllBytes(in2, new byte[250]);
        File.WriteAllBytes(out1, new byte[120]);
        File.WriteAllBytes(out2, new byte[280]);

        var sharedSymbols = new Dictionary<string, string>
        {
            ["Type:Ns.A"] = "a",
            ["Method:Ns.A.M"] = "b",
            ["Event:Ns.A.E"] = "c",
            ["Namespace:Ns"] = "d"
        };

        var r1 = ObfuscationResult.Successful(
            new ObfuscationStatistics { TypesRenamed = 1, MethodsRenamed = 1, EventsRenamed = 1, NamespacesRenamed = 1 },
            inputPath: in1,
            outputPath: out1,
            symbolMap: new Dictionary<string, string>(sharedSymbols),
            warnings: ["Session warning"]);

        var r2 = ObfuscationResult.Successful(
            new ObfuscationStatistics(),
            inputPath: in2,
            outputPath: out2,
            symbolMap: new Dictionary<string, string>(sharedSymbols),
            warnings: ["Session warning"]);

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            SymbolRenaming = { Enabled = true },
            StringEncryption = { Enabled = false }
        };

        var reportService = new ReportService(
            new Mock<ILogger<ReportService>>().Object,
            new HtmlReportGenerator(new Mock<ILogger<HtmlReportGenerator>>().Object),
            new JsonReportGenerator(new Mock<ILogger<JsonReportGenerator>>().Object));

        var combined = reportService.BuildCombinedReport([r1, r2], settings);

        combined.SymbolSummary.TotalSymbolsRenamed.ShouldBe(4);
        combined.SymbolSummary.TypesRenamed.ShouldBe(1);
        combined.SymbolSummary.MethodsRenamed.ShouldBe(1);
        combined.FileInfo.InputSizeBytes.ShouldBe(350);
        combined.FileInfo.OutputSizeBytes.ShouldBe(400);
        combined.Warnings.Count(w => w.Message == "Session warning").ShouldBe(1);
        combined.Warnings.ShouldNotContain(w => w.Message.Contains("no symbols were renamed", StringComparison.OrdinalIgnoreCase));

        var eventsOnly = ObfuscationResult.Successful(
            new ObfuscationStatistics { EventsRenamed = 1 },
            inputPath: in1,
            outputPath: out1,
            symbolMap: new Dictionary<string, string> { ["Event:Ns.A.E"] = "a" });
        var eventsOnlyReport = reportService.BuildReport(eventsOnly, settings);
        eventsOnlyReport.Warnings.ShouldNotContain(w => w.Message.Contains("no symbols were renamed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HtmlReportGenerator_EncodesObfyVersionAndObfuscatorName()
    {
        var generator = new HtmlReportGenerator(new Mock<ILogger<HtmlReportGenerator>>().Object);
        var report = new ObfuscationReport
        {
            Metadata = new ReportMetadata { ObfyVersion = "<script>v</script>" },
            ProcessingTimes =
            [
                new ProcessingTimeEntry { ObfuscatorName = "<b>Obf</b>", Duration = TimeSpan.FromMilliseconds(10), TransformationsApplied = 1 }
            ]
        };

        var html = await generator.GenerateToStringAsync(report);
        html.ShouldNotContain("<script>v</script>");
        html.ShouldContain("&lt;script&gt;v&lt;/script&gt;");
        html.ShouldNotContain("<b>Obf</b>");
        html.ShouldContain("&lt;b&gt;Obf&lt;/b&gt;");
    }

    #endregion

    #region ObfuscationService

    [Fact]
    public async Task ObfuscationService_UnsupportedExtension_ReturnsFailedResultInsteadOfThrowing()
    {
        var txtPath = Path.Combine(_dir, "notes.txt");
        await File.WriteAllTextAsync(txtPath, "hello");

        using var container = CoreServicesTestSupport.BuildContainer();
        var result = await container.Resolve<IObfuscationService>().ObfuscateAsync(txtPath, null, new ObfySettings());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain("Unsupported file type");
    }

    [Fact]
    public void ObfuscationService_GenerateOutputPath_TrimsTrailingDirectorySeparator()
    {
        var folder = Path.Combine(_dir, "SourceFolder");
        var withSep = folder + Path.DirectorySeparatorChar;
        var withAlt = folder + Path.AltDirectorySeparatorChar;

        ObfuscationService.GenerateOutputPath(withSep).ShouldBe(Path.Combine(_dir, "SourceFolder.obfuscated"));
        ObfuscationService.GenerateOutputPath(withAlt).ShouldBe(Path.Combine(_dir, "SourceFolder.obfuscated"));
    }

    [Fact]
    public async Task ObfuscationService_MergeAndObfuscate_PreservesPrimaryInputPath()
    {
        var libA = CoreServicesTestSupport.CompileLibrary(_dir, "MergeA", "public class A { public static int N() => 1; }");
        var libB = CoreServicesTestSupport.CompileLibrary(_dir, "MergeB", "public class B { public static int N() => 2; }");
        var outPath = Path.Combine(_dir, "Merged.dll");

        using var container = CoreServicesTestSupport.BuildContainer();
        var result = await container.Resolve<IObfuscationService>().MergeAndObfuscateAsync(
            [libA, libB], outPath, CoreServicesTestSupport.AllOff());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.InputPath.ShouldBe(libA);
        File.Exists(result.InputPath!).ShouldBeTrue();
    }

    [Fact]
    public async Task ObfuscationService_NullOutputPath_SetsContextOutputPathToGeneratedPath()
    {
        var libPath = CoreServicesTestSupport.CompileLibrary(_dir, "AutoOut", "public class C { public static int N() => 1; }");
        string? observedContextOutputPath = null;

        var pipeline = new Mock<IObfuscationPipeline>();
        pipeline
            .Setup(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineContext, CancellationToken>((ctx, _) => observedContextOutputPath = ctx.OutputPath)
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics()));

        var service = new ObfuscationService(
            new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object),
            new Mock<ISourceProcessor>().Object,
            pipeline.Object,
            new Mock<IAssemblyMerger>().Object,
            new Mock<ILogger<ObfuscationService>>().Object,
            new Mock<IClosedSetProcessor>().Object);

        var result = await service.ObfuscateAsync(libPath, null, CoreServicesTestSupport.AllOff());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        var expectedOutput = ObfuscationService.GenerateOutputPath(libPath);
        observedContextOutputPath.ShouldBe(expectedOutput);
        result.OutputPath.ShouldBe(expectedOutput);
    }

    #endregion

    #region WildcardMatcher & FrameworkReferences & SettingsValidator

    [Fact]
    public void WildcardMatcher_AdversarialReDoSPattern_FinishesImmediately()
    {
        var sw = Stopwatch.StartNew();
        WildcardMatcher.IsMatch(new string('a', 60) + "c", "*a*a*a*a*a*a*a*a*a*a*b").ShouldBeFalse();
        sw.Stop();

        sw.ElapsedMilliseconds.ShouldBeLessThan(2000);
        WildcardMatcher.IsMatch("aaaaaaaaaaab", "*a*a*a*a*a*a*a*a*a*a*b").ShouldBeTrue();
        WildcardMatcher.IsMatch("MyNamespace.FooBar", "mynamespace.foo?ar").ShouldBeTrue();
    }

    [Fact]
    public void FrameworkReferences_PrivateCoreLibCryptoTypes_UseMicrosoftPublicKeyToken()
    {
        var corlib = new AssemblyRefUser("System.Private.CoreLib", new Version(8, 0, 0, 0), new PublicKeyToken("7cec85d7bea7798e"), UTF8String.Empty);
        var module = new ModuleDefUser("Subject", Guid.NewGuid(), corlib);

        var scope = (AssemblyRef)FrameworkReferences.Type(module, "System.Security.Cryptography", "SHA256").ResolutionScope;
        scope.Name.String.ShouldBe("System.Security.Cryptography");
        scope.PublicKeyOrToken.Token.ToString().ShouldBe("b03f5f7f11d50a3a");
    }

    [Fact]
    public void SettingsValidator_ValidatesMultiLevelNesting_SkipsIndexers_AndHandlesCycles()
    {
        var results = new List<ValidationResult>();
        var root = new DeepRootSettings { Level1 = { Level2 = { Value = 99 } } };

        SettingsValidator.TryValidate(root, results).ShouldBeFalse();
        results.ShouldContain(r => r.ErrorMessage != null && r.ErrorMessage.StartsWith("Level1.Level2.", StringComparison.Ordinal));

        var validResults = new List<ValidationResult>();
        SettingsValidator.TryValidate(new DeepRootSettings(), validResults).ShouldBeTrue();

        var a = new CyclicSettings { Count = 1 };
        var b = new CyclicSettings { Count = 99, Next = a };
        a.Next = b;
        var cycleResults = new List<ValidationResult>();
        SettingsValidator.TryValidate(a, cycleResults).ShouldBeFalse();
        cycleResults.ShouldContain(r => r.ErrorMessage != null && r.ErrorMessage.StartsWith("Next.", StringComparison.Ordinal));
    }

    private sealed class DeepLevel2Settings
    {
        [Range(1, 10)]
        public int Value { get; set; } = 5;
    }

    private sealed class DeepLevel1Settings
    {
        public DeepLevel2Settings Level2 { get; set; } = new();
    }

    private sealed class DeepRootSettings
    {
        public DeepLevel1Settings Level1 { get; set; } = new();
        public string this[int index] => index.ToString();
    }

    private sealed class CyclicSettings
    {
        public CyclicSettings? Next { get; set; }

        [Range(1, 10)]
        public int Count { get; set; } = 5;
    }

    #endregion

    private static void ReplaceBody(ModuleDef module, string typeName, string methodName, Action<CilBody> build)
    {
        var method = VmFixHarness.Find(module, typeName, methodName);
        var body = new CilBody { MaxStack = 8 };
        build(body);
        method.Body = body;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}

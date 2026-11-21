using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Shouldly;

namespace Obfy.Tests.Virtualization;

/// <summary>
/// CLR locals are zero-initialized with their declared type. C# definite assignment hides a read
/// before the first store, but VB/F#/hand-written IL do it: <c>ldloc; ldnull; ceq</c> on a string
/// local, or a long/double local added to a constant. The VM frame must hand back a typed zero.
/// </summary>
public class VmLocalInitTests
{
    [Fact]
    public void UnassignedReferenceLocal_ComparesEqualToNull()
    {
        const string source = """
            public static class Lib {
                public static bool IsNull() { string s = null; return s == null; }
            }
            """;
        Run(source, "IsNull", dropStoreOfLocal: 0).ShouldBe(true);
    }

    [Fact]
    public void UnassignedReferenceLocal_PassesAsNullToACall()
    {
        const string source = """
            public static class Lib {
                public static string Cat(string x) { string s = null; return s + x; }
            }
            """;
        Run(source, "Cat", dropStoreOfLocal: 0, "tail").ShouldBe("tail");
    }

    [Fact]
    public void UnassignedLongLocal_IsInt64Zero()
    {
        const string source = """
            public static class Lib {
                public static long L() { long x = 0; return x + 1; }
            }
            """;
        Run(source, "L", dropStoreOfLocal: 0).ShouldBe(1L);
    }

    [Fact]
    public void UnassignedDoubleLocal_IsFloat64Zero()
    {
        const string source = """
            public static class Lib {
                public static double D() { double x = 0; return x + 1.5; }
            }
            """;
        Run(source, "D", dropStoreOfLocal: 0).ShouldBe(1.5d);
    }

    [Fact]
    public void UnassignedFloatLocal_IsFloat32Zero()
    {
        const string source = """
            public static class Lib {
                public static float F() { float x = 0; return x + 1.5f; }
            }
            """;
        Run(source, "F", dropStoreOfLocal: 0).ShouldBe(1.5f);
    }

    [Fact]
    public void AssignedLocals_AreUnaffectedByTheZeroInit()
    {
        const string source = """
            public static class Lib {
                public static string Mix(long a, double b, string c) {
                    long x = a; double y = b; string z = c;
                    if (x + (long)y == 7) return z + "!";
                    return z;
                }
            }
            """;
        Run(source, "Mix", dropStoreOfLocal: -1, 5L, 2.5d, "n").ShouldBe("n!");
        Run(source, "Mix", dropStoreOfLocal: -1, 1L, 2.5d, "n").ShouldBe("n");
    }

    [Fact]
    public void LocalsAssignedInALoop_KeepTheirValue()
    {
        const string source = """
            public static class Lib {
                public static string Repeat(string x, int n) {
                    string acc = null;
                    for (int i = 0; i < n; i++) acc = acc + x;
                    return acc;
                }
            }
            """;
        Run(source, "Repeat", dropStoreOfLocal: -1, "ab", 3).ShouldBe("ababab");
    }

    [Fact]
    public void BranchToTheFirstInstruction_LandsAfterTheZeroInit()
    {
        // The typed-zero prologue must run once: a back-edge to instruction 0 skips over it.
        var module = new ModuleDefUser("T", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        new AssemblyDefUser("T", new Version(1, 0, 0, 0)).Modules.Add(module);
        var type = new TypeDefUser("N", "Lib", module.CorLibTypes.Object.ToTypeDefOrRef());
        module.Types.Add(type);
        var method = new MethodDefUser(
            "Spin",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody { InitLocals = true };
        method.Body.Variables.Add(new Local(module.CorLibTypes.String));
        var first = Instruction.Create(OpCodes.Ldarg_0);
        method.Body.Instructions.Add(first);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, first));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        Obfy.Core.Virtualization.VmEncoder.TryEncode(
                method, new Dictionary<MethodDef, int>(), new Obfy.Core.Virtualization.VmMemberTables(),
                out var code, out var reason)
            .ShouldBeTrue(reason);
        code.Take(3).ToArray().ShouldBe(new[]
        {
            (byte)Obfy.Core.Virtualization.VmOp.Ldnull,
            (byte)Obfy.Core.Virtualization.VmOp.Stloc,
            (byte)0
        });
        code[3].ShouldBe((byte)Obfy.Core.Virtualization.VmOp.Ldarg);
        code[5].ShouldBe((byte)Obfy.Core.Virtualization.VmOp.Brfalse);
        BitConverter.ToUInt16(code, 6).ShouldBe((ushort)3, "the branch target is the first real instruction, after the prologue");
    }

    private static object? Run(string source, string method, int dropStoreOfLocal, params object?[] args)
    {
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Debug);
        var def = VmFixHarness.Find(module, "Lib", method);
        if (dropStoreOfLocal >= 0)
            DropFirstStore(def, dropStoreOfLocal);

        return VmFixHarness.VirtualizeAndRun(module, "Lib", new[] { method }, assembly =>
            VmExecuteHarness.GetType(assembly, "Lib").GetMethod(method)!.Invoke(null, args));
    }

    /// <summary>
    /// Turns the first <c>stloc</c> into a <c>pop</c>: stack-neutral, and the local is then read
    /// while still holding its CLR zero value.
    /// </summary>
    private static void DropFirstStore(MethodDef method, int localIndex)
    {
        var store = method.Body.Instructions.First(i =>
            i.IsStloc() && i.GetLocal(method.Body.Variables)?.Index == localIndex);
        store.OpCode = OpCodes.Pop;
        store.Operand = null;
        method.Body.UpdateInstructionOffsets();
    }
}

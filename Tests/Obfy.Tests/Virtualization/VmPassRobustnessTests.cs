using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Obfy.Core.Virtualization;
using Shouldly;

namespace Obfy.Tests.Virtualization;

/// <summary>
/// The pass must degrade by skipping a method, never by failing the whole assembly: a limit that
/// one candidate hits (member table, u16 branch offsets) is that candidate's problem only.
/// </summary>
public class VmPassRobustnessTests
{
    [Fact]
    public void TryEncode_MethodThatOverflowsTheMemberTable_IsSkippedAndRolledBack()
    {
        var (module, type) = NewModule();
        var spam = AddSpamMethod(module, type, "Spam", 0, 66_000);
        var tables = new VmMemberTables();

        VmEncoder.TryEncode(spam, new Dictionary<MethodDef, int>(), tables, out _, out var reason)
            .ShouldBeFalse();
        reason.ShouldBe(VmSkipReasons.IndexOutOfRange);
        tables.Methods.ShouldBeEmpty("a skipped method must not leave members behind");
    }

    [Fact]
    public async Task Pass_ManyCandidatesWithDistinctMembers_DoesNotOverflowTheDiagnosticTable()
    {
        // With MaxMethods=1 every other candidate goes through the diagnostic TryEncode. They used to
        // share one table, so their distinct members together passed 65,535 and failed the pass.
        var (module, type) = NewModule();
        var good = AddGoodMethod(module, type);
        for (var i = 0; i < 70; i++)
            AddSpamMethod(module, type, "Spam" + i, i, 1000);

        var (result, context) = await RunPass(module, maxMethods: 1);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.ProtectionsApplied.ShouldBe(1);
        CallsRun(good).ShouldBeTrue();
        context.Warnings.ShouldContain(w => w.Contains("maxMethods=1"));
        context.SkippedItems.ShouldBeEmpty("every spam method encodes; none may be reported as skipped");
    }

    [Fact]
    public async Task Pass_MethodThatFitsOnlyWithoutCallVm_IsSkippedNotFatal()
    {
        // Select encodes without ids (calls are 3 bytes); the final encode uses CallVm (4 bytes), so a
        // method near the u16 branch limit can pass Select and then fail the final encode.
        var (module, callee, huge) = NewModuleWithNearLimitMethod();

        // Sanity: it does encode on its own (3-byte calls), which is how Select admits it.
        VmEncoder.TryEncode(huge, new Dictionary<MethodDef, int>(), new VmMemberTables(), out _, out var plain)
            .ShouldBeTrue(plain);
        VmEncoder.TryEncode(huge, new Dictionary<MethodDef, int> { [callee] = 1 }, new VmMemberTables(), out _, out var withIds)
            .ShouldBeFalse();
        withIds.ShouldBe(VmSkipReasons.IndexOutOfRange);

        var (result, context) = await RunPass(module, maxMethods: 32);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.ProtectionsApplied.ShouldBe(1);
        CallsRun(callee).ShouldBeTrue();
        CallsRun(huge).ShouldBeFalse();
        huge.Body.Instructions.Count.ShouldBe(20_003, "the skipped method keeps its original body");
        context.SkippedItems.ShouldContain(s => s.ItemName.Contains("::A") && s.Details == VmSkipReasons.IndexOutOfRange);
    }

    [Fact]
    public void SelectSet_MatchesTheMethodsThePassActuallyVirtualizes()
    {
        // String/constant encryption leave the methods in this set untouched so the VM can encode
        // them; a method that the final encode drops must not be among them.
        var (module, callee, huge) = NewModuleWithNearLimitMethod();
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            Virtualization = { Enabled = true }
        });

        var set = VirtualizationObfuscator.SelectSet(context);

        set.ShouldContain(callee);
        set.ShouldNotContain(huge);
    }

    private static (ModuleDef Module, MethodDef Callee, MethodDef Huge) NewModuleWithNearLimitMethod()
    {
        var (module, type) = NewModule();
        var callee = new MethodDefUser(
            "B",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        callee.Body = new CilBody();
        callee.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(callee);

        // 8 bytes of prologue + 20,000 calls: 60,008 with 3-byte Call, 80,008 with 4-byte CallVm.
        var huge = new MethodDefUser(
            "A",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        huge.Body = new CilBody();
        var end = Instruction.Create(OpCodes.Ret);
        huge.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        huge.Body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, end));
        for (var i = 0; i < 20_000; i++)
            huge.Body.Instructions.Add(Instruction.Create(OpCodes.Call, callee));
        huge.Body.Instructions.Add(end);
        type.Methods.Add(huge);
        return (module, callee, huge);
    }

    [Fact]
    public async Task Pass_InitOnlySetter_KeepsTheVmWorking()
    {
        // void modreq(IsExternalInit) must not reach an ldtoken in the VM static constructor.
        const string source = """
            public class Person {
                public int Age { get; init; }
                public string Name { get; init; }
            }
            public static class Lib {
                public static string Describe() {
                    var p = new Person { Age = 5, Name = "n" };
                    return p.Name + p.Age.ToString();
                }
                public static int Age() { return new Person { Age = 7 }.Age; }
            }
            """;
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);

        var (result, _) = await RunPass(module, maxMethods: 32);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0);

        var cctor = module.Types.First(t => t.Name == "Vm").FindStaticConstructor()!;
        cctor.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldtoken && i.Operand is TypeSpec { TypeSig: ModifierSig })
            .ShouldBeEmpty("a return type with a custom modifier must not be loaded with ldtoken");

        var outcome = VmExecuteHarness.Invoke(module, assembly =>
        {
            var lib = VmExecuteHarness.GetType(assembly, "Lib");
            return lib.GetMethod("Age")!.Invoke(null, null);
        });
        outcome.ShouldBe(7);
    }

    [Fact]
    public async Task Pass_RunTwiceOnTheSameModule_DoesNotDuplicateTheVmTypeName()
    {
        // Re-protecting output whose helper types were not renamed used to add a second TypeDef with
        // the identical name Obfy.Runtime.Vm, which ECMA-335 forbids and tools reject.
        const string source = """
            public static class Lib {
                public static int Add(int a, int b) => a + b;
                public static int Mul(int a, int b) => a * b;
            }
            """;
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);

        (await RunPass(module, maxMethods: 32)).Result.Success.ShouldBeTrue();
        (await RunPass(module, maxMethods: 32)).Result.Success.ShouldBeTrue();

        var vms = module.Types.Where(t => t.Name == "Vm").ToList();
        vms.Count.ShouldBe(2);
        vms.Select(t => t.FullName).Distinct(StringComparer.Ordinal).Count().ShouldBe(2, "type names must stay unique");
        vms.ShouldAllBe(t => ObfuscatorHelpers.IsRuntimeHelper(t), "both stay recognizable as runtime helpers");

        VmExecuteHarness.Invoke(module, assembly =>
                VmExecuteHarness.GetType(assembly, "Lib").GetMethod("Add")!.Invoke(null, new object[] { 2, 3 }))
            .ShouldBe(5);
    }

    private static async Task<(ObfuscationResult Result, PipelineContext Context)> RunPass(ModuleDef module, int maxMethods)
    {
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            Virtualization = { Enabled = true, MaxMethods = maxMethods }
        };
        var context = PipelineContext.ForAssembly(module, settings);
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);
        return (result, context);
    }

    private static (ModuleDef Module, TypeDef Type) NewModule()
    {
        var module = new ModuleDefUser("T", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        new AssemblyDefUser("T", new Version(1, 0, 0, 0)).Modules.Add(module);
        var type = new TypeDefUser("N", "Lib", module.CorLibTypes.Object.ToTypeDefOrRef())
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(type);
        return (module, type);
    }

    private static MethodDef AddGoodMethod(ModuleDef module, TypeDef type)
    {
        // Returns Int32 so it sorts before the Void spam methods and is the one Select keeps.
        var method = new MethodDefUser(
            "Good",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);
        return method;
    }

    /// <summary>A method of <paramref name="calls"/> calls, each to a distinct member reference.</summary>
    private static MethodDef AddSpamMethod(ModuleDef module, TypeDef type, string name, int serial, int calls)
    {
        var objectRef = module.CorLibTypes.Object.TypeDefOrRef;
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        for (var i = 0; i < calls; i++)
        {
            var target = new MemberRefUser(
                module, $"M{serial}_{i}", MethodSig.CreateStatic(module.CorLibTypes.Void), objectRef);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, target));
        }

        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);
        return method;
    }

    private static bool CallsRun(MethodDef method) =>
        method.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "Run");
}

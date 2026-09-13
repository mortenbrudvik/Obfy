using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Shouldly;

namespace Obfy.Tests;

public class VirtualizationObfuscatorTests
{
    [Fact]
    public async Task Virtualization_ReplacesSimpleAddWithExecuteStub()
    {
        var (module, add) = CreateModuleWithMethod("Add", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        var result = await RunAsync(module);

        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(1);
        module.Types.ShouldContain(t => t.Name == "<Vm>");
        CallsExecute(add).ShouldBeTrue();
        var execute = module.Types.First(t => t.Name == "<Vm>").FindMethod("Execute");
        execute.ShouldNotBeNull();
        execute!.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Throw);
        execute.Body.Instructions.ShouldNotContain(i => i.OpCode == OpCodes.Ldnull);
    }

    [Fact]
    public async Task Virtualization_DoesNotEncodeUnsignedCompare()
    {
        var (module, method) = CreateModuleWithMethod("IsNonZero", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Cgt_Un));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        var context = PipelineContext.ForAssembly(module, VmSettings());
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        CallsExecute(method).ShouldBeFalse();
        context.SkippedItems.ShouldContain(s =>
            s.ItemName.Contains("IsNonZero") && s.Details == "unsigned compare");
        context.Warnings.ShouldContain(w => w.Contains("no eligible methods"));
    }

    [Fact]
    public async Task Virtualization_DoesNotEncodeUnsignedBranch()
    {
        var (module, method) = CreateModuleWithMethod("IfUnsignedGt", body =>
        {
            var ret1 = Instruction.Create(OpCodes.Ldc_I4_1);
            var ret0 = Instruction.Create(OpCodes.Ldc_I4_0);
            var end = Instruction.Create(OpCodes.Ret);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Bgt_Un, ret1));
            body.Instructions.Add(Instruction.Create(OpCodes.Br, ret0));
            body.Instructions.Add(ret1);
            body.Instructions.Add(Instruction.Create(OpCodes.Br, end));
            body.Instructions.Add(ret0);
            body.Instructions.Add(end);
        });

        var result = await RunAsync(module);

        result.Success.ShouldBeTrue();
        CallsExecute(method).ShouldBeFalse();
        result.Statistics.ProtectionsApplied.ShouldBe(0);
    }

    [Fact]
    public async Task Virtualization_DoesNotEncodeNonIntLocal()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = CreateInt32Method(type, "WithLong", 0);
        var body = method.Body;
        body.Variables.Add(new Local(module.CorLibTypes.Int64));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        var context = PipelineContext.ForAssembly(module, VmSettings());
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        CallsExecute(method).ShouldBeFalse();
        context.SkippedItems.ShouldContain(s =>
            s.ItemName.Contains("WithLong") && s.Details == "non-int local");
    }

    [Fact]
    public async Task Virtualization_DoesNotEncodeUnsupportedOpcode()
    {
        var (module, method) = CreateModuleWithMethod("Div", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Div));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        var context = PipelineContext.ForAssembly(module, VmSettings());
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        CallsExecute(method).ShouldBeFalse();
        context.SkippedItems.ShouldContain(s =>
            s.ItemName.Contains("Div") && s.Details == "unsupported opcode");
    }

    [Fact]
    public async Task Virtualization_WarnsWhenMaxMethodsReached()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var first = CreateAdd(type, "A");
        var second = CreateAdd(type, "B");
        var settings = VmSettings();
        settings.Virtualization.MaxMethods = 1;
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(1);
        CallsExecute(first).ShouldBeTrue();
        CallsExecute(second).ShouldBeFalse();
        context.Warnings.ShouldContain(w => w.Contains("maxMethods=1"));
    }

    [Fact]
    public async Task Virtualization_HonorsMethodExclusion()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var skip = CreateAdd(type, "SkipMe");
        var keep = CreateAdd(type, "KeepMe");
        var settings = VmSettings();
        settings.Exclusions.Methods.Add("SkipMe");
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        CallsExecute(skip).ShouldBeFalse();
        CallsExecute(keep).ShouldBeTrue();
    }

    [Fact]
    public async Task Virtualization_LeavesIneligibleInstanceMethodUnchanged_AndSucceeds()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = new MethodDefUser(
            "Inst",
            MethodSig.CreateInstance(module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_7));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);
        var snapshot = method.Body.Instructions.Select(i => i.OpCode.Code).ToArray();

        var context = PipelineContext.ForAssembly(module, VmSettings());
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(0);
        CallsExecute(method).ShouldBeFalse();
        method.Body.Instructions.Select(i => i.OpCode.Code).ShouldBe(snapshot);
        context.Warnings.ShouldContain(w => w.Contains("no eligible methods"));
    }

    [Fact]
    public async Task Virtualization_EncodesLdcI4SubAndMul()
    {
        var (module, method) = CreateModuleWithMethod("SubMul", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Mul));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 1));
            body.Instructions.Add(Instruction.Create(OpCodes.Sub));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        var result = await RunAsync(module);

        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(1);
        CallsExecute(method).ShouldBeTrue();
    }

    [Fact]
    public async Task Virtualization_EncodesSignedCompareBranches()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = CreateInt32Method(type, "IfBlt", 2);
        var taken = Instruction.Create(OpCodes.Ldc_I4_1);
        var end = Instruction.Create(OpCodes.Ret);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Blt, taken));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Br, end));
        method.Body.Instructions.Add(taken);
        method.Body.Instructions.Add(end);

        var result = await RunAsync(module);

        result.Success.ShouldBeTrue();
        CallsExecute(method).ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(1);
    }

    private static async Task<ObfuscationResult> RunAsync(ModuleDef module)
    {
        var obfuscator = new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object);
        return await obfuscator.ObfuscateAsync(PipelineContext.ForAssembly(module, VmSettings()));
    }

    private static ObfySettings VmSettings() => new()
    {
        Level = ObfuscationLevel.Custom,
        Virtualization = { Enabled = true }
    };

    private static (ModuleDef Module, MethodDef Method) CreateModuleWithMethod(
        string name, Action<CilBody> fill, int paramCount = 2)
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = CreateInt32Method(type, name, paramCount);
        fill(method.Body);
        return (module, method);
    }

    private static MethodDef CreateAdd(TypeDef type, string name)
    {
        var method = CreateInt32Method(type, name, 2);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Add));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        return method;
    }

    private static bool CallsExecute(MethodDef method) =>
        method.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "Execute");

    private static ModuleDef CreateTestModule()
    {
        var module = new ModuleDefUser("TestAssembly", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var assembly = new AssemblyDefUser("TestAssembly", new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);
        return module;
    }

    private static TypeDef CreateTestType(ModuleDef module)
    {
        var typeDef = new TypeDefUser("TestNamespace", "Lib", module.CorLibTypes.Object.ToTypeDefOrRef())
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(typeDef);
        return typeDef;
    }

    private static MethodDef CreateInt32Method(TypeDef type, string name, int paramCount)
    {
        var types = Enumerable.Repeat(type.Module.CorLibTypes.Int32, paramCount).ToArray();
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(type.Module.CorLibTypes.Int32, types),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        type.Methods.Add(method);
        return method;
    }
}

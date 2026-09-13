using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class RuntimeInjectionTests
{
    [Fact]
    public void ShouldFlattenControlFlow_RegisteredInterpreter_IsFalseEvenIfNotNamedVm()
    {
        var module = new ModuleDefUser("t");
        var context = PipelineContext.ForAssembly(module, new ObfySettings());
        var helper = new TypeDefUser("Obfy.Runtime", "Interpreter", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(helper);
        RuntimeInjection.Register(context, helper, RuntimeHelperOptions.Interpreter);

        RuntimeInjection.ShouldFlattenControlFlow(context, helper).ShouldBeFalse();
        ObfuscatorHelpers.SkipControlFlowFlattening(helper).ShouldBeFalse();
    }

    [Fact]
    public void ShouldFlattenControlFlow_NestedTypeOfInterpreter_IsFalse()
    {
        var module = new ModuleDefUser("t");
        var context = PipelineContext.ForAssembly(module, new ObfySettings());
        var helper = new TypeDefUser("Obfy.Runtime", "Interpreter", module.CorLibTypes.Object.TypeDefOrRef);
        var nested = new TypeDefUser("Blob", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NestedPrivate
        };
        helper.NestedTypes.Add(nested);
        module.Types.Add(helper);
        RuntimeInjection.Register(context, helper, RuntimeHelperOptions.Interpreter);

        RuntimeInjection.ShouldFlattenControlFlow(context, nested).ShouldBeFalse();
    }

    [Fact]
    public void ShouldEncryptIl_DefaultHelper_IsFalse_InterpreterUnchanged()
    {
        var module = new ModuleDefUser("t");
        var context = PipelineContext.ForAssembly(module, new ObfySettings());
        var helper = new TypeDefUser("Obfy.Runtime", "<StringDecryptor>", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(helper);
        RuntimeInjection.Register(context, helper);

        RuntimeInjection.ShouldEncryptIl(context, helper).ShouldBeFalse();
        RuntimeInjection.ShouldRename(context, helper).ShouldBeTrue();
        RuntimeInjection.ShouldFlattenControlFlow(context, helper).ShouldBeTrue();
    }

    [Fact]
    public void ShouldEncryptIl_OptInHelper_IsTrue()
    {
        var module = new ModuleDefUser("t");
        var context = PipelineContext.ForAssembly(module, new ObfySettings());
        var helper = new TypeDefUser("Obfy.Runtime", "<OptIn>", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(helper);
        RuntimeInjection.Register(context, helper, new RuntimeHelperOptions { EncryptIl = true });

        RuntimeInjection.ShouldEncryptIl(context, helper).ShouldBeTrue();
    }

    [Fact]
    public async Task ControlFlow_DoesNotFlattenRegisteredInterpreter()
    {
        var module = new ModuleDefUser("t");
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        });

        var helper = new TypeDefUser("Obfy.Runtime", "NotVm", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract
        };
        var method = new MethodDefUser(
            "Run",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Add));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Mul));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        helper.Methods.Add(method);
        module.Types.Add(helper);
        RuntimeInjection.Register(context, helper, RuntimeHelperOptions.Interpreter);

        var result = await new ControlFlowObfuscator(new Mock<ILogger<ControlFlowObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        method.Body.Instructions.ShouldNotContain(i => i.OpCode == OpCodes.Switch);
    }

    [Fact]
    public async Task Virtualization_RegistersInterpreterOptions()
    {
        var module = new ModuleDefUser("t");
        var type = new TypeDefUser("App", "C", module.CorLibTypes.Object.TypeDefOrRef);
        var method = new MethodDefUser(
            "Add",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.Int32, module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.ParamDefs.Add(new ParamDefUser("a", 1));
        method.ParamDefs.Add(new ParamDefUser("b", 2));
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Add));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);
        module.Types.Add(type);

        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            Virtualization = { Enabled = true }
        });
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        var vm = module.Types.Single(t => t.Name == "<Vm>");
        context.InjectedHelpers.ContainsKey(vm).ShouldBeTrue();
        RuntimeInjection.ShouldFlattenControlFlow(context, vm).ShouldBeFalse();
    }
}

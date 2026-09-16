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
    private static PipelineContext ContextWithModule(out ModuleDef module)
    {
        module = new ModuleDefUser("t");
        var assembly = new AssemblyDefUser("t");
        assembly.Modules.Add(module);
        return PipelineContext.ForAssembly(module, new ObfySettings());
    }

    private static TypeDef AddHelper(ModuleDef module, string name, string ns = "Obfy.Runtime")
    {
        var helper = new TypeDefUser(ns, name, module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract
        };
        module.Types.Add(helper);
        return helper;
    }

    private static MethodDef AddIntMethod(TypeDef type, string name)
    {
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(type.Module.CorLibTypes.Int32, type.Module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Add));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Mul));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);
        return method;
    }

    [Fact]
    public void ShouldFlattenControlFlow_RegisteredInterpreter_IsFalseEvenIfNotNamedVm()
    {
        var context = ContextWithModule(out var module);
        var helper = AddHelper(module, "Interpreter");
        RuntimeInjection.Register(context, helper, RuntimeHelperOptions.Interpreter);

        RuntimeInjection.ShouldFlattenControlFlow(context, helper).ShouldBeFalse();
    }

    [Fact]
    public void ShouldFlattenControlFlow_NestedTypeOfInterpreter_IsFalse()
    {
        var context = ContextWithModule(out var module);
        var helper = AddHelper(module, "Interpreter");
        var nested = new TypeDefUser("Blob", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NestedPrivate
        };
        helper.NestedTypes.Add(nested);
        RuntimeInjection.Register(context, helper, RuntimeHelperOptions.Interpreter);

        RuntimeInjection.ShouldFlattenControlFlow(context, nested).ShouldBeFalse();
        RuntimeInjection.ShouldEncryptIl(context, nested).ShouldBeFalse();
        RuntimeInjection.ShouldRename(context, nested).ShouldBeTrue();
    }

    [Fact]
    public void ShouldEncryptIl_DefaultHelper_IsFalse()
    {
        var context = ContextWithModule(out var module);
        var helper = AddHelper(module, "<StringDecryptor>");
        RuntimeInjection.Register(context, helper);

        RuntimeInjection.ShouldEncryptIl(context, helper).ShouldBeFalse();
        RuntimeInjection.ShouldRename(context, helper).ShouldBeTrue();
        RuntimeInjection.ShouldFlattenControlFlow(context, helper).ShouldBeTrue();
    }

    [Fact]
    public void ShouldEncryptIl_OptInHelper_IsTrue()
    {
        var context = ContextWithModule(out var module);
        var helper = AddHelper(module, "<OptIn>");
        RuntimeInjection.Register(context, helper, new RuntimeHelperOptions { EncryptIl = true });

        RuntimeInjection.ShouldEncryptIl(context, helper).ShouldBeTrue();
    }

    [Fact]
    public void ShouldEncryptIl_NestedTypeOfRegisteredHelper_InheritsFalse()
    {
        var context = ContextWithModule(out var module);
        var helper = AddHelper(module, "<Parent>");
        var nested = new TypeDefUser("Blob", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NestedPrivate
        };
        helper.NestedTypes.Add(nested);
        RuntimeInjection.Register(context, helper);

        RuntimeInjection.ShouldEncryptIl(context, nested).ShouldBeFalse();
        RuntimeInjection.ShouldRename(context, nested).ShouldBeTrue();
    }

    [Fact]
    public void UnregisteredRuntimeHelper_DoesNotFlattenOrEncrypt_EvenIfNamedVm()
    {
        var context = ContextWithModule(out var module);
        var vm = AddHelper(module, "<Vm>");
        var nested = new TypeDefUser("D", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NestedPrivate
        };
        vm.NestedTypes.Add(nested);
        var other = AddHelper(module, "Other");

        RuntimeInjection.ShouldFlattenControlFlow(context, vm).ShouldBeFalse();
        RuntimeInjection.ShouldEncryptIl(context, vm).ShouldBeFalse();
        RuntimeInjection.ShouldFlattenControlFlow(context, nested).ShouldBeFalse();
        RuntimeInjection.ShouldEncryptIl(context, nested).ShouldBeFalse();
        RuntimeInjection.ShouldFlattenControlFlow(context, other).ShouldBeFalse();
        RuntimeInjection.ShouldEncryptIl(context, other).ShouldBeFalse();
    }

    [Fact]
    public void UserTypeNamedVm_StillFlattensAndEncrypts()
    {
        var context = ContextWithModule(out var module);
        var user = AddHelper(module, "<Vm>", ns: "App");

        RuntimeInjection.ShouldFlattenControlFlow(context, user).ShouldBeTrue();
        RuntimeInjection.ShouldEncryptIl(context, user).ShouldBeTrue();
        RuntimeInjection.ShouldRename(context, user).ShouldBeTrue();
    }

    [Fact]
    public void ShouldRename_PinnedAttribute_IsFalse()
    {
        var context = ContextWithModule(out var module);
        var watermark = new TypeDefUser(
            ObfuscatorHelpers.PinnedAttributeNames.WatermarkNamespace,
            ObfuscatorHelpers.PinnedAttributeNames.Watermark,
            module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(watermark);
        var confused = new TypeDefUser("", ObfuscatorHelpers.PinnedAttributeNames.ConfusedBy,
            module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(confused);
        var userNamedWatermark = new TypeDefUser("App", ObfuscatorHelpers.PinnedAttributeNames.Watermark,
            module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(userNamedWatermark);

        RuntimeInjection.ShouldRename(context, watermark).ShouldBeFalse();
        RuntimeInjection.ShouldRename(context, confused).ShouldBeFalse();
        RuntimeInjection.ShouldRename(context, userNamedWatermark).ShouldBeTrue();
    }

    [Fact]
    public void Register_SamePolicy_IsIdempotent()
    {
        var context = ContextWithModule(out var module);
        var helper = AddHelper(module, "H");
        RuntimeInjection.Register(context, helper, RuntimeHelperOptions.Interpreter);
        RuntimeInjection.Register(context, helper, RuntimeHelperOptions.Interpreter);
        context.InjectedHelpers[helper].ShouldBe(RuntimeHelperOptions.Interpreter);
    }

    [Fact]
    public void Register_ConflictingPolicy_Throws()
    {
        var context = ContextWithModule(out var module);
        var helper = AddHelper(module, "H");
        RuntimeInjection.Register(context, helper, RuntimeHelperOptions.Interpreter);
        var ex = Should.Throw<InvalidOperationException>(() =>
            RuntimeInjection.Register(context, helper, RuntimeHelperOptions.Default));
        ex.Message.ShouldContain("already registered");
    }

    [Fact]
    public void AddType_AddsToModuleAndRegistry()
    {
        var context = ContextWithModule(out var module);
        var helper = new TypeDefUser("Obfy.Runtime", "<X>", module.CorLibTypes.Object.TypeDefOrRef);
        RuntimeInjection.AddType(context, helper, RuntimeHelperOptions.Pinned);

        module.Types.ShouldContain(helper);
        context.InjectedHelpers[helper].ShouldBe(RuntimeHelperOptions.Pinned);
    }

    [Fact]
    public void PrependModuleInitializerCall_InsertsCallAtZero()
    {
        var module = new ModuleDefUser("t");
        var type = AddHelper(module, "H");
        var target = AddIntMethod(type, "Go");
        RuntimeInjection.PrependModuleInitializerCall(module, target);

        var cctor = module.GlobalType.FindStaticConstructor();
        cctor.ShouldNotBeNull();
        cctor!.Body.Instructions[0].OpCode.ShouldBe(OpCodes.Call);
        ((IMethod)cctor.Body.Instructions[0].Operand!).Name.String.ShouldBe("Go");
    }

    [Fact]
    public void PrependModuleInitializerCall_NoIlBody_Throws()
    {
        var module = new ModuleDefUser("t");
        var global = module.GlobalType ?? new TypeDefUser("", "<Module>", null)
        {
            Attributes = TypeAttributes.NotPublic
        };
        if (module.GlobalType is null)
            module.Types.Insert(0, global);

        foreach (var existing in global.Methods.Where(m => m.IsStaticConstructor || m.Name == ".cctor").ToList())
            global.Methods.Remove(existing);

        global.Methods.Add(new MethodDefUser(
            ".cctor",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName));

        var type = AddHelper(module, "H");
        var target = AddIntMethod(type, "Go");
        var ex = Should.Throw<InvalidOperationException>(() =>
            RuntimeInjection.PrependModuleInitializerCall(module, target));
        ex.Message.ShouldContain("no IL body");
    }

    [Fact]
    public async Task ControlFlow_DoesNotFlattenRegisteredInterpreter()
    {
        var context = ContextWithModule(out var module);
        context.Settings.ControlFlow.Enabled = true;
        context.Settings.ControlFlow.Mode = ControlFlowMode.Switch;
        context.Settings.ControlFlow.Intensity = 100;

        var helper = AddHelper(module, "NotVm");
        var method = AddIntMethod(helper, "Run");
        RuntimeInjection.Register(context, helper, RuntimeHelperOptions.Interpreter);

        var result = await new ControlFlowObfuscator(new Mock<ILogger<ControlFlowObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        method.Body.Instructions.Select(i => i.OpCode).ToArray().ShouldBe(new[]
        {
            OpCodes.Ldarg_0, OpCodes.Ldc_I4_1, OpCodes.Add, OpCodes.Ldc_I4_2, OpCodes.Mul, OpCodes.Ret
        });
    }

    [Fact]
    public async Task Virtualization_RegistersInterpreterOptions()
    {
        var context = ContextWithModule(out var module);
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

        context.Settings.Virtualization.Enabled = true;
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        var vm = module.Types.Single(t => t.Name == "Vm");
        context.InjectedHelpers.ContainsKey(vm).ShouldBeTrue();
        context.InjectedHelpers[vm].ShouldBe(new RuntimeHelperOptions
        {
            FlattenControlFlow = true,
            Rename = true,
            EncryptIl = false
        });
        RuntimeInjection.ShouldFlattenControlFlow(context, vm).ShouldBeTrue();
        RuntimeInjection.ShouldEncryptIl(context, vm).ShouldBeFalse();
        RuntimeInjection.ShouldRename(context, vm).ShouldBeTrue();
        vm.FindMethod("Run").ShouldNotBeNull();
        vm.FindMethod("Execute").ShouldBeNull();
        var nested = vm.NestedTypes.FirstOrDefault();
        if (nested is not null)
        {
            RuntimeInjection.ShouldFlattenControlFlow(context, nested).ShouldBeTrue();
            RuntimeInjection.ShouldEncryptIl(context, nested).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task MethodEncryption_SkipsUnregisteredAndRegisteredHelpers()
    {
        var context = ContextWithModule(out var module);
        context.Settings.Protection.MethodEncryption = true;

        var user = new TypeDefUser("App", "Work", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(user);
        var userMethod = AddIntMethod(user, "Go");

        var unregistered = AddHelper(module, "<Unreg>");
        var unregisteredMethod = AddIntMethod(unregistered, "H");

        var registered = AddHelper(module, "<Reg>");
        var registeredMethod = AddIntMethod(registered, "H");
        RuntimeInjection.Register(context, registered);

        var nestedParent = AddHelper(module, "<Parent>");
        var nested = new TypeDefUser("Blob", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NestedPrivate
        };
        nestedParent.NestedTypes.Add(nested);
        var nestedMethod = AddIntMethod(nested, "N");
        RuntimeInjection.Register(context, nestedParent);

        var result = await new MethodEncryptionObfuscator(new Mock<ILogger<MethodEncryptionObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        var names = context.MethodEncryptionMetadata!.Entries.Select(e => e.Method).ToList();
        names.ShouldContain(userMethod);
        names.ShouldNotContain(unregisteredMethod);
        names.ShouldNotContain(registeredMethod);
        names.ShouldNotContain(nestedMethod);
        context.InjectedHelpers.Keys.ShouldContain(t => t.Name == "<MethodCrypt>");
    }

    [Fact]
    public async Task AntiDump_RegistersHelper()
    {
        var context = ContextWithModule(out var module);
        context.Settings.Protection.AntiDump = true;
        var result = await new AntiDumpObfuscator(new Mock<ILogger<AntiDumpObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        context.InjectedHelpers.Keys.ShouldContain(t => t.Name == "<AntiDump>");
    }

    [Fact]
    public async Task StringEncryption_RegistersHelper()
    {
        var context = ContextWithModule(out var module);
        context.Settings.StringEncryption.Enabled = true;
        var type = new TypeDefUser("App", "G", module.CorLibTypes.Object.TypeDefOrRef);
        var method = new MethodDefUser(
            "Hi",
            MethodSig.CreateStatic(module.CorLibTypes.String),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "hello"));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);
        module.Types.Add(type);

        var result = await new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        context.InjectedHelpers.Keys.ShouldContain(t => t.Namespace == "Obfy.Runtime");
    }

    [Fact]
    public async Task Watermark_RegistersPinnedOnInjectAndReuse()
    {
        var context = ContextWithModule(out var module);
        context.Settings.Watermark.Enabled = true;
        context.Settings.Watermark.Id = "id-1";
        var watermark = new WatermarkObfuscator(new Mock<ILogger<WatermarkObfuscator>>().Object);

        (await watermark.ObfuscateAsync(context)).Success.ShouldBeTrue();
        var attr = module.Types.Single(t => t.Name == WatermarkObfuscator.AttributeTypeName);
        context.InjectedHelpers[attr].ShouldBe(RuntimeHelperOptions.Pinned);

        var reuse = PipelineContext.ForAssembly(module, context.Settings);
        reuse.Settings.Watermark.Id = "id-1";
        (await watermark.ObfuscateAsync(reuse)).Success.ShouldBeTrue();
        reuse.InjectedHelpers.ContainsKey(attr).ShouldBeTrue();
        reuse.InjectedHelpers[attr].ShouldBe(RuntimeHelperOptions.Pinned);
    }

    [Fact]
    public async Task Watermark_RegistersPinnedWhenReusingExistingType()
    {
        var context = ContextWithModule(out var module);
        var attrType = new TypeDefUser(
            WatermarkObfuscator.AttributeNamespace,
            WatermarkObfuscator.AttributeTypeName,
            new TypeRefUser(module, "System", "Attribute", module.CorLibTypes.AssemblyRef))
        {
            Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit
        };
        var ctor = new MethodDefUser(
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String),
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        ctor.Body = new CilBody();
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        attrType.Methods.Add(ctor);
        module.Types.Add(attrType);

        context.Settings.Watermark.Enabled = true;
        context.Settings.Watermark.Id = "fresh";
        var result = await new WatermarkObfuscator(new Mock<ILogger<WatermarkObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        context.InjectedHelpers.ContainsKey(attrType).ShouldBeTrue();
        context.InjectedHelpers[attrType].ShouldBe(RuntimeHelperOptions.Pinned);
    }
}

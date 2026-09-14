using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Virtualization;
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
        module.Types.ShouldContain(t => t.Name == "Vm");
        CallsRun(add).ShouldBeTrue();
        add.Body.Instructions.ShouldNotContain(i => i.OpCode == OpCodes.Add);
        var run = module.Types.First(t => t.Name == "Vm").FindMethod("Run");
        run.ShouldNotBeNull();
        run!.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Throw);
    }

    [Fact]
    public async Task Virtualization_EncodesUnsignedCompare()
    {
        var (module, method) = CreateModuleWithMethod("IsNonZero", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Cgt_Un));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });

        var result = await RunAsync(module);

        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(1);
        CallsRun(method).ShouldBeTrue();
    }

    [Fact]
    public async Task Virtualization_EncodesUnsignedBranch()
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
        CallsRun(method).ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(1);
    }

    [Fact]
    public async Task Virtualization_SkipsCustomStructParam()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var structType = CreateCustomStruct(module, "Point");
        var method = new MethodDefUser(
            "TakesPoint",
            MethodSig.CreateStatic(module.CorLibTypes.Void, new ValueTypeSig(structType)),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        var context = PipelineContext.ForAssembly(module, VmSettings());
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        CallsRun(method).ShouldBeFalse();
        context.SkippedItems.ShouldContain(s =>
            s.ItemName.Contains("TakesPoint") && s.Details == VmSkipReasons.NonPrimitiveValuetypeLocal);
        context.Warnings.ShouldContain(w => w.Contains("no eligible methods"));
    }

    [Fact]
    public async Task Virtualization_SkipsNullableReturn()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var nullableRef = new TypeRefUser(module, "System", "Nullable`1", module.CorLibTypes.AssemblyRef);
        var nullableInt = new GenericInstSig(new ValueTypeSig(nullableRef), module.CorLibTypes.Int32);
        var method = new MethodDefUser(
            "Bar",
            MethodSig.CreateStatic(nullableInt, module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        var context = PipelineContext.ForAssembly(module, VmSettings());
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        CallsRun(method).ShouldBeFalse();
        context.SkippedItems.ShouldContain(s =>
            s.ItemName.Contains("Bar") && s.Details == VmSkipReasons.NonPrimitiveValuetypeLocal);
    }

    [Fact]
    public async Task Virtualization_DoesNotEncodeUnsupportedOpcode()
    {
        var (module, method) = CreateModuleWithMethod("Sw", body =>
        {
            var end = Instruction.Create(OpCodes.Ret);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Switch, new[] { end }));
            body.Instructions.Add(end);
        }, paramCount: 1);

        var context = PipelineContext.ForAssembly(module, VmSettings());
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        CallsRun(method).ShouldBeFalse();
        context.SkippedItems.ShouldContain(s =>
            s.ItemName.Contains("Sw") && s.Details == VmSkipReasons.Switch);
    }

    [Fact]
    public async Task Virtualization_FailsWhenMaxMethodsOutOfRange()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        CreateAdd(type, "A");
        var settings = VmSettings();
        settings.Virtualization.MaxMethods = 0;
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage!.ShouldContain("MaxMethods");
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
        CallsRun(first).ShouldBeTrue();
        CallsRun(second).ShouldBeFalse();
        context.Warnings.ShouldContain(w => w.Contains("maxMethods=1") && w.Contains("B"));
        context.SkippedItems.ShouldNotContain(s => s.ItemName.Contains("B"));
    }

    [Fact]
    public async Task Virtualization_DoesNotReportConstructorsAsSkippedItems()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        CreateAdd(type, "Add");
        var ctor = new MethodDefUser(
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        ctor.Body = new CilBody();
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(ctor);

        var context = PipelineContext.ForAssembly(module, VmSettings());
        var result = await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context);

        result.Success.ShouldBeTrue();
        context.SkippedItems.ShouldNotContain(s => s.Details == VmSkipReasons.Constructor);
        context.SkippedItems.ShouldNotContain(s => s.Details == VmSkipReasons.NoBody);
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
        CallsRun(skip).ShouldBeFalse();
        CallsRun(keep).ShouldBeTrue();
    }

    [Fact]
    public async Task Virtualization_HonorsObfuscationFeatureVirtualization()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var skip = CreateAdd(type, "SkipMe");
        var keep = CreateAdd(type, "KeepMe");
        AddObfuscationAttribute(skip, exclude: true, feature: "virtualization");

        var result = await RunAsync(module);

        result.Success.ShouldBeTrue();
        CallsRun(skip).ShouldBeFalse();
        CallsRun(keep).ShouldBeTrue();
    }

    [Theory]
    [InlineData("vm")]
    [InlineData("virtualize")]
    public async Task Virtualization_HonorsObfuscationFeatureAliases(string feature)
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var skip = CreateAdd(type, "SkipMe");
        var keep = CreateAdd(type, "KeepMe");
        AddObfuscationAttribute(skip, exclude: true, feature: feature);

        var result = await RunAsync(module);

        result.Success.ShouldBeTrue();
        CallsRun(skip).ShouldBeFalse();
        CallsRun(keep).ShouldBeTrue();
    }

    [Fact]
    public async Task Virtualization_EncodesInstanceMethod()
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

        var result = await RunAsync(module);

        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(1);
        CallsRun(method).ShouldBeTrue();
    }

    [Fact]
    public async Task Virtualization_EncodesBoolAndUIntReturn()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var flag = CreateTypedMethod(type, "Flag", module.CorLibTypes.Boolean, 0);
        flag.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        flag.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var id = CreateTypedMethod(type, "Id", module.CorLibTypes.UInt32, 1, module.CorLibTypes.UInt32);
        id.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        id.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        var result = await RunAsync(module);

        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(2);
        CallsRun(flag).ShouldBeTrue();
        CallsRun(id).ShouldBeTrue();
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
        CallsRun(method).ShouldBeTrue();
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
        CallsRun(method).ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(1);
    }

    [Fact]
    public async Task Virtualization_StubCallIsNotReferenceProxied()
    {
        var (module, add) = CreateModuleWithMethod("Add", body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });
        var settings = VmSettings();
        settings.Protection.ReferenceProxy = true;
        var context = PipelineContext.ForAssembly(module, settings);

        (await new VirtualizationObfuscator(new Mock<ILogger<VirtualizationObfuscator>>().Object)
            .ObfuscateAsync(context)).Success.ShouldBeTrue();
        (await new ReferenceProxyObfuscator(new Mock<ILogger<ReferenceProxyObfuscator>>().Object)
            .ObfuscateAsync(context)).Success.ShouldBeTrue();

        var call = add.Body.Instructions.First(i => i.OpCode == OpCodes.Call);
        var target = (IMethod)call.Operand!;
        target.Name.String.ShouldBe("Run");
        target.DeclaringType.Name.String.ShouldNotBe("<RefProxy>");
        target.Name.String.ShouldNotStartWith("P");
    }

    [Fact]
    public async Task StringEncryption_SkipsVirtualizationSelectSet()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var hi = CreateTypedMethod(type, "Hi", module.CorLibTypes.String, 0);
        hi.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "hello"));
        hi.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var settings = VmSettings();
        settings.StringEncryption.Enabled = true;
        settings.StringEncryption.MinStringLength = 3;
        var context = PipelineContext.ForAssembly(module, settings);

        (await new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object)
            .ObfuscateAsync(context)).Success.ShouldBeTrue();

        hi.Body.Instructions.ShouldContain(i =>
            i.OpCode == OpCodes.Ldstr && (string)i.Operand! == "hello");
    }

    [Fact]
    public async Task ConstantEncryption_SkipsVirtualizationSelectSet()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = CreateInt32Method(type, "Const", 0);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 42));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var settings = VmSettings();
        settings.ConstantEncryption.Enabled = true;
        var context = PipelineContext.ForAssembly(module, settings);

        (await new ConstantEncryptionObfuscator(new Mock<ILogger<ConstantEncryptionObfuscator>>().Object)
            .ObfuscateAsync(context)).Success.ShouldBeTrue();

        method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4 && Equals(i.Operand, 42))
            .ShouldBeTrue("selected method should keep ldc.i4 42");
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

    private static bool CallsRun(MethodDef method) =>
        method.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "Run");

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

    private static TypeDef CreateCustomStruct(ModuleDef module, string name)
    {
        var structType = new TypeDefUser(
            name,
            new TypeRefUser(module, "System", "ValueType", module.CorLibTypes.AssemblyRef))
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Sealed |
                         TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit
        };
        module.Types.Add(structType);
        return structType;
    }

    private static MethodDef CreateInt32Method(TypeDef type, string name, int paramCount)
    {
        var types = Enumerable.Repeat(type.Module.CorLibTypes.Int32, paramCount).ToArray();
        return CreateTypedMethod(type, name, type.Module.CorLibTypes.Int32, paramCount, types);
    }

    private static MethodDef CreateTypedMethod(
        TypeDef type, string name, TypeSig ret, int paramCount, params TypeSig[] paramTypes)
    {
        var types = paramTypes.Length > 0
            ? paramTypes
            : Enumerable.Repeat(type.Module.CorLibTypes.Int32, paramCount).ToArray();
        if (paramTypes.Length == 0 && paramCount == 0)
            types = Array.Empty<TypeSig>();
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(ret, types),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        type.Methods.Add(method);
        return method;
    }

    private static void AddObfuscationAttribute(
        IHasCustomAttribute target,
        bool exclude,
        string? feature = null)
    {
        var module = target switch
        {
            TypeDef t => t.Module,
            IMemberDef m => m.Module,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        var attrType = new TypeRefUser(module, "System.Reflection", "ObfuscationAttribute", module.CorLibTypes.AssemblyRef);
        var ctor = new MemberRefUser(module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), attrType);
        var attr = new CustomAttribute(ctor);
        attr.NamedArguments.Add(new CANamedArgument(
            false, module.CorLibTypes.Boolean, "Exclude", new CAArgument(module.CorLibTypes.Boolean, exclude)));
        if (feature != null)
            attr.NamedArguments.Add(new CANamedArgument(
                false, module.CorLibTypes.String, "Feature", new CAArgument(module.CorLibTypes.String, feature)));
        target.CustomAttributes.Add(attr);
    }
}

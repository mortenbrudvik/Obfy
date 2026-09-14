using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Obfy.Core.Virtualization;
using Shouldly;

namespace Obfy.Tests.Virtualization;

public class VmEncoderTests
{
    private static readonly IReadOnlyDictionary<MethodDef, int> EmptyIds =
        new Dictionary<MethodDef, int>();

    [Fact]
    public void TryEncode_SimpleAdd_WritesLdcPatternAndRet()
    {
        var encoded = Encode(body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });
        encoded.ShouldNotBeNull();
        encoded.Code[0].ShouldBe((byte)VmOp.Ldarg);
        encoded.Code[1].ShouldBe((byte)0);
        encoded.Code[2].ShouldBe((byte)VmOp.Ldarg);
        encoded.Code[3].ShouldBe((byte)1);
        encoded.Code[4].ShouldBe((byte)VmOp.Add);
        encoded.Code[5].ShouldBe((byte)VmOp.Ret);
    }

    [Fact]
    public void TryEncode_UnsignedCompare_Succeeds()
    {
        Encode(body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Cgt_Un));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }).Code[4].ShouldBe((byte)VmOp.CgtUn);
    }

    [Fact]
    public void TryEncode_InstanceMethod_Succeeds()
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

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
    }

    [Fact]
    public void TryEncode_Ldstr_WritesUtf8Payload()
    {
        var encoded = Encode(
            body =>
            {
                body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "hi"));
                body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            },
            paramCount: 0);
        encoded.Code[0].ShouldBe((byte)VmOp.Ldstr);
        BitConverter.ToUInt16(encoded.Code, 1).ShouldBe((ushort)2);
        Encoding.UTF8.GetString(encoded.Code, 3, 2).ShouldBe("hi");
    }

    [Fact]
    public void TryEncode_CallToVirtualizedStatic_WritesCallVm()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var inner = CreateInt32Method(type, "Inner", 1);
        inner.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        inner.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var caller = CreateInt32Method(type, "Caller", 0);
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Call, inner));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var ids = new Dictionary<MethodDef, int> { [inner] = 7 };

        VmEncoder.TryEncode(caller, ids, new VmMemberTables(), out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[0].ShouldBe((byte)VmOp.CallVm);
        BitConverter.ToUInt16(code, 1).ShouldBe((ushort)7);
        code[3].ShouldBe((byte)1);
    }

    [Fact]
    public void TryEncode_Callvirt_WritesCallNotCallVm()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var virt = new MethodDefUser(
            "M",
            MethodSig.CreateInstance(module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig);
        virt.Body = new CilBody();
        virt.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        virt.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(virt);

        var caller = new MethodDefUser(
            "Caller",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, new ClassSig(type)),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        caller.Body = new CilBody();
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, virt));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(caller);

        var ids = new Dictionary<MethodDef, int> { [virt] = 7 };
        VmEncoder.TryEncode(caller, ids, new VmMemberTables(), out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[2].ShouldBe((byte)VmOp.Callvirt);
        code.ShouldNotContain((byte)VmOp.CallVm);
    }

    [Fact]
    public void TryEncode_Switch_Skips()
    {
        EncodeSkip(body =>
        {
            var ret = Instruction.Create(OpCodes.Ret);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Switch, new[] { ret }));
            body.Instructions.Add(ret);
        }).ShouldBe(VmSkipReasons.Switch);
    }

    [Fact]
    public void TryEncode_Eh_Skips()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = CreateInt32Method(type, "WithEh", 0);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = method.Body.Instructions[0],
            TryEnd = method.Body.Instructions[1],
            HandlerStart = method.Body.Instructions[1],
            HandlerEnd = method.Body.Instructions[1]
        });

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.ExceptionHandlers);
    }

    [Fact]
    public void TryEncode_Generic_Skips()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = CreateInt32Method(type, "Gen", 0);
        method.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T"));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.Generic);
    }

    [Fact]
    public void TryEncode_Ctor_Skips()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var ctor = new MethodDefUser(
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.HideBySig |
            MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        ctor.Body = new CilBody();
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(ctor);

        VmEncoder.TryEncode(ctor, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.Constructor);
    }

    [Fact]
    public void TryEncode_ByRefParam_Skips()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = new MethodDefUser(
            "ByRef",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, new ByRefSig(module.CorLibTypes.Int32)),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.ByRef);
    }

    [Fact]
    public void TryEncode_SpanParam_UnresolvedTypeRef_SkipsByRef()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var spanRef = new TypeRefUser(module, "System", "Span`1", module.CorLibTypes.AssemblyRef);
        spanRef.ResolveTypeDef().ShouldBeNull();
        var spanInt = new GenericInstSig(new ValueTypeSig(spanRef), module.CorLibTypes.Int32);
        var method = new MethodDefUser(
            "TakesSpan",
            MethodSig.CreateStatic(module.CorLibTypes.Void, spanInt),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.ByRef);
    }

    [Fact]
    public void TryEncode_CustomStructLocal_Skips()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var structType = new TypeDefUser(
            "S",
            new TypeRefUser(module, "System", "ValueType", module.CorLibTypes.AssemblyRef))
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Sealed |
                         TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit
        };
        module.Types.Add(structType);
        var method = CreateInt32Method(type, "WithStruct", 0);
        method.Body.Variables.Add(new Local(new ValueTypeSig(structType)));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.NonPrimitiveValuetypeLocal);
    }

    [Fact]
    public void TryEncode_CustomStructParam_Static_Skips()
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

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.NonPrimitiveValuetypeLocal);
    }

    [Fact]
    public void TryEncode_CustomStructParam_Instance_Skips()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var structType = CreateCustomStruct(module, "Point");
        var method = new MethodDefUser(
            "TakesPoint",
            MethodSig.CreateInstance(module.CorLibTypes.Void, new ValueTypeSig(structType)),
            MethodImplAttributes.IL,
            MethodAttributes.Public);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.NonPrimitiveValuetypeLocal);
    }

    [Fact]
    public void TryEncode_CustomStructReturn_Skips()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var structType = CreateCustomStruct(module, "Point");
        var method = new MethodDefUser(
            "MakePoint",
            MethodSig.CreateStatic(new ValueTypeSig(structType)),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.NonPrimitiveValuetypeLocal);
    }

    [Fact]
    public void TryEncode_NullableIntReturn_Skips()
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

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.NonPrimitiveValuetypeLocal);
    }

    [Fact]
    public void TryEncode_ValuetypeNewobj_Skips()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var structType = new TypeDefUser(
            "S",
            new TypeRefUser(module, "System", "ValueType", module.CorLibTypes.AssemblyRef))
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Sealed |
                         TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit
        };
        module.Types.Add(structType);
        var ctor = new MethodDefUser(
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.HideBySig |
            MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        ctor.Body = new CilBody();
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        structType.Methods.Add(ctor);

        var method = CreateInt32Method(type, "Make", 0);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, ctor));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        skipReason.ShouldBe(VmSkipReasons.ValuetypeNewobj);
    }

    [Fact]
    public void TryEncode_UnsupportedOpcode_Skips()
    {
        EncodeSkip(body =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_8));
            body.Instructions.Add(Instruction.Create(OpCodes.Localloc));
            body.Instructions.Add(Instruction.Create(OpCodes.Pop));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }).ShouldBe(VmSkipReasons.UnsupportedOpcode);
    }

    [Fact]
    public void TryEncode_StackHeightMismatch_Skips()
    {
        EncodeSkip(body =>
        {
            var end = Instruction.Create(OpCodes.Ret);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, end));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Br, end));
            body.Instructions.Add(end);
        }, paramCount: 1).ShouldBe(VmSkipReasons.StackHeightMismatch);
    }

    [Fact]
    public void Select_IsDeterministicAndRespectsMaxMethods()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var c = CreateAdd(type, "C");
        var a = CreateAdd(type, "A");
        var b = CreateAdd(type, "B");

        var selected = VmEncoder.Select(new[] { c, a, b }, maxMethods: 2);

        selected.Count.ShouldBe(2);
        selected[0].Name.String.ShouldBe("A");
        selected[1].Name.String.ShouldBe("B");
    }

    [Fact]
    public void TryEncode_LdcI8_WritesFirstOpcode()
    {
        Encode(
            body =>
            {
                body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I8, 1L));
                body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            },
            paramCount: 0).Code[0].ShouldBe((byte)VmOp.LdcI8);
    }

    [Fact]
    public void TryEncode_LdcR4_WritesFirstOpcode()
    {
        Encode(
            body =>
            {
                body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4, 1f));
                body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            },
            paramCount: 0).Code[0].ShouldBe((byte)VmOp.LdcR4);
    }

    [Fact]
    public void TryEncode_LdcR8_WritesFirstOpcode()
    {
        Encode(
            body =>
            {
                body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R8, 1d));
                body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            },
            paramCount: 0).Code[0].ShouldBe((byte)VmOp.LdcR8);
    }

    [Fact]
    public void TryEncode_Newobj_WritesFirstOpcodeAndInternsCtor()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var ctor = CreateEmptyCtor(type);
        var method = CreateInt32Method(type, "Make", 0);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, ctor));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        var tables = new VmMemberTables();
        VmEncoder.TryEncode(method, EmptyIds, tables, out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[0].ShouldBe((byte)VmOp.Newobj);
        tables.Methods.Count.ShouldBe(1);
    }

    [Fact]
    public void TryEncode_Ldfld_WritesLdfld()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var field = new FieldDefUser(
            "N",
            new FieldSig(module.CorLibTypes.Int32),
            FieldAttributes.Public);
        type.Fields.Add(field);
        var method = new MethodDefUser(
            "GetN",
            MethodSig.CreateInstance(module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, field));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        var tables = new VmMemberTables();
        VmEncoder.TryEncode(method, EmptyIds, tables, out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[2].ShouldBe((byte)VmOp.Ldfld);
        tables.Fields.Count.ShouldBe(1);
    }

    [Fact]
    public void TryEncode_Box_WritesBox()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = CreateInt32Method(type, "BoxIt", 1);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(
            OpCodes.Box, module.CorLibTypes.Int32.ToTypeDefOrRef()));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        var tables = new VmMemberTables();
        VmEncoder.TryEncode(method, EmptyIds, tables, out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[2].ShouldBe((byte)VmOp.Box);
        tables.Types.Count.ShouldBe(1);
    }

    [Fact]
    public void TryEncode_UnboxAny_WritesUnboxAny()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = new MethodDefUser(
            "Unbox",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.Object),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(
            OpCodes.Unbox_Any, module.CorLibTypes.Int32.ToTypeDefOrRef()));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[2].ShouldBe((byte)VmOp.UnboxAny);
    }

    [Fact]
    public void TryEncode_Castclass_WritesCastclass()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = new MethodDefUser(
            "Cast",
            MethodSig.CreateStatic(new ClassSig(type), module.CorLibTypes.Object),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Castclass, type));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[2].ShouldBe((byte)VmOp.Castclass);
    }

    [Fact]
    public void TryEncode_Isinst_WritesIsinst()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = new MethodDefUser(
            "AsC",
            MethodSig.CreateStatic(new ClassSig(type), module.CorLibTypes.Object),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Isinst, type));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[2].ShouldBe((byte)VmOp.Isinst);
    }

    [Fact]
    public void TryEncode_Newarr_WritesNewarr()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = CreateInt32Method(type, "Arr", 0);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        method.Body.Instructions.Add(Instruction.Create(
            OpCodes.Newarr, module.CorLibTypes.Int32.ToTypeDefOrRef()));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        var tables = new VmMemberTables();
        VmEncoder.TryEncode(method, EmptyIds, tables, out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[5].ShouldBe((byte)VmOp.Newarr);
        tables.Types.Count.ShouldBe(1);
    }

    [Fact]
    public void TryEncode_Ldlen_WritesLdlenAndConvI4()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = new MethodDefUser(
            "Len",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, new SZArraySig(module.CorLibTypes.Int32)),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[2].ShouldBe((byte)VmOp.Ldlen);
        code[3].ShouldBe((byte)VmOp.ConvI4);
    }

    [Fact]
    public void TryEncode_LdelemRef_WritesLdelemRef()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = new MethodDefUser(
            "Get",
            MethodSig.CreateStatic(module.CorLibTypes.Object, new SZArraySig(module.CorLibTypes.Object)),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(method);

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[7].ShouldBe((byte)VmOp.LdelemRef);
    }

    [Fact]
    public void TryEncode_Throw_AllowsThrowAsLastOpcode()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var ex = new TypeRefUser(module, "System", "Exception", module.CorLibTypes.AssemblyRef);
        var exCtor = new MemberRefUser(
            module,
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void),
            ex);
        var method = CreateInt32Method(type, "Fail", 0);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, exCtor));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Throw));

        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out var code, out var skipReason)
            .ShouldBeTrue();
        skipReason.ShouldBeNull();
        code[0].ShouldBe((byte)VmOp.Newobj);
        code[^1].ShouldBe((byte)VmOp.Throw);
    }

    [Fact]
    public void TryEncode_Br_WritesLittleEndianOffsetFromMethodStart()
    {
        var encoded = Encode(
            body =>
            {
                var end = Instruction.Create(OpCodes.Ret);
                body.Instructions.Add(Instruction.Create(OpCodes.Br, end));
                body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
                body.Instructions.Add(end);
            },
            paramCount: 0);
        encoded.Code[0].ShouldBe((byte)VmOp.Br);
        BitConverter.ToUInt16(encoded.Code, 1).ShouldBe((ushort)8);
        encoded.Code[8].ShouldBe((byte)VmOp.Ret);
    }

    private static EncodeResult Encode(Action<CilBody> fill, int paramCount = 2)
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = CreateInt32Method(type, "M", paramCount);
        fill(method.Body);
        var tables = new VmMemberTables();
        VmEncoder.TryEncode(method, EmptyIds, tables, out var code, out var skipReason)
            .ShouldBeTrue($"expected encode to succeed, skipReason={skipReason}");
        skipReason.ShouldBeNull();
        return new EncodeResult(code, tables);
    }

    private static string? EncodeSkip(Action<CilBody> fill, int paramCount = 2)
    {
        var module = CreateTestModule();
        var type = CreateTestType(module);
        var method = CreateInt32Method(type, "M", paramCount);
        fill(method.Body);
        VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason)
            .ShouldBeFalse();
        return skipReason;
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

    private static MethodDef CreateEmptyCtor(TypeDef type)
    {
        var ctor = new MethodDefUser(
            ".ctor",
            MethodSig.CreateInstance(type.Module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.HideBySig |
            MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        ctor.Body = new CilBody();
        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(ctor);
        return ctor;
    }

    private static ModuleDef CreateTestModule()
    {
        var module = new ModuleDefUser(
            "TestAssembly",
            Guid.NewGuid(),
            AssemblyRefUser.CreateMscorlibReferenceCLR40());
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
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(type.Module.CorLibTypes.Int32, types),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        method.Body = new CilBody();
        type.Methods.Add(method);
        return method;
    }

    private sealed record EncodeResult(byte[] Code, VmMemberTables Tables);
}

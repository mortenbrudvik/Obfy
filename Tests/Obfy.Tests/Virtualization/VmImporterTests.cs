using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Virtualization;
using Shouldly;

namespace Obfy.Tests.Virtualization;

public class VmImporterTests
{
    [Fact]
    public void Import_CopiesVmType_WithoutVmRuntimeAssemblyRef()
    {
        var module = new ModuleDefUser("T", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var asm = new AssemblyDefUser("T", new Version(1, 0, 0, 0));
        asm.Modules.Add(module);
        var context = PipelineContext.ForAssembly(module, new ObfySettings());
        var identity = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        var type = ImportEmpty(context, identity);

        type.FullName.ShouldBe("Obfy.Runtime.Vm");
        type.FindMethod("Run").ShouldNotBeNull();
        var init = type.FindMethod("Init");
        init.ShouldNotBeNull();
        init!.MethodSig.Params.Count.ShouldBe(8);
        type.FindStaticConstructor().ShouldNotBeNull();
        module.GetAssemblyRefs().ShouldNotContain(r => r.Name == "Obfy.VmRuntime");
        RuntimeInjection.ShouldRename(context, type).ShouldBeTrue();
        RuntimeInjection.ShouldEncryptIl(context, type).ShouldBeFalse();
        RuntimeInjection.ShouldFlattenControlFlow(context, type).ShouldBeTrue();

        using var ms = new MemoryStream();
        module.Write(ms);
        using var reloaded = ModuleDefMD.Load(ms.ToArray());
        reloaded.GetAssemblyRefs().ShouldNotContain(r => r.Name == "Obfy.VmRuntime");
        var reloadedVm = reloaded.Find("Obfy.Runtime.Vm", isReflectionName: false);
        reloadedVm.ShouldNotBeNull();
        reloadedVm!.FindMethod("Run").ShouldNotBeNull();
        reloadedVm.FindMethod("Init").ShouldNotBeNull();
        reloadedVm.FindStaticConstructor().ShouldNotBeNull();
    }

    [Fact]
    public void EmbeddedResource_IsPresentOnObfyCore()
    {
        typeof(Obfy.Core.Pipeline.PipelineContext).Assembly
            .GetManifestResourceNames()
            .ShouldContain(VmImporter.EmbeddedName);
    }

    [Fact]
    public void Import_CctorEmitsReturnTypesAndTwoArgHandles()
    {
        var module = new ModuleDefUser("T", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var asm = new AssemblyDefUser("T", new Version(1, 0, 0, 0));
        asm.Modules.Add(module);
        var context = PipelineContext.ForAssembly(module, new ObfySettings());
        var identity = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var listType = new TypeRefUser(module, "System.Collections.Generic", "List`1", module.CorLibTypes.AssemblyRef);
        var listInt = new TypeSpecUser(new GenericInstSig(new ClassSig(listType), module.CorLibTypes.Int32));
        var add = new MemberRefUser(
            module,
            "Add",
            MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Int32),
            listInt);
        var field = new MemberRefUser(
            module,
            "_size",
            new FieldSig(module.CorLibTypes.Int32),
            listInt);

        var type = VmImporter.Import(
            context,
            code: Array.Empty<byte>(),
            starts: Array.Empty<int>(),
            opMap: identity,
            xorKey: new byte[8],
            methods: new IMethod[] { add },
            fields: new IField[] { field },
            types: new ITypeDefOrRef[] { listInt },
            returnTypes: new ITypeDefOrRef[] { module.CorLibTypes.Boolean.ToTypeDefOrRef() });

        var init = type.FindMethod("Init");
        init.ShouldNotBeNull();
        init!.MethodSig.Params.Count.ShouldBe(8);

        var cctor = type.FindStaticConstructor();
        cctor.ShouldNotBeNull();
        var instructions = cctor!.Body.Instructions;

        TypeArrayCount(instructions).ShouldBe(2);

        var getMethod = Called(instructions, "GetMethodFromHandle").ShouldHaveSingleItem();
        getMethod.MethodSig.Params.Count.ShouldBe(2);
        var getField = Called(instructions, "GetFieldFromHandle").ShouldHaveSingleItem();
        getField.MethodSig.Params.Count.ShouldBe(2);

        var methodTokenIndex = IndexOfLdtoken(instructions, add);
        methodTokenIndex.ShouldBeGreaterThanOrEqualTo(0);
        instructions[methodTokenIndex + 1].OpCode.ShouldBe(OpCodes.Ldtoken);
        instructions[methodTokenIndex + 1].Operand.ShouldBe(listInt);
        instructions[methodTokenIndex + 2].OpCode.ShouldBe(OpCodes.Call);
        instructions[methodTokenIndex + 2].Operand.ShouldBe(getMethod);
    }

    private static TypeDef ImportEmpty(PipelineContext context, byte[] identity) =>
        VmImporter.Import(
            context,
            code: Array.Empty<byte>(),
            starts: Array.Empty<int>(),
            opMap: identity,
            xorKey: new byte[8],
            methods: Array.Empty<IMethod>(),
            fields: Array.Empty<IField>(),
            types: Array.Empty<ITypeDefOrRef>(),
            returnTypes: Array.Empty<ITypeDefOrRef>());

    private static int TypeArrayCount(IList<Instruction> instructions) =>
        instructions.Count(i =>
            i.OpCode == OpCodes.Newarr &&
            i.Operand is ITypeDefOrRef t &&
            t.Name == "Type");

    private static IReadOnlyList<IMethod> Called(IList<Instruction> instructions, string name) =>
        instructions
            .Where(i => i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == name)
            .Select(i => (IMethod)i.Operand)
            .ToList();

    private static int IndexOfLdtoken(IList<Instruction> instructions, object operand)
    {
        for (var i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode == OpCodes.Ldtoken && ReferenceEquals(instructions[i].Operand, operand))
                return i;
        }

        return -1;
    }
}

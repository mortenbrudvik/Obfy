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
    public void Import_ResolvesEachMemberThroughItsOwnHelper()
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

        // The static constructor must not name a single member: the CLR resolves an ldtoken when it
        // compiles the method holding it, so one unresolvable member would fail the whole VM.
        var cctor = type.FindStaticConstructor();
        cctor.ShouldNotBeNull();
        var named = new object[] { add, field, listInt, module.CorLibTypes.Boolean.ToTypeDefOrRef() };
        cctor!.Body.Instructions.ShouldNotContain(i =>
            i.OpCode == OpCodes.Ldtoken && named.Any(n => ReferenceEquals(n, i.Operand)));
        Called(cctor.Body.Instructions, "GetMethodFromHandle").ShouldBeEmpty();
        Called(cctor.Body.Instructions, "GetFieldFromHandle").ShouldBeEmpty();
        Called(cctor.Body.Instructions, "GetTypeFromHandle").ShouldBeEmpty();

        var methodHelper = HelperLoading(type, add);
        var methodTokens = methodHelper.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldtoken).ToList();
        methodTokens.Count.ShouldBe(2);
        methodTokens[1].Operand.ShouldBe(listInt);
        var getMethod = Called(methodHelper.Body.Instructions, "GetMethodFromHandle").ShouldHaveSingleItem();
        getMethod.MethodSig.Params.Count.ShouldBe(2);
        methodHelper.IsNoInlining.ShouldBeTrue("an inlined helper would pull its token into the resolver");

        var fieldHelper = HelperLoading(type, field);
        Called(fieldHelper.Body.Instructions, "GetFieldFromHandle").ShouldHaveSingleItem()
            .MethodSig.Params.Count.ShouldBe(2);

        var typeHelper = HelperLoading(type, listInt, "GetTypeFromHandle");
        Called(typeHelper.Body.Instructions, "GetTypeFromHandle").ShouldHaveSingleItem()
            .MethodSig.Params.Count.ShouldBe(1);
        HelperLoading(type, module.CorLibTypes.Boolean.ToTypeDefOrRef())
            .Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Ldtoken);

        // Each resolver dispatches to its helpers and Init receives the counts, not member arrays.
        type.FindMethod("ResolveMethod")!.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Switch);
        type.FindMethod("ResolveField")!.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Switch);
        type.FindMethod("ResolveType")!.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Switch);
        type.FindMethod("ResolveReturnType")!.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Switch);
        type.FindMethod("ResolveArgumentType")!.Body.Instructions.ShouldNotContain(i => i.OpCode == OpCodes.Switch);
    }

    private static MethodDef HelperLoading(TypeDef vmType, object token, string calls = "")
    {
        var holders = vmType.Methods
            .Where(m => m.Body is not null
                && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldtoken && ReferenceEquals(i.Operand, token))
                && (calls.Length == 0 || Called(m.Body.Instructions, calls).Count > 0))
            .ToList();
        holders.Count.ShouldBe(1, "exactly one helper holds the token");
        return holders[0];
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

    private static IReadOnlyList<IMethod> Called(IList<Instruction> instructions, string name) =>
        instructions
            .Where(i => i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == name)
            .Select(i => (IMethod)i.Operand)
            .ToList();
}

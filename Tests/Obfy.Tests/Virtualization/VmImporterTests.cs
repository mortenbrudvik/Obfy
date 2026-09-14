using dnlib.DotNet;
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

        var type = VmImporter.Import(
            context,
            code: Array.Empty<byte>(),
            starts: Array.Empty<int>(),
            opMap: identity,
            xorKey: new byte[8],
            methods: Array.Empty<IMethod>(),
            fields: Array.Empty<IField>(),
            types: Array.Empty<ITypeDefOrRef>());

        type.FullName.ShouldBe("Obfy.Runtime.Vm");
        type.FindMethod("Run").ShouldNotBeNull();
        type.FindMethod("Init").ShouldNotBeNull();
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
}

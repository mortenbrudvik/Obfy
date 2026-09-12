using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class RuntimeProfileGatingTests
{
    [Theory]
    [InlineData(RuntimeProfile.NativeAot, "NativeAOT")]
    [InlineData(RuntimeProfile.UnityIl2Cpp, "Unity IL2CPP")]
    [InlineData(RuntimeProfile.BlazorWasm, "Blazor WebAssembly")]
    public void Apply_DisablesEmbeddingOnRestrictedProfiles(RuntimeProfile profile, string label)
    {
        var settings = new ObfySettings
        {
            RuntimeProfile = profile,
            DependencyEmbedding = { Enabled = true },
            Packing = { Enabled = true },
            Protection = { MethodEncryption = true, AntiDump = true }
        };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);

        RuntimeProfileGating.Apply(settings, context);

        settings.DependencyEmbedding.Enabled.ShouldBeFalse();
        settings.Packing.Enabled.ShouldBeFalse();
        settings.Protection.MethodEncryption.ShouldBeFalse();
        settings.Protection.AntiDump.ShouldBeFalse();
        context.Warnings.ShouldContain(w => w.Contains("Dependency embedding disabled", StringComparison.OrdinalIgnoreCase)
                                           && w.Contains(label));
        context.Warnings.ShouldContain(w => w.Contains("Managed launcher packing disabled", StringComparison.OrdinalIgnoreCase)
                                           && w.Contains(label));
        RuntimeProfileGating.BlocksAssemblyResolve(profile).ShouldBeTrue();
        RuntimeProfileGating.BlocksPeProtections(profile).ShouldBeTrue();
        RuntimeProfileGating.AllowsPeMutation(profile).ShouldBeFalse();
        RuntimeProfileGating.AllowsKernel32PInvoke(profile).ShouldBeFalse();
    }

    [Theory]
    [InlineData(RuntimeProfile.NativeAot)]
    [InlineData(RuntimeProfile.UnityIl2Cpp)]
    [InlineData(RuntimeProfile.BlazorWasm)]
    public void Apply_RestrictedProfiles_KeepAntiDebugEnabled(RuntimeProfile profile)
    {
        var settings = new ObfySettings
        {
            RuntimeProfile = profile,
            Protection = { AntiDebug = true, AntiDump = true, MethodEncryption = true }
        };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);

        RuntimeProfileGating.Apply(settings, context);

        settings.Protection.AntiDebug.ShouldBeTrue();
        settings.Protection.AntiDump.ShouldBeFalse();
        settings.Protection.MethodEncryption.ShouldBeFalse();
        RuntimeProfileGating.AllowsKernel32PInvoke(profile).ShouldBeFalse();
        RuntimeProfileGating.AllowsPeMutation(profile).ShouldBeFalse();
    }

    [Fact]
    public void Apply_DefaultProfileKeepsEmbeddingAndPeProtections()
    {
        var settings = new ObfySettings
        {
            DependencyEmbedding = { Enabled = true },
            Packing = { Enabled = true },
            Protection = { MethodEncryption = true, AntiDump = true }
        };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);

        RuntimeProfileGating.Apply(settings, context);

        settings.DependencyEmbedding.Enabled.ShouldBeTrue();
        settings.Packing.Enabled.ShouldBeTrue();
        settings.Protection.MethodEncryption.ShouldBeTrue();
        settings.Protection.AntiDump.ShouldBeTrue();
        RuntimeProfileGating.AllowsPeMutation(RuntimeProfile.Default).ShouldBeTrue();
        RuntimeProfileGating.AllowsKernel32PInvoke(RuntimeProfile.Default).ShouldBeTrue();
        context.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_WarnsWhenProxyExternalIsOnWithoutReferenceProxy()
    {
        var settings = new ObfySettings
        {
            Protection = { ProxyExternalCalls = true, ReferenceProxy = false }
        };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);

        RuntimeProfileGating.Apply(settings, context);

        context.Warnings.ShouldContain(w => w.Contains("proxyExternalCalls is ignored", StringComparison.OrdinalIgnoreCase));
    }
}

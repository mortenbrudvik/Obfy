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
            Protection = { MethodEncryption = true, AntiDump = true }
        };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);

        RuntimeProfileGating.Apply(settings, context);

        settings.DependencyEmbedding.Enabled.ShouldBeFalse();
        settings.Protection.MethodEncryption.ShouldBeFalse();
        settings.Protection.AntiDump.ShouldBeFalse();
        context.Warnings.ShouldContain(w => w.Contains("Dependency embedding disabled", StringComparison.OrdinalIgnoreCase)
                                           && w.Contains(label));
        RuntimeProfileGating.BlocksAssemblyResolve(profile).ShouldBeTrue();
        RuntimeProfileGating.BlocksPeProtections(profile).ShouldBeTrue();
    }

    [Fact]
    public void Apply_DefaultProfileKeepsEmbeddingAndPeProtections()
    {
        var settings = new ObfySettings
        {
            DependencyEmbedding = { Enabled = true },
            Protection = { MethodEncryption = true, AntiDump = true }
        };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);

        RuntimeProfileGating.Apply(settings, context);

        settings.DependencyEmbedding.Enabled.ShouldBeTrue();
        settings.Protection.MethodEncryption.ShouldBeTrue();
        settings.Protection.AntiDump.ShouldBeTrue();
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

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
            Protection = { MethodEncryption = true, AntiDump = true, AntiTamper = { Enabled = true } }
        };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);

        RuntimeProfileGating.Apply(settings, context);

        settings.DependencyEmbedding.Enabled.ShouldBeFalse();
        settings.Packing.Enabled.ShouldBeFalse();
        settings.Protection.MethodEncryption.ShouldBeFalse();
        settings.Protection.AntiDump.ShouldBeFalse();
        settings.Protection.AntiTamper.Enabled.ShouldBeFalse();
        context.Warnings.ShouldContain(w => w.Contains("Dependency embedding disabled", StringComparison.OrdinalIgnoreCase)
                                           && w.Contains(label));
        context.Warnings.ShouldContain(w => w.Contains("Native packing disabled", StringComparison.OrdinalIgnoreCase)
                                           && w.Contains(label));
        context.Warnings.ShouldContain(w => w.Contains("Anti-tamper disabled", StringComparison.OrdinalIgnoreCase)
                                           && w.Contains(label));
        RuntimeProfileGating.BlocksAssemblyResolve(profile).ShouldBeTrue();
        RuntimeProfileGating.BlocksPeProtections(profile).ShouldBeTrue();
        RuntimeProfileGating.AllowsPeMutation(profile).ShouldBeFalse();
        RuntimeProfileGating.AllowsAntiTamper(profile).ShouldBeFalse();
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
            Protection = { MethodEncryption = true, AntiDump = true, AntiTamper = { Enabled = true } }
        };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);

        RuntimeProfileGating.Apply(settings, context);

        settings.DependencyEmbedding.Enabled.ShouldBeTrue();
        settings.Packing.Enabled.ShouldBeTrue();
        settings.Protection.MethodEncryption.ShouldBeTrue();
        settings.Protection.AntiDump.ShouldBeTrue();
        settings.Protection.AntiTamper.Enabled.ShouldBeTrue();
        RuntimeProfileGating.AllowsPeMutation(RuntimeProfile.Default).ShouldBeTrue();
        RuntimeProfileGating.AllowsAntiTamper(RuntimeProfile.Default).ShouldBeTrue();
        RuntimeProfileGating.AllowsKernel32PInvoke(RuntimeProfile.Default).ShouldBeTrue();
        context.Warnings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(RuntimeProfile.NativeAot, "NativeAOT")]
    [InlineData(RuntimeProfile.UnityIl2Cpp, "Unity IL2CPP")]
    [InlineData(RuntimeProfile.BlazorWasm, "Blazor WebAssembly")]
    public void Apply_DisablesVirtualizationOnRestrictedProfiles(RuntimeProfile profile, string label)
    {
        var settings = new ObfySettings { RuntimeProfile = profile, Virtualization = { Enabled = true } };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);
        RuntimeProfileGating.Apply(settings, context);
        settings.Virtualization.Enabled.ShouldBeFalse();
        context.Warnings.ShouldContain(w => w.Contains("Virtualization disabled") && w.Contains(label));
        RuntimeProfileGating.AllowsManagedVm(profile).ShouldBeFalse();
    }

    [Fact]
    public void Apply_DefaultProfileKeepsVirtualization()
    {
        var settings = new ObfySettings { Virtualization = { Enabled = true } };
        RuntimeProfileGating.Apply(settings, PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings));
        settings.Virtualization.Enabled.ShouldBeTrue();
        RuntimeProfileGating.AllowsManagedVm(RuntimeProfile.Default).ShouldBeTrue();
    }

    [Theory]
    [InlineData(RuntimeProfile.NativeAot, "NativeAOT")]
    [InlineData(RuntimeProfile.UnityIl2Cpp, "Unity IL2CPP")]
    [InlineData(RuntimeProfile.BlazorWasm, "Blazor WebAssembly")]
    public void Apply_DisablesAntiTamperOnRestrictedProfiles(RuntimeProfile profile, string label)
    {
        var settings = new ObfySettings
        {
            RuntimeProfile = profile,
            Protection = { AntiTamper = { Enabled = true } }
        };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);

        RuntimeProfileGating.Apply(settings, context);

        settings.Protection.AntiTamper.Enabled.ShouldBeFalse();
        context.Warnings.ShouldContain(w =>
            w.Contains("Anti-tamper disabled", StringComparison.OrdinalIgnoreCase) &&
            w.Contains(label) &&
            w.Contains("IL DLL on disk", StringComparison.OrdinalIgnoreCase));
        RuntimeProfileGating.AllowsAntiTamper(profile).ShouldBeFalse();
    }

    [Fact]
    public void Apply_DefaultProfileKeepsAntiTamper()
    {
        var settings = new ObfySettings { Protection = { AntiTamper = { Enabled = true } } };
        var context = PipelineContext.ForAssembly(new dnlib.DotNet.ModuleDefUser("t"), settings);

        RuntimeProfileGating.Apply(settings, context);

        settings.Protection.AntiTamper.Enabled.ShouldBeTrue();
        context.Warnings.ShouldBeEmpty();
        RuntimeProfileGating.AllowsAntiTamper(RuntimeProfile.Default).ShouldBeTrue();
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

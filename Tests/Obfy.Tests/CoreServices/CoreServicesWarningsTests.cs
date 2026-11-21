using Autofac;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests.CoreServices;

/// <summary>A run must never look like a full success when part of what was asked for did not happen.</summary>
public class CoreServicesWarningsTests : IDisposable
{
    private readonly string _dir = CoreServicesTestSupport.NewTempDirectory("obfy-warn-svc");

    public void Dispose() => CoreServicesTestSupport.DeleteQuietly(_dir);

    private sealed class FakeObfuscator(string name, int priority, TargetType supports, bool enabled) : IObfuscator
    {
        public string Name { get; } = name;
        public int Priority { get; } = priority;
        public bool SupportsTargetType(TargetType targetType) => targetType == supports;
        public bool IsEnabled(ObfySettings settings) => enabled;

        public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
        {
            var stats = new ObfuscationStatistics { ProtectionsApplied = 1 };
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
    }

    private static PipelineContext Context(TargetType type) => new() { TargetType = type, Settings = new ObfySettings() };

    [Fact]
    public async Task Pipeline_EnabledTechniqueThatDoesNotApplyToTheTarget_AddsAWarning()
    {
        var pipeline = new ObfuscationPipeline(
            [new FakeObfuscator("AntiDebug", 18, TargetType.Assembly, enabled: true)],
            NullLogger<ObfuscationPipeline>.Instance);
        var context = Context(TargetType.SourceCode);

        var result = await pipeline.ExecuteAsync(context);

        result.Success.ShouldBeTrue();
        context.Warnings.ShouldContain(w => w.Contains("AntiDebug") && w.Contains("source", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Pipeline_AssemblyTargetWithSourceOnlyTechnique_AlsoWarns()
    {
        var pipeline = new ObfuscationPipeline(
            [new FakeObfuscator("SourceOnly", 77, TargetType.SourceCode, enabled: true)],
            NullLogger<ObfuscationPipeline>.Instance);
        var context = Context(TargetType.Assembly);

        await pipeline.ExecuteAsync(context);

        context.Warnings.ShouldContain(w => w.Contains("SourceOnly") && w.Contains("assembl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Pipeline_DisabledTechniqueThatDoesNotApply_StaysSilent()
    {
        var pipeline = new ObfuscationPipeline(
            [new FakeObfuscator("AntiDebug", 18, TargetType.Assembly, enabled: false)],
            NullLogger<ObfuscationPipeline>.Instance);
        var context = Context(TargetType.SourceCode);

        await pipeline.ExecuteAsync(context);

        context.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pipeline_TechniqueCoveredByAnEnabledCounterpartOfTheSamePhase_StaysSilent()
    {
        // StringEncryption (assembly) and SourceStringEncryption (source) are two implementations of one technique.
        var pipeline = new ObfuscationPipeline(
            [
                new FakeObfuscator("StringEncryption", 10, TargetType.Assembly, enabled: true),
                new FakeObfuscator("SourceStringEncryption", 10, TargetType.SourceCode, enabled: true)
            ],
            NullLogger<ObfuscationPipeline>.Instance);
        var context = Context(TargetType.SourceCode);

        await pipeline.ExecuteAsync(context);

        context.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Service_SourceInput_WarnsForAssemblyOnlyTechniques_ButNotForTheSourceEquivalents()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = Path.Combine(_dir, "Input.cs");
        await File.WriteAllTextAsync(input, "public class Widget { public string Name() => \"widget name\"; }");
        var settings = CoreServicesTestSupport.AllOff();
        settings.StringEncryption.Enabled = true;
        settings.SymbolRenaming.Enabled = true;
        settings.ControlFlow.Enabled = true;
        settings.Protection.AntiDebug = true;
        settings.Protection.ReferenceProxy = true;
        settings.ResourceEncryption.Enabled = true;

        var result = await service.ObfuscateAsync(input, Path.Combine(_dir, "Output.cs"), settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldContain(w => w.Contains("AntiDebug"));
        result.Warnings.ShouldContain(w => w.Contains("ReferenceProxy"));
        result.Warnings.ShouldContain(w => w.Contains("ResourceEncryption"));
        result.Warnings.ShouldNotContain(w => w.Contains("StringEncryption"));
        result.Warnings.ShouldNotContain(w => w.Contains("SymbolRenaming"));
        result.Warnings.ShouldNotContain(w => w.Contains("ControlFlow"));
    }

    [Fact]
    public async Task Service_AssemblyRunThatChangesNothing_Warns()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CoreServicesTestSupport.CompileLibrary(_dir, "Lib", "public class C { public static int N() => 1; }");

        var result = await service.ObfuscateAsync(input, Path.Combine(_dir, "Lib.out.dll"), CoreServicesTestSupport.AllOff());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.TotalTransformations.ShouldBe(0);
        result.Warnings.ShouldContain(w => w.Contains("No transformations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Service_AssemblyRunThatTransforms_DoesNotWarnAboutNoTransformations()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CoreServicesTestSupport.CompileLibrary(_dir, "Lib", "public class C { public static string S() => \"some text\"; }");
        var settings = CoreServicesTestSupport.AllOff();
        settings.StringEncryption.Enabled = true;

        var result = await service.ObfuscateAsync(input, Path.Combine(_dir, "Lib.out.dll"), settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.TotalTransformations.ShouldBeGreaterThan(0);
        result.Warnings.ShouldNotContain(w => w.Contains("No transformations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Service_SourceSetThatChangesNothing_Warns()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var a = Path.Combine(_dir, "A.cs");
        var b = Path.Combine(_dir, "B.cs");
        await File.WriteAllTextAsync(a, "class A { }");
        await File.WriteAllTextAsync(b, "class B { }");

        var result = await service.ObfuscateSourcesAsync([a, b], Path.Combine(_dir, "out"), CoreServicesTestSupport.AllOff());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldContain(w => w.Contains("No transformations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Service_MergeWithExcludedInput_ReportsWhatWasLeftOut()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var libA = CoreServicesTestSupport.CompileLibrary(_dir, "LibA", "public class A { public static int N() => 1; }");
        var libB = CoreServicesTestSupport.CompileLibrary(_dir, "LibB", "public class B { public static int N() => 2; }");
        var libC = CoreServicesTestSupport.CompileLibrary(_dir, "LibC", "public class C { public static int N() => 3; }");
        var settings = CoreServicesTestSupport.AllOff();
        settings.StringEncryption.Enabled = true;
        settings.AssemblyMerge.ExcludePatterns = ["LibC.dll"];

        var result = await service.MergeAndObfuscateAsync([libA, libB, libC], Path.Combine(_dir, "Merged.dll"), settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Warnings.ShouldContain(w => w.Contains("LibC.dll") && w.Contains("merge", StringComparison.OrdinalIgnoreCase));
        result.Warnings.ShouldNotContain(w => w.Contains("LibA.dll") || w.Contains("LibB.dll"));
    }

    [Fact]
    public async Task Service_AlreadyProtectedInput_IsRefusedBeforeAnyOutputIsWritten()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CoreServicesTestSupport.CompileLibrary(_dir, "Lib", "public class C { public static string S() => \"some text\"; }");
        var firstOutput = Path.Combine(_dir, "Lib.protected.dll");
        var settings = CoreServicesTestSupport.AllOff();
        settings.StringEncryption.Enabled = true;
        (await service.ObfuscateAsync(input, firstOutput, settings)).Success.ShouldBeTrue();
        var secondOutput = Path.Combine(_dir, "Lib.protected-twice.dll");

        var result = await service.ObfuscateAsync(firstOutput, secondOutput, settings);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain("already protected");
        File.Exists(secondOutput).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Obfy.Core.dll")]
    [InlineData("Settings.Core.dll")]
    [InlineData("Logging.Core.dll")]
    public void ProtectedInputGuard_DoesNotFlagObfysOwnSelfObfuscationInputs(string assemblyFile)
    {
        // build/build-installer.ps1 runs Obfy over obfy.dll, Obfy.Core.dll, Settings.Core.dll and Logging.Core.dll.
        // None of them may carry Obfy.Runtime.* types, or the release build would be refused.
        using var module = dnlib.DotNet.ModuleDefMD.Load(Path.Combine(AppContext.BaseDirectory, assemblyFile));

        Obfy.Core.Services.ProtectedInputGuard.ContainsInjectedHelpers(module).ShouldBeFalse();
    }

    [Fact]
    public async Task Service_PlainInput_IsNotMistakenForProtected()
    {
        using var container = CoreServicesTestSupport.BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var input = CoreServicesTestSupport.CompileLibrary(_dir, "Lib", "public class C { public static string S() => \"some text\"; }");
        var settings = CoreServicesTestSupport.AllOff();
        settings.StringEncryption.Enabled = true;

        var result = await service.ObfuscateAsync(input, Path.Combine(_dir, "Lib.out.dll"), settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
    }
}

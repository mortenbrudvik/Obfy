using Autofac;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Obfuscators;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Verifies the Autofac registration in <see cref="ObfuscationModule"/>: everything resolves, every
/// obfuscator has a unique name, and priorities order as documented.
/// </summary>
public class DependencyInjectionTests
{
    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build();
    }

    [Fact]
    public void Module_ResolvesCoreServices()
    {
        using var container = BuildContainer();

        container.Resolve<IObfuscationPipeline>().ShouldNotBeNull();
        container.Resolve<IObfuscationService>().ShouldNotBeNull();
        container.Resolve<IAssemblyProcessor>().ShouldNotBeNull();
        container.Resolve<ISourceProcessor>().ShouldNotBeNull();
        container.Resolve<IAssemblyMerger>().ShouldNotBeNull();
        container.Resolve<IReportService>().ShouldNotBeNull();
    }

    [Fact]
    public void Module_RegistersAllObfuscators_WithUniqueNames()
    {
        using var container = BuildContainer();

        var obfuscators = container.Resolve<IEnumerable<IObfuscator>>().ToList();

        // 9 assembly + 3 source obfuscators are registered.
        obfuscators.Count.ShouldBe(12);

        var names = obfuscators.Select(o => o.Name).ToList();
        names.Distinct().Count().ShouldBe(names.Count); // Name is the identity used in errors/stats

        obfuscators.ShouldContain(o => o.Name == "StringEncryption");
        obfuscators.ShouldContain(o => o.Name == "AntiTamper");
        obfuscators.ShouldContain(o => o.Name == "SourceSymbolRenaming");
    }

    [Fact]
    public void Module_ObfuscatorPriorities_MatchDocumentedPhases()
    {
        using var container = BuildContainer();

        var byName = container.Resolve<IEnumerable<IObfuscator>>().ToDictionary(o => o.Name, o => o.Priority);

        byName["StringEncryption"].ShouldBe((int)ObfuscationPhase.StringEncryption);
        byName["SymbolRenaming"].ShouldBe((int)ObfuscationPhase.SymbolRenaming);
        byName["MetadataRemoval"].ShouldBe((int)ObfuscationPhase.MetadataRemoval);

        // String encryption must run before symbol renaming, which must run before metadata cleanup.
        byName["StringEncryption"].ShouldBeLessThan(byName["SymbolRenaming"]);
        byName["SymbolRenaming"].ShouldBeLessThan(byName["MetadataRemoval"]);
    }
}

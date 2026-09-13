using Autofac;
using Obfy.Core.Obfuscators;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Obfuscators.Source;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Obfy.Core.Services.Solution;
using Obfy.Core.Utilities;

namespace Obfy.Core.DependencyInjection;

/// <summary>
/// Autofac module for registering obfuscation services.
/// </summary>
public class ObfuscationModule : Module
{
    /// <inheritdoc/>
    protected override void Load(ContainerBuilder builder)
    {
        // Register processors
        builder.RegisterType<AssemblyProcessor>()
            .As<IAssemblyProcessor>()
            .SingleInstance();

        builder.RegisterType<SourceProcessor>()
            .As<ISourceProcessor>()
            .SingleInstance();

        // Register utilities
        builder.RegisterType<NameGenerator>()
            .As<INameGenerator>()
            .InstancePerLifetimeScope();

        // Register assembly obfuscators
        builder.RegisterType<StringEncryptionObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<ConstantEncryptionObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<ResourceEncryptionObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<SymbolRenamingObfuscator>()
            .AsSelf()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<ControlFlowObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<MetadataRemovalObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<AntiDebugObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<AntiDecompilerObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<AntiTamperObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<AntiDumpObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<ReferenceProxyObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<MethodEncryptionObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<DependencyEmbeddingObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<WatermarkObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<VirtualizationObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        // Register source code obfuscators
        builder.RegisterType<SourceStringEncryptor>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<SourceSymbolRenamer>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<SourceControlFlowObfuscator>()
            .As<IObfuscator>()
            .InstancePerLifetimeScope();

        // Register pipeline
        builder.RegisterType<ObfuscationPipeline>()
            .As<IObfuscationPipeline>()
            .InstancePerLifetimeScope();

        // Register assembly merger
        builder.RegisterType<AssemblyMerger>()
            .As<IAssemblyMerger>()
            .SingleInstance();

        // Register main service
        builder.RegisterType<ObfuscationService>()
            .As<IObfuscationService>()
            .SingleInstance();

        builder.RegisterType<SolutionAnalyzer>()
            .As<ISolutionAnalyzer>()
            .SingleInstance();

        builder.RegisterType<ClosedSetProcessor>()
            .As<IClosedSetProcessor>()
            .SingleInstance();

        builder.RegisterType<MethodEncryptionPePostProcessor>()
            .As<IPePostProcessor>()
            .SingleInstance();

        builder.RegisterType<AntiTamperPePostProcessor>()
            .As<IPePostProcessor>()
            .SingleInstance();

        // Register report generators
        builder.RegisterType<HtmlReportGenerator>()
            .As<IReportGenerator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<JsonReportGenerator>()
            .As<IReportGenerator>()
            .AsSelf()
            .SingleInstance();

        // Register report service
        builder.RegisterType<ReportService>()
            .As<IReportService>()
            .SingleInstance();
    }
}

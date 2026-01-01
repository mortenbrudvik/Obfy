using Autofac;
using Obfy.Core.Obfuscators;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Obfuscators.Source;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
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
            .SingleInstance();

        // Register assembly obfuscators
        builder.RegisterType<StringEncryptionObfuscator>()
            .As<IObfuscator>()
            .SingleInstance();

        builder.RegisterType<ConstantEncryptionObfuscator>()
            .As<IObfuscator>()
            .SingleInstance();

        builder.RegisterType<ResourceEncryptionObfuscator>()
            .As<IObfuscator>()
            .SingleInstance();

        builder.RegisterType<SymbolRenamingObfuscator>()
            .As<IObfuscator>()
            .SingleInstance();

        builder.RegisterType<ControlFlowObfuscator>()
            .As<IObfuscator>()
            .SingleInstance();

        builder.RegisterType<MetadataRemovalObfuscator>()
            .As<IObfuscator>()
            .SingleInstance();

        builder.RegisterType<AntiDebugObfuscator>()
            .As<IObfuscator>()
            .SingleInstance();

        builder.RegisterType<AntiDecompilerObfuscator>()
            .As<IObfuscator>()
            .SingleInstance();

        builder.RegisterType<AntiTamperObfuscator>()
            .As<IObfuscator>()
            .SingleInstance();

        // Register source code obfuscators
        builder.RegisterType<SourceStringEncryptor>()
            .As<IObfuscator>()
            .SingleInstance();

        builder.RegisterType<SourceSymbolRenamer>()
            .As<IObfuscator>()
            .SingleInstance();

        builder.RegisterType<SourceControlFlowObfuscator>()
            .As<IObfuscator>()
            .SingleInstance();

        // Register pipeline
        builder.RegisterType<ObfuscationPipeline>()
            .As<IObfuscationPipeline>()
            .SingleInstance();

        // Register main service
        builder.RegisterType<ObfuscationService>()
            .As<IObfuscationService>()
            .SingleInstance();

        // Register report generators
        builder.RegisterType<HtmlReportGenerator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<JsonReportGenerator>()
            .AsSelf()
            .SingleInstance();

        // Register report service
        builder.RegisterType<ReportService>()
            .As<IReportService>()
            .SingleInstance();
    }
}

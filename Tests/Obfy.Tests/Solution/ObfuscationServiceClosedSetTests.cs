using Autofac;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Solution;
using Shouldly;

namespace Obfy.Tests.Solution;

public class ObfuscationServiceClosedSetTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"obfy-svc-closed-{Guid.NewGuid():N}");

    public ObfuscationServiceClosedSetTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public async Task ObfuscateClosedSetAsync_TwoCompiledAssemblies_SucceedsAndWritesBothOutputs()
    {
        var libA = CompileLibrary("LibA", "public class Alpha { public static int Value() => 1; }");
        var libB = CompileLibrary("LibB", "public class Beta { public static int Value() => 2; }");
        var outputDir = Path.Combine(_tempDirectory, "out");

        using var container = BuildContainer();
        var service = container.Resolve<IObfuscationService>();

        var result = await service.ObfuscateClosedSetAsync(
            [
                new ClosedSetInput { AssemblyPath = libA, Hints = new ProjectSettingsHints() },
                new ClosedSetInput { AssemblyPath = libB, Hints = new ProjectSettingsHints() }
            ],
            outputDir,
            new ObfySettings { Level = ObfuscationLevel.Custom });

        result.Success.ShouldBeTrue(result.ErrorMessage);
        File.Exists(Path.Combine(outputDir, "LibA.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "LibB.dll")).ShouldBeTrue();
    }

    [Fact]
    public async Task ObfuscateClosedSetAsync_DelegatesToClosedSetProcessor()
    {
        var inputs = new List<ClosedSetInput>
        {
            new() { AssemblyPath = "a.dll", Hints = new ProjectSettingsHints() }
        };
        const string outputDir = "closed-out";
        var settings = new ObfySettings { Level = ObfuscationLevel.Custom };
        using var cts = new CancellationTokenSource();
        var expected = ClosedSetResult.Succeeded([]);

        var processor = new Mock<IClosedSetProcessor>();
        processor
            .Setup(p => p.ExecuteAsync(inputs, outputDir, settings, true, cts.Token))
            .ReturnsAsync(expected);

        var service = new ObfuscationService(
            new Mock<IAssemblyProcessor>().Object,
            new Mock<ISourceProcessor>().Object,
            new Mock<IObfuscationPipeline>().Object,
            new Mock<IAssemblyMerger>().Object,
            new Mock<ILogger<ObfuscationService>>().Object,
            processor.Object);

        var result = await service.ObfuscateClosedSetAsync(inputs, outputDir, settings, forcePreservePublic: true, cts.Token);

        result.ShouldBeSameAs(expected);
        processor.Verify(p => p.ExecuteAsync(inputs, outputDir, settings, true, cts.Token), Times.Once);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build();
    }

    private string CompileLibrary(string assemblyName, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(_tempDirectory, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }
}

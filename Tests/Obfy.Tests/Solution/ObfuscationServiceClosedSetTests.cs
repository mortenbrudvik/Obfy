using System.Reflection;
using System.Runtime.Loader;
using Autofac;
using dnlib.DotNet;
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

    [Fact]
    public async Task ObfuscateClosedSetAsync_SecondRun_RestartsSequentialNames()
    {
        var libA = CompileLibrary("LibA", "public class Alpha { internal static int Hidden() => 1; }");
        var libB = CompileLibrary("LibB", "public class Beta { internal static int Hidden() => 2; }");
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            StringEncryption = { Enabled = false },
            ControlFlow = { Enabled = false },
            ConstantEncryption = { Enabled = false },
            ResourceEncryption = { Enabled = false },
            SymbolRenaming = { Enabled = true, Mode = NamingMode.Sequential, PreservePublicApi = false },
            Metadata = { RemoveDebugInfo = false, RemoveAttributes = false, StripDocumentation = false },
            Protection =
            {
                AntiDebug = false,
                AntiTamper = { Enabled = false },
                AntiDecompiler = { Enabled = false },
                AntiDump = false,
                ReferenceProxy = false,
                MethodEncryption = false
            }
        };
        var inputs = new List<ClosedSetInput>
        {
            new() { AssemblyPath = libA, Hints = new ProjectSettingsHints() },
            new() { AssemblyPath = libB, Hints = new ProjectSettingsHints() }
        };

        using var container = BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var firstDir = Path.Combine(_tempDirectory, "out1");
        var secondDir = Path.Combine(_tempDirectory, "out2");

        var first = await service.ObfuscateClosedSetAsync(inputs, firstDir, settings);
        var second = await service.ObfuscateClosedSetAsync(inputs, secondDir, settings);

        first.Success.ShouldBeTrue(first.ErrorMessage);
        second.Success.ShouldBeTrue(second.ErrorMessage);
        var firstNames = MethodNames(Path.Combine(firstDir, "LibA.dll"));
        var secondNames = MethodNames(Path.Combine(secondDir, "LibA.dll"));
        firstNames.ShouldBe(secondNames);
        firstNames.ShouldContain("Alpha.a");
        firstNames.ShouldNotContain("Alpha.Hidden");
    }

    [Fact]
    public async Task ObfuscateBatchAsync_AppAndLib_RenamesTogetherAndStillRuns()
    {
        var libPath = CompileLibrary("Lib", """
            namespace Lib.Api;
            public class Greeter
            {
                public static string Hello() => "hi";
            }
            """);
        var appPath = CompileExe("App", """
            using Lib.Api;
            public static class Program
            {
                public static void Main() { }
                public static string Run() => Greeter.Hello();
            }
            """, libPath);
        var outputDir = Path.Combine(_tempDirectory, "batch-out");
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            StringEncryption = { Enabled = false },
            ControlFlow = { Enabled = false },
            ConstantEncryption = { Enabled = false },
            ResourceEncryption = { Enabled = false },
            SymbolRenaming =
            {
                Enabled = true,
                Mode = NamingMode.Sequential,
                PreservePublicApi = false,
                RenameTypes = true,
                RenameMethods = true
            },
            Metadata = { RemoveDebugInfo = false, RemoveAttributes = false, StripDocumentation = false },
            Protection =
            {
                AntiDebug = false,
                AntiTamper = { Enabled = false },
                AntiDecompiler = { Enabled = false },
                AntiDump = false,
                ReferenceProxy = false,
                MethodEncryption = false
            }
        };

        using var container = BuildContainer();
        var service = container.Resolve<IObfuscationService>();
        var results = await service.ObfuscateBatchAsync([libPath, appPath], outputDir, settings);

        results.Count.ShouldBe(2);
        results.ShouldAllBe(r => r.Success, string.Join("; ", results.Select(r => r.ErrorMessage)));
        var outLib = Path.Combine(outputDir, "Lib.dll");
        var outApp = Path.Combine(outputDir, "App.exe");
        using (var libModule = ModuleDefMD.Load(outLib))
            libModule.GetTypes().ShouldNotContain(t => t.Name == "Greeter");

        InvokeProgramRun(outApp, outLib).ShouldBe("hi");
    }

    private static List<string> MethodNames(string path)
    {
        using var module = ModuleDefMD.Load(path);
        return module.GetTypes()
            .SelectMany(type => type.Methods.Select(method => type.Name + "." + method.Name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
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
        => Compile(assemblyName, source, OutputKind.DynamicallyLinkedLibrary);

    private string CompileExe(string assemblyName, string source, string referencePath)
        => Compile(assemblyName, source, OutputKind.ConsoleApplication, referencePath);

    private string Compile(string assemblyName, string source, OutputKind kind, string? referencePath = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        if (referencePath != null)
            references = references.Append(MetadataReference.CreateFromFile(referencePath));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(kind));

        var extension = kind == OutputKind.ConsoleApplication ? ".exe" : ".dll";
        var path = Path.Combine(_tempDirectory, assemblyName + extension);
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static string InvokeProgramRun(string appPath, string libPath)
    {
        var alc = new AssemblyLoadContext($"batch-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            alc.LoadFromAssemblyPath(libPath);
            var asm = alc.LoadFromAssemblyPath(appPath);
            var program = asm.GetTypes().Single(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Any(IsRunLike));
            var run = program.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Single(IsRunLike);
            return (string)run.Invoke(null, null)!;
        }
        finally
        {
            alc.Unload();
        }
    }

    private static bool IsRunLike(MethodInfo method) =>
        method.IsStatic
        && method.ReturnType == typeof(string)
        && method.GetParameters().Length == 0
        && method.Name != "Main";
}

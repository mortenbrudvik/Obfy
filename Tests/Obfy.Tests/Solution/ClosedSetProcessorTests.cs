using System.Reflection;
using System.Runtime.Loader;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Solution;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests.Solution;

public class ClosedSetProcessorTests
{
    private const string LibSource = """
        namespace Lib.Api;
        public class Greeter
        {
            public static string Hello() => "hi";
        }
        """;

    // ConsoleApplication requires an entry point; Main is otherwise unused.
    private const string AppSource = """
        using Lib.Api;
        public static class Program
        {
            public static void Main() { }
            public static string Run() => Greeter.Hello();
        }
        """;

    [Fact]
    public void RenameClosedSet_RewritesLibTypeRefsInApp_AndProgramRunStillReturnsHi()
    {
        using var fixture = new ClosedSetEmit();
        var (libModule, appModule) = fixture.LoadClosedSet();
        var greeter = libModule.GetTypes().Single(t => t.Name == "Greeter");
        var greeterRef = appModule.GetTypeRefs().Single(r => r.Name == "Greeter");

        var libSettings = ClosedSetRenameSettings(preservePublicApi: false);
        var appSettings = ClosedSetRenameSettings(preservePublicApi: false);
        var shared = PipelineContext.ForAssembly(libModule, libSettings);
        var renamer = CreateRenamer();

        renamer.RenameClosedSet([(libModule, libSettings), (appModule, appSettings)], shared);

        greeter.Name.String.ShouldNotBe("Greeter");
        greeterRef.Name.ShouldBe(greeter.Name);
        greeterRef.Namespace.ShouldBe(greeter.Namespace);

        var (libPath, appPath) = fixture.Write(libModule, appModule);
        InvokeProgramRun(appPath, libPath).ShouldBe("hi");
    }

    [Fact]
    public void RenameClosedSet_PreservePublicApi_KeepsGreeterName()
    {
        using var fixture = new ClosedSetEmit();
        var (libModule, appModule) = fixture.LoadClosedSet();
        var greeter = libModule.GetTypes().Single(t => t.Name == "Greeter");
        var greeterRef = appModule.GetTypeRefs().Single(r => r.Name == "Greeter");

        var libSettings = ClosedSetRenameSettings(preservePublicApi: true);
        var appSettings = ClosedSetRenameSettings(preservePublicApi: true);
        var shared = PipelineContext.ForAssembly(libModule, libSettings);
        var renamer = CreateRenamer();

        renamer.RenameClosedSet([(libModule, libSettings), (appModule, appSettings)], shared);

        greeter.Name.String.ShouldBe("Greeter");
        greeterRef.Name.String.ShouldBe("Greeter");
        greeter.Namespace.String.ShouldBe("Lib.Api");

        var (libPath, appPath) = fixture.Write(libModule, appModule);
        InvokeProgramRun(appPath, libPath).ShouldBe("hi");
    }

    [Fact]
    public async Task ExecuteAsync_AppAndLib_WritesBoth_RenamesLibPublicType_AndRunStillReturnsHi()
    {
        using var fixture = new ClosedSetEmit();
        var (libPath, appPath) = fixture.CompileClosedSet();
        var outputDir = Path.Combine(fixture.Root, "closed-out");

        var processor = CreateProcessor();
        var result = await processor.ExecuteAsync(
            [
                new ClosedSetInput { AssemblyPath = libPath, Hints = new ProjectSettingsHints { PreservePublicApi = true } },
                new ClosedSetInput { AssemblyPath = appPath, Hints = new ProjectSettingsHints() }
            ],
            outputDir,
            ClosedSetRenameSettings(preservePublicApi: false));

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.LoadFailures.ShouldBeEmpty();

        var outLib = Path.Combine(outputDir, "Lib.dll");
        var outApp = Path.Combine(outputDir, "App.exe");
        File.Exists(outLib).ShouldBeTrue();
        File.Exists(outApp).ShouldBeTrue();

        using (var libModule = ModuleDefMD.Load(await File.ReadAllBytesAsync(outLib)))
            libModule.GetTypes().ShouldNotContain(t => t.Name == "Greeter");

        InvokeProgramRun(outApp, outLib).ShouldBe("hi");
    }

    [Fact]
    public async Task ExecuteAsync_CorruptSecondInput_ListsLoadFailure_AndPreservesLibPublicApi()
    {
        using var fixture = new ClosedSetEmit();
        var libPath = fixture.CompileLib();
        var badPath = Path.Combine(fixture.Root, "Bad.dll");
        await File.WriteAllTextAsync(badPath, "not a valid assembly");
        var outputDir = Path.Combine(fixture.Root, "load-fail-out");

        var processor = CreateProcessor();
        var result = await processor.ExecuteAsync(
            [
                new ClosedSetInput { AssemblyPath = libPath, Hints = new ProjectSettingsHints { PreservePublicApi = false } },
                new ClosedSetInput { AssemblyPath = badPath, Hints = new ProjectSettingsHints() }
            ],
            outputDir,
            ClosedSetRenameSettings(preservePublicApi: false));

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.LoadFailures.ShouldContain(badPath);

        var outLib = Path.Combine(outputDir, "Lib.dll");
        File.Exists(outLib).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "Bad.dll")).ShouldBeFalse();

        using var libModule = ModuleDefMD.Load(await File.ReadAllBytesAsync(outLib));
        libModule.GetTypes().ShouldContain(t => t.Name == "Greeter");
    }

    [Fact]
    public async Task ExecuteAsync_PipelineFailure_WritesNothingToOutputDirectory()
    {
        using var fixture = new ClosedSetEmit();
        var (libPath, appPath) = fixture.CompileClosedSet();
        var outputDir = Path.Combine(fixture.Root, "all-or-nothing");
        Directory.CreateDirectory(outputDir);
        var existingLib = Path.Combine(outputDir, "Lib.dll");
        await File.WriteAllTextAsync(existingLib, "keep-me");

        var failingPipeline = new Mock<IObfuscationPipeline>();
        failingPipeline.SetupSequence(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics()))
            .ReturnsAsync(ObfuscationResult.Failed("boom"));

        var processor = CreateProcessor(failingPipeline.Object);
        var result = await processor.ExecuteAsync(
            [
                new ClosedSetInput { AssemblyPath = libPath, Hints = new ProjectSettingsHints() },
                new ClosedSetInput { AssemblyPath = appPath, Hints = new ProjectSettingsHints() }
            ],
            outputDir,
            ClosedSetRenameSettings(preservePublicApi: false));

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("boom");
        (await File.ReadAllTextAsync(existingLib)).ShouldBe("keep-me");
        File.Exists(Path.Combine(outputDir, "App.exe")).ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CommitFailure_RestoresPreExistingOutputFiles()
    {
        using var fixture = new ClosedSetEmit();
        var libPath = fixture.CompileLib();
        var net8 = Path.Combine(fixture.Root, "net8.0");
        var net9 = Path.Combine(fixture.Root, "net9.0");
        Directory.CreateDirectory(net8);
        Directory.CreateDirectory(net9);
        var lib8 = Path.Combine(net8, "Lib.dll");
        var lib9 = Path.Combine(net9, "Lib.dll");
        File.Copy(libPath, lib8);
        File.Copy(libPath, lib9);

        var outputDir = Path.Combine(fixture.Root, "commit-fail");
        var net8Out = Path.Combine(outputDir, "net8.0");
        Directory.CreateDirectory(net8Out);
        var existing = Path.Combine(net8Out, "Lib.dll");
        await File.WriteAllTextAsync(existing, "original-lib");
        await File.WriteAllTextAsync(Path.Combine(outputDir, "net9.0"), "not-a-directory");

        var processor = CreateProcessor();
        var result = await processor.ExecuteAsync(
            [
                new ClosedSetInput { AssemblyPath = lib8, Hints = new ProjectSettingsHints() },
                new ClosedSetInput { AssemblyPath = lib9, Hints = new ProjectSettingsHints() }
            ],
            outputDir,
            ClosedSetRenameSettings(preservePublicApi: true));

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        (await File.ReadAllTextAsync(existing)).ShouldBe("original-lib");
    }

    [Fact]
    public async Task ExecuteAsync_SymbolRenamingDisabled_KeepsGreeter()
    {
        using var fixture = new ClosedSetEmit();
        var (libPath, appPath) = fixture.CompileClosedSet();
        var outputDir = Path.Combine(fixture.Root, "rename-off");
        var settings = ClosedSetRenameSettings(preservePublicApi: false);
        settings.SymbolRenaming.Enabled = false;

        var processor = CreateProcessor();
        var result = await processor.ExecuteAsync(
            [
                new ClosedSetInput { AssemblyPath = libPath, Hints = new ProjectSettingsHints { PreservePublicApi = true } },
                new ClosedSetInput { AssemblyPath = appPath, Hints = new ProjectSettingsHints() }
            ],
            outputDir,
            settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        using var libModule = ModuleDefMD.Load(await File.ReadAllBytesAsync(Path.Combine(outputDir, "Lib.dll")));
        libModule.GetTypes().ShouldContain(t => t.Name == "Greeter");
    }

    [Fact]
    public async Task ExecuteAsync_GatingWarnings_ArePerModule_AndCopiedToModuleResults()
    {
        using var fixture = new ClosedSetEmit();
        var (libPath, appPath) = fixture.CompileClosedSet();
        var outputDir = Path.Combine(fixture.Root, "gating-warn");
        var settings = ClosedSetRenameSettings(preservePublicApi: false);
        settings.Protection.ProxyExternalCalls = true;
        settings.Protection.ReferenceProxy = false;

        var processor = CreateProcessor();
        var result = await processor.ExecuteAsync(
            [
                new ClosedSetInput { AssemblyPath = libPath, Hints = new ProjectSettingsHints() },
                new ClosedSetInput { AssemblyPath = appPath, Hints = new ProjectSettingsHints() }
            ],
            outputDir,
            settings);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.ModuleResults.Count.ShouldBe(2);
        foreach (var module in result.ModuleResults)
        {
            module.Warnings.Count(w => w.Contains("proxyExternalCalls", StringComparison.Ordinal)).ShouldBe(1);
        }
    }

    private static ClosedSetProcessor CreateProcessor(IObfuscationPipeline? pipeline = null)
    {
        if (pipeline is null)
        {
            var pipelineMock = new Mock<IObfuscationPipeline>();
            pipelineMock.Setup(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics()));
            pipeline = pipelineMock.Object;
        }

        return new ClosedSetProcessor(
            new Mock<ILogger<ClosedSetProcessor>>().Object,
            pipeline,
            new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object),
            CreateRenamer());
    }

    private static SymbolRenamingObfuscator CreateRenamer() =>
        new(new NameGenerator(), new Mock<ILogger<SymbolRenamingObfuscator>>().Object);

    private static ObfySettings ClosedSetRenameSettings(bool preservePublicApi) => new()
    {
        SymbolRenaming =
        {
            Enabled = true,
            PreservePublicApi = preservePublicApi,
            Mode = NamingMode.Sequential,
            RenameTypes = true,
            RenameMethods = true
        }
    };

    private static string InvokeProgramRun(string appPath, string libPath)
    {
        var alc = new AssemblyLoadContext($"closed-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            alc.LoadFromAssemblyPath(libPath);
            var asm = alc.LoadFromAssemblyPath(appPath);
            var program = asm.GetType("Program")
                ?? asm.GetTypes().Single(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Any(IsRunLike));
            var run = program.GetMethod("Run", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? program.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Single(IsRunLike);
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

    private sealed class ClosedSetEmit : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"obfy-closed-{Guid.NewGuid():N}");
        private ModuleDefMD? _lib;
        private ModuleDefMD? _app;

        public string Root => _root;

        public ClosedSetEmit() => Directory.CreateDirectory(_root);

        public (string LibPath, string AppPath) CompileClosedSet()
        {
            var libPath = Compile(LibSource, "Lib", OutputKind.DynamicallyLinkedLibrary);
            var appPath = Compile(AppSource, "App", OutputKind.ConsoleApplication, libPath);
            return (libPath, appPath);
        }

        public string CompileLib() => Compile(LibSource, "Lib", OutputKind.DynamicallyLinkedLibrary);

        public (ModuleDefMD Lib, ModuleDefMD App) LoadClosedSet()
        {
            var (libPath, appPath) = CompileClosedSet();

            var ctx = ModuleDef.CreateModuleContext();
            _lib = ModuleDefMD.Load(File.ReadAllBytes(libPath), ctx);
            _app = ModuleDefMD.Load(File.ReadAllBytes(appPath), ctx);
            var resolver = (AssemblyResolver)ctx.AssemblyResolver;
            resolver.AddToCache(_lib);
            resolver.AddToCache(_app);
            return (_lib, _app);
        }

        public (string LibPath, string AppPath) Write(ModuleDef libModule, ModuleDef appModule)
        {
            var outDir = Path.Combine(_root, "out");
            Directory.CreateDirectory(outDir);
            var libPath = Path.Combine(outDir, "Lib.dll");
            var appPath = Path.Combine(outDir, "App.exe");
            libModule.Write(libPath);
            appModule.Write(appPath);
            return (libPath, appPath);
        }

        private string Compile(string source, string assemblyName, OutputKind kind, string? referencePath = null)
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
            var path = Path.Combine(_root, assemblyName + extension);
            var emit = compilation.Emit(path);
            emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            return path;
        }

        public void Dispose()
        {
            _lib?.Dispose();
            _app?.Dispose();
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

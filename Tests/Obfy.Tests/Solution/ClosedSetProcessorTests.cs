using System.Reflection;
using System.Runtime.Loader;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
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

        public ClosedSetEmit() => Directory.CreateDirectory(_root);

        public (ModuleDefMD Lib, ModuleDefMD App) LoadClosedSet()
        {
            var libPath = Compile(LibSource, "Lib", OutputKind.DynamicallyLinkedLibrary);
            var appPath = Compile(AppSource, "App", OutputKind.ConsoleApplication, libPath);

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

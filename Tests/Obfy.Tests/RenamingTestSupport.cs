using System.Reflection;
using System.Runtime.Loader;
using Autofac;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Compile a small library with Roslyn, protect it, run its <c>Driver.Run()</c> and compare the answers: the
/// harness the renaming tests share. <c>Driver</c> is excluded from renaming (see <see cref="DriverAttribute"/>)
/// so the test can call it, and every name it uses is renamed.
/// </summary>
internal static class RenamingTestSupport
{
    public const string DriverAttribute =
        "[System.Reflection.Obfuscation(Feature = \"renaming\", Exclude = true, ApplyToMembers = true)]";

    /// <summary>
    /// <c>rename-only</c> is symbol renaming alone (<c>preserve-public</c> the same with
    /// <c>PreservePublicApi</c>); <c>minimal</c>, <c>standard</c> and <c>aggressive</c> are the presets of the
    /// same name.
    /// </summary>
    public static ObfySettings SettingsFor(string configuration) =>
        configuration switch
        {
            "minimal" => new ObfySettings { Level = ObfuscationLevel.Minimal },
            "standard" => new ObfySettings { Level = ObfuscationLevel.Standard },
            "aggressive" => new ObfySettings { Level = ObfuscationLevel.Aggressive },
            "rename-only" or "preserve-public" => new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                ControlFlow = { Enabled = false },
                Metadata = { RemoveDebugInfo = false, RemoveAttributes = false },
                SymbolRenaming = { Enabled = true, PreservePublicApi = configuration == "preserve-public" }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(configuration), configuration, null)
        };

    public static IObfuscationService CreateService()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build().Resolve<IObfuscationService>();
    }

    private static readonly Lazy<List<MetadataReference>> RuntimeReferences = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList());

    public static string Compile(
        string source,
        string dir,
        string assemblyName,
        OutputKind kind = OutputKind.DynamicallyLinkedLibrary,
        params string[] referencePaths)
    {
        var references = new List<MetadataReference>(RuntimeReferences.Value);
        foreach (var referencePath in referencePaths)
            references.Add(MetadataReference.CreateFromFile(referencePath));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(kind, optimizationLevel: OptimizationLevel.Release, allowUnsafe: true));
        var path = Path.Combine(dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    /// <summary>
    /// Loads the assembly into its own collectible context, runs <c>Driver.Run()</c> and returns what it returned,
    /// or the failure prefixed with <c>!</c> (so a missing member shows up as the answer, not as a crash).
    /// </summary>
    public static string Run(string assemblyPath, string driverType = "Driver")
    {
        var context = new IsolatedLoadContext(assemblyPath);
        try
        {
            var method = context.LoadFromAssemblyPath(assemblyPath).GetType(driverType)!.GetMethod("Run")!;
            return (string)method.Invoke(null, null)!;
        }
        catch (TargetInvocationException ex)
        {
            return "!" + ex.InnerException!.GetType().Name + ": " + ex.InnerException.Message;
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>Resolves siblings of the assembly from its own folder, so each copy sees its own library.</summary>
    private sealed class IsolatedLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;

        public IsolatedLoadContext(string assemblyPath)
            : base("rename-test-" + Guid.NewGuid().ToString("N"), isCollectible: true) =>
            _directory = Path.GetDirectoryName(assemblyPath)!;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var candidate = Path.Combine(_directory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }

    /// <summary>
    /// The three ways the renamer is run on a program, with the sources it is made of: <c>single</c> (one assembly),
    /// <c>closed-set</c> (a library and an application that references it, protected together) and <c>merge</c>
    /// (the same two, merged into one assembly). <paramref name="libSource"/> holds what the library defines and
    /// <paramref name="appSource"/> what the application defines, including <c>Driver</c>; <paramref name="appPreamble"/>
    /// is what has to precede every type of the application (assembly attributes).
    /// </summary>
    public static (List<string> Inputs, string DriverAssembly) BuildShape(
        string shape, string dir, string usings, string libSource, string appPreamble, string appSource)
    {
        const string Main = "public static class Program { public static int Main() => 0; }\n";
        switch (shape)
        {
            case "single":
                {
                    var path = Compile(
                        usings + appPreamble + libSource + appSource + Main, dir, "ShapeApp", OutputKind.ConsoleApplication);
                    return (new List<string> { path }, path);
                }
            case "closed-set":
                {
                    var lib = Compile(usings + libSource, dir, "ShapeLib");
                    var app = Compile(usings + appPreamble + appSource + Main, dir, "ShapeApp", OutputKind.ConsoleApplication, lib);
                    return (new List<string> { app, lib }, app);
                }
            case "merge":
                {
                    var lib = Compile(usings + libSource, dir, "ShapeLib");
                    var app = Compile(usings + appPreamble + appSource, dir, "ShapeApp", OutputKind.DynamicallyLinkedLibrary, lib);
                    return (new List<string> { app, lib }, app);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    /// <summary>
    /// Protects what <see cref="BuildShape"/> built and returns the output files (the application first for
    /// <c>closed-set</c>; the one merged file for <c>merge</c>). Fails the test when the run fails.
    /// </summary>
    public static async Task<List<string>> ProtectAsync(string shape, List<string> inputs, string outDir, ObfySettings settings)
    {
        var service = CreateService();
        switch (shape)
        {
            case "closed-set":
                {
                    var result = await service.ObfuscateClosedSetAsync(
                        inputs.Select(i => ClosedSetInput.From(i)).ToList(), outDir, settings);
                    result.Success.ShouldBeTrue(result.ErrorMessage);
                    return inputs.Select(i => Path.Combine(outDir, Path.GetFileName(i))).ToList();
                }
            case "merge":
                {
                    Directory.CreateDirectory(outDir);
                    var output = Path.Combine(outDir, "Merged.dll");
                    var result = await service.MergeAndObfuscateAsync(inputs, output, settings);
                    result.Success.ShouldBeTrue(result.ErrorMessage);
                    return new List<string> { output };
                }
            default:
                {
                    Directory.CreateDirectory(outDir);
                    var output = Path.Combine(outDir, Path.GetFileName(inputs[0]));
                    var result = await service.ObfuscateAsync(inputs[0], output, settings);
                    result.Success.ShouldBeTrue(result.ErrorMessage);
                    return new List<string> { output };
                }
        }
    }

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* ignore */ }
    }
}

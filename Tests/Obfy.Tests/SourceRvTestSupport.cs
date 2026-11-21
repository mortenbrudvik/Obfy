using System.Reflection;
using System.Runtime.Loader;
using Autofac;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Source;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Shared helpers for the source-obfuscator regression tests (string encryption, control flow,
/// symbol renaming, source processor). Compiles with the full framework reference set so the
/// obfuscated output is validated the way a user's build would.
/// </summary>
internal static class SourceRvTestSupport
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> FullReferences = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList());

    public static CSharpCompilation CreateCompilation(string sourceCode, bool allowUnsafe = false) =>
        CreateCompilation(new[] { ("input.cs", sourceCode) }, allowUnsafe);

    public static CSharpCompilation CreateCompilation(IEnumerable<(string Path, string Source)> files, bool allowUnsafe = false)
    {
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.Path)).ToList();
        return CSharpCompilation.Create(
            "RvTestAssembly",
            trees,
            FullReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));
    }

    /// <summary>
    /// Compile errors of the output as WRITTEN: every tree is printed to text and parsed again first.
    /// Nodes built with SyntaxFactory carry no whitespace of their own, so a tree that compiles in memory
    /// can still print as invalid source ("thrownew", "int__state") or misparse (a "::" in an
    /// interpolation hole).
    /// </summary>
    public static IReadOnlyList<Diagnostic> GetErrors(Compilation compilation) =>
        RoundTrip(compilation).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    public static Compilation RoundTrip(Compilation compilation)
    {
        var trees = compilation.SyntaxTrees
            .Select(t => (SyntaxTree)CSharpSyntaxTree.ParseText(
                t.GetRoot().ToFullString(), (CSharpParseOptions)t.Options, t.FilePath))
            .ToList();
        return compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(trees);
    }

    public static string ErrorText(IEnumerable<Diagnostic> errors) =>
        string.Join("\n", errors.Select(e => e.ToString()));

    public static ObfySettings StringSettings(int minLength = 3) =>
        new() { StringEncryption = { Enabled = true, MinStringLength = minLength } };

    public static async Task<PipelineContext> EncryptStringsAsync(string source, ObfySettings? settings = null)
    {
        var context = PipelineContext.ForSourceCode(CreateCompilation(source), settings ?? StringSettings());
        var result = await new SourceStringEncryptor(NullLogger<SourceStringEncryptor>.Instance).ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        return context;
    }

    /// <summary>Resolves the real <see cref="IObfuscationService"/> (no mocks).</summary>
    public static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build();
    }

    /// <summary>Settings that run only the named source obfuscators.</summary>
    public static ObfySettings OnlySettings(bool strings = false, bool controlFlow = false, bool renaming = false) => new()
    {
        Level = ObfuscationLevel.Custom,
        StringEncryption = { Enabled = strings, MinStringLength = 3 },
        ControlFlow = { Enabled = controlFlow, Mode = ControlFlowMode.Switch, Intensity = 100 },
        SymbolRenaming = { Enabled = renaming, Mode = NamingMode.Sequential }
    };

    public static string NewTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-rvsrc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void DeleteQuietly(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static string SourceOf(PipelineContext context) =>
        string.Join("\n", context.Compilation!.SyntaxTrees.Select(t => t.ToString()));

    /// <summary>
    /// Emits the compilation and invokes <paramref name="typeName"/>.<paramref name="methodName"/>.
    /// A fresh instance is created for instance methods. Fails the test if the code does not compile.
    /// </summary>
    public static object? Invoke(Compilation compilation, string typeName, string methodName, params object?[] args)
    {
        using var ms = new MemoryStream();
        var emit = RoundTrip(compilation).Emit(ms);
        emit.Success.ShouldBeTrue(
            "Output failed to compile:\n" +
            ErrorText(emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        ms.Position = 0;
        var alc = new AssemblyLoadContext($"rv-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var asm = alc.LoadFromStream(ms);
            var type = asm.GetTypes().First(t => t.Name == typeName);
            var method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;
            var instance = method.IsStatic ? null : Activator.CreateInstance(type, nonPublic: true);
            try
            {
                return method.Invoke(instance, args);
            }
            catch (TargetInvocationException ex)
            {
                // Surface the real exception from the obfuscated program.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                throw;
            }
        }
        finally
        {
            alc.Unload();
        }
    }
}

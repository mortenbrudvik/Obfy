using System.Security.Cryptography;
using Autofac;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Tests.CoreServices;

/// <summary>Everything the capturing loggers saw, with the category (the logger's type name).</summary>
internal sealed class LogSink
{
    private readonly List<(string Category, LogLevel Level, string Text)> _entries = [];

    /// <summary>Categories whose logging belongs to the orchestration services under test.</summary>
    public static readonly string[] OwnedCategories =
    [
        nameof(Obfy.Core.Services.ObfuscationService),
        nameof(Obfy.Core.Services.Solution.ClosedSetProcessor),
        nameof(Obfy.Core.Services.AssemblyMerger),
        nameof(Obfy.Core.Pipeline.ObfuscationPipeline),
        nameof(Obfy.Core.Services.Reporting.HtmlReportGenerator),
        nameof(Obfy.Core.Services.Reporting.JsonReportGenerator)
    ];

    public void Add(string category, LogLevel level, string text)
    {
        lock (_entries)
            _entries.Add((category, level, text));
    }

    /// <summary>Rendered message plus the full exception text (message and stack) of every owned entry.</summary>
    public IReadOnlyList<string> OwnedTexts()
    {
        lock (_entries)
        {
            return _entries
                .Where(e => OwnedCategories.Contains(e.Category))
                .Select(e => $"[{e.Category}/{e.Level}] {e.Text}")
                .ToList();
        }
    }

    public int OwnedCount()
    {
        lock (_entries)
            return _entries.Count(e => OwnedCategories.Contains(e.Category));
    }

    public void ShouldHaveLoggedNoneOf(params string[] forbidden)
    {
        var texts = OwnedTexts();
        foreach (var needle in forbidden)
        {
            texts.Where(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ShouldBeEmpty($"the log must not contain '{needle}'");
        }
    }
}

internal sealed class CapturingLogger<T>(LogSink sink) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var text = formatter(state, exception);
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            text += " | " + string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"));
        if (exception is not null)
            text += " | EXCEPTION: " + exception;
        sink.Add(typeof(T).Name, logLevel, text);
    }
}

internal static class CoreServicesTestSupport
{
    public static IContainer BuildContainer(LogSink? sink = null)
    {
        var builder = new ContainerBuilder();
        if (sink is null)
        {
            builder.RegisterGeneric(typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>))
                .As(typeof(ILogger<>)).SingleInstance();
        }
        else
        {
            builder.RegisterInstance(sink);
            builder.RegisterGeneric(typeof(CapturingLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        }

        builder.RegisterModule<ObfuscationModule>();
        return builder.Build();
    }

    public static string NewTempDirectory(string prefix = "obfy-core-svc")
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void DeleteQuietly(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static string Compile(
        string directory, string assemblyName, string source, OutputKind kind, params string[] referencePaths)
    {
        Directory.CreateDirectory(directory);
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Concat(referencePaths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)));

        var compilation = CSharpCompilation.Create(assemblyName, [tree], references, new CSharpCompilationOptions(kind));
        var extension = kind == OutputKind.ConsoleApplication ? ".exe" : ".dll";
        var path = Path.Combine(directory, assemblyName + extension);
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    public static string CompileLibrary(string directory, string assemblyName, string source, params string[] referencePaths)
        => Compile(directory, assemblyName, source, OutputKind.DynamicallyLinkedLibrary, referencePaths);

    public static string CompileExe(string directory, string assemblyName, string source, params string[] referencePaths)
        => Compile(directory, assemblyName, source, OutputKind.ConsoleApplication, referencePaths);

    public static string WriteSnk(string path)
    {
#pragma warning disable SYSLIB0028, CA1416
        var cspParams = new CspParameters { KeyNumber = (int)KeyNumber.Signature };
        using var csp = new RSACryptoServiceProvider(1024, cspParams);
        File.WriteAllBytes(path, csp.ExportCspBlob(includePrivateParameters: true));
#pragma warning restore SYSLIB0028, CA1416
        return path;
    }

    /// <summary>A Custom settings bag with every technique off, so a test turns on only what it exercises.</summary>
    public static ObfySettings AllOff()
    {
        return new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            StringEncryption = { Enabled = false },
            ControlFlow = { Enabled = false },
            ConstantEncryption = { Enabled = false },
            ResourceEncryption = { Enabled = false },
            SymbolRenaming = { Enabled = false },
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
    }
}

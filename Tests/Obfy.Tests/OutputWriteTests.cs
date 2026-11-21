using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Autofac;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// How the obfuscated assembly reaches disk: no orphaned names/literals from the original heaps,
/// and an atomic write (temp file + replace) so a failure never truncates or deletes an existing
/// target, including the documented in-place case (<c>-o</c> pointing at the input's directory).
/// </summary>
public class OutputWriteTests
{
    private const string SecretSource = """
        public class SecretNamesHolder
        {
            public string UniqueFieldNameQwerty = "x";
            public string RevealSecretValue() { return "super-secret-literal-12345"; }
        }
        """;

    private const string SimpleLib = "public static class Lib { public static int Get() => 42; }";

    private static string NewDir(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-outwrite-{tag}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* ignore */ }
    }

    private static string Compile(string source, string dir, string assemblyName, OutputKind kind = OutputKind.DynamicallyLinkedLibrary)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            assemblyName, new[] { tree }, references, new CSharpCompilationOptions(kind));
        var ext = kind == OutputKind.ConsoleApplication ? ".exe" : ".dll";
        var path = Path.Combine(dir, assemblyName + ext);
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static IObfuscationService CreateService()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build().Resolve<IObfuscationService>();
    }

    private static bool ContainsAscii(byte[] bytes, string text) =>
        bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    private static bool ContainsUtf16(byte[] bytes, string text) =>
        bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)) >= 0;

    private static void WriteSnk(string path)
    {
#pragma warning disable SYSLIB0028, CA1416
        var cspParams = new CspParameters { KeyNumber = (int)KeyNumber.Signature };
        using var csp = new RSACryptoServiceProvider(1024, cspParams);
        File.WriteAllBytes(path, csp.ExportCspBlob(includePrivateParameters: true));
#pragma warning restore SYSLIB0028, CA1416
    }

    private static string InvokeFirstStringReturningInstanceMethod(string assemblyPath)
    {
        var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(assemblyPath);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var type in asm.GetTypes())
            {
                var method = type.GetMethods(flags).FirstOrDefault(m => m.ReturnType == typeof(string) && m.GetParameters().Length == 0);
                if (method is null)
                    continue;
                var target = Activator.CreateInstance(type);
                return (string)method.Invoke(target, null)!;
            }

            throw new InvalidOperationException("No string-returning instance method found.");
        }
        finally
        {
            alc.Unload();
        }
    }

    private static object? LoadAndInvoke(string assemblyPath, string typeName, string methodName)
    {
        var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(assemblyPath);
            var type = asm.GetType(typeName);
            type.ShouldNotBeNull();
            var method = type!.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            method.ShouldNotBeNull();
            return method!.Invoke(method.IsStatic ? null : Activator.CreateInstance(type), null);
        }
        finally
        {
            alc.Unload();
        }
    }

    private static ObfySettings RenameAndEncryptSettings() => new()
    {
        Level = ObfuscationLevel.Custom,
        StringEncryption = { Enabled = true, MinStringLength = 3 },
        SymbolRenaming = { Enabled = true, PreservePublicApi = false }
    };

    // ---- Finding 1: orphaned heap content -----------------------------------------------------

    [Fact]
    public async Task Output_DoesNotContainOriginalNamesOrPlaintextLiterals()
    {
        var dir = NewDir("heaps");
        try
        {
            var input = Compile(SecretSource, dir, "SecretLib");
            var inputBytes = File.ReadAllBytes(input);
            // Sanity: the unobfuscated input does contain them, so the scan below is meaningful.
            ContainsAscii(inputBytes, "SecretNamesHolder").ShouldBeTrue();
            ContainsUtf16(inputBytes, "super-secret-literal-12345").ShouldBeTrue();

            var output = Path.Combine(dir, "SecretLib.obf.dll");
            var result = await CreateService().ObfuscateAsync(input, output, RenameAndEncryptSettings());
            result.Success.ShouldBeTrue(result.ErrorMessage);

            var bytes = File.ReadAllBytes(output);
            ContainsAscii(bytes, "SecretNamesHolder").ShouldBeFalse("original type name left in the #Strings heap");
            ContainsAscii(bytes, "UniqueFieldNameQwerty").ShouldBeFalse("original field name left in the #Strings heap");
            ContainsAscii(bytes, "RevealSecretValue").ShouldBeFalse("original method name left in the #Strings heap");
            ContainsUtf16(bytes, "super-secret-literal-12345").ShouldBeFalse("plaintext literal left in the #US heap");
            ContainsAscii(bytes, "super-secret-literal-12345").ShouldBeFalse();

            InvokeFirstStringReturningInstanceMethod(output).ShouldBe("super-secret-literal-12345");
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Output_WithMethodEncryptionAntiTamperAndSigning_StillRunsStaysSignedAndLeaksNothing()
    {
        var dir = NewDir("heaps-features");
        try
        {
            var snk = Path.Combine(dir, "test.snk");
            WriteSnk(snk);
            var input = Compile(SecretSource, dir, "SecretFeatLib");
            var output = Path.Combine(dir, "SecretFeatLib.obf.dll");

            var settings = RenameAndEncryptSettings();
            settings.Protection.MethodEncryption = true;
            settings.Protection.AntiTamper.Enabled = true;
            settings.Signing.Enabled = true;
            settings.Signing.KeyFile = snk;

            var result = await CreateService().ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            var bytes = File.ReadAllBytes(output);
            ContainsAscii(bytes, "SecretNamesHolder").ShouldBeFalse();
            ContainsAscii(bytes, "RevealSecretValue").ShouldBeFalse();
            ContainsUtf16(bytes, "super-secret-literal-12345").ShouldBeFalse();

            using (var loaded = ModuleDefMD.Load(bytes))
                loaded.IsStrongNameSigned.ShouldBeTrue();

            InvokeFirstStringReturningInstanceMethod(output).ShouldBe("super-secret-literal-12345");
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Output_NativePacked_WithRenamingStringEncryptionAndAntiTamper_StillRuns()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dir = NewDir("heaps-packed");
        try
        {
            const string source = """
                public static class Program
                {
                    private static string Secret() { return "packed-secret-literal-777"; }
                    public static int Main() { return Secret().Length; }
                }
                """;
            var input = Compile(source, dir, "PackedFeat", OutputKind.ConsoleApplication);
            var output = Path.Combine(dir, "PackedFeat.obf.exe");
            var settings = RenameAndEncryptSettings();
            settings.Protection.AntiTamper.Enabled = true;
            settings.Packing.Enabled = true;

            var result = await CreateService().ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(output)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            })!;
            var stderr = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            process.WaitForExit(30000).ShouldBeTrue("packed exe timed out");
            process.ExitCode.ShouldBe("packed-secret-literal-777".Length, stderr.GetAwaiter().GetResult());
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    // ---- Findings 2 and 3: atomic write, in-place -------------------------------------------

    private sealed class ThrowingPostProcessor : IPePostProcessor
    {
        public int Order => 5;
        public string? SeenPath { get; private set; }

        public void Process(string pePath, PipelineContext context)
        {
            SeenPath = pePath;
            throw new IOException("post-processor failed");
        }
    }

    private sealed class LockingThenThrowingPostProcessor : IPePostProcessor, IDisposable
    {
        private FileStream? _lock;
        public int Order => 5;

        public void Process(string pePath, PipelineContext context)
        {
            _lock = new FileStream(pePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            throw new IOException("post-processor failed");
        }

        public void Dispose() => _lock?.Dispose();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
            if (exception is not null)
                Lines.Add(exception.ToString());
        }
    }

    private static async Task<PipelineContext> LoadAsync(AssemblyProcessor processor, string path) =>
        await processor.LoadAsync(path, new ObfySettings { Level = ObfuscationLevel.Custom });

    private static string[] StrayFiles(string dir, params string[] expected)
    {
        var keep = expected.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.GetFiles(dir).Where(f => !keep.Contains(Path.GetFullPath(f))).ToArray();
    }

    [Fact]
    public async Task Save_PostProcessorFailure_LeavesExistingTargetUntouchedAndNoTempFile()
    {
        var dir = NewDir("atomic-post");
        try
        {
            var input = Compile(SimpleLib, dir, "AtomicLib");
            var target = Path.Combine(dir, "target.dll");
            var sentinel = Encoding.UTF8.GetBytes("PREEXISTING TARGET CONTENT");
            File.WriteAllBytes(target, sentinel);

            var processor = new AssemblyProcessor(NullLogger<AssemblyProcessor>.Instance, new IPePostProcessor[] { new ThrowingPostProcessor() });
            var context = await LoadAsync(processor, input);

            var ex = await Should.ThrowAsync<Exception>(() => processor.SaveAsync(context, target));
            ex.Message.ShouldContain("not modified");

            File.ReadAllBytes(target).ShouldBe(sentinel);
            StrayFiles(dir, input, target).ShouldBeEmpty();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Save_PostProcessorFailure_WithoutExistingTarget_LeavesNoFileBehind()
    {
        var dir = NewDir("atomic-post-new");
        try
        {
            var input = Compile(SimpleLib, dir, "AtomicNewLib");
            var target = Path.Combine(dir, "fresh.dll");

            var processor = new AssemblyProcessor(NullLogger<AssemblyProcessor>.Instance, new IPePostProcessor[] { new ThrowingPostProcessor() });
            var context = await LoadAsync(processor, input);

            await Should.ThrowAsync<IOException>(() => processor.SaveAsync(context, target));

            File.Exists(target).ShouldBeFalse("a half-processed PE must not be left at the output path");
            StrayFiles(dir, input).ShouldBeEmpty();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Save_WriterFailure_LeavesExistingTargetUntouched()
    {
        var dir = NewDir("atomic-writer");
        try
        {
            var input = Compile(SimpleLib, dir, "AtomicWriterLib");
            var target = Path.Combine(dir, "target.dll");
            var sentinel = Encoding.UTF8.GetBytes("PREEXISTING TARGET CONTENT");
            File.WriteAllBytes(target, sentinel);

            var processor = new AssemblyProcessor(NullLogger<AssemblyProcessor>.Instance);
            var context = await LoadAsync(processor, input);

            // An operand dnlib cannot encode makes Module.Write throw after it has opened its output.
            var method = context.Module!.GetTypes().SelectMany(t => t.Methods).First(m => m.Name == "Get");
            method.Body.Instructions.Insert(0, new Instruction(OpCodes.Call, new object()));

            await Should.ThrowAsync<Exception>(() => processor.SaveAsync(context, target));

            File.ReadAllBytes(target).ShouldBe(sentinel);
            StrayFiles(dir, input, target).ShouldBeEmpty();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Save_InPlace_PostProcessorFailure_LeavesTheInputIntact()
    {
        // -o <input dir> makes output == input (the documented MSBuild AfterBuild integration).
        var dir = NewDir("inplace-fail");
        try
        {
            var input = Compile(SimpleLib, dir, "InPlaceFailLib");
            var original = File.ReadAllBytes(input);

            var processor = new AssemblyProcessor(NullLogger<AssemblyProcessor>.Instance, new IPePostProcessor[] { new ThrowingPostProcessor() });
            var context = await LoadAsync(processor, input);

            await Should.ThrowAsync<Exception>(() => processor.SaveAsync(context, input));

            File.ReadAllBytes(input).ShouldBe(original);
            StrayFiles(dir, input).ShouldBeEmpty();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Obfuscate_InPlace_ReplacesTheInputWithTheObfuscatedAssembly()
    {
        var dir = NewDir("inplace-ok");
        try
        {
            var input = Compile(SecretSource, dir, "InPlaceLib");
            var original = File.ReadAllBytes(input);

            // Same path for input and output, spelled differently to prove the comparison is not textual.
            var sameFile = Path.Combine(dir, ".", "InPlaceLib.dll");
            var result = await CreateService().ObfuscateAsync(input, sameFile, RenameAndEncryptSettings());
            result.Success.ShouldBeTrue(result.ErrorMessage);

            var bytes = File.ReadAllBytes(input);
            bytes.ShouldNotBe(original);
            ContainsAscii(bytes, "SecretNamesHolder").ShouldBeFalse();
            ContainsUtf16(bytes, "super-secret-literal-12345").ShouldBeFalse();
            InvokeFirstStringReturningInstanceMethod(input).ShouldBe("super-secret-literal-12345");
            StrayFiles(dir, input).ShouldBeEmpty();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Save_Success_ReplacesExistingTargetAndLeavesNoTempFile()
    {
        var dir = NewDir("atomic-ok");
        try
        {
            var input = Compile(SimpleLib, dir, "AtomicOkLib");
            var target = Path.Combine(dir, "target.dll");
            File.WriteAllBytes(target, Encoding.UTF8.GetBytes("OLD"));

            var processor = new AssemblyProcessor(NullLogger<AssemblyProcessor>.Instance);
            var context = await LoadAsync(processor, input);
            await processor.SaveAsync(context, target);

            LoadAndInvoke(target, "Lib", "Get").ShouldBe(42);
            StrayFiles(dir, input, target).ShouldBeEmpty();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    // ---- Finding 6: privacy of AssemblyProcessor log lines -----------------------------------

    [Fact]
    public async Task Logs_DoNotContainPathsOrExceptionText_OnSuccessOrFailure()
    {
        var dir = NewDir("privacy-" + "UniqueDirMarker7431");
        try
        {
            var input = Compile(SimpleLib, dir, "PrivacyLib");
            var target = Path.Combine(dir, "PrivacyTarget.dll");

            var logger = new RecordingLogger<AssemblyProcessor>();
            var locker = new LockingThenThrowingPostProcessor();
            try
            {
                // Failure path: the temp file stays locked, so its cleanup logs a warning as well.
                var failing = new AssemblyProcessor(logger, new IPePostProcessor[] { locker });
                var failContext = await LoadAsync(failing, input);
                await Should.ThrowAsync<Exception>(() => failing.SaveAsync(failContext, target));
            }
            finally
            {
                locker.Dispose();
            }

            var ok = new AssemblyProcessor(logger);
            var okContext = await LoadAsync(ok, input);
            await ok.SaveAsync(okContext, target);

            logger.Lines.ShouldNotBeEmpty();
            logger.Lines.ShouldContain(l => l.Contains("temporary output file (IOException)"),
                "the locked temp file must produce the type-only cleanup warning");
            foreach (var line in logger.Lines)
            {
                line.ShouldNotContain("UniqueDirMarker7431");
                line.ShouldNotContain("PrivacyTarget");
                line.ShouldNotContain("PrivacyLib");
                line.ShouldNotContain("post-processor failed");
            }
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }
}

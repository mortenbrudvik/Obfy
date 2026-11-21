using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// The native host loads <c>Obfy.PackedBootstrap</c> as a prebuilt resource, so its source cannot be
/// exercised through the stub without rebuilding the host. These tests compile
/// <c>PackedBootstrap.cs</c> from the repository and drive its entry-point invocation directly.
/// </summary>
public class PackedBootstrapTests
{
    private const string GlobalUsings =
        "global using System; global using System.IO; global using System.Threading; " +
        "global using System.Threading.Tasks; global using System.Collections.Generic; global using System.Linq;";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Obfy.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Obfy.sln) not found.");
    }

    private static MetadataReference[] References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();

    private static Assembly CompileAndLoad(AssemblyLoadContext alc, string assemblyName, OutputKind kind, params string[] sources)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            References(),
            new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        return alc.LoadFromStream(stream);
    }

    private static int Invoke(string payloadSource, params string[] args)
    {
        var alc = new AssemblyLoadContext($"bootstrap-test-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var bootstrapSource = File.ReadAllText(
                Path.Combine(RepoRoot(), "Src", "Obfy.PackedBootstrap", "PackedBootstrap.cs"));
            var bootstrap = CompileAndLoad(alc, "BootstrapUnderTest", OutputKind.DynamicallyLinkedLibrary, GlobalUsings, bootstrapSource);
            var payload = CompileAndLoad(alc, $"Payload{Guid.NewGuid():N}", OutputKind.ConsoleApplication, payloadSource);

            var invoke = bootstrap.GetType("Obfy.Runtime.PackedBootstrap")!
                .GetMethod("InvokeEntryPoint", BindingFlags.NonPublic | BindingFlags.Static);
            invoke.ShouldNotBeNull();
            payload.EntryPoint.ShouldNotBeNull();
            return (int)invoke!.Invoke(null, new object[] { payload.EntryPoint!, args })!;
        }
        finally
        {
            // A payload that sets Environment.ExitCode must not leak into the test host's own exit code.
            Environment.ExitCode = 0;
            alc.Unload();
        }
    }

    [Fact]
    public void VoidMain_ExitsWithEnvironmentExitCode()
    {
        Invoke("public static class P { public static void Main() { System.Environment.ExitCode = 7; } }").ShouldBe(7);
    }

    [Fact]
    public void VoidMainWithArgs_ExitsWithEnvironmentExitCode()
    {
        Invoke("public static class P { public static void Main(string[] args) { System.Environment.ExitCode = args.Length; } }", "a", "b", "c")
            .ShouldBe(3);
    }

    [Fact]
    public void VoidMain_WithoutSettingIt_ExitsZero()
    {
        Invoke("public static class P { public static void Main() { } }").ShouldBe(0);
    }

    [Fact]
    public void IntMain_ReturnsItsValue_EvenWhenEnvironmentExitCodeIsSet()
    {
        Invoke("public static class P { public static int Main() { System.Environment.ExitCode = 7; return 3; } }").ShouldBe(3);
    }

    [Fact]
    public void AsyncTaskMain_ExitsWithEnvironmentExitCode()
    {
        Invoke("""
            public static class P
            {
                public static async System.Threading.Tasks.Task Main()
                {
                    await System.Threading.Tasks.Task.Yield();
                    System.Environment.ExitCode = 5;
                }
            }
            """).ShouldBe(5);
    }

    [Fact]
    public void AsyncTaskIntMain_ReturnsItsValue()
    {
        Invoke("""
            public static class P
            {
                public static async System.Threading.Tasks.Task<int> Main()
                {
                    await System.Threading.Tasks.Task.Yield();
                    return 4;
                }
            }
            """).ShouldBe(4);
    }

    // ---- End to end through the embedded native host -----------------------------------------
    // These run the real packed EXE, so they cover the bootstrap only once Src/Obfy.NativeHost/dist
    // has been rebuilt from the current PackedBootstrap.cs (build.ps1); until then the prebuilt host
    // still carries the old bootstrap.

    private static (int ExitCode, string StdOut, string StdErr) RunPacked(string source, string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-bootstrap-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var compilation = CSharpCompilation.Create(
                name,
                new[] { CSharpSyntaxTree.ParseText(source) },
                References(),
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));
            var input = Path.Combine(dir, name + ".exe");
            var emit = compilation.Emit(input);
            emit.Success.ShouldBeTrue(string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            var packed = Path.Combine(dir, name + ".packed.exe");
            File.Copy(input, packed);
            Obfy.Core.Utilities.NativePacker.Pack(packed, new Obfy.Core.Models.ObfySettings(), input);

            var start = new System.Diagnostics.ProcessStartInfo(packed)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit(30000).ShouldBeTrue("packed exe timed out");
            return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void PackedExe_VoidMainEnvironmentExitCode_IsTheProcessExitCode()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = RunPacked(
            "public static class Program { public static void Main() { System.Environment.ExitCode = 7; } }",
            "BootVoidExit");
        result.ExitCode.ShouldBe(7, result.StdErr);
    }

    // Assembly.GetEntryAssembly() is null inside a packed payload on purpose: the anti-tamper check uses that to
    // recognise a packed app and skip. Setting it made every packed app with AntiTamper fail at startup
    // (Output_NativePacked_WithRenamingStringEncryptionAndAntiTamper_StillRuns pins that combination).
}

using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Obfy.Core.Models;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Empirical end-to-end tests through the win-x64 native packed host covering B-34 behavior:
/// <list type="bullet">
/// <item><c>Assembly.GetEntryAssembly()</c> stays <c>null</c> and executing <c>Location</c> is empty by design (required by AntiTamper).</item>
/// <item>Unhandled <c>Main</c> exceptions exit cleanly with code <c>1</c> and stderr stack trace without firing <c>AppDomain.UnhandledException</c>; uncaught exceptions on secondary threads fire <c>AppDomain.UnhandledException</c> and exit non-zero.</item>
/// <item>Under <c>hostfxr</c> component hosting, joined foreground threads complete before exit whereas unjoined foreground threads do not block <c>wmain</c> exit.</item>
/// </list>
/// </summary>
public class ReviewFixesWave4PackedTests
{
    private static MetadataReference[] References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();

    private static (int ExitCode, string StdOut, string StdErr) RunPacked(string source, string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-wave4d-e2e-{Guid.NewGuid():N}");
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
            emit.Success.ShouldBeTrue(string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            var packed = Path.Combine(dir, name + ".packed.exe");
            File.Copy(input, packed);
            NativePacker.Pack(packed, new ObfySettings(), input);

            var start = new ProcessStartInfo(packed)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExit(30000);
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            }

            exited.ShouldBeTrue("packed exe timed out");
            process.WaitForExit();
            return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void NativePacked_GetEntryAssembly_IsNullAndExecutingLocationIsEmpty()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = RunPacked(
            """
            using System;
            using System.Reflection;

            public static class Program
            {
                public static int Main()
                {
                    var entry = Assembly.GetEntryAssembly();
                    var location = Assembly.GetExecutingAssembly().Location;
                    Console.WriteLine($"ENTRY_IS_NULL={entry is null}");
                    Console.WriteLine($"EXEC_LOCATION_EMPTY={string.IsNullOrEmpty(location)}");
                    return 0;
                }
            }
            """,
            "NativeEntryAsmApp");

        result.ExitCode.ShouldBe(0, result.StdErr);
        result.StdOut.ShouldContain("ENTRY_IS_NULL=True");
        result.StdOut.ShouldContain("EXEC_LOCATION_EMPTY=True");
    }

    [Fact]
    public void NativePacked_SyncMainException_Exits1WithInnerTraceOnStderr_AndDoesNotFireUnhandledException()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = RunPacked(
            """
            using System;

            public static class Program
            {
                public static void Main()
                {
                    AppDomain.CurrentDomain.UnhandledException += (_, _) =>
                    {
                        Console.WriteLine("UE_FIRED");
                        Console.Out.Flush();
                    };

                    throw new InvalidOperationException("native-sync-boom");
                }
            }
            """,
            "NativeSyncMainFailApp");

        result.ExitCode.ShouldBe(1);
        result.StdOut.ShouldNotContain("UE_FIRED");
        result.StdErr.ShouldContain(typeof(InvalidOperationException).FullName!);
        result.StdErr.ShouldContain("native-sync-boom");
        result.StdErr.ShouldContain("   at ");
        result.StdErr.ShouldContain("Program");
        result.StdErr.ShouldNotContain("TargetInvocationException");
    }

    [Fact]
    public void NativePacked_SecondaryThreadUnhandledException_FiresAppDomainUnhandledExceptionAndExitsNonZero()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = RunPacked(
            """
            using System;
            using System.Threading;

            public static class Program
            {
                public static int Main()
                {
                    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                    {
                        var ex = e.ExceptionObject as Exception;
                        Console.WriteLine($"UE_FIRED:{e.IsTerminating}:{ex?.GetType().FullName}:{ex?.Message}");
                        Console.Out.Flush();
                    };

                    var thread = new Thread(() => throw new InvalidOperationException("worker-thread-boom"));
                    thread.Start();
                    thread.Join();
                    return 0;
                }
            }
            """,
            "NativeSecondaryUeApp");

        result.ExitCode.ShouldNotBe(0);
        result.StdOut.ShouldContain("UE_FIRED:True:System.InvalidOperationException:worker-thread-boom");
        result.StdErr.ShouldContain(typeof(InvalidOperationException).FullName!);
        result.StdErr.ShouldContain("worker-thread-boom");
    }

    [Fact]
    public void NativePacked_JoinedForegroundThread_CompletesBeforeExit()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = RunPacked(
            """
            using System;
            using System.Threading;

            public static class Program
            {
                public static int Main()
                {
                    var thread = new Thread(() =>
                    {
                        Console.WriteLine("FG_JOINED_DONE");
                        Console.Out.Flush();
                    })
                    {
                        IsBackground = false
                    };
                    thread.Start();
                    thread.Join();
                    return 17;
                }
            }
            """,
            "NativeFgJoinedApp");

        result.ExitCode.ShouldBe(17, result.StdErr);
        result.StdOut.ShouldContain("FG_JOINED_DONE");
    }

    [Fact]
    public void NativePacked_UnjoinedForegroundThread_DoesNotBlockComponentHostExit()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = RunPacked(
            """
            using System;
            using System.Threading;

            public static class Program
            {
                public static int Main()
                {
                    var started = new ManualResetEventSlim(false);
                    var thread = new Thread(() =>
                    {
                        Console.WriteLine("FG_STARTED");
                        Console.Out.Flush();
                        started.Set();
                        Thread.Sleep(5000);
                        Console.WriteLine("FG_AFTER_SLEEP");
                        Console.Out.Flush();
                    })
                    {
                        IsBackground = false
                    };
                    thread.Start();
                    started.Wait();
                    Console.WriteLine("MAIN_RETURN");
                    Console.Out.Flush();
                    return 19;
                }
            }
            """,
            "NativeFgUnjoinedApp");

        result.ExitCode.ShouldBe(19, result.StdErr);
        result.StdOut.ShouldContain("FG_STARTED");
        result.StdOut.ShouldContain("MAIN_RETURN");
        result.StdOut.ShouldNotContain("FG_AFTER_SLEEP");
    }
}

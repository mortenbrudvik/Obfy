using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Obfy.Core.Models;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Regression tests for Wave 3B fixes across <c>Src/Obfy.NativeHost/build.ps1</c> (B-32 + UTF-8),
/// <c>Src/Obfy.NativeHost/host.c</c> (B-112), and <c>Src/Obfy.PackedBootstrap/PackedBootstrap.cs</c>
/// (B-105, B-112, B-34).
/// </summary>
public class ReviewFixesWave3NativeHostTests
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

    private static Assembly CompileAndLoad(
        AssemblyLoadContext alc, string assemblyName, OutputKind kind, params string[] sources)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            References(),
            new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.ShouldBeTrue(string.Join(
            Environment.NewLine,
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        return alc.LoadFromStream(stream);
    }

    private static Assembly CompileBootstrap(AssemblyLoadContext alc)
    {
        var bootstrapDir = Path.Combine(RepoRoot(), "Src", "Obfy.PackedBootstrap");
        var bootstrapSource = File.ReadAllText(Path.Combine(bootstrapDir, "PackedBootstrap.cs"));
        var decryptorSource = File.ReadAllText(Path.Combine(bootstrapDir, "MethodImageDecryptor.cs"));
        return CompileAndLoad(
            alc,
            $"BootstrapUnderTest{Guid.NewGuid():N}",
            OutputKind.DynamicallyLinkedLibrary,
            GlobalUsings,
            bootstrapSource,
            decryptorSource);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunPacked(
        string source,
        string name,
        Action<string>? prepareDirectory = null,
        string? workingDirectory = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-wave3b-e2e-{Guid.NewGuid():N}");
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
            prepareDirectory?.Invoke(dir);

            var start = new ProcessStartInfo(packed)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            if (!string.IsNullOrEmpty(workingDirectory))
                start.WorkingDirectory = workingDirectory;

            using var process = Process.Start(start)!;
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
    public void BuildScript_PinsExpectedPackHashes_VerifiesWithGetFileHash_AndUsesUtf8RcEncoding()
    {
        var buildScriptPath = Path.Combine(RepoRoot(), "Src", "Obfy.NativeHost", "build.ps1");
        var script = File.ReadAllText(buildScriptPath);

        script.ShouldContain("$expectedPackHashes");
        script.ShouldContain("Get-FileHash");
        script.ShouldContain("-Algorithm SHA256");

        var expectedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["libnethost.lib"] = "812C7B0CA2B9B5783565A2632AE63F7ACFAA3ECD6ACF0307C2BB82A836EF47FE",
            ["nethost.h"] = "D5254A53C8C95CF1878D39D88A5274F94B31717543AA5FB6A8EF005BC1007DEE",
            ["hostfxr.h"] = "D510F774012576AB31B6964223F41491C5326998CA8AEBBD90F0422C239140C9",
            ["coreclr_delegates.h"] = "344E5467128AFFF9074B4FE65285E810FD65C9C9823366C53EA0A1793060F2A8"
        };

        foreach (var (fileName, hash) in expectedEntries)
        {
            Regex.IsMatch(
                    script,
                    $@"['""]{Regex.Escape(fileName)}['""]\s*=\s*['""]{hash}['""]",
                    RegexOptions.IgnoreCase)
                .ShouldBeTrue($"build.ps1 must pin SHA-256 hash for {fileName}");
        }

        var hashCheckIdx = script.IndexOf("Get-FileHash", StringComparison.Ordinal);
        var restoreIdx = script.IndexOf("Restore-NetHostPack $packVersion", StringComparison.Ordinal);
        var rcIdx = script.IndexOf("rc.exe", StringComparison.Ordinal);
        var clIdx = script.IndexOf("cl.exe /nologo", StringComparison.Ordinal);

        restoreIdx.ShouldBeGreaterThan(0);
        hashCheckIdx.ShouldBeGreaterThan(restoreIdx, "hash check must run after both local and restored pack lookup");
        rcIdx.ShouldBeGreaterThan(hashCheckIdx, "hash check must run before rc.exe");
        clIdx.ShouldBeGreaterThan(hashCheckIdx, "hash check must run before cl.exe");

        script.ShouldNotContain("-Encoding ascii");
        script.ShouldNotContain("-Encoding utf8");
        Regex.IsMatch(
                script,
                @"WriteAllText\(\s*\$rc\b[^\r\n]*UTF8Encoding[^\r\n]*\$false")
            .ShouldBeTrue("build.ps1 must write host.generated.rc as BOM-free UTF-8");
        script.ShouldContain("rc.exe /nologo /c 65001");

        var localPackNative = @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\8.0.6\runtimes\win-x64\native";
        if (OperatingSystem.IsWindows() && Directory.Exists(localPackNative))
        {
            foreach (var (fileName, expectedHash) in expectedEntries)
            {
                var filePath = Path.Combine(localPackNative, fileName);
                File.Exists(filePath).ShouldBeTrue();
                var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath)));
                actualHash.ShouldBe(expectedHash);
            }
        }
    }

    [Fact]
    public void HostC_HeapAllocatesPathBuffers_FreesBeforeRun_AndChecksGetModuleFileNameTruncation()
    {
        var hostCPath = Path.Combine(RepoRoot(), "Src", "Obfy.NativeHost", "host.c");
        var source = File.ReadAllText(hostCPath);

        Regex.IsMatch(source, @"\b(?:wchar_t|char_t)\s+\w+\s*\[\s*OBFY_PATH_CCH\s*\]")
            .ShouldBeFalse("host.c must not stack-allocate OBFY_PATH_CCH buffers");

        source.ShouldContain("calloc(OBFY_PATH_CCH, sizeof(char_t))");
        source.ShouldContain("calloc(OBFY_PATH_CCH, sizeof(wchar_t))");

        var loadAndRunIdx = source.IndexOf("static int load_and_run(", StringComparison.Ordinal);
        var wmainIdx = source.IndexOf("int wmain(", StringComparison.Ordinal);
        loadAndRunIdx.ShouldBeGreaterThan(0);
        wmainIdx.ShouldBeGreaterThan(loadAndRunIdx);

        var loadAndRunBody = source[loadAndRunIdx..wmainIdx];
        var freeBeforeRunIdx = loadAndRunBody.IndexOf("free_path_buffers(", StringComparison.Ordinal);
        var runCallIdx = loadAndRunBody.IndexOf("run(plain, plain_len)", StringComparison.Ordinal);
        var cleanupIdx = loadAndRunBody.IndexOf("cleanup:", StringComparison.Ordinal);
        var freeInCleanupIdx = loadAndRunBody.IndexOf("free_path_buffers(", cleanupIdx, StringComparison.Ordinal);

        freeBeforeRunIdx.ShouldBeGreaterThan(0);
        runCallIdx.ShouldBeGreaterThan(freeBeforeRunIdx, "path buffers must be freed before calling run()");
        cleanupIdx.ShouldBeGreaterThan(runCallIdx);
        freeInCleanupIdx.ShouldBeGreaterThan(cleanupIdx, "cleanup path must also free path buffers");

        var getModuleMatches = Regex.Matches(source, @"GetModuleFileNameW\([^;]+;");
        getModuleMatches.Count.ShouldBe(4);

        var directChecks = Regex.Matches(
            source,
            @"GetModuleFileNameW\(NULL,\s*(?:host_path|exe_path),\s*OBFY_PATH_CCH\);\s*if\s*\(\s*n\s*==\s*0\s*\|\|\s*n\s*>=\s*OBFY_PATH_CCH\s*\)");
        directChecks.Count.ShouldBe(2);

        var helperChecks = Regex.Matches(
            source,
            @"GetModuleFileNameW\(NULL,\s*buf,\s*\(DWORD\)buf_cch\);\s*if\s*\(\s*n\s*==\s*0\s*\|\|\s*n\s*>=\s*buf_cch\s*\)");
        helperChecks.Count.ShouldBe(2);
    }

    [Fact]
    public void PackedBootstrap_StaThreadEntryPoint_HonorsStaOnWindows()
    {
        var alc = new AssemblyLoadContext($"wave3b-sta-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var bootstrap = CompileBootstrap(alc);
            var payload = CompileAndLoad(
                alc,
                $"StaPayload{Guid.NewGuid():N}",
                OutputKind.ConsoleApplication,
                """
                using System;
                using System.Threading;

                public static class Program
                {
                    [STAThread]
                    public static int Main() =>
                        Thread.CurrentThread.GetApartmentState() == ApartmentState.STA ? 42 : 7;
                }
                """);

            var invoke = bootstrap.GetType("Obfy.Runtime.PackedBootstrap")!
                .GetMethod("InvokeEntryPoint", BindingFlags.NonPublic | BindingFlags.Static)!;

            var code = (int)invoke.Invoke(null, new object[] { payload.EntryPoint!, Array.Empty<string>() })!;
            var expected = OperatingSystem.IsWindows() ? 42 : 7;
            code.ShouldBe(expected);
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public void PackedBootstrap_StaThreadEntryPoint_PropagatesSyncAndAsyncExceptions()
    {
        var alc = new AssemblyLoadContext($"wave3b-sta-ex-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var bootstrap = CompileBootstrap(alc);
            var invoke = bootstrap.GetType("Obfy.Runtime.PackedBootstrap")!
                .GetMethod("InvokeEntryPoint", BindingFlags.NonPublic | BindingFlags.Static)!;

            var asyncPayload = CompileAndLoad(
                alc,
                $"StaAsyncEx{Guid.NewGuid():N}",
                OutputKind.ConsoleApplication,
                """
                using System;
                using System.Threading;
                using System.Threading.Tasks;

                public static class Program
                {
                    [STAThread]
                    public static async Task Main()
                    {
                        if (OperatingSystem.IsWindows()
                            && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                        {
                            throw new InvalidOperationException("expected-sta-apartment");
                        }

                        await Task.Yield();
                        throw new InvalidOperationException("sta-async-boom");
                    }
                }
                """);

            var asyncMain = asyncPayload.GetType("Program")!
                .GetMethod("Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            var asyncOuter = Should.Throw<TargetInvocationException>(
                () => invoke.Invoke(null, new object[] { asyncMain, Array.Empty<string>() }));
            var asyncEx = asyncOuter.InnerException.ShouldBeOfType<InvalidOperationException>();
            asyncEx.Message.ShouldBe("sta-async-boom");
            asyncEx.StackTrace.ShouldNotBeNullOrWhiteSpace();
            asyncEx.StackTrace!.ShouldContain("Program");

            var syncPayload = CompileAndLoad(
                alc,
                $"StaSyncEx{Guid.NewGuid():N}",
                OutputKind.ConsoleApplication,
                """
                using System;

                public static class Program
                {
                    [STAThread]
                    public static void Main() => throw new InvalidOperationException("sta-sync-boom");
                }
                """);

            var syncOuter = Should.Throw<TargetInvocationException>(
                () => invoke.Invoke(null, new object[] { syncPayload.EntryPoint!, Array.Empty<string>() }));
            var syncInnerTie = syncOuter.InnerException.ShouldBeOfType<TargetInvocationException>();
            syncInnerTie.InnerException.ShouldBeOfType<InvalidOperationException>();
            syncInnerTie.InnerException!.Message.ShouldBe("sta-sync-boom");
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public void PackedBootstrap_ResolvingUnmanagedDll_ProbesBaseAndNativeSubdir_AndIsIdempotent()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var tempBase = Path.Combine(Path.GetTempPath(), $"obfy-wave3b-native-{Guid.NewGuid():N}");
        var nativeSubdir = Path.Combine(tempBase, "runtimes", "win-x64", "native");
        Directory.CreateDirectory(nativeSubdir);

        var alc = new AssemblyLoadContext($"wave3b-unmanaged-{Guid.NewGuid():N}", isCollectible: true);
        var handles = new List<IntPtr>();
        try
        {
            var sourceNative = Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.IO.Compression.Native.dll");
            File.Exists(sourceNative).ShouldBeTrue();

            var rootProbeName = $"obfy_root_{Guid.NewGuid():N}";
            var subProbeName = $"obfy_sub_{Guid.NewGuid():N}";
            File.Copy(sourceNative, Path.Combine(tempBase, rootProbeName + ".dll"));
            File.Copy(sourceNative, Path.Combine(nativeSubdir, subProbeName + ".dll"));

            var bootstrap = CompileBootstrap(alc);
            var bootstrapType = bootstrap.GetType("Obfy.Runtime.PackedBootstrap")!;
            var resolverMethod = bootstrapType.GetMethod(
                "ResolveUnmanagedFromBaseDirectory",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            IntPtr Probe(string name) =>
                (IntPtr)resolverMethod.Invoke(null, new object[] { tempBase, name })!;

            var hRootNoExt = Probe(rootProbeName);
            hRootNoExt.ShouldNotBe(IntPtr.Zero);
            handles.Add(hRootNoExt);

            var hRootWithExt = Probe(rootProbeName + ".dll");
            hRootWithExt.ShouldNotBe(IntPtr.Zero);
            handles.Add(hRootWithExt);

            var hSubNoExt = Probe(subProbeName);
            hSubNoExt.ShouldNotBe(IntPtr.Zero);
            handles.Add(hSubNoExt);

            var hSubWithExt = Probe(subProbeName + ".dll");
            hSubWithExt.ShouldNotBe(IntPtr.Zero);
            handles.Add(hSubWithExt);

            Probe("obfy_missing_dll").ShouldBe(IntPtr.Zero);
            Probe(string.Empty).ShouldBe(IntPtr.Zero);
            Probe(Path.Combine(tempBase, rootProbeName + ".dll")).ShouldBe(IntPtr.Zero);

            alc.ResolvingUnmanagedDll += (_, name) => Probe(name);
            var payload = CompileAndLoad(
                alc,
                $"UnmanagedConsumer{Guid.NewGuid():N}",
                OutputKind.DynamicallyLinkedLibrary,
                """
                using System;
                using System.Runtime.InteropServices;

                public static class ProbeRunner
                {
                    public static IntPtr LoadLib(string name) =>
                        NativeLibrary.Load(name, typeof(ProbeRunner).Assembly, null);
                }
                """);

            var loadLib = payload.GetType("ProbeRunner")!.GetMethod("LoadLib")!;
            var viaEventHandle = (IntPtr)loadLib.Invoke(null, new object[] { subProbeName })!;
            viaEventHandle.ShouldNotBe(IntPtr.Zero);
            handles.Add(viaEventHandle);

            var registerMethod = bootstrapType.GetMethod(
                "RegisterDefaultResolvers",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var dummyAlc = new AssemblyLoadContext($"wave3b-hook-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                ((bool)registerMethod.Invoke(null, new object[] { dummyAlc })!).ShouldBeTrue();
                ((bool)registerMethod.Invoke(null, new object[] { dummyAlc })!).ShouldBeFalse();
            }
            finally
            {
                dummyAlc.Unload();
            }
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                    NativeLibrary.Free(handle);
            }

            alc.Unload();
            try { Directory.Delete(tempBase, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void PackedBootstrap_ResolvingUnmanagedDll_RejectsRelativePathTraversal()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var tempParent = Path.Combine(Path.GetTempPath(), $"obfy-wave3b-traversal-{Guid.NewGuid():N}");
        var tempBase = Path.Combine(tempParent, "base");
        var subDir = Path.Combine(tempBase, "sub");
        var nativeParent = Path.Combine(tempBase, "runtimes", "win-x64");
        var nativeSubdir = Path.Combine(nativeParent, "native");
        Directory.CreateDirectory(subDir);
        Directory.CreateDirectory(nativeSubdir);

        var alc = new AssemblyLoadContext($"wave3b-traversal-{Guid.NewGuid():N}", isCollectible: true);
        var handles = new List<IntPtr>();
        try
        {
            var sourceNative = Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.IO.Compression.Native.dll");
            File.Exists(sourceNative).ShouldBeTrue();

            // Place real loadable DLLs in the parent directories so only the traversal guard can reject them.
            File.Copy(sourceNative, Path.Combine(tempParent, "escape.dll"));
            File.Copy(sourceNative, Path.Combine(nativeParent, "escape.dll"));

            var validSubName = $"obfy_valid_sub_{Guid.NewGuid():N}";
            File.Copy(sourceNative, Path.Combine(subDir, validSubName + ".dll"));

            var bootstrap = CompileBootstrap(alc);
            var resolverMethod = bootstrap.GetType("Obfy.Runtime.PackedBootstrap")!
                .GetMethod("ResolveUnmanagedFromBaseDirectory", BindingFlags.NonPublic | BindingFlags.Static)!;

            IntPtr Probe(string name) =>
                (IntPtr)resolverMethod.Invoke(null, new object[] { tempBase, name })!;

            Probe(@"..\escape.dll").ShouldBe(IntPtr.Zero);
            Probe(@"..\escape").ShouldBe(IntPtr.Zero);
            Probe("../escape.dll").ShouldBe(IntPtr.Zero);
            Probe("../escape").ShouldBe(IntPtr.Zero);
            Probe(@"sub\..\..\escape.dll").ShouldBe(IntPtr.Zero);
            Probe("sub/../../escape.dll").ShouldBe(IntPtr.Zero);
            Probe(".").ShouldBe(IntPtr.Zero);
            Probe("   ").ShouldBe(IntPtr.Zero);
            Probe("bad\0name.dll").ShouldBe(IntPtr.Zero);

            var validHandle = Probe(Path.Combine("sub", validSubName + ".dll"));
            validHandle.ShouldNotBe(IntPtr.Zero);
            handles.Add(validHandle);
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                    NativeLibrary.Free(handle);
            }

            alc.Unload();
            try { Directory.Delete(tempParent, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void NativePacked_StaThreadMain_RunsOnStaThreadOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = RunPacked(
            """
            using System;
            using System.Threading;

            public static class Program
            {
                [STAThread]
                public static int Main() =>
                    Thread.CurrentThread.GetApartmentState() == ApartmentState.STA ? 42 : 7;
            }
            """,
            "NativeStaApp");

        result.ExitCode.ShouldBe(42, result.StdErr);
    }

    [Fact]
    public void NativePacked_ResolvingUnmanagedDll_LoadsRootAndNativeSubdirDlls()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var rootDllName = $"obfy_e2e_root_{Guid.NewGuid():N}";
        var subDllName = $"obfy_e2e_sub_{Guid.NewGuid():N}";
        var sourceNative = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.IO.Compression.Native.dll");

        var source = $$"""
            using System;
            using System.Runtime.InteropServices;

            public static class Program
            {
                public static int Main()
                {
                    var h1 = NativeLibrary.Load("{{rootDllName}}", typeof(Program).Assembly, null);
                    var h2 = NativeLibrary.Load("{{subDllName}}.dll", typeof(Program).Assembly, null);
                    return h1 != IntPtr.Zero && h2 != IntPtr.Zero ? 23 : 9;
                }
            }
            """;

        var result = RunPacked(
            source,
            "NativeUnmanagedApp",
            prepareDirectory: dir =>
            {
                var nativeDir = Path.Combine(dir, "runtimes", "win-x64", "native");
                Directory.CreateDirectory(nativeDir);
                File.Copy(sourceNative, Path.Combine(dir, rootDllName + ".dll"));
                File.Copy(sourceNative, Path.Combine(nativeDir, subDllName + ".dll"));
            },
            workingDirectory: Path.GetTempPath());

        result.ExitCode.ShouldBe(23, result.StdErr);
    }

    [Fact]
    public void NativePacked_AsyncTaskMainException_Exits1WithTypeAndStackTraceOnStderr()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = RunPacked(
            """
            using System;
            using System.Threading.Tasks;

            public static class Program
            {
                public static async Task Main()
                {
                    await Task.Yield();
                    throw new InvalidOperationException("native-async-boom");
                }
            }
            """,
            "NativeAsyncFailApp");

        result.ExitCode.ShouldBe(1);
        result.StdErr.ShouldContain(typeof(InvalidOperationException).FullName!);
        result.StdErr.ShouldContain("native-async-boom");
        result.StdErr.ShouldContain("   at ");
        result.StdErr.ShouldContain("Program");
    }
}

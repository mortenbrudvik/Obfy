using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Obfy.Core.Models;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Packing is all-or-nothing and only for payloads the packed host can run: a failure restores the
/// managed PE, an unsupported target framework is refused before any file changes, the original
/// runtimeconfig (frameworks, configProperties) is kept, the portable launcher's runtimeconfig follows
/// the payload, and a void Main's <c>Environment.ExitCode</c> survives the launcher.
/// </summary>
public class PackingTargetTests
{
    private static string NewDir(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-packtarget-{tag}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* ignore */ }
    }

    private static string CompileExe(string source, string dir, string name)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            name, new[] { tree }, references, new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        var path = Path.Combine(dir, name + ".exe");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static string AppSource(string targetFramework) => $$"""
        [assembly: System.Runtime.Versioning.TargetFramework("{{targetFramework}}")]
        public static class Program { public static int Main() => 11; }
        """;

    private static ObfySettings NativeSettings() => new()
    {
        Level = ObfuscationLevel.Custom,
        Packing = { Enabled = true }
    };

    private static ObfySettings PortableSettings() => new()
    {
        Level = ObfuscationLevel.Custom,
        Packing = { Enabled = true, Rid = "portable" }
    };

    private static string[] Leftovers(string dir, params string[] expected)
    {
        var keep = expected.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.GetFileSystemEntries(dir).Where(f => !keep.Contains(Path.GetFullPath(f))).ToArray();
    }

    // ---- NativePacker is transactional ---------------------------------------------------------

    [Fact]
    public void NativePack_RuntimeConfigCannotBePublished_RestoresTheManagedPe()
    {
        var dir = NewDir("txn");
        try
        {
            var input = CompileExe(AppSource(".NETCoreApp,Version=v8.0"), dir, "TxnApp");
            var output = Path.Combine(dir, "TxnApp.obf.exe");
            File.Copy(input, output);
            var original = File.ReadAllBytes(output);

            // A directory squatting on the runtimeconfig path makes the second step of the swap fail.
            var runtimeConfig = NativePacker.RuntimeConfigPathFor(output);
            Directory.CreateDirectory(runtimeConfig);

            var ex = Should.Throw<InvalidOperationException>(() => NativePacker.Pack(output, NativeSettings(), input));
            ex.Message.ShouldContain("restored");

            File.ReadAllBytes(output).ShouldBe(original, "the output must still be the runnable managed PE");
            Leftovers(dir, input, output, runtimeConfig).ShouldBeEmpty();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void NativePack_RuntimeConfigCannotBePublished_KeepsAnExistingRuntimeConfig()
    {
        var dir = NewDir("txn-cfg");
        try
        {
            var input = CompileExe(AppSource(".NETCoreApp,Version=v8.0"), dir, "TxnCfgApp");
            var output = Path.Combine(dir, "TxnCfgApp.obf.exe");
            File.Copy(input, output);
            var original = File.ReadAllBytes(output);

            // A read-only existing runtimeconfig cannot be replaced; the swap must roll back.
            var runtimeConfig = NativePacker.RuntimeConfigPathFor(output);
            File.WriteAllText(runtimeConfig, """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}""");
            var before = File.ReadAllText(runtimeConfig);
            File.SetAttributes(runtimeConfig, FileAttributes.ReadOnly);
            try
            {
                Should.Throw<InvalidOperationException>(() => NativePacker.Pack(output, NativeSettings(), input));

                File.ReadAllBytes(output).ShouldBe(original);
                File.ReadAllText(runtimeConfig).ShouldBe(before);
            }
            finally
            {
                File.SetAttributes(runtimeConfig, FileAttributes.Normal);
            }
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    // ---- NativePacker refuses payloads the host cannot run ------------------------------------

    [Theory]
    [InlineData(".NETFramework,Version=v4.8")]
    [InlineData(".NETCoreApp,Version=v6.0")]
    [InlineData(".NETCoreApp,Version=v7.0")]
    public void NativePack_UnsupportedTarget_FailsBeforeTouchingAnything(string targetFramework)
    {
        var dir = NewDir("reject");
        try
        {
            var input = CompileExe(AppSource(targetFramework), dir, "RejectApp");
            var output = Path.Combine(dir, "RejectApp.obf.exe");
            File.Copy(input, output);
            var original = File.ReadAllBytes(output);

            var ex = Should.Throw<InvalidOperationException>(() => NativePacker.Pack(output, NativeSettings(), input));
            ex.Message.ShouldContain("portable");

            File.ReadAllBytes(output).ShouldBe(original);
            File.Exists(NativePacker.RuntimeConfigPathFor(output)).ShouldBeFalse();
            Leftovers(dir, input, output).ShouldBeEmpty();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void NativePack_NetFrameworkRuntimeConfigMoniker_IsNotReadAsMajor48()
    {
        var dir = NewDir("net48json");
        try
        {
            var input = CompileExe("public static class Program { public static int Main() => 11; }", dir, "Net48Json");
            var stripped = Path.Combine(dir, "Stripped.exe");
            using (var module = dnlib.DotNet.ModuleDefMD.Load(input))
            {
                var assembly = module.Assembly!;
                for (var i = assembly.CustomAttributes.Count - 1; i >= 0; i--)
                {
                    if (assembly.CustomAttributes[i].TypeFullName == "System.Runtime.Versioning.TargetFrameworkAttribute")
                        assembly.CustomAttributes.RemoveAt(i);
                }

                foreach (var reference in module.GetAssemblyRefs())
                    reference.Version = new Version(0, 0, 0, 0);
                module.Write(stripped);
            }

            File.WriteAllText(Path.ChangeExtension(stripped, ".runtimeconfig.json"),
                """{"runtimeOptions":{"tfm":"net48"}}""");

            // "net48" carries no major version; with nothing else to go on the target is unknown.
            Should.Throw<InvalidOperationException>(() =>
                NativePacker.ResolveTfmMajor(File.ReadAllBytes(stripped), stripped));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    // ---- NativePacker keeps the original runtimeconfig ----------------------------------------

    [Fact]
    public void NativePack_KeepsFrameworksAndConfigPropertiesFromTheInputRuntimeConfig()
    {
        var dir = NewDir("keepcfg");
        try
        {
            var input = CompileExe(AppSource(".NETCoreApp,Version=v8.0"), dir, "KeepCfgApp");
            File.WriteAllText(Path.ChangeExtension(input, ".runtimeconfig.json"), """
                {
                  "runtimeOptions": {
                    "tfm": "net8.0",
                    "frameworks": [
                      { "name": "Microsoft.NETCore.App", "version": "8.0.0" },
                      { "name": "Microsoft.WindowsDesktop.App", "version": "8.0.0" }
                    ],
                    "configProperties": {
                      "System.Runtime.TieredPGO": true,
                      "System.GC.Server": true
                    }
                  }
                }
                """);
            var output = Path.Combine(dir, "KeepCfgApp.obf.exe");
            File.Copy(input, output);

            NativePacker.Pack(output, NativeSettings(), input);

            using var doc = JsonDocument.Parse(File.ReadAllText(NativePacker.RuntimeConfigPathFor(output)));
            var options = doc.RootElement.GetProperty("runtimeOptions");
            options.GetProperty("tfm").GetString().ShouldBe("net8.0");
            var names = options.GetProperty("frameworks").EnumerateArray()
                .Select(f => f.GetProperty("name").GetString()).ToArray();
            names.ShouldBe(new[] { "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App" });
            options.GetProperty("configProperties").GetProperty("System.GC.Server").GetBoolean().ShouldBeTrue();
            options.GetProperty("configProperties").GetProperty("System.Runtime.TieredPGO").GetBoolean().ShouldBeTrue();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    // ---- ManagedLauncherPacker follows the payload --------------------------------------------

    private static string PackPortable(string dir, string targetFramework, string name)
    {
        var input = CompileExe(AppSource(targetFramework), dir, name);
        var output = Path.Combine(dir, name + ".obf.exe");
        File.Copy(input, output);
        var warnings = new List<string>();
        PackingApplicator.Pack(output, PortableSettings(), input, warnings);
        return File.ReadAllText(ManagedLauncherPacker.RuntimeConfigPathFor(output));
    }

    [Fact]
    public void PortablePack_PayloadNewerThanTheObfyRuntime_TargetsThePayloadRuntime()
    {
        var dir = NewDir("portable-newer");
        try
        {
            var json = PackPortable(dir, ".NETCoreApp,Version=v99.0", "NewerApp");
            json.ShouldContain("\"tfm\": \"net99.0\"");
            json.ShouldContain("\"version\": \"99.0.0\"");
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void PortablePack_PayloadOlderThanTheObfyRuntime_KeepsTheRuntimeTheLauncherWasCompiledAgainst()
    {
        var dir = NewDir("portable-older");
        try
        {
            var json = PackPortable(dir, ".NETCoreApp,Version=v8.0", "OlderApp");
            var host = Environment.Version.Major;
            json.ShouldContain($"\"tfm\": \"net{host}.0\"");
            json.ShouldContain($"\"version\": \"{host}.0.0\"");
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void PortablePack_KeepsDesktopFrameworkAndRaisesItToTheLauncherRuntime()
    {
        var dir = NewDir("portable-desktop");
        try
        {
            var input = CompileExe(AppSource(".NETCoreApp,Version=v8.0"), dir, "DesktopApp");
            File.WriteAllText(Path.ChangeExtension(input, ".runtimeconfig.json"), """
                {
                  "runtimeOptions": {
                    "tfm": "net8.0",
                    "frameworks": [
                      { "name": "Microsoft.NETCore.App", "version": "8.0.0" },
                      { "name": "Microsoft.WindowsDesktop.App", "version": "8.0.0" }
                    ],
                    "configProperties": { "System.GC.Server": true }
                  }
                }
                """);
            var output = Path.Combine(dir, "DesktopApp.obf.exe");
            File.Copy(input, output);
            PackingApplicator.Pack(output, PortableSettings(), input, new List<string>());

            using var doc = JsonDocument.Parse(File.ReadAllText(ManagedLauncherPacker.RuntimeConfigPathFor(output)));
            var options = doc.RootElement.GetProperty("runtimeOptions");
            var host = Environment.Version.Major;
            options.GetProperty("tfm").GetString().ShouldBe($"net{host}.0");
            foreach (var framework in options.GetProperty("frameworks").EnumerateArray())
                framework.GetProperty("version").GetString().ShouldBe($"{host}.0.0");
            options.GetProperty("frameworks").EnumerateArray()
                .Select(f => f.GetProperty("name").GetString())
                .ShouldContain("Microsoft.WindowsDesktop.App");
            options.GetProperty("configProperties").GetProperty("System.GC.Server").GetBoolean().ShouldBeTrue();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void PortablePack_VoidMainEnvironmentExitCode_IsTheLauncherExitCode()
    {
        var dir = NewDir("portable-exitcode");
        try
        {
            const string source = "public static class Program { public static void Main() { System.Environment.ExitCode = 9; } }";
            var input = CompileExe(source, dir, "ExitCodeApp");
            var output = Path.Combine(dir, "ExitCodeApp.obf.exe");
            File.Copy(input, output);
            PackingApplicator.Pack(output, PortableSettings(), input, new List<string>());

            var launcher = ManagedLauncherPacker.LauncherPathFor(output);
            var start = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{launcher}\"")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var process = System.Diagnostics.Process.Start(start)!;
            var stderr = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit(30000).ShouldBeTrue("launcher timed out");
            process.ExitCode.ShouldBe(9, stderr);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }
}

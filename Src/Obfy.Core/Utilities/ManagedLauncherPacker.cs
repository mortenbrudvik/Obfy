using System.Collections.Immutable;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Obfy.Core.Utilities;

/// <summary>
/// Portable packing path only (<c>packing.rid: portable</c>). Compiles a framework-dependent
/// managed console host that embeds the obfuscated assembly as a manifest resource and invokes
/// its entry point via <see cref="System.Runtime.Loader.AssemblyLoadContext"/>. Run with
/// <c>dotnet {name}.launcher.exe</c> next to the generated <c>.runtimeconfig.json</c>. The
/// original obfuscated assembly is left in place. Default packing is the native win-x64 host.
/// </summary>
public static class ManagedLauncherPacker
{
    private const string ResourceName = "packed.dll";
    private const string LauncherSuffix = ".launcher.exe";

    private static readonly string HostSource = $$"""
        using System;
        using System.IO;
        using System.Reflection;
        using System.Runtime.Loader;
        using System.Threading.Tasks;

        internal static class PackedHost
        {
            private static int Main(string[] args)
            {
                try
                {
                    using var stream = typeof(PackedHost).Assembly.GetManifestResourceStream("{{ResourceName}}");
                    if (stream == null)
                    {
                        Console.Error.WriteLine("Packed host: embedded resource '{{ResourceName}}' was not found.");
                        return 1;
                    }

                    using var payload = new MemoryStream();
                    stream.CopyTo(payload);
                    var bytes = payload.ToArray();
                    // LoadFromStream has no mapped image, so the in-assembly decryptor leaves
                    // method IL ciphertext. Undo that XOR before the load.
                    Obfy.Runtime.MethodImageDecryptor.Decrypt(bytes);

                    var alc = new AssemblyLoadContext("obfy-packed");
                    alc.Resolving += static (context, name) =>
                    {
                        if (string.IsNullOrEmpty(name.Name))
                            return null;
                        var fileName = name.Name + ".dll";
                        var probe = Path.Combine(AppContext.BaseDirectory, fileName);
                        if (!File.Exists(probe) && !string.IsNullOrEmpty(name.CultureName))
                            probe = Path.Combine(AppContext.BaseDirectory, name.CultureName, fileName);
                        return File.Exists(probe) ? context.LoadFromAssemblyPath(probe) : null;
                    };

                    var assembly = alc.LoadFromStream(new MemoryStream(bytes));
                    var entry = assembly.EntryPoint;
                    if (entry == null)
                    {
                        Console.Error.WriteLine("Packed host: payload assembly has no entry point.");
                        return 1;
                    }

                    object?[]? invokeArgs = entry.GetParameters().Length == 0 ? null : new object[] { args };
                    var result = entry.Invoke(null, invokeArgs);

                    if (result is Task<int> ti)
                        return ti.GetAwaiter().GetResult();
                    if (result is Task t)
                    {
                        t.GetAwaiter().GetResult();
                        return Environment.ExitCode;
                    }

                    // A void Main reports its status through Environment.ExitCode.
                    return result is int code ? code : Environment.ExitCode;
                }
                catch (TargetInvocationException ex)
                {
                    Console.Error.WriteLine(ex.InnerException ?? ex);
                    return 1;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Packed host failed: " + ex.Message);
                    return 1;
                }
            }
        }
        """;

    private static string ReadMethodImageDecryptorSource()
    {
        using var stream = typeof(ManagedLauncherPacker).Assembly
            .GetManifestResourceStream("Obfy.MethodImageDecryptor.cs");
        if (stream == null)
            throw new InvalidOperationException("Method-image decryptor source is missing from Obfy.Core.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string LauncherPathFor(string assemblyPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (string.IsNullOrEmpty(dir))
            throw new InvalidOperationException("Assembly path has no directory.");
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(assemblyPath) + LauncherSuffix);
    }

    public static string RuntimeConfigPathFor(string assemblyPath) =>
        Path.ChangeExtension(LauncherPathFor(assemblyPath), ".runtimeconfig.json");

    /// <summary>
    /// Emits <c>{name}.launcher.exe</c> and <c>{name}.launcher.runtimeconfig.json</c> beside
    /// <paramref name="assemblyPath"/>. The launcher is compiled against the Obfy process's runtime, so
    /// the runtimeconfig targets the newer of that runtime and the packed assembly's own target
    /// (<c>TargetFrameworkAttribute</c> / the sibling JSON of <paramref name="inputPath"/>), and keeps
    /// that JSON's other frameworks and <c>configProperties</c>.
    /// Throws <see cref="InvalidOperationException"/> when the module has no entry point or emit fails.
    /// Partial output files are deleted on failure.
    /// </summary>
    public static string Pack(string assemblyPath, string? inputPath = null)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            throw new InvalidOperationException("Packing requires an existing assembly file.");

        using (var module = ModuleDefMD.Load(File.ReadAllBytes(assemblyPath)))
        {
            if (module.EntryPoint == null)
                throw new InvalidOperationException("Packing requires an assembly with an entry point.");
        }

        var launcherPath = LauncherPathFor(assemblyPath);
        var runtimeConfigPath = RuntimeConfigPathFor(assemblyPath);

        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
            throw new InvalidOperationException(
                "Failed to compile packed launcher: TRUSTED_PLATFORM_ASSEMBLIES is unavailable in this host.");

        var references = tpa
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            "ObfyLauncher",
            new[]
            {
                CSharpSyntaxTree.ParseText(HostSource),
                CSharpSyntaxTree.ParseText(ReadMethodImageDecryptorSource())
            },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release));

        var resource = new ResourceDescription(ResourceName, () => File.OpenRead(assemblyPath), isPublic: true);
        try
        {
            using (var output = File.Create(launcherPath))
            {
                var emitted = compilation.Emit(output, manifestResources: ImmutableArray.Create(resource));
                if (!emitted.Success)
                {
                    var errors = string.Join("; ", emitted.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString())
                        .DefaultIfEmpty("(no error diagnostics reported)"));
                    throw new InvalidOperationException("Failed to compile packed launcher: " + errors);
                }
            }

            var configSource = inputPath ?? assemblyPath;
            File.WriteAllText(
                runtimeConfigPath,
                NativePacker.BuildRuntimeConfig(ResolveLauncherMajor(assemblyPath, configSource), configSource));
            return launcherPath;
        }
        catch
        {
            TryDelete(launcherPath);
            TryDelete(runtimeConfigPath);
            throw;
        }
    }

    // The launcher needs at least the runtime it was compiled against (Obfy's own), and a newer
    // payload needs its own runtime; an unresolvable or non-.NET Core payload keeps the host's.
    private static uint ResolveLauncherMajor(string assemblyPath, string configSource)
    {
        var host = (uint)Environment.Version.Major;
        try
        {
            var target = NativePacker.ResolvePayloadTarget(File.ReadAllBytes(assemblyPath), configSource);
            return target.IsNetCoreApp ? Math.Max(host, target.Major) : host;
        }
        catch (InvalidOperationException)
        {
            return host;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"ManagedLauncherPacker: failed to delete '{path}': {ex.Message}");
        }
    }
}

using System.Collections.Immutable;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Obfy.Core.Utilities;

/// <summary>
/// Compiles a framework-dependent managed console host that embeds the obfuscated assembly as a
/// manifest resource and invokes its entry point via <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
/// Not a native/unmanaged packer. Run with <c>dotnet {name}.launcher.exe</c> next to the generated
/// <c>.runtimeconfig.json</c>. The original obfuscated assembly is left in place.
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

                    var payloadPath = Path.Combine(
                        AppContext.BaseDirectory,
                        Path.GetFileNameWithoutExtension(typeof(PackedHost).Assembly.Location) + ".payload.dll");
                    using (var file = File.Create(payloadPath))
                        stream.CopyTo(file);

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

                    var assembly = alc.LoadFromAssemblyPath(payloadPath);
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
                        return 0;
                    }

                    return result is int code ? code : 0;
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
    /// <paramref name="assemblyPath"/>. The TFM comes from the Obfy process, not the packed assembly.
    /// Throws <see cref="InvalidOperationException"/> when the module has no entry point or emit fails.
    /// Partial output files are deleted on failure.
    /// </summary>
    public static string Pack(string assemblyPath)
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
            new[] { CSharpSyntaxTree.ParseText(HostSource) },
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

            var major = Environment.Version.Major;
            var runtimeConfig = $$"""
                {
                  "runtimeOptions": {
                    "tfm": "net{{major}}.0",
                    "rollForward": "LatestMinor",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "{{major}}.0.0"
                    }
                  }
                }
                """;
            File.WriteAllText(runtimeConfigPath, runtimeConfig);
            return launcherPath;
        }
        catch
        {
            TryDelete(launcherPath);
            TryDelete(runtimeConfigPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

using System.Collections.Immutable;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Obfy.Core.Utilities;

/// <summary>
/// Compiles a Windows EXE that embeds an obfuscated assembly and runs its entry point.
/// </summary>
public static class NativePacker
{
    private const string ResourceName = "packed.dll";

    private const string HostSource = """
        using System;
        using System.IO;
        using System.Reflection;
        using System.Runtime.Loader;

        internal static class PackedHost
        {
            private static int Main(string[] args)
            {
                using var stream = typeof(PackedHost).Assembly.GetManifestResourceStream("packed.dll");
                if (stream == null)
                    return 1;
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                copy.Position = 0;
                var assembly = new AssemblyLoadContext("obfy-packed").LoadFromStream(copy);
                var entry = assembly.EntryPoint;
                if (entry == null)
                    return 1;
                var result = entry.GetParameters().Length == 0
                    ? entry.Invoke(null, null)
                    : entry.Invoke(null, new object[] { args });
                return result is int code ? code : 0;
            }
        }
        """;

    public static string Pack(string assemblyPath)
    {
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(assemblyPath)))
        {
            if (module.EntryPoint == null)
                throw new InvalidOperationException("Packing requires an assembly with an entry point.");
        }

        var dir = Path.GetDirectoryName(assemblyPath) ?? throw new InvalidOperationException("Assembly path has no directory.");
        var launcherPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(assemblyPath) + ".launcher.exe");

        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            "ObfyLauncher",
            new[] { CSharpSyntaxTree.ParseText(HostSource) },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release));

        var resource = new ResourceDescription(ResourceName, () => File.OpenRead(assemblyPath), isPublic: true);
        using var output = File.Create(launcherPath);
        var emitted = compilation.Emit(output, manifestResources: ImmutableArray.Create(resource));
        if (!emitted.Success)
        {
            var errors = string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException("Failed to compile packed launcher: " + errors);
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
        File.WriteAllText(Path.ChangeExtension(launcherPath, ".runtimeconfig.json"), runtimeConfig);

        return launcherPath;
    }
}

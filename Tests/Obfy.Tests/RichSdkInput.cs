using System.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// A small app compiled against the reference assemblies of one target framework, the way an SDK build compiles it:
/// its core library reference is what that target's SDK produces (<c>System.Runtime</c> of the matching version,
/// <c>mscorlib</c>, or <c>netstandard</c>), not the <c>System.Private.CoreLib</c> Roslyn picks when a test compiles
/// against the runtime running the tests. It carries what the injected code touches: strings, constants of every kind,
/// a switch, an embedded resource, a <c>.resx</c>-style resource read through <c>ResourceManager.GetString</c>, and a
/// sibling assembly it calls.
/// </summary>
internal static class RichSdkInput
{
    public const string SiblingName = "SiblingLib";
    public const string AppName = "RichApp";

    private const string SiblingSource = """
        namespace SiblingLib
        {
            public static class Greeter
            {
                public static string Greet(string who) { return "Hello from the sibling, " + who + "!"; }
            }
        }
        """;

    private const string AppSource = """
        using System;
        using System.Globalization;
        using System.IO;
        using System.Resources;

        namespace Rich
        {
            public static class Program
            {
                public static int Main(string[] args)
                {
                    Console.WriteLine("Hello from the rich app");
                    Console.WriteLine(SiblingLib.Greeter.Greet("world"));

                    var assembly = typeof(Program).Assembly;
                    using (var stream = assembly.GetManifestResourceStream("Rich.data.txt"))
                    using (var reader = new StreamReader(stream))
                        Console.WriteLine("resource:" + reader.ReadToEnd().Trim());

                    var resources = new ResourceManager("Rich.Strings", assembly);
                    Console.WriteLine("resx:" + resources.GetString("Welcome", CultureInfo.InvariantCulture));

                    long total = 0;
                    for (int i = 0; i < 1000; i++)
                        total += i * 3 + 12345;
                    double d = total * 3.14159 + 2.71828;
                    float f = 1.5f * total + 0.25f;
                    switch (args.Length)
                    {
                        case 0: total += 1; break;
                        case 1: total += 2; break;
                        case 2: total += 3; break;
                        default: total += 4; break;
                    }

                    Console.WriteLine(total + ":" + d + ":" + f + ":" + 9999999999L);
                    Console.WriteLine(Join("virtual") + Compute(4, 5));
                    return Helper(args.Length) > 0 ? 0 : 1;
                }

                public static int Compute(int a, int b)
                {
                    return (a * 3 + b) - 7;
                }

                public static string Join(string text)
                {
                    return text + "!";
                }

                private static int Helper(int n)
                {
                    var text = n.ToString();
                    return text.Length + 1;
                }
            }
        }
        """;

    /// <summary>Compiles the sibling and the app into <paramref name="directory"/> and returns the app's path.</summary>
    public static string Create(TargetFramework target, string directory)
    {
        Directory.CreateDirectory(directory);
        var references = target.ReferenceFiles
            .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f))
            .ToList();

        var sibling = Path.Combine(directory, SiblingName + ".dll");
        Emit(
            CSharpCompilation.Create(
                SiblingName,
                new[] { CSharpSyntaxTree.ParseText(SiblingSource), FrameworkAttribute(target) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)),
            sibling,
            resources: null);

        byte[] stringsResource;
        using (var buffer = new MemoryStream())
        {
            using (var writer = new ResourceWriter(buffer))
            {
                writer.AddResource("Welcome", "Welcome from the resource file");
                writer.Generate();
            }

            stringsResource = buffer.ToArray();
        }

        var data = System.Text.Encoding.UTF8.GetBytes("payload from an embedded resource");
        var resources = new[]
        {
            new ResourceDescription("Rich.Strings.resources", () => new MemoryStream(stringsResource), isPublic: true),
            new ResourceDescription("Rich.data.txt", () => new MemoryStream(data), isPublic: true)
        };

        var app = Path.Combine(directory, AppName + ".dll");
        var appReferences = references.Concat(new[] { MetadataReference.CreateFromFile(sibling) });
        Emit(
            CSharpCompilation.Create(
                AppName,
                new[] { CSharpSyntaxTree.ParseText(AppSource), FrameworkAttribute(target) },
                appReferences,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release)),
            app,
            resources);
        return app;
    }

    /// <summary>The attribute an SDK build puts on every assembly, which tells what the app was built for.</summary>
    private static SyntaxTree FrameworkAttribute(TargetFramework target) =>
        CSharpSyntaxTree.ParseText(
            "[assembly: System.Runtime.Versioning.TargetFramework(\"" + target.FrameworkName + "\")]");

    private static void Emit(CSharpCompilation compilation, string path, IEnumerable<ResourceDescription>? resources)
    {
        using var stream = File.Create(path);
        var result = compilation.Emit(stream, manifestResources: resources);
        result.Success.ShouldBeTrue(
            string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
    }
}

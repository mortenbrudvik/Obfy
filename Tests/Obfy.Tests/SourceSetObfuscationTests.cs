using System.Reflection;
using System.Runtime.Loader;
using Autofac;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests;

public class SourceSetObfuscationTests
{
    [Fact]
    public async Task ObfuscateSourcesAsync_RenamesSharedTypeInEveryFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-srcset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var first = Path.Combine(dir, "A.cs");
            var second = Path.Combine(dir, "B.cs");
            await File.WriteAllTextAsync(first, """
                internal static class SharedMarker
                {
                    public static int Value() => 7;
                }
                """);
            await File.WriteAllTextAsync(second, """
                public static class UseMarker
                {
                    public static int Read() => SharedMarker.Value();
                }
                """);

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();
            var service = container.Resolve<IObfuscationService>();
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                ControlFlow = { Enabled = false },
                SymbolRenaming =
                {
                    Enabled = true,
                    RenameTypes = true,
                    RenameMethods = false,
                    PreservePublicApi = true,
                    Mode = NamingMode.Sequential
                }
            };

            var output = Path.Combine(dir, "out");
            var result = await service.ObfuscateSourcesAsync([first, second], output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            var renamedA = await File.ReadAllTextAsync(Path.Combine(output, "A.cs"));
            var renamedB = await File.ReadAllTextAsync(Path.Combine(output, "B.cs"));
            renamedA.ShouldNotContain("SharedMarker");
            renamedB.ShouldNotContain("SharedMarker");
            renamedB.ShouldContain("UseMarker");
            CompileAndInvoke(renamedA, renamedB, "UseMarker", "Read").ShouldBe(7);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ObfuscateSourcesAsync_WithoutAFolder_WritesObfySrcNextToTheFirstFile()
    {
        // The desktop app's output hint for two or more C# files promises exactly this folder.
        var root = Path.Combine(Path.GetTempPath(), "obfy-srcdefault-" + Guid.NewGuid().ToString("N"));
        var firstDir = Path.Combine(root, "one");
        Directory.CreateDirectory(firstDir);
        try
        {
            var first = Path.Combine(firstDir, "A.cs");
            var second = Path.Combine(firstDir, "B.cs");
            await File.WriteAllTextAsync(first, "public static class OnlyA { public static int Value() => 1; }");
            await File.WriteAllTextAsync(second, "public static class OnlyB { public static int Value() => 2; }");

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();
            var service = container.Resolve<IObfuscationService>();
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                ControlFlow = { Enabled = false },
                SymbolRenaming = { Enabled = false }
            };

            var result = await service.ObfuscateSourcesAsync([first, second], null, settings);

            result.Success.ShouldBeTrue(result.ErrorMessage);
            var output = Path.Combine(firstDir, "obfy-src");
            result.OutputPath.ShouldBe(output);
            File.Exists(Path.Combine(output, "A.cs")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "B.cs")).ShouldBeTrue();
            File.Exists(Path.Combine(firstDir, "A.obfuscated.cs")).ShouldBeFalse();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static object? CompileAndInvoke(string first, string second, string typeName, string methodName)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "SourceSetVerify",
            [CSharpSyntaxTree.ParseText(first), CSharpSyntaxTree.ParseText(second)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        emit.Success.ShouldBeTrue(string.Join(Environment.NewLine, emit.Diagnostics));

        var alc = new AssemblyLoadContext("srcset-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            pe.Position = 0;
            var assembly = alc.LoadFromStream(pe);
            var type = assembly.GetType(typeName);
            type.ShouldNotBeNull();
            return type!.GetMethod(methodName)!.Invoke(null, null);
        }
        finally
        {
            alc.Unload();
        }
    }
}

using System.Reflection;
using System.Runtime.Loader;
using Autofac;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.ScenarioTests;

/// <summary>
/// Symbol renaming and generic instantiations of the application's own types, with SDK-built input (core library
/// reference <c>System.Runtime</c>): the member references the compiler writes against <c>Box&lt;int&gt;</c>
/// are bound by name at run time, so they must follow the rename. Without that the program dies with
/// <c>MissingFieldException</c> on the first use. The in-memory cases in <c>Obfy.Tests</c> cover many more shapes;
/// these confirm the same on what <c>dotnet build</c> emits, and through the merge path.
/// </summary>
public sealed class GenericInstantiationScenarioTests
{
    [Theory]
    [InlineData("Standard")]
    [InlineData("RenameOnly")]
    public async Task SdkConsoleApp_WithGenericTypes_RunsLikeTheOriginalAfterRenaming(string configuration)
    {
        var fixture = Path.Combine(
            ScenarioHarness.FindRepoRoot(), "Tests", "Obfy.ScenarioTests", "Fixtures", "GenericConsole");
        var copy = ScenarioHarness.CopyToTemp(fixture);
        try
        {
            ScenarioHarness.DotnetBuild(Path.Combine(copy, "GenericConsole.csproj"));
            var buildOutput = Path.Combine(copy, "bin", "Release", "net10.0");

            var original = ScenarioHarness.RunDotnet(Path.Combine(buildOutput, "GenericConsole.dll"));
            original.ExitCode.ShouldBe(0, original.StdOut + original.StdErr);
            original.StdOut.ShouldContain("84");

            var staged = ScenarioHarness.StageBuildOutput(buildOutput, "GenericConsole.dll");
            var obfDir = Path.Combine(Path.GetDirectoryName(staged)!, "obf");
            ScenarioHarness.CopySidecars(Path.GetDirectoryName(staged)!, obfDir, "GenericConsole.dll");
            var output = Path.Combine(obfDir, "GenericConsole.dll");
            await ScenarioHarness.ObfuscateAsync(staged, output, SettingsFor(configuration));

            var run = ScenarioHarness.RunDotnet(output);
            run.ExitCode.ShouldBe(0, $"{configuration}:{Environment.NewLine}{run.StdOut}{run.StdErr}");
            run.StdOut.ReplaceLineEndings("\n").ShouldBe(original.StdOut.ReplaceLineEndings("\n"), configuration);
        }
        finally
        {
            ScenarioHarness.TryDelete(copy);
        }
    }

    [Fact]
    public async Task MergeAndObfuscate_GenericTypeDefinedInOneLibraryAndUsedInAnother_RunsAfterRenaming()
    {
        var root = Path.Combine(Path.GetTempPath(), "obfy-merge-generic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteClassLib(root, "GenericA", """
                public class Box<T>
                {
                    public T Item;
                    public static int Created;
                    public Box(T item) { Item = item; Created++; }
                    public T Get() => Item;
                    public T Value { get; set; }
                }

                public struct Pair<T>
                {
                    public T First;
                    public Pair(T first) { First = first; }
                    public T Pick() => First;
                }
                """);
            WriteClassLib(root, "GenericB", """
                [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
                public static class UseBox
                {
                    public static string Run()
                    {
                        var b = new Box<int>(42);
                        b.Value = b.Item + 1;
                        var pair = new Pair<string>("p");
                        return (b.Item + b.Get() + b.Value) + "/" + Box<int>.Created + "/" + pair.First + pair.Pick();
                    }
                }
                """, projectReference: "../GenericA/GenericA.csproj");

            var bProject = Path.Combine(root, "GenericB", "GenericB.csproj");
            ScenarioHarness.DotnetBuild(bProject);
            var builtDir = Path.Combine(root, "GenericB", "bin", "Release", "netstandard2.0");
            var aDll = Path.Combine(root, "GenericA.dll");
            var bDll = Path.Combine(root, "GenericB.dll");
            File.Copy(Path.Combine(builtDir, "GenericA.dll"), aDll, overwrite: true);
            File.Copy(Path.Combine(builtDir, "GenericB.dll"), bDll, overwrite: true);

            var expected = Run(bDll, "UseBox");
            expected.ShouldBe("127/1/pp");

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();
            var service = container.Resolve<IObfuscationService>();

            var output = Path.Combine(root, "Merged.obf.dll");
            var result = await service.MergeAndObfuscateAsync([bDll, aDll], output, SettingsFor("RenameOnly"));
            result.Success.ShouldBeTrue(result.ErrorMessage);

            Run(output, "UseBox").ShouldBe(expected);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    private static ObfySettings SettingsFor(string configuration)
    {
        if (configuration == "Standard")
            return new ObfySettings { Level = ObfuscationLevel.Standard };

        var settings = new ObfySettings { Level = ObfuscationLevel.Custom };
        settings.StringEncryption.Enabled = false;
        settings.Metadata.RemoveDebugInfo = false;
        settings.Metadata.RemoveAttributes = false;
        settings.SymbolRenaming.Enabled = true;
        settings.SymbolRenaming.PreservePublicApi = false;
        return settings;
    }

    private static void WriteClassLib(string root, string name, string source, string? projectReference = null)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        var reference = projectReference is null
            ? string.Empty
            : "\n  <ItemGroup>\n    <ProjectReference Include=\"" + projectReference + "\" />\n  </ItemGroup>";
        File.WriteAllText(
            Path.Combine(dir, $"{name}.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>netstandard2.0</TargetFramework>\n"
            + "    <LangVersion>latest</LangVersion>\n  </PropertyGroup>" + reference + "\n</Project>\n");
        File.WriteAllText(Path.Combine(dir, "Class1.cs"), source);
    }

    /// <summary>Runs <c>typeName.Run()</c> from the assembly, or returns the failure prefixed with <c>!</c>.</summary>
    private static string Run(string assemblyPath, string typeName)
    {
        var context = new AssemblyLoadContext("generic-scenario-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        var directory = Path.GetDirectoryName(assemblyPath)!;
        context.Resolving += (loadContext, name) =>
        {
            var sibling = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(sibling) ? loadContext.LoadFromAssemblyPath(sibling) : null;
        };
        try
        {
            var type = context.LoadFromAssemblyPath(assemblyPath).GetType(typeName);
            type.ShouldNotBeNull(typeName);
            return (string)type!.GetMethod("Run")!.Invoke(null, null)!;
        }
        catch (TargetInvocationException ex)
        {
            return "!" + ex.InnerException!.GetType().Name + ": " + ex.InnerException.Message;
        }
        finally
        {
            context.Unload();
        }
    }
}

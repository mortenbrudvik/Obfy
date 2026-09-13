using Autofac;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.ScenarioTests;

public class MergeScenarioTests
{
    [Fact]
    public async Task Merge_TwoSdkClassLibraries_SucceedsAndRuns()
    {
        var root = Path.Combine(Path.GetTempPath(), "obfy-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteClassLib(root, "MergeA", "public static class Alpha { public static int Value() => 3; }");
            WriteClassLib(root, "MergeB", "public static class Beta { public static int Value() => 4; }");

            var aProj = Path.Combine(root, "MergeA", "MergeA.csproj");
            var bProj = Path.Combine(root, "MergeB", "MergeB.csproj");
            ScenarioHarness.DotnetBuild(aProj);
            ScenarioHarness.DotnetBuild(bProj);

            var aBuilt = Path.Combine(root, "MergeA", "bin", "Release", "netstandard2.0", "MergeA.dll");
            var bBuilt = Path.Combine(root, "MergeB", "bin", "Release", "netstandard2.0", "MergeB.dll");
            File.Exists(aBuilt).ShouldBeTrue(aBuilt);
            File.Exists(bBuilt).ShouldBeTrue(bBuilt);

            var aDll = Path.Combine(root, "MergeA.dll");
            var bDll = Path.Combine(root, "MergeB.dll");
            File.Copy(aBuilt, aDll, overwrite: true);
            File.Copy(bBuilt, bDll, overwrite: true);
            var output = Path.Combine(root, "Merged.dll");

            var builder = new ContainerBuilder();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
            builder.RegisterModule<ObfuscationModule>();
            await using var container = builder.Build();
            var merger = container.Resolve<IAssemblyMerger>();

            var result = await merger.MergeAsync([aDll, bDll], output, new AssemblyMergeSettings());
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.MergedAssemblyCount.ShouldBe(2);
            File.Exists(output).ShouldBeTrue();

            var alc = new System.Runtime.Loader.AssemblyLoadContext("merge-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                var asm = alc.LoadFromAssemblyPath(output);
                Invoke(asm, "Alpha", "Value").ShouldBe(3);
                Invoke(asm, "Beta", "Value").ShouldBe(4);
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    private static void WriteClassLib(string root, string name, string source)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <LangVersion>latest</LangVersion>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "Class1.cs"), source);
    }

    private static object? Invoke(System.Reflection.Assembly asm, string typeName, string methodName)
    {
        var type = asm.GetType(typeName);
        type.ShouldNotBeNull(typeName);
        var method = type!.GetMethod(methodName);
        method.ShouldNotBeNull(methodName);
        return method!.Invoke(null, null);
    }
}

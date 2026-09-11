using System.Runtime.Loader;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// End-to-end tests that obfuscate a real compiled assembly (referencing the actual runtime, so its
/// core-library reference is System.Runtime / System.Private.CoreLib as in a shipped app) and then
/// load and run the result. These catch failures that unit tests built on an mscorlib test module miss
/// — most importantly, references to framework types (crypto, ...) that do not resolve through the
/// core-library facade at runtime.
/// </summary>
public class EndToEndObfuscationTests
{
    private static string CompileToAssembly(string source, string dir, string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static object? LoadAndInvoke(string assemblyPath, string typeName, string methodName)
    {
        var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(assemblyPath);
            var type = asm.GetType(typeName);
            type.ShouldNotBeNull();
            var method = type!.GetMethod(methodName);
            method.ShouldNotBeNull();
            return method!.Invoke(null, null);
        }
        finally
        {
            alc.Unload();
        }
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public async Task StringEncryption_RunsOnRealAssembly(EncryptionAlgorithm algorithm)
    {
        // A real assembly references System.Runtime, which does not host the crypto types. Referencing
        // Aes through the corlib facade throws TypeLoadException at runtime; this catches that.
        const string source = "public static class Lib { public static string Get() => \"IntegrationSecretString\"; }";

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "StrLib");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = true, Algorithm = algorithm, MinStringLength = 3 }
            };
            var context = PipelineContext.ForAssembly(module, settings);
            var obfuscator = new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object);

            (await obfuscator.ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "StrLib.obf.dll");
            module.Write(output);

            LoadAndInvoke(output, "Lib", "Get").ShouldBe("IntegrationSecretString");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task AntiTamper_VerifyPassesOnUntamperedRealAssembly()
    {
        // Exercises the full anti-tamper chain on a real assembly: inject -> AssemblyProcessor.SaveAsync
        // (writes, then patches the integrity hash) -> load and run. The runtime Verify() in the module
        // initializer must NOT falsely trip, and references SHA256, which is not in the corlib facade.
        const string source = "public static class Lib { public static int Get() => 4242; }";

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = CompileToAssembly(source, dir, "TamperLib");
            var module = ModuleDefMD.Load(File.ReadAllBytes(input));

            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                Protection = { AntiTamper = { Enabled = true, CheckModuleInitializer = true } }
            };
            var context = PipelineContext.ForAssembly(module, settings);

            var obfuscator = new AntiTamperObfuscator(new Mock<ILogger<AntiTamperObfuscator>>().Object);
            (await obfuscator.ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "TamperLib.obf.dll");
            var processor = new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object);
            await processor.SaveAsync(context, output); // writes and patches the integrity hash

            // If Verify() threw a TypeLoadException or falsely detected tampering, this would fail.
            LoadAndInvoke(output, "Lib", "Get").ShouldBe(4242);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}

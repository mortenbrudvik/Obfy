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
using Obfy.Runtime;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Method encryption XORs IL in the PE and decrypts it from the module initializer when
/// <c>Marshal.GetHINSTANCE</c> is a real base. A stream load reports -1 and the initializer
/// returns, so packing has to decrypt the image before <c>LoadFromStream</c>.
/// </summary>
public class MethodImageDecryptorTests
{
    [Fact]
    public void Magic_MatchesTheBlobTheEncryptorWrites()
    {
        MethodImageDecryptor.Magic.ToArray().ShouldBe(MethodEncryptionMetadata.Magic.ToArray());
        MethodImageDecryptor.MagicLength.ShouldBe(MethodEncryptionMetadata.MagicLength);
        MethodImageDecryptor.HeaderBytes.ShouldBe(MethodEncryptionMetadata.HeaderBytes);
        MethodImageDecryptor.EntryBytes.ShouldBe(MethodEncryptionMetadata.EntryBytes);
        MethodImageDecryptor.KeyOffset.ShouldBe(MethodEncryptionMetadata.KeyOffset);
    }

    [Fact]
    public void Decrypt_LeavesAnImageWithNoBlobAlone()
    {
        var bytes = new byte[] { 0x4D, 0x5A, 0x00, 0x01 };
        MethodImageDecryptor.Decrypt(bytes);
        bytes.ShouldBe(new byte[] { 0x4D, 0x5A, 0x00, 0x01 });
    }

    [Fact]
    public async Task Decrypt_LetsAStreamLoadRun_AndASecondPassDoesNotXorAgain()
    {
        const string source = """
            public static class Lib
            {
                public static int Get()
                {
                    int x = 7;
                    x = x + 35;
                    return x;
                }
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-method-image-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = Compile(source, dir, "MethodImageLib");
            var output = Path.Combine(dir, "MethodImageLib.obf.dll");
            var result = await CreateService().ObfuscateAsync(input, output, new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = false },
                SymbolRenaming = { Enabled = false, PreservePublicApi = true },
                Protection = { MethodEncryption = true }
            });
            result.Success.ShouldBeTrue(result.ErrorMessage);

            var encrypted = File.ReadAllBytes(output);
            var once = encrypted.ToArray();
            MethodImageDecryptor.Decrypt(once);
            once.SequenceEqual(encrypted).ShouldBeFalse("the decryptor should undo the method-body XOR");
            InvokeFromStream(once.ToArray()).ShouldBe(42);

            var twice = once.ToArray();
            MethodImageDecryptor.Decrypt(twice);
            twice.SequenceEqual(once).ShouldBeTrue("a second pass must leave the plaintext image alone");
            InvokeFromStream(twice).ShouldBe(42);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    private static int InvokeFromStream(byte[] pe)
    {
        var alc = new AssemblyLoadContext($"method-image-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var type = alc.LoadFromStream(new MemoryStream(pe)).GetType("Lib");
            var method = type!.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
            return (int)method!.Invoke(null, null)!;
        }
        finally
        {
            alc.Unload();
        }
    }

    private static string Compile(string source, string dir, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(dir, assemblyName + ".dll");
        using var stream = File.Create(path);
        var emit = compilation.Emit(stream);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static IObfuscationService CreateService()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build().Resolve<IObfuscationService>();
    }
}

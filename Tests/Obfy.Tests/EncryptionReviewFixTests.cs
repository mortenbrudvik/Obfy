using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Regression tests for the encryption / anti-* review findings: idempotent resource encryption,
/// explicit method-encryption key reuse, prefix-safe call injection, visible resource failures and
/// dependency-embedding path containment.
/// </summary>
public class EncryptionReviewFixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"obfy-encfix-{Guid.NewGuid():N}");

    public EncryptionReviewFixTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    #region Helpers

    private const string ReaderSource = """
        using System.IO;
        using System.Reflection;
        using System.Resources;
        public static class Reader
        {
            public static string Get(string key) =>
                new ResourceManager("Test.Strings", typeof(Reader).Assembly).GetString(key);

            public static byte[] Raw(string name)
            {
                using var s = typeof(Reader).Assembly.GetManifestResourceStream(name);
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
        """;

    private string CompileLibrary(string source, string assemblyName, params ResourceDescription[] resources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(_dir, assemblyName + ".dll");
        using var stream = File.Create(path);
        var emit = compilation.Emit(stream, manifestResources: resources);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static byte[] BuildResources(params (string Key, string Value)[] entries)
    {
        using var ms = new MemoryStream();
        using (var writer = new System.Resources.ResourceWriter(ms))
        {
            foreach (var (key, value) in entries)
                writer.AddResource(key, value);
            writer.Generate();
        }

        return ms.ToArray();
    }

    private static T WithLoadedAssembly<T>(string path, Func<System.Reflection.Assembly, T> body)
    {
        var alc = new AssemblyLoadContext($"encfix-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            return body(alc.LoadFromAssemblyPath(path));
        }
        finally
        {
            alc.Unload();
        }
    }

    /// <summary>Loads <paramref name="input"/>, runs the obfuscators in order, writes the result to <paramref name="output"/>.</summary>
    private static async Task<(ObfuscationResult Result, PipelineContext Context)> RunPass(
        string input, string output, ObfySettings settings, params IObfuscator[] obfuscators)
    {
        using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
        var context = PipelineContext.ForAssembly(module, settings);
        context.InputPath = input;
        ObfuscationResult result = null!;
        foreach (var obfuscator in obfuscators)
        {
            result = await obfuscator.ObfuscateAsync(context);
            result.Success.ShouldBeTrue(result.ErrorMessage);
        }

        module.Write(output);
        return (result, context);
    }

    private static ModuleDef CreateModule(string name = "TestAssembly")
    {
        var module = new ModuleDefUser(name, Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var assembly = new AssemblyDefUser(name, new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);
        return module;
    }

    #endregion

    #region 1. Resource encryption is idempotent

    [Fact]
    public async Task StringEncryption_SecondPassOverResourceStrings_KeepsRuntimeValueCorrect()
    {
        const string secret = "ResourceSecretValue";
        var resources = BuildResources(("long", secret), ("short", "Hi"));
        var original = CompileLibrary(
            ReaderSource, "StrIdem",
            new ResourceDescription("Test.Strings.resources", () => new MemoryStream(resources), isPublic: true));

        var settings = new ObfySettings
        {
            StringEncryption = { Enabled = true, EncryptResourceStrings = true, MinStringLength = 3 }
        };
        var pass1 = Path.Combine(_dir, "StrIdem.pass1.dll");
        var pass2 = Path.Combine(_dir, "StrIdem.pass2.dll");
        var obfuscator = new StringEncryptionObfuscator(NullLogger<StringEncryptionObfuscator>.Instance);

        await RunPass(original, pass1, settings, obfuscator);
        WithLoadedAssembly(pass1, asm => Invoke(asm, "Get", "long")).ShouldBe(secret);

        await RunPass(pass1, pass2, settings, obfuscator);

        WithLoadedAssembly(pass2, asm => Invoke(asm, "Get", "long")).ShouldBe(secret);
        WithLoadedAssembly(pass2, asm => Invoke(asm, "Get", "short")).ShouldBe("Hi");
    }

    [Fact]
    public async Task StringEncryption_ResourceStringAlreadyCarryingCipherPrefix_IsNotEncryptedAgain()
    {
        var already = StringEncryptionObfuscator.ResourceCipherPrefix + "QUJDREVGR0hJSktMTU5PUA==";
        var module = CreateModule();
        module.Resources.Add(new EmbeddedResource(
            "App.Form.resources",
            BuildResources(("prefixed", already), ("plain", "PlainSecretText"))));

        var obfuscator = new StringEncryptionObfuscator(NullLogger<StringEncryptionObfuscator>.Instance);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            StringEncryption = { Enabled = true, EncryptResourceStrings = true, MinStringLength = 3 }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.StringsEncrypted.ShouldBe(1);

        var values = ReadResourceStrings(module.Resources.OfType<EmbeddedResource>().Single());
        values["prefixed"].ShouldBe(already);
        values["plain"].ShouldNotBe("PlainSecretText");
        values["plain"][0].ShouldBe(StringEncryptionObfuscator.ResourceCipherPrefix);
        context.Warnings.ShouldContain(w => w.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResourceEncryption_SecondPassOverProtectedInput_KeepsRuntimeBytesCorrectAndWarns()
    {
        var payload = Enumerable.Range(0, 64).Select(i => (byte)(i * 7 + 3)).ToArray();
        var original = CompileLibrary(
            ReaderSource, "ResIdem",
            new ResourceDescription("Test.data.bin", () => new MemoryStream(payload), isPublic: true));

        var settings = new ObfySettings { ResourceEncryption = { Enabled = true } };
        var pass1 = Path.Combine(_dir, "ResIdem.pass1.dll");
        var pass2 = Path.Combine(_dir, "ResIdem.pass2.dll");
        var obfuscator = new ResourceEncryptionObfuscator(NullLogger<ResourceEncryptionObfuscator>.Instance);

        var (first, _) = await RunPass(original, pass1, settings, obfuscator);
        first.Statistics.ResourcesEncrypted.ShouldBe(1);
        WithLoadedAssembly(pass1, asm => InvokeBytes(asm, "Raw", "Test.data.bin")).ShouldBe(payload);

        var (second, context) = await RunPass(pass1, pass2, settings, obfuscator);

        second.Statistics.ResourcesEncrypted.ShouldBe(0);
        context.Warnings.ShouldContain(w => w.Contains("already", StringComparison.OrdinalIgnoreCase));
        context.Warnings.ShouldNotContain(w => w.Contains("no Assembly.GetManifestResourceStream", StringComparison.Ordinal));
        WithLoadedAssembly(pass2, asm => InvokeBytes(asm, "Raw", "Test.data.bin")).ShouldBe(payload);

        using var loaded = ModuleDefMD.Load(File.ReadAllBytes(pass2));
        loaded.Types.Count(t => t.Name == "<ResourceDecryptor>").ShouldBe(1);
    }

    [Fact]
    public async Task ResourceEncryption_SecondPassAfterSymbolRenaming_StillRecognisesTheLoader()
    {
        // Symbol renaming renames the injected helper type, fields and methods, so detection must not
        // depend on the helper names.
        var payload = Enumerable.Range(0, 64).Select(i => (byte)(i * 5 + 1)).ToArray();
        var original = CompileLibrary(
            ReaderSource, "ResRenamed",
            new ResourceDescription("Test.data.bin", () => new MemoryStream(payload), isPublic: true));

        var settings = new ObfySettings
        {
            ResourceEncryption = { Enabled = true },
            SymbolRenaming = { Enabled = true, PreservePublicApi = true }
        };
        var pass1 = Path.Combine(_dir, "ResRenamed.pass1.dll");
        var pass2 = Path.Combine(_dir, "ResRenamed.pass2.dll");
        var encrypt = new ResourceEncryptionObfuscator(NullLogger<ResourceEncryptionObfuscator>.Instance);
        var rename = new SymbolRenamingObfuscator(new NameGenerator(), NullLogger<SymbolRenamingObfuscator>.Instance);

        await RunPass(original, pass1, settings, encrypt, rename);
        using (var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(pass1)))
            protectedModule.Types.ShouldNotContain(t => t.Name == "<ResourceDecryptor>");
        WithLoadedAssembly(pass1, asm => InvokeBytes(asm, "Raw", "Test.data.bin")).ShouldBe(payload);

        var (second, context) = await RunPass(pass1, pass2, settings, encrypt);

        second.Statistics.ResourcesEncrypted.ShouldBe(0);
        context.Warnings.ShouldContain(w => w.Contains("already", StringComparison.OrdinalIgnoreCase));
        WithLoadedAssembly(pass2, asm => InvokeBytes(asm, "Raw", "Test.data.bin")).ShouldBe(payload);
    }

    private static string? Invoke(System.Reflection.Assembly asm, string method, string arg) =>
        (string?)asm.GetType("Reader")!.GetMethod(method)!.Invoke(null, new object[] { arg });

    private static byte[] InvokeBytes(System.Reflection.Assembly asm, string method, string arg) =>
        (byte[])asm.GetType("Reader")!.GetMethod(method)!.Invoke(null, new object[] { arg })!;

    private static Dictionary<string, string> ReadResourceStrings(EmbeddedResource resource)
    {
        using var input = new MemoryStream(resource.CreateReader().ToArray());
        using var reader = new System.Resources.ResourceReader(input);
        var values = new Dictionary<string, string>();
        var enumerator = reader.GetEnumerator();
        while (enumerator.MoveNext())
            values[enumerator.Key.ToString()!] = (string)enumerator.Value!;
        return values;
    }

    #endregion

    #region 2. Method-encryption keys

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(600)]
    public void MethodEncryption_Keys_AreNonZeroAndDistinctUpToTheSingleByteLimit(int count)
    {
        var keys = MethodEncryptionObfuscator.CreateDistinctKeys(count);

        keys.Length.ShouldBe(count);
        keys.ShouldAllBe(k => k != 0);
        keys.Take(255).Distinct().Count().ShouldBe(Math.Min(count, 255));
    }

    [Fact]
    public void MethodEncryption_Keys_ReuseIsEvenlySpreadOnceAllNonZeroBytesAreUsed()
    {
        // 255 non-zero single-byte keys exist. Beyond that, keys must repeat; reuse is spread over the
        // whole key space (each key used at most twice for 510 methods) rather than piling onto a few.
        var keys = MethodEncryptionObfuscator.CreateDistinctKeys(510);

        keys.GroupBy(k => k).ShouldAllBe(g => g.Count() == 2);
    }

    #endregion

    #region 3. Prefix-safe call injection

    private static MethodDef AddVolatileReader(ModuleDef module, TypeDef type, string name, FieldDef field, bool asEntryPoint = false)
    {
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Volatile));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, field));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;
        type.Methods.Add(method);
        if (asEntryPoint)
            module.EntryPoint = method;
        return method;
    }

    private static (ModuleDef Module, TypeDef Type, FieldDef Field) CreateVolatileHolder()
    {
        var module = CreateModule();
        var type = new TypeDefUser("TestNamespace", "Holder", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        var field = new FieldDefUser(
            "Flag",
            new FieldSig(module.CorLibTypes.Int32),
            FieldAttributes.Public | FieldAttributes.Static);
        type.Fields.Add(field);
        module.Types.Add(type);
        return (module, type, field);
    }

    /// <summary>Every prefix opcode must be immediately followed by the instruction it prefixes.</summary>
    private static void AssertPrefixesAreAdjacent(MethodDef method)
    {
        var instructions = method.Body.Instructions;
        for (var i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode.OpCodeType != OpCodeType.Prefix)
                continue;

            var next = instructions[i + 1];
            (next.OpCode == OpCodes.Ldsfld || next.OpCode == OpCodes.Ldfld || next.OpCode == OpCodes.Stsfld)
                .ShouldBeTrue($"prefix {instructions[i].OpCode.Name} is followed by {next.OpCode.Name}");
        }
    }

    [Fact]
    public async Task AntiDebug_MethodStartingWithVolatilePrefix_StaysValidIl()
    {
        var (module, type, field) = CreateVolatileHolder();
        var reader = AddVolatileReader(module, type, "Read", field);

        var obfuscator = new AntiDebugObfuscator(NullLogger<AntiDebugObfuscator>.Instance);
        var context = PipelineContext.ForAssembly(module, new ObfySettings { Protection = { AntiDebug = true } });
        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        reader.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "Check").ShouldBeTrue();
        AssertPrefixesAreAdjacent(reader);

        // The CLR JIT rejects a prefix that does not directly precede its instruction.
        var path = Path.Combine(_dir, "AntiDebugPrefix.dll");
        module.Write(path);
        WithLoadedAssembly(path, asm =>
        {
            var method = asm.GetType("TestNamespace.Holder")!.GetMethod("Read")!;
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            return 0;
        });
    }

    [Fact]
    public async Task AntiTamper_EntryPointStartingWithVolatilePrefix_StaysValidIl()
    {
        var (module, type, field) = CreateVolatileHolder();
        var entry = AddVolatileReader(module, type, "Main", field, asEntryPoint: true);

        var obfuscator = new AntiTamperObfuscator(NullLogger<AntiTamperObfuscator>.Instance);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            Protection = { AntiTamper = { Enabled = true, CheckEntryPoint = true, CheckModuleInitializer = false } }
        });
        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        entry.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "Verify").ShouldBeTrue();
        AssertPrefixesAreAdjacent(entry);
    }

    #endregion

    #region 4. Failed resource rewrites are visible

    private static byte[] UnreadableResourceBytes()
    {
        var original = BuildResources(("keep", "KeepMePlain"));
        var marker = System.Text.Encoding.UTF8.GetBytes("System.Resources.ResourceReader");
        var index = original.AsSpan().IndexOf(marker);
        index.ShouldBeGreaterThanOrEqualTo(0);
        original[index + marker.Length - 1] = (byte)'X';
        return original;
    }

    [Fact]
    public async Task StringEncryption_FailedResourceRewrite_IsReportedAsNotProtectedNotAsExcludedByPattern()
    {
        var module = CreateModule();
        module.Resources.Add(new EmbeddedResource("App.Form.resources", UnreadableResourceBytes()));

        var obfuscator = new StringEncryptionObfuscator(NullLogger<StringEncryptionObfuscator>.Instance);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            StringEncryption = { Enabled = true, EncryptResourceStrings = true, MinStringLength = 3 }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        context.SkippedItems.ShouldNotContain(i => i.Reason == SkipReason.ResourceExcluded);
        context.Warnings.ShouldContain(w =>
            w.Contains("App.Form.resources", StringComparison.Ordinal) &&
            w.Contains("not", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StringEncryption_FailedResourceRewrite_DoesNotLogTheException()
    {
        var module = CreateModule();
        module.Resources.Add(new EmbeddedResource("App.Form.resources", UnreadableResourceBytes()));

        var logger = new Mock<ILogger<StringEncryptionObfuscator>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var obfuscator = new StringEncryptionObfuscator(logger.Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            StringEncryption = { Enabled = true, EncryptResourceStrings = true, MinStringLength = 3 }
        });

        await obfuscator.ObfuscateAsync(context);

        // Log the exception type only: exception messages can carry paths or content.
        logger.Invocations
            .Where(i => i.Method.Name == "Log")
            .Select(i => i.Arguments[3])
            .ShouldNotContain(e => e != null);
    }

    #endregion

    #region 5. Dependency embedding stays inside the input folder

    [Theory]
    [InlineData("..{0}evil")]
    [InlineData("..{0}..{0}evil")]
    [InlineData("sub{0}evil")]
    public async Task DependencyEmbedding_AssemblyRefNameWithSeparators_IsNotEmbedded(string template)
    {
        var inputDir = Path.Combine(_dir, "in", "bin");
        Directory.CreateDirectory(Path.Combine(inputDir, "sub"));
        File.WriteAllBytes(Path.Combine(_dir, "in", "evil.dll"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(_dir, "evil.dll"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(inputDir, "sub", "evil.dll"), new byte[] { 1, 2, 3 });

        await AssertHostileRefNotEmbedded(inputDir, string.Format(template, Path.DirectorySeparatorChar));
    }

    [Fact]
    public async Task DependencyEmbedding_RootedAssemblyRefName_IsNotEmbedded()
    {
        var inputDir = Path.Combine(_dir, "in2", "bin");
        Directory.CreateDirectory(inputDir);
        var outside = Path.Combine(_dir, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "evil.dll"), new byte[] { 1, 2, 3 });

        await AssertHostileRefNotEmbedded(inputDir, Path.Combine(outside, "evil"));
    }

    private static ModuleDef CreateModuleReferencing(string assemblyRefName)
    {
        var module = CreateModule("Host");
        var type = new TypeDefUser("TestNamespace", "UsesIt", module.CorLibTypes.Object.TypeDefOrRef);
        var typeRef = new TypeRefUser(module, "Ns", "T", new AssemblyRefUser(assemblyRefName, new Version(1, 0, 0, 0)));
        type.Fields.Add(new FieldDefUser(
            "F", new FieldSig(new ClassSig(typeRef)), FieldAttributes.Public | FieldAttributes.Static));
        module.Types.Add(type);

        // GetAssemblyRefs only enumerates a module loaded from metadata, as the pipeline always does.
        using var stream = new MemoryStream();
        module.Write(stream);
        var loaded = ModuleDefMD.Load(stream.ToArray());
        loaded.GetAssemblyRefs().ShouldContain(r => r.Name.String == assemblyRefName);
        return loaded;
    }

    private async Task AssertHostileRefNotEmbedded(string inputDir, string refName)
    {
        var module = CreateModuleReferencing(refName);
        var inputPath = Path.Combine(inputDir, "Host.dll");
        File.WriteAllBytes(inputPath, new byte[] { 0 });

        var obfuscator = new DependencyEmbeddingObfuscator(NullLogger<DependencyEmbeddingObfuscator>.Instance);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            DependencyEmbedding = { Enabled = true, IncludePatterns = new List<string> { "*" } }
        });
        context.InputPath = inputPath;

        var result = await obfuscator.ObfuscateAsync(context);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.AssembliesEmbedded.ShouldBe(0);
        module.Resources.ShouldNotContain(r => r.Name.String.StartsWith(DependencyEmbeddingObfuscator.ResourcePrefix));
    }

    [Theory]
    [InlineData("Newtonsoft.Json", true)]
    [InlineData("Foo..Bar", true)]
    [InlineData("..", true)]
    [InlineData("", false)]
    [InlineData("a/b", false)]
    [InlineData("a\b", false)]
    [InlineData("C:evil", false)]
    [InlineData("evil:stream", false)]
    [InlineData("we*ird", false)]
    public void DependencyEmbedding_TryResolveSibling_AcceptsOnlyPlainFileNames(string simpleName, bool accepted)
    {
        var inputDir = Path.Combine(_dir, "resolve");
        Directory.CreateDirectory(inputDir);

        var ok = DependencyEmbeddingObfuscator.TryResolveSibling(inputDir, simpleName, simpleName + ".dll", out var path);

        ok.ShouldBe(accepted);
        if (accepted)
            Path.GetDirectoryName(path).ShouldBe(Path.GetFullPath(inputDir));
    }

    [Fact]
    public async Task DependencyEmbedding_PlainSiblingAssembly_StillEmbedded()
    {
        var inputDir = Path.Combine(_dir, "in3");
        Directory.CreateDirectory(inputDir);
        File.WriteAllBytes(Path.Combine(inputDir, "Sibling.Lib.dll"), new byte[] { 9, 9, 9 });

        var module = CreateModuleReferencing("Sibling.Lib");
        var inputPath = Path.Combine(inputDir, "Host.dll");
        File.WriteAllBytes(inputPath, new byte[] { 0 });

        var obfuscator = new DependencyEmbeddingObfuscator(NullLogger<DependencyEmbeddingObfuscator>.Instance);
        var context = PipelineContext.ForAssembly(module, new ObfySettings { DependencyEmbedding = { Enabled = true } });
        context.InputPath = inputPath;

        var result = await obfuscator.ObfuscateAsync(context);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.AssembliesEmbedded.ShouldBe(1);
        module.Resources.ShouldContain(r => r.Name.String == DependencyEmbeddingObfuscator.ResourcePrefix + "Sibling.Lib.dll");
    }

    #endregion
}

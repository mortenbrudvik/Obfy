using System.Runtime.Loader;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class ReviewFixesIlObfuscatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"obfy-revfixes-il-{Guid.NewGuid():N}");

    public ReviewFixesIlObfuscatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    #region Helpers

    private string CompileLibrary(
        string source,
        string assemblyName,
        OptimizationLevel optimizationLevel = OptimizationLevel.Release,
        params ResourceDescription[] resources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: optimizationLevel));

        var path = Path.Combine(_dir, assemblyName + ".dll");
        using var stream = File.Create(path);
        var emit = compilation.Emit(stream, manifestResources: resources);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static T WithLoadedAssembly<T>(string path, Func<System.Reflection.Assembly, T> body)
    {
        var alc = new AssemblyLoadContext($"revfix-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            return body(alc.LoadFromAssemblyPath(path));
        }
        finally
        {
            alc.Unload();
        }
    }

    private static ModuleDefUser CreateTestModule(string name = "TestAssembly")
    {
        var module = new ModuleDefUser(
            name,
            Guid.NewGuid(),
            new AssemblyRefUser("System.Runtime", new Version(10, 0, 0, 0), new PublicKeyToken("b03f5f7f11d50a3a"), UTF8String.Empty));
        new AssemblyDefUser(name, new Version(1, 0, 0, 0)).Modules.Add(module);
        return module;
    }

    private static byte[] BuildResxBytes(params (string Key, string Value)[] entries)
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

    private static void AddObfuscationAttribute(
        IHasCustomAttribute target,
        bool exclude = true,
        string? feature = null,
        bool? applyToMembers = null)
    {
        var module = target switch
        {
            TypeDef t => t.Module,
            IMemberDef m => m.Module,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        var attrType = new TypeRefUser(module, "System.Reflection", "ObfuscationAttribute", module.CorLibTypes.AssemblyRef);
        var ctor = new MemberRefUser(module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), attrType);
        var attr = new CustomAttribute(ctor);
        attr.NamedArguments.Add(new CANamedArgument(
            isField: false, module.CorLibTypes.Boolean, "Exclude",
            new CAArgument(module.CorLibTypes.Boolean, exclude)));
        if (feature is not null)
        {
            attr.NamedArguments.Add(new CANamedArgument(
                isField: false, module.CorLibTypes.String, "Feature",
                new CAArgument(module.CorLibTypes.String, feature)));
        }

        if (applyToMembers.HasValue)
        {
            attr.NamedArguments.Add(new CANamedArgument(
                isField: false, module.CorLibTypes.Boolean, "ApplyToMembers",
                new CAArgument(module.CorLibTypes.Boolean, applyToMembers.Value)));
        }

        target.CustomAttributes.Add(attr);
    }

    #endregion

    #region ResourceEncryption Tests

    [Fact]
    public async Task ResourceEncryption_TypedGetManifestResourceStream_WorksForAllThreePaths()
    {
        var rootBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        var nsBytes = new byte[] { 10, 20, 30, 40, 50, 60 };
        const string source = """
            using System;
            using System.IO;
            using System.Reflection;

            public class GlobalMarker { }

            namespace MyApp.Resources
            {
                public class NsMarker { }
            }

            public static class Loader
            {
                public static byte[] ReadNullType() => Read(typeof(Loader).Assembly.GetManifestResourceStream((Type)null!, "root.bin"));
                public static byte[] ReadNullNamespace() => Read(typeof(Loader).Assembly.GetManifestResourceStream(typeof(GlobalMarker), "root.bin"));
                public static byte[] ReadWithNamespace() => Read(typeof(Loader).Assembly.GetManifestResourceStream(typeof(MyApp.Resources.NsMarker), "sub.bin"));

                private static byte[] Read(Stream? s)
                {
                    if (s == null) return Array.Empty<byte>();
                    using (s)
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            """;

        var input = CompileLibrary(
            source,
            "ResTyped",
            OptimizationLevel.Release,
            new ResourceDescription("root.bin", () => new MemoryStream(rootBytes), isPublic: true),
            new ResourceDescription("MyApp.Resources.sub.bin", () => new MemoryStream(nsBytes), isPublic: true));

        var output = Path.Combine(_dir, "ResTyped.obf.dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(input)))
        {
            var context = PipelineContext.ForAssembly(module, new ObfySettings
            {
                ResourceEncryption = { Enabled = true, Algorithm = EncryptionAlgorithm.Aes256 }
            });
            var result = await new ResourceEncryptionObfuscator(NullLogger<ResourceEncryptionObfuscator>.Instance)
                .ObfuscateAsync(context);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            context.Warnings.ShouldBeEmpty();

            var decryptor = module.Types.Single(t => t.Name == "<ResourceDecryptor>");
            var loadStream = decryptor.FindMethod("LoadResourceStream")!;
            loadStream.Body.ExceptionHandlers.ShouldContain(eh => eh.HandlerType == ExceptionHandlerType.Finally);

            module.Write(output);
        }

        WithLoadedAssembly(output, asm =>
        {
            var loader = asm.GetType("Loader")!;
            ((byte[])loader.GetMethod("ReadNullType")!.Invoke(null, null)!).ShouldBe(rootBytes);
            ((byte[])loader.GetMethod("ReadNullNamespace")!.Invoke(null, null)!).ShouldBe(rootBytes);
            ((byte[])loader.GetMethod("ReadWithNamespace")!.Invoke(null, null)!).ShouldBe(nsBytes);
            return 0;
        });
    }

    [Fact]
    public async Task ResourceEncryption_IgnoresUserClassNamedAssembly()
    {
        var realBytes = new byte[] { 42, 43, 44, 45 };
        const string source = """
            using System;
            using System.IO;
            using System.Text;

            namespace UserCode
            {
                public class Assembly
                {
                    public Stream GetManifestResourceStream(string name) =>
                        new MemoryStream(Encoding.UTF8.GetBytes("user1:" + name));

                    public Stream GetManifestResourceStream(Type type, string name) =>
                        new MemoryStream(Encoding.UTF8.GetBytes("user2:" + (type?.Name ?? "null") + ":" + name));
                }
            }

            public static class Probe
            {
                public static string CallFake1()
                {
                    using var s = new UserCode.Assembly().GetManifestResourceStream("alpha");
                    using var sr = new StreamReader(s);
                    return sr.ReadToEnd();
                }

                public static string CallFake2()
                {
                    using var s = new UserCode.Assembly().GetManifestResourceStream(typeof(Probe), "beta");
                    using var sr = new StreamReader(s);
                    return sr.ReadToEnd();
                }

                public static byte[] CallReal()
                {
                    using var s = typeof(Probe).Assembly.GetManifestResourceStream("real.bin")!;
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            """;

        var input = CompileLibrary(
            source,
            "FakeAssemblyRes",
            OptimizationLevel.Release,
            new ResourceDescription("real.bin", () => new MemoryStream(realBytes), isPublic: true));

        var output = Path.Combine(_dir, "FakeAssemblyRes.obf.dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(input)))
        {
            var context = PipelineContext.ForAssembly(module, new ObfySettings
            {
                ResourceEncryption = { Enabled = true, Algorithm = EncryptionAlgorithm.Xor }
            });
            var result = await new ResourceEncryptionObfuscator(NullLogger<ResourceEncryptionObfuscator>.Instance)
                .ObfuscateAsync(context);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            context.Warnings.ShouldNotContain(w => w.Contains("UserCode.Assembly", StringComparison.Ordinal));

            var probe = module.Types.Single(t => t.Name == "Probe");
            probe.FindMethod("CallFake1")!.Body.Instructions
                .Any(i => i.Operand is IMethod m && m.DeclaringType?.Name == "<ResourceDecryptor>")
                .ShouldBeFalse();
            probe.FindMethod("CallFake2")!.Body.Instructions
                .Any(i => i.Operand is IMethod m && m.DeclaringType?.Name == "<ResourceDecryptor>")
                .ShouldBeFalse();

            module.Write(output);
        }

        WithLoadedAssembly(output, asm =>
        {
            var probe = asm.GetType("Probe")!;
            ((string)probe.GetMethod("CallFake1")!.Invoke(null, null)!).ShouldBe("user1:alpha");
            ((string)probe.GetMethod("CallFake2")!.Invoke(null, null)!).ShouldBe("user2:Probe:beta");
            ((byte[])probe.GetMethod("CallReal")!.Invoke(null, null)!).ShouldBe(realBytes);
            return 0;
        });
    }

    #endregion

    #region StringEncryption Tests

    [Fact]
    public async Task StringEncryption_IgnoresUserClassNamedResourceManager()
    {
        var resBytes = BuildResxBytes(("Greeting", "HelloFromRealRes"));
        const string source = """
            using System.Globalization;
            using System.Resources;

            namespace CustomRm
            {
                public class ResourceManager
                {
                    public string GetString(string name) => "custom:" + name;
                    public string GetString(string name, CultureInfo? culture) => "custom2:" + name;
                }
            }

            public static class Probe
            {
                public static string Fake1() => new CustomRm.ResourceManager().GetString("Greeting");
                public static string Fake2() => new CustomRm.ResourceManager().GetString("Greeting", CultureInfo.InvariantCulture);
                public static string? Real() => new System.Resources.ResourceManager("App.Strings", typeof(Probe).Assembly).GetString("Greeting");
            }
            """;

        var input = CompileLibrary(
            source,
            "FakeRm",
            OptimizationLevel.Release,
            new ResourceDescription("App.Strings.resources", () => new MemoryStream(resBytes), isPublic: true));

        var output = Path.Combine(_dir, "FakeRm.obf.dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(input)))
        {
            var context = PipelineContext.ForAssembly(module, new ObfySettings
            {
                StringEncryption = { Enabled = true, EncryptConstantStrings = false, EncryptResourceStrings = true, MinStringLength = 3 }
            });
            var result = await new StringEncryptionObfuscator(NullLogger<StringEncryptionObfuscator>.Instance)
                .ObfuscateAsync(context);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            var probe = module.Types.Single(t => t.Name == "Probe");
            probe.FindMethod("Fake1")!.Body.Instructions
                .Any(i => i.Operand is IMethod m && m.DeclaringType?.Name == "<StringDecryptor>")
                .ShouldBeFalse();
            probe.FindMethod("Fake2")!.Body.Instructions
                .Any(i => i.Operand is IMethod m && m.DeclaringType?.Name == "<StringDecryptor>")
                .ShouldBeFalse();
            probe.FindMethod("Real")!.Body.Instructions
                .Any(i => i.Operand is IMethod m && m.DeclaringType?.Name == "<StringDecryptor>")
                .ShouldBeTrue();

            module.Write(output);
        }

        WithLoadedAssembly(output, asm =>
        {
            var probe = asm.GetType("Probe")!;
            ((string)probe.GetMethod("Fake1")!.Invoke(null, null)!).ShouldBe("custom:Greeting");
            ((string)probe.GetMethod("Fake2")!.Invoke(null, null)!).ShouldBe("custom2:Greeting");
            ((string?)probe.GetMethod("Real")!.Invoke(null, null)).ShouldBe("HelloFromRealRes");
            return 0;
        });
    }

    #endregion

    #region ControlFlow & Flattener Tests

    [Fact]
    public async Task ControlFlow_OpaquePredicate_ShortBranchesAndTernaryMerge_ExecutesCleanly()
    {
        const string source = """
            public static class Calc
            {
                public static int Compute(int x, bool flag)
                {
                    int acc = 0;
                    if (x < 0)
                    {
                        acc += 1;
                        acc += 2;
                        acc += 3;
                        acc += 4;
                        acc += 5;
                        acc += 6;
                        acc += 7;
                        acc += 8;
                        acc += 9;
                        acc += 10;
                    }
                    else
                    {
                        acc = flag ? (x + 10) : (x + 20);
                    }

                    try
                    {
                        acc += flag ? 100 : 200;
                    }
                    finally
                    {
                        acc += 1;
                    }

                    return acc;
                }
            }
            """;

        var input = CompileLibrary(source, "CfShortTernary", OptimizationLevel.Release);
        var output = Path.Combine(_dir, "CfShortTernary.obf.dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(input)))
        {
            var context = PipelineContext.ForAssembly(module, new ObfySettings
            {
                ControlFlow = { Enabled = true, Mode = ControlFlowMode.OpaquePredicate, Intensity = 100 }
            });
            var result = await new ControlFlowObfuscator(NullLogger<ControlFlowObfuscator>.Instance)
                .ObfuscateAsync(context);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Statistics.MethodsControlFlowObfuscated.ShouldBe(1);

            module.Write(output);
        }

        WithLoadedAssembly(output, asm =>
        {
            var m = asm.GetType("Calc")!.GetMethod("Compute")!;
            ((int)m.Invoke(null, [-1, true])!).ShouldBe(156);
            ((int)m.Invoke(null, [5, true])!).ShouldBe(116);
            ((int)m.Invoke(null, [5, false])!).ShouldBe(226);
            return 0;
        });
    }

    [Fact]
    public async Task ControlFlow_OpaquePredicate_ZeroMaxStackIsNotForcedToKeepOldMaxStack()
    {
        var module = CreateTestModule("ZeroMaxStack");
        var type = new TypeDefUser("Ns", "Machine", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(type);

        var method = new MethodDefUser(
            "SumTen",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody { InitLocals = true, MaxStack = 0, KeepOldMaxStack = false };
        var total = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(total);

        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, total));
        for (var v = 1; v <= 10; v++)
            body.Instructions.Add(Instruction.CreateLdcI4(v));
        for (var a = 0; a < 9; a++)
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, total));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, total));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;
        type.Methods.Add(method);

        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.OpaquePredicate, Intensity = 100 }
        });
        var result = await new ControlFlowObfuscator(NullLogger<ControlFlowObfuscator>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        method.Body.KeepOldMaxStack.ShouldBeFalse();

        var output = Path.Combine(_dir, "ZeroMaxStack.obf.dll");
        module.Write(output);
        WithLoadedAssembly(output, asm =>
        {
            ((int)asm.GetType("Ns.Machine")!.GetMethod("SumTen")!.Invoke(null, null)!).ShouldBe(55);
            return 0;
        });
    }

    [Fact]
    public void ControlFlowFlattener_EarlyReturnWithZeroEntryBlocks_FlattensAndExecutesCleanly()
    {
        const string source = """
            public static class EarlyRet
            {
                public static int Classify(int x)
                {
                    if (x < 0)
                    {
                        return -1;
                    }

                    if (x == 0)
                    {
                        return 0;
                    }

                    int y = x * 2;
                    return y + 1;
                }
            }
            """;

        var input = CompileLibrary(source, "CfEarlyRet", OptimizationLevel.Debug);
        var output = Path.Combine(_dir, "CfEarlyRet.obf.dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(input)))
        {
            var method = module.Types.Single(t => t.Name == "EarlyRet").FindMethod("Classify")!;
            var depths = ControlFlowFlattener.ComputeDepthBefore(method);
            depths.ShouldNotBeNull();

            ControlFlowFlattener.Flatten(method, new Random(123)).ShouldBeTrue();
            module.Write(output);
        }

        WithLoadedAssembly(output, asm =>
        {
            var m = asm.GetType("EarlyRet")!.GetMethod("Classify")!;
            ((int)m.Invoke(null, [-5])!).ShouldBe(-1);
            ((int)m.Invoke(null, [0])!).ShouldBe(0);
            ((int)m.Invoke(null, [7])!).ShouldBe(15);
            return 0;
        });
    }

    [Fact]
    public async Task ControlFlowFlattener_EarlyReturnAndNonZeroMerge_ComputesCfgDepthsSafely()
    {
        const string source = """
            public static class MergeSample
            {
                public static int Eval(int x)
                {
                    if (x < 0)
                        return -1;
                    int y = x > 10 ? 100 : 200;
                    return y + x;
                }
            }
            """;

        var input = CompileLibrary(source, "CfMerge", OptimizationLevel.Release);
        var output = Path.Combine(_dir, "CfMerge.obf.dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(input)))
        {
            var method = module.Types.Single(t => t.Name == "MergeSample").FindMethod("Eval")!;
            var depths = ControlFlowFlattener.ComputeDepthBefore(method);
            depths.ShouldNotBeNull();
            depths![0].ShouldBe(0);
            depths.ShouldContain(1);

            // Non-zero block entry at the ternary merge must be safely declined by the flattener.
            ControlFlowFlattener.Flatten(method, new Random(7)).ShouldBeFalse();

            var context = PipelineContext.ForAssembly(module, new ObfySettings
            {
                ControlFlow = { Enabled = true, Mode = ControlFlowMode.Combined, Intensity = 100 }
            });
            var result = await new ControlFlowObfuscator(NullLogger<ControlFlowObfuscator>.Instance)
                .ObfuscateAsync(context);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            module.Write(output);
        }

        WithLoadedAssembly(output, asm =>
        {
            var m = asm.GetType("MergeSample")!.GetMethod("Eval")!;
            ((int)m.Invoke(null, [-1])!).ShouldBe(-1);
            ((int)m.Invoke(null, [15])!).ShouldBe(115);
            ((int)m.Invoke(null, [5])!).ShouldBe(205);
            return 0;
        });
    }

    [Fact]
    public void ControlFlowFlattener_ComputeDepthBefore_HandlesTryCatchFilterFinallyAndLeave()
    {
        const string source = """
            using System;
            public static class EhSample
            {
                public static int Run(bool fail)
                {
                    int res = 0;
                    try
                    {
                        if (fail) throw new InvalidOperationException();
                        res = 10;
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Length > 0)
                    {
                        res = 20;
                    }
                    finally
                    {
                        res += 5;
                    }
                    return res;
                }
            }
            """;

        var input = CompileLibrary(source, "CfEhDepth", OptimizationLevel.Release);
        using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
        var method = module.Types.Single(t => t.Name == "EhSample").FindMethod("Run")!;
        var depths = ControlFlowFlattener.ComputeDepthBefore(method);
        depths.ShouldNotBeNull();

        var instructions = method.Body.Instructions;
        foreach (var eh in method.Body.ExceptionHandlers)
        {
            if (eh.FilterStart != null)
                depths![instructions.IndexOf(eh.FilterStart)].ShouldBe(1);
            if (eh.HandlerStart != null)
            {
                var expected = eh.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter ? 1 : 0;
                depths![instructions.IndexOf(eh.HandlerStart)].ShouldBe(expected);
            }
        }
    }

    #endregion

    #region ObfuscationAttributeRules Tests

    [Fact]
    public void ObfuscationAttributeRules_FeatureMatching_IsCaseInsensitive()
    {
        var module = CreateTestModule();
        var renameType = new TypeDefUser("Ns", "RenameTarget", module.CorLibTypes.Object.TypeDefOrRef);
        var cfType = new TypeDefUser("Ns", "CfTarget", module.CorLibTypes.Object.TypeDefOrRef);
        var hyphenType = new TypeDefUser("Ns", "HyphenCfTarget", module.CorLibTypes.Object.TypeDefOrRef);
        var strType = new TypeDefUser("Ns", "StringTarget", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(renameType);
        module.Types.Add(cfType);
        module.Types.Add(hyphenType);
        module.Types.Add(strType);

        AddObfuscationAttribute(renameType, exclude: true, feature: "Renaming");
        AddObfuscationAttribute(cfType, exclude: true, feature: "ControlFlow");
        AddObfuscationAttribute(hyphenType, exclude: true, feature: "Control-Flow");
        AddObfuscationAttribute(strType, exclude: true, feature: "String Encryption");

        var warnings = new List<string>();
        ObfuscationAttributeRules.IsExcluded(renameType, ObfuscationFeature.Renaming, warnings).ShouldBeTrue();
        ObfuscationAttributeRules.IsExcluded(renameType, ObfuscationFeature.Strings, warnings).ShouldBeFalse();

        ObfuscationAttributeRules.IsExcluded(cfType, ObfuscationFeature.ControlFlow, warnings).ShouldBeTrue();
        ObfuscationAttributeRules.IsExcluded(hyphenType, ObfuscationFeature.ControlFlow, warnings).ShouldBeTrue();
        ObfuscationAttributeRules.IsExcluded(strType, ObfuscationFeature.Strings, warnings).ShouldBeTrue();

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public void ObfuscationAttributeRules_NestedType_InheritsOuterExclusionAndSettings()
    {
        var module = CreateTestModule();
        var outer = new TypeDefUser("ExcludedNs", "Outer", module.CorLibTypes.Object.TypeDefOrRef);
        var inner = new TypeDefUser(UTF8String.Empty, "Inner", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NestedPublic | TypeAttributes.Class
        };
        var innerMethod = new MethodDefUser(
            "Run",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);
        outer.NestedTypes.Add(inner);
        inner.Methods.Add(innerMethod);
        module.Types.Add(outer);

        AddObfuscationAttribute(outer, exclude: true, feature: "Renaming", applyToMembers: true);

        var settings = new ObfySettings();
        ObfuscationAttributeRules.IsExcluded(inner, ObfuscationFeature.Renaming).ShouldBeTrue();
        ObfuscationAttributeRules.IsExcluded((IHasCustomAttribute)inner, ObfuscationFeature.Renaming).ShouldBeTrue();
        ObfuscationAttributeRules.IsExcluded(innerMethod, ObfuscationFeature.Renaming).ShouldBeTrue();
        ObfuscationAttributeRules.AllowType(inner, settings, ObfuscationFeature.Renaming).ShouldBeFalse();

        // When outer excludes with ApplyToMembers = false, nested type is not excluded.
        outer.CustomAttributes.Clear();
        AddObfuscationAttribute(outer, exclude: true, feature: "Renaming", applyToMembers: false);
        ObfuscationAttributeRules.IsExcluded(inner, ObfuscationFeature.Renaming).ShouldBeFalse();
        ObfuscationAttributeRules.AllowType(inner, settings, ObfuscationFeature.Renaming).ShouldBeTrue();

        // Namespace exclusions and inclusions also propagate through declaring type chain.
        outer.CustomAttributes.Clear();
        var nsExcludedSettings = new ObfySettings { Exclusions = { Namespaces = ["ExcludedNs"] } };
        ObfuscationAttributeRules.AllowType(inner, nsExcludedSettings, ObfuscationFeature.Renaming).ShouldBeFalse();

        var nsIncludedSettings = new ObfySettings { Inclusions = { Namespaces = ["ExcludedNs"] } };
        ObfuscationAttributeRules.AllowType(inner, nsIncludedSettings, ObfuscationFeature.Renaming).ShouldBeTrue();
    }

    #endregion

    #region SymbolRenaming Tests

    [Fact]
    public async Task SymbolRenaming_PreservePublicApi_KeepsExternallyVisibleNestedTypesAndProtectedMembers()
    {
        const string source = """
            using System;

            public class Outer
            {
                public class NestedPub
                {
                    public void PubMethod() { ProtMethod(); PrivMethod(); }
                    protected void ProtMethod() { }
                    internal void IntMethod() { }
                    private void PrivMethod() { }
                }

                protected class NestedProt
                {
                    protected internal int ProtIntField;
                    private int _privField;
                }

                protected internal class NestedProtInt { }

                internal class NestedInt
                {
                    public void PubOnInternal() { }
                }

                private class NestedPriv
                {
                    public void PubOnPrivate() { }
                }

                protected void OuterProt() { }
                protected internal int OuterProtIntProp { get; set; }
                protected event Action? OuterProtEvent;
                internal void OuterInternal() { }
                private void OuterPrivate() { }

                public int TouchAll()
                {
                    OuterProt();
                    OuterInternal();
                    OuterPrivate();
                    OuterProtIntProp = 1;
                    OuterProtEvent?.Invoke();
                    var p = new NestedPub();
                    p.PubMethod();
                    p.IntMethod();
                    var np = new NestedProt();
                    np.ProtIntField = 2;
                    new NestedInt().PubOnInternal();
                    new NestedPriv().PubOnPrivate();
                    return np.ProtIntField + OuterProtIntProp;
                }
            }
            """;

        var input = CompileLibrary(source, "PreserveVis");
        using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                PreservePublicApi = true,
                Mode = NamingMode.Sequential
            }
        });

        var result = await new SymbolRenamingObfuscator(new NameGenerator(), NullLogger<SymbolRenamingObfuscator>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var outer = module.Types.Single(t => t.Name == "Outer");
        outer.NestedTypes.Select(n => n.Name.String).ShouldContain("NestedPub");
        outer.NestedTypes.Select(n => n.Name.String).ShouldContain("NestedProt");
        outer.NestedTypes.Select(n => n.Name.String).ShouldContain("NestedProtInt");
        outer.NestedTypes.Select(n => n.Name.String).ShouldNotContain("NestedInt");
        outer.NestedTypes.Select(n => n.Name.String).ShouldNotContain("NestedPriv");

        var nestedPub = outer.NestedTypes.Single(n => n.Name == "NestedPub");
        nestedPub.Methods.Select(m => m.Name.String).ShouldContain("PubMethod");
        nestedPub.Methods.Select(m => m.Name.String).ShouldContain("ProtMethod");
        nestedPub.Methods.Select(m => m.Name.String).ShouldNotContain("IntMethod");
        nestedPub.Methods.Select(m => m.Name.String).ShouldNotContain("PrivMethod");

        var nestedProt = outer.NestedTypes.Single(n => n.Name == "NestedProt");
        nestedProt.Fields.Select(f => f.Name.String).ShouldContain("ProtIntField");
        nestedProt.Fields.Select(f => f.Name.String).ShouldNotContain("_privField");

        outer.Methods.Select(m => m.Name.String).ShouldContain("OuterProt");
        outer.Methods.Select(m => m.Name.String).ShouldNotContain("OuterInternal");
        outer.Methods.Select(m => m.Name.String).ShouldNotContain("OuterPrivate");
        outer.Properties.Select(p => p.Name.String).ShouldContain("OuterProtIntProp");
        outer.Events.Select(e => e.Name.String).ShouldContain("OuterProtEvent");

        // Internal and private nested types have all non-special members renamed (not externally visible).
        var internalNested = outer.NestedTypes.Single(n => n.IsNestedAssembly);
        internalNested.Methods.Select(m => m.Name.String).ShouldNotContain("PubOnInternal");
        var privateNested = outer.NestedTypes.Single(n => n.IsNestedPrivate);
        privateNested.Methods.Select(m => m.Name.String).ShouldNotContain("PubOnPrivate");
    }

    [Fact]
    public async Task SymbolRenaming_RenameParameters_WorksWithInclusionsAndExclusions()
    {
        const string source = """
            namespace AppNs
            {
                internal class Target
                {
                    internal int RenameMe(int myParam) => myParam + 1;
                    internal int KeepParams(int keepParam) => keepParam + 2;
                }

                internal class Other
                {
                    internal int Untouched(int otherParam) => otherParam + 3;
                }
            }
            """;

        var input = CompileLibrary(source, "ParamInclusions");
        using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                RenameTypes = true,
                RenameMethods = true,
                RenameNamespaces = true,
                RenameParameters = true,
                PreservePublicApi = false,
                Mode = NamingMode.Sequential
            },
            Inclusions = { Types = ["Target"] },
            Exclusions = { Methods = ["KeepParams"] }
        });

        var result = await new SymbolRenamingObfuscator(new NameGenerator(), NullLogger<SymbolRenamingObfuscator>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.ParametersRenamed.ShouldBe(1);

        var allParams = module.GetTypes()
            .SelectMany(t => t.Methods)
            .SelectMany(m => m.Parameters)
            .Where(p => !p.IsHiddenThisParameter && !string.IsNullOrEmpty(p.Name))
            .Select(p => p.Name)
            .ToList();

        allParams.ShouldNotContain("myParam");
        allParams.ShouldContain("keepParam");
        allParams.ShouldContain("otherParam");
    }

    [Fact]
    public async Task SymbolRenaming_RenameNamespaces_SkipsExcludedTypesAndUpdatesSymbolMapOnlyWhenRenamed()
    {
        const string source = """
            using System.Reflection;
            using System.Runtime.InteropServices;

            namespace MixedNs
            {
                public class PublicType { }
                internal class InternalType { }
            }

            namespace PartialNs
            {
                internal class NormalInternal { }

                [Obfuscation(Exclude = true)]
                internal class AttrExcluded { }

                [ComVisible(true)]
                internal class ComVisibleType { }

                internal class EntryHolder
                {
                    public static void Main() { }
                }
            }

            namespace OnlyExcludedNs
            {
                [Obfuscation(Exclude = true)]
                internal class AloneExcluded { }
            }
            """;

        var input = CompileLibrary(source, "NsRenameRules");
        using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
        module.EntryPoint = module.Types.Single(t => t.Name == "EntryHolder").FindMethod("Main")!;

        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                RenameTypes = true,
                RenameNamespaces = true,
                PreservePublicApi = true,
                Mode = NamingMode.Sequential
            }
        });

        var result = await new SymbolRenamingObfuscator(new NameGenerator(), NullLogger<SymbolRenamingObfuscator>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        // MixedNs has a public type -> preserved -> SymbolMap must NOT claim MixedNs was renamed.
        context.SymbolMap.Keys.ShouldNotContain("Namespace:MixedNs");
        module.Types.Single(t => t.Name == "PublicType").Namespace.String.ShouldBe("MixedNs");

        // OnlyExcludedNs has no renamable types -> preserved -> SymbolMap must NOT claim it was renamed.
        context.SymbolMap.Keys.ShouldNotContain("Namespace:OnlyExcludedNs");
        module.Types.Single(t => t.Name == "AloneExcluded").Namespace.String.ShouldBe("OnlyExcludedNs");

        // PartialNs renames NormalInternal's namespace while keeping AttrExcluded, ComVisibleType, and EntryHolder in PartialNs.
        context.SymbolMap.Keys.ShouldContain("Namespace:PartialNs");
        result.Statistics.NamespacesRenamed.ShouldBe(1);
        module.Types.Single(t => t.Name == "AttrExcluded").Namespace.String.ShouldBe("PartialNs");
        module.Types.Single(t => t.Name == "ComVisibleType").Namespace.String.ShouldBe("PartialNs");
        module.Types.Single(t => t.Name == "EntryHolder").Namespace.String.ShouldBe("PartialNs");
        module.Types.Where(t => !t.IsGlobalModuleType).ShouldContain(t => t.Namespace.String == context.SymbolMap["Namespace:PartialNs"]);
    }

    #endregion

    #region AntiDump Tests

    [Fact]
    public async Task AntiDump_EmitsX86Ret1C_X64Ret_UIntPtr_LoadLibraryExW_AndVirtualProtectRestore()
    {
        var module = CreateTestModule();
        module.Types.Add(new TypeDefUser("Ns", "Program", module.CorLibTypes.Object.TypeDefOrRef));

        var context = PipelineContext.ForAssembly(module, new ObfySettings { Protection = { AntiDump = true } });
        var result = await new AntiDumpObfuscator(NullLogger<AntiDumpObfuscator>.Instance).ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var dump = module.Types.Single(t => t.Name == "<AntiDump>");

        // VirtualProtect dwSize must be UIntPtr.
        var vp = dump.FindMethod("VirtualProtect")!;
        vp.MethodSig.Params[1].ElementType.ShouldBe(ElementType.U);

        // Uses LoadLibraryExW with LOAD_LIBRARY_SEARCH_SYSTEM32 (0x800), not LoadLibraryW.
        dump.FindMethod("LoadLibraryW").ShouldBeNull();
        var loadLibEx = dump.FindMethod("LoadLibraryExW")!;
        (loadLibEx.ImplMap!.Attributes & PInvokeAttributes.CharSetMask).ShouldBe(PInvokeAttributes.CharSetUnicode);
        loadLibEx.MethodSig.Params.Count.ShouldBe(3);

        var neutralize = dump.FindMethod("NeutralizeDumpers")!;
        var ins = neutralize.Body.Instructions;
        ins.Any(i => i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "LoadLibraryExW").ShouldBeTrue();
        ins.ShouldContain(i => i.IsLdcI4() && i.GetLdcI4Value() == 0x00000800);

        // Two VirtualProtect calls in NeutralizeDumpers (apply RWX + restore oldProtect).
        ins.Count(i => i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "VirtualProtect").ShouldBe(2);

        // Patch bytes: x64 (31 C0 C3) and x86 (31 C0 C2 1C 00).
        var ldcValues = ins.Where(i => i.IsLdcI4()).Select(i => i.GetLdcI4Value()).ToList();
        foreach (var b in new[] { 0x31, 0xC0, 0xC3, 0xC2, 0x1C })
            ldcValues.ShouldContain(b);

        // Wipe also converts 0x1000 to UIntPtr via Conv_U.
        var wipe = dump.FindMethod("Wipe")!;
        wipe.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Conv_U);
    }

    [Fact]
    public async Task AntiDump_Net461Fallback_ChecksProcessorArchitectureForArm()
    {
        var module = new ModuleDefUser("LegacyApp", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var asm = new AssemblyDefUser("LegacyApp", new Version(1, 0, 0, 0));
        asm.Modules.Add(module);
        var tfAttr = FrameworkReferences.Type(module, "System.Runtime.Versioning", "TargetFrameworkAttribute");
        var tfCtor = new MemberRefUser(module, ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String), tfAttr);
        asm.CustomAttributes.Add(new CustomAttribute(tfCtor, [new CAArgument(module.CorLibTypes.String, ".NETFramework,Version=v4.6.1")]));
        module.Types.Add(new TypeDefUser("Ns", "Program", module.CorLibTypes.Object.TypeDefOrRef));

        var context = PipelineContext.ForAssembly(module, new ObfySettings { Protection = { AntiDump = true } });
        var result = await new AntiDumpObfuscator(NullLogger<AntiDumpObfuscator>.Instance).ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var neutralize = module.Types.Single(t => t.Name == "<AntiDump>").FindMethod("NeutralizeDumpers")!;
        var ins = neutralize.Body.Instructions;
        ins.ShouldContain(i => i.OpCode == OpCodes.Ldstr && (string?)i.Operand == "PROCESSOR_ARCHITECTURE");
        ins.ShouldContain(i => i.OpCode == OpCodes.Ldstr && (string?)i.Operand == "ARM");
        ins.Any(i => i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "GetEnvironmentVariable").ShouldBeTrue();
        ins.Any(i => i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "get_Size").ShouldBeTrue();
    }

    #endregion

    #region DecryptorIl, AntiTamper, ReferenceProxy & MetadataRemoval Tests

    [Fact]
    public void DecryptorIl_CreateAesDecryptBytes_WrapsDisposalInTryFinally()
    {
        var module = CreateTestModule();
        var method = DecryptorIl.CreateAesDecryptBytes(module, "D", MethodAttributes.Private | MethodAttributes.Static);

        method.Body.ExceptionHandlers.ShouldContain(eh => eh.HandlerType == ExceptionHandlerType.Finally);
        method.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Endfinally);
        method.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Leave);
        method.Body.Instructions.Count(i => i.Operand is IMethod m && m.Name == "Dispose").ShouldBe(2);
    }

    [Fact]
    public async Task AntiTamper_Verify_DisposesSha256InFinally_AndSetsVerifiedAfterCheck()
    {
        var module = CreateTestModule();
        var type = new TypeDefUser("Ns", "App", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(type);
        var main = new MethodDefUser("Main", MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static)
        {
            Body = new CilBody { Instructions = { Instruction.Create(OpCodes.Ret) } }
        };
        type.Methods.Add(main);
        module.EntryPoint = main;

        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            Protection = { AntiTamper = { Enabled = true, CheckEntryPoint = true } }
        });
        var result = await new AntiTamperObfuscator(NullLogger<AntiTamperObfuscator>.Instance).ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var verify = module.Types.Single(t => t.Name == "<AntiTamper>").FindMethod("Verify")!;
        verify.Body.ExceptionHandlers.ShouldContain(eh => eh.HandlerType == ExceptionHandlerType.Finally);
        verify.Body.Instructions.Any(i => i.Operand is IMethod m && m.Name == "Dispose").ShouldBeTrue();

        var ins = verify.Body.Instructions.ToList();
        var stsfldIdx = ins.FindIndex(i => i.OpCode == OpCodes.Stsfld && i.Operand is IField f && f.Name == "_v");
        var computeHashIdx = ins.FindIndex(i => i.Operand is IMethod m && m.Name == "ComputeHash");
        stsfldIdx.ShouldBeGreaterThan(computeHashIdx);
    }

    [Fact]
    public async Task ReferenceProxy_SkipsNonPublicEnclosingExternalType_AndCallerSensitiveApis()
    {
        const string source = """
            using System.Diagnostics;
            using System.Reflection;

            public static class CallerSensitiveProbe
            {
                public static int Touch()
                {
                    var a = Assembly.GetCallingAssembly();
                    var b = Assembly.GetExecutingAssembly();
                    var c = MethodBase.GetCurrentMethod();
                    var f = new StackFrame();
                    var m = f.GetMethod();
                    var t = new StackTrace();
                    var frames = t.GetFrames();
                    return (a != null && b != null && c != null && m != null && frames.Length >= 0) ? 1 : 0;
                }
            }
            """;

        var input = CompileLibrary(source, "CallerSensitiveProxy");
        using var module = ModuleDefMD.Load(File.ReadAllBytes(input));

        // Add a resolved external method whose declaring type chain is non-public (NestedPrivate inside Public).
        var extModule = CreateTestModule("ExtHidden");
        var extOuter = new TypeDefUser("ExtNs", "PublicOuter", extModule.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        var extInner = new TypeDefUser(UTF8String.Empty, "HiddenInner", extModule.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NestedPrivate | TypeAttributes.Class
        };
        var extMethod = new MethodDefUser("SecretCall", MethodSig.CreateStatic(extModule.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static)
        {
            Body = new CilBody { Instructions = { Instruction.Create(OpCodes.Ret) } }
        };
        extInner.Methods.Add(extMethod);
        extOuter.NestedTypes.Add(extInner);
        extModule.Types.Add(extOuter);

        var callerType = module.Types.Single(t => t.Name == "CallerSensitiveProbe");
        var callerHidden = new MethodDefUser("CallHiddenExternal", MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static)
        {
            Body = new CilBody
            {
                Instructions =
                {
                    Instruction.Create(OpCodes.Call, extMethod),
                    Instruction.Create(OpCodes.Ret)
                }
            }
        };
        callerType.Methods.Add(callerHidden);

        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            Protection = { ReferenceProxy = true, ProxyExternalCalls = true }
        });
        var result = await new ReferenceProxyObfuscator(NullLogger<ReferenceProxyObfuscator>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        // Hidden external type method must not be proxied to module <RefProxy>.
        callerHidden.Body.Instructions[0].Operand.ShouldBeSameAs(extMethod);

        // Caller-sensitive APIs must stay direct calls.
        var touchCalls = callerType.FindMethod("Touch")!.Body.Instructions
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is IMethod)
            .Select(i => (IMethod)i.Operand!)
            .ToList();

        touchCalls.ShouldContain(m => m.Name == "GetCallingAssembly");
        touchCalls.ShouldContain(m => m.Name == "GetExecutingAssembly");
        touchCalls.ShouldContain(m => m.Name == "GetCurrentMethod");
        touchCalls.Any(m => m.Name == "GetMethod" && m.DeclaringType != null && m.DeclaringType.Name == "StackFrame").ShouldBeTrue();
        touchCalls.Any(m => m.Name == "GetFrames" && m.DeclaringType != null && m.DeclaringType.Name == "StackTrace").ShouldBeTrue();
    }

    [Fact]
    public async Task MetadataRemoval_RemoveAttributes_RemovesMatchingAssemblyLevelAttributes()
    {
        var module = CreateTestModule();
        var assembly = module.Assembly!;

        var compGen = module.CorLibTypes.GetTypeRef("System.Runtime.CompilerServices", "CompilerGeneratedAttribute");
        assembly.CustomAttributes.Add(new CustomAttribute(new MemberRefUser(
            module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), compGen)));

        var nullableCtx = module.CorLibTypes.GetTypeRef("System.Runtime.CompilerServices", "NullableContextAttribute");
        assembly.CustomAttributes.Add(new CustomAttribute(new MemberRefUser(
            module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Byte), nullableCtx),
            [new CAArgument(module.CorLibTypes.Byte, (byte)1)]));

        var compat = module.CorLibTypes.GetTypeRef("System.Runtime.CompilerServices", "RuntimeCompatibilityAttribute");
        assembly.CustomAttributes.Add(new CustomAttribute(new MemberRefUser(
            module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), compat)));

        var context = PipelineContext.ForAssembly(module, new ObfySettings { Metadata = { RemoveAttributes = true } });
        var result = await new MetadataRemovalObfuscator(NullLogger<MetadataRemovalObfuscator>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        result.Statistics.MetadataItemsRemoved.ShouldBe(2);

        assembly.CustomAttributes.ShouldNotContain(a => a.TypeFullName.Contains("CompilerGeneratedAttribute"));
        assembly.CustomAttributes.ShouldNotContain(a => a.TypeFullName.Contains("NullableContextAttribute"));
        assembly.CustomAttributes.ShouldContain(a => a.TypeFullName.Contains("RuntimeCompatibilityAttribute"));
    }

    #endregion
}

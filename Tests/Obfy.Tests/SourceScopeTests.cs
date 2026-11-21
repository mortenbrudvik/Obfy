using Autofac;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Source;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Shouldly;
using static Obfy.Tests.SourceRvTestSupport;

namespace Obfy.Tests;

/// <summary>
/// Inclusions, exclusions and ObfuscationAttribute-style scope rules must limit the string and control
/// flow source obfuscators the same way they limit the assembly ones (the renamer is covered in
/// <see cref="SourceSymbolRenamerRegressionTests"/>).
/// </summary>
public class SourceScopeTests
{
    private const string Strings = """
        using System;
        namespace Keep.Me
        {
            public class Stay
            {
                public string A() => "stay text";
                public string SkipMe() => "skipped text";
            }
        }
        namespace Change.Me
        {
            public class Go
            {
                public string B() => "go text";
                public string SkipMe() => "also skipped";
            }
            [Serializable]
            public class Saved
            {
                public string C() => "saved text";
            }
        }
        """;

    private const string Flow = """
        using System;
        namespace Keep.Me
        {
            public class Stay
            {
                public int A(int x) { x = x + 1; x = x * 2; x = x - 3; return x; }
            }
        }
        namespace Change.Me
        {
            public class Go
            {
                public int B(int x) { x = x + 1; x = x * 2; x = x - 3; return x; }
                public int SkipMe(int x) { x = x + 1; x = x * 2; x = x - 3; return x; }
            }
            [Serializable]
            public class Saved
            {
                public int C(int x) { x = x + 1; x = x * 2; x = x - 3; return x; }
            }
        }
        """;

    private static async Task<PipelineContext> EncryptAsync(Action<ObfySettings>? configure = null)
    {
        var settings = StringSettings();
        configure?.Invoke(settings);
        return await EncryptStringsAsync(Strings, settings);
    }

    private static async Task<(PipelineContext Context, int Methods)> FlattenAsync(Action<ObfySettings>? configure = null)
    {
        var settings = new ObfySettings { ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 } };
        configure?.Invoke(settings);
        var context = PipelineContext.ForSourceCode(CreateCompilation(Flow), settings);
        var result = await new SourceControlFlowObfuscator(NullLogger<SourceControlFlowObfuscator>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        return (context, result.Statistics.MethodsControlFlowObfuscated);
    }

    // ---- strings --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Strings_NoRules_EncryptsEverythingExceptSerializableTypes()
    {
        // [Serializable] is in the default attribute exclusions, as it is for the assembly obfuscators.
        var text = SourceOf(await EncryptAsync());

        text.ShouldNotContain("stay text");
        text.ShouldNotContain("skipped text");
        text.ShouldNotContain("go text");
        text.ShouldContain("saved text");
    }

    [Fact]
    public async Task Strings_ExcludedMethodsTypesAndNamespaces_AreLeftAlone()
    {
        var byMethod = SourceOf(await EncryptAsync(s => s.Exclusions.Methods.Add("Skip*")));
        byMethod.ShouldContain("skipped text");
        byMethod.ShouldContain("also skipped");
        byMethod.ShouldNotContain("stay text");

        var byType = SourceOf(await EncryptAsync(s => s.Exclusions.Types.Add("Go")));
        byType.ShouldContain("go text");
        byType.ShouldContain("also skipped");
        byType.ShouldNotContain("stay text");

        var byNamespace = SourceOf(await EncryptAsync(s => s.Exclusions.Namespaces.Add("Keep.*")));
        byNamespace.ShouldContain("stay text");
        byNamespace.ShouldContain("skipped text");
        byNamespace.ShouldNotContain("go text");
    }

    [Fact]
    public async Task Strings_Inclusions_OnlyMatchingTypesNamespacesAndMethodsAreEncrypted()
    {
        var byType = SourceOf(await EncryptAsync(s => s.Inclusions.Types.Add("Go")));
        byType.ShouldNotContain("go text");
        byType.ShouldNotContain("also skipped");
        byType.ShouldContain("stay text");

        var byNamespace = SourceOf(await EncryptAsync(s => s.Inclusions.Namespaces.Add("Keep.Me")));
        byNamespace.ShouldNotContain("stay text");
        byNamespace.ShouldContain("go text");

        var byMethod = SourceOf(await EncryptAsync(s => s.Inclusions.Methods.Add("B")));
        byMethod.ShouldNotContain("go text");
        byMethod.ShouldContain("stay text");
        byMethod.ShouldContain("also skipped");
    }

    [Fact]
    public async Task Strings_InclusionsMatchingNothing_WarnAndEncryptNothing()
    {
        var context = await EncryptAsync(s => s.Inclusions.Types.Add("DoesNotExist"));

        SourceOf(context).ShouldContain("stay text");
        context.Warnings.ShouldContain(w => w.Contains("Inclusions matched 0"));
    }

    [Fact]
    public async Task Strings_UserNamespacesCalledObfyOrSystem_CannotCaptureTheHelper()
    {
        // "Obfy.Runtime.__ObfyStringDecryptor" used to bind to MyApp.Obfy when one existed.
        const string source = """
            namespace MyApp.Obfy { public class Marker { } }
            namespace MyApp.System { public class Marker { } }
            namespace MyApp
            {
                public class Greeter
                {
                    public string Hello() => "hello world";
                    public string Interpolated(int a) => $"value {a} units";
                }
            }
            """;

        foreach (var algorithm in new[] { EncryptionAlgorithm.Xor, EncryptionAlgorithm.Aes256 })
        {
            var settings = StringSettings();
            settings.StringEncryption.Algorithm = algorithm;
            var context = await EncryptStringsAsync(source, settings);

            GetErrors(context.Compilation!).ShouldBeEmpty($"{algorithm}: " + ErrorText(GetErrors(context.Compilation!)));
            Invoke(context.Compilation!, "Greeter", "Hello").ShouldBe("hello world");
            Invoke(context.Compilation!, "Greeter", "Interpolated", 7).ShouldBe("value 7 units");
        }
    }

    [Fact]
    public async Task Strings_UserTypesCalledConvertOrBuffer_CannotBreakTheHelper()
    {
        const string source = """
            public class Convert { }
            public class Buffer { }
            public class Aes { }
            public class Greeter { public string Hello() => "hello world"; }
            """;

        var settings = StringSettings();
        settings.StringEncryption.Algorithm = EncryptionAlgorithm.Aes256;
        var context = await EncryptStringsAsync(source, settings);

        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));
        Invoke(context.Compilation!, "Greeter", "Hello").ShouldBe("hello world");
    }

    // ---- control flow ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Flow_NoRules_FlattensEverythingExceptSerializableTypes()
    {
        var (_, methods) = await FlattenAsync();

        methods.ShouldBe(3); // A, B and SkipMe; Saved is [Serializable]
    }

    [Fact]
    public async Task Flow_ExcludedMethodsTypesAndNamespaces_AreLeftAlone()
    {
        (await FlattenAsync(s => s.Exclusions.Methods.Add("Skip*"))).Methods.ShouldBe(2);
        (await FlattenAsync(s => s.Exclusions.Types.Add("Go"))).Methods.ShouldBe(1);
        (await FlattenAsync(s => s.Exclusions.Namespaces.Add("Keep.*"))).Methods.ShouldBe(2);
    }

    [Fact]
    public async Task Flow_Inclusions_OnlyMatchingMethodsAreFlattened()
    {
        (await FlattenAsync(s => s.Inclusions.Types.Add("Go"))).Methods.ShouldBe(2);
        (await FlattenAsync(s => s.Inclusions.Namespaces.Add("Keep.Me"))).Methods.ShouldBe(1);
        (await FlattenAsync(s => s.Inclusions.Methods.Add("B"))).Methods.ShouldBe(1);
    }

    [Fact]
    public async Task Flow_InclusionsMatchingNothing_WarnAndFlattenNothing()
    {
        var (context, methods) = await FlattenAsync(s => s.Inclusions.Types.Add("DoesNotExist"));

        methods.ShouldBe(0);
        context.Warnings.ShouldContain(w => w.Contains("Inclusions matched 0"));
    }

    // ---- the three together ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AllThreeTechniques_ThroughTheService_WriteValidRunnableSource()
    {
        var dir = NewTempDirectory();
        try
        {
            var input = Path.Combine(dir, "calc.cs");
            var output = Path.Combine(dir, "calc.obfuscated.cs");
            File.WriteAllText(input, """
                public class Calc
                {
                    private int Helper(int x)
                    {
                        x = x + 1;
                        x = x * 2;
                        x = x - 3;
                        return x;
                    }
                    public string Run(int a, int b)
                    {
                        var label = "result";
                        var text = $"{label} {a}{b} of {Helper(a)}";
                        return text;
                    }
                }
                """);
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                StringEncryption = { Enabled = true, MinStringLength = 3 },
                ControlFlow = { Enabled = true, Mode = ControlFlowMode.Combined, Intensity = 100 },
                SymbolRenaming = { Enabled = true, PreservePublicApi = true, Mode = NamingMode.Sequential }
            };

            await using var container = BuildContainer();
            var result = await container.Resolve<IObfuscationService>().ObfuscateAsync(input, output, settings);

            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Warnings.ShouldNotContain(w => w.Contains("compile error"), string.Join(" / ", result.Warnings));
            var text = File.ReadAllText(output);
            text.ShouldNotContain("Helper");
            text.ShouldNotContain("result");
            var compilation = CreateCompilation(text);
            GetErrors(compilation).ShouldBeEmpty(ErrorText(GetErrors(compilation)));
            // 3 and 4 give "34"; Helper(3) = (3 + 1) * 2 - 3 = 5.
            Invoke(compilation, "Calc", "Run", 3, 4).ShouldBe("result 34 of 5");
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }
}

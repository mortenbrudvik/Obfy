using Autofac;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Obfy.Core.Services;
using Shouldly;
using static Obfy.Tests.SourceRvTestSupport;

namespace Obfy.Tests;

/// <summary>
/// Real-filesystem tests for how source output is written. A single input file must produce a
/// single output file (the string decryptor helper lives in the same file), and re-running must
/// never leave two copies of the helper behind.
/// </summary>
public class SourceProcessorFilesystemTests
{
    private const string Calc = """
        public class Calc
        {
            public string Greet(string name) => "Hello there, " + name + "!";
            public static string Static() => "static message";
        }
        """;

    private static CSharpCompilation CompileFiles(IEnumerable<string> files, OutputKind kind = OutputKind.DynamicallyLinkedLibrary) =>
        CreateCompilation(files.Select(f => (f, File.ReadAllText(f))))
            .WithOptions(new CSharpCompilationOptions(kind));

    private static void ShouldCompile(IEnumerable<string> files, OutputKind kind = OutputKind.DynamicallyLinkedLibrary)
    {
        var errors = GetErrors(CompileFiles(files, kind));
        errors.ShouldBeEmpty(ErrorText(errors));
    }

    private static int CountHelperTypes(IEnumerable<string> files) =>
        files.Sum(f => File.ReadAllText(f).Split("class __ObfyStringDecryptor").Length - 1);

    [Fact]
    public async Task SingleFile_StringEncryption_WritesOneFile_NotADirectory()
    {
        var dir = NewTempDirectory();
        try
        {
            var input = Path.Combine(dir, "foo.cs");
            var output = Path.Combine(dir, "foo.obfuscated.cs");
            File.WriteAllText(input, Calc);

            await using var container = BuildContainer();
            var result = await container.Resolve<IObfuscationService>()
                .ObfuscateAsync(input, output, OnlySettings(strings: true));

            result.Success.ShouldBeTrue(result.ErrorMessage);
            File.Exists(output).ShouldBeTrue("the output of a single .cs input must be a file");
            Directory.Exists(output).ShouldBeFalse("the output of a single .cs input must not be a directory");

            var text = File.ReadAllText(output);
            text.ShouldNotContain("static message");
            text.ShouldContain("class __ObfyStringDecryptor");
            CountHelperTypes([output]).ShouldBe(1);
            ShouldCompile([output]);
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task SingleFile_OutputStillRuns()
    {
        var dir = NewTempDirectory();
        try
        {
            var input = Path.Combine(dir, "foo.cs");
            var output = Path.Combine(dir, "foo.obfuscated.cs");
            File.WriteAllText(input, Calc);

            await using var container = BuildContainer();
            (await container.Resolve<IObfuscationService>().ObfuscateAsync(input, output, OnlySettings(strings: true)))
                .Success.ShouldBeTrue();

            var compilation = CompileFiles([output]);
            Invoke(compilation, "Calc", "Greet", "Ada").ShouldBe("Hello there, Ada!");
            Invoke(compilation, "Calc", "Static").ShouldBe("static message");
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task SingleFile_Aes_OutputStillRuns()
    {
        var dir = NewTempDirectory();
        try
        {
            var input = Path.Combine(dir, "foo.cs");
            var output = Path.Combine(dir, "foo.obfuscated.cs");
            File.WriteAllText(input, Calc);
            var settings = OnlySettings(strings: true);
            settings.StringEncryption.Algorithm = Obfy.Core.Models.EncryptionAlgorithm.Aes256;

            await using var container = BuildContainer();
            (await container.Resolve<IObfuscationService>().ObfuscateAsync(input, output, settings))
                .Success.ShouldBeTrue();

            File.ReadAllText(output).ShouldNotContain("static message");
            Invoke(CompileFiles([output]), "Calc", "Greet", "Ada").ShouldBe("Hello there, Ada!");
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task SingleFile_OutputPathIsAnExistingDirectory_FailsClearly()
    {
        // Older versions wrote "foo.obfuscated.cs" as a directory; a later run must say so, not crash.
        var dir = NewTempDirectory();
        try
        {
            var input = Path.Combine(dir, "foo.cs");
            var output = Directory.CreateDirectory(Path.Combine(dir, "foo.obfuscated.cs")).FullName;
            File.WriteAllText(input, Calc);

            await using var container = BuildContainer();
            var result = await container.Resolve<IObfuscationService>()
                .ObfuscateAsync(input, output, OnlySettings(strings: true));

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldNotBeNull().ShouldContain("existing directory");
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task SingleFile_RunTwiceToSameOutput_StaysOneCompilableFile()
    {
        var dir = NewTempDirectory();
        try
        {
            var input = Path.Combine(dir, "foo.cs");
            var output = Path.Combine(dir, "foo.obfuscated.cs");
            File.WriteAllText(input, Calc);

            await using var container = BuildContainer();
            var service = container.Resolve<IObfuscationService>();
            (await service.ObfuscateAsync(input, output, OnlySettings(strings: true))).Success.ShouldBeTrue();
            var second = await service.ObfuscateAsync(input, output, OnlySettings(strings: true));

            second.Success.ShouldBeTrue(second.ErrorMessage);
            File.Exists(output).ShouldBeTrue();
            CountHelperTypes([output]).ShouldBe(1);
            ShouldCompile([output]);
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task SingleFile_ObfuscatingTheOutputAgain_DoesNotDuplicateTheHelper()
    {
        var dir = NewTempDirectory();
        try
        {
            var input = Path.Combine(dir, "foo.cs");
            var once = Path.Combine(dir, "foo.once.cs");
            var twice = Path.Combine(dir, "foo.twice.cs");
            File.WriteAllText(input, Calc);

            await using var container = BuildContainer();
            var service = container.Resolve<IObfuscationService>();
            (await service.ObfuscateAsync(input, once, OnlySettings(strings: true))).Success.ShouldBeTrue();
            var again = await service.ObfuscateAsync(once, twice, OnlySettings(strings: true));

            again.Success.ShouldBeTrue(again.ErrorMessage);
            File.Exists(twice).ShouldBeTrue();
            ShouldCompile([twice]);

            var compilation = CompileFiles([twice]);
            Invoke(compilation, "Calc", "Greet", "Ada").ShouldBe("Hello there, Ada!");
            Invoke(compilation, "Calc", "Static").ShouldBe("static message");
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task SingleFile_WithFileScopedNamespace_StillOneCompilableFile()
    {
        var dir = NewTempDirectory();
        try
        {
            var input = Path.Combine(dir, "scoped.cs");
            var output = Path.Combine(dir, "scoped.obfuscated.cs");
            File.WriteAllText(input, """
                using System;

                namespace My.App;

                public class Widget
                {
                    public string Name() => "widget name";
                }
                """);

            await using var container = BuildContainer();
            var result = await container.Resolve<IObfuscationService>()
                .ObfuscateAsync(input, output, OnlySettings(strings: true));

            result.Success.ShouldBeTrue(result.ErrorMessage);
            File.Exists(output).ShouldBeTrue();
            ShouldCompile([output]);
            Invoke(CompileFiles([output]), "Widget", "Name").ShouldBe("widget name");
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task SingleFile_WithTopLevelStatements_StillOneCompilableFile()
    {
        var dir = NewTempDirectory();
        try
        {
            var input = Path.Combine(dir, "Program.cs");
            var output = Path.Combine(dir, "Program.obfuscated.cs");
            File.WriteAllText(input, """
                System.Console.WriteLine("top level text");
                """);

            await using var container = BuildContainer();
            var result = await container.Resolve<IObfuscationService>()
                .ObfuscateAsync(input, output, OnlySettings(strings: true));

            result.Success.ShouldBeTrue(result.ErrorMessage);
            File.Exists(output).ShouldBeTrue();
            File.ReadAllText(output).ShouldNotContain("top level text");
            ShouldCompile([output], OutputKind.ConsoleApplication);
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task Directory_RunTwiceIntoSameOutput_KeepsExactlyOneHelper()
    {
        var dir = NewTempDirectory();
        try
        {
            var src = Directory.CreateDirectory(Path.Combine(dir, "src")).FullName;
            var output = Path.Combine(dir, "out");
            File.WriteAllText(Path.Combine(src, "A.cs"), """
                public class A { public string Name() => "name of a"; }
                """);
            File.WriteAllText(Path.Combine(src, "B.cs"), """
                public class B { public string Name() => "name of b"; }
                """);

            await using var container = BuildContainer();
            var service = container.Resolve<IObfuscationService>();
            (await service.ObfuscateAsync(src, output, OnlySettings(strings: true))).Success.ShouldBeTrue();
            (await service.ObfuscateAsync(src, output, OnlySettings(strings: true))).Success.ShouldBeTrue();

            var files = Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories);
            CountHelperTypes(files).ShouldBe(1, "a re-run must overwrite the helper, not add a second one");
            ShouldCompile(files);
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task Directory_ObfuscatingAnObfuscatedDirectory_DoesNotCollideHelpers()
    {
        var dir = NewTempDirectory();
        try
        {
            var src = Directory.CreateDirectory(Path.Combine(dir, "src")).FullName;
            File.WriteAllText(Path.Combine(src, "A.cs"), """
                public class A { public string Name() => "name of a"; }
                """);
            File.WriteAllText(Path.Combine(src, "B.cs"), """
                public class B { public string Name() => "name of b"; }
                """);

            await using var container = BuildContainer();
            var service = container.Resolve<IObfuscationService>();
            var out1 = Path.Combine(dir, "out1");
            var out2 = Path.Combine(dir, "out2");
            (await service.ObfuscateAsync(src, out1, OnlySettings(strings: true))).Success.ShouldBeTrue();
            (await service.ObfuscateAsync(out1, out2, OnlySettings(strings: true))).Success.ShouldBeTrue();

            var files = Directory.GetFiles(out2, "*.cs", SearchOption.AllDirectories);
            ShouldCompile(files);
            var compilation = CompileFiles(files);
            Invoke(compilation, "A", "Name").ShouldBe("name of a");
            Invoke(compilation, "B", "Name").ShouldBe("name of b");
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task Directory_WithOneFile_StaysADirectoryOutput()
    {
        var dir = NewTempDirectory();
        try
        {
            var src = Directory.CreateDirectory(Path.Combine(dir, "src")).FullName;
            var output = Path.Combine(dir, "out");
            File.WriteAllText(Path.Combine(src, "Only.cs"), Calc);

            await using var container = BuildContainer();
            var result = await container.Resolve<IObfuscationService>()
                .ObfuscateAsync(src, output, OnlySettings(strings: true));

            result.Success.ShouldBeTrue(result.ErrorMessage);
            Directory.Exists(output).ShouldBeTrue("a directory input keeps a directory output");
            ShouldCompile(Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories));
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Fact]
    public async Task SourceSet_RunTwiceIntoSameOutput_KeepsExactlyOneHelper()
    {
        var dir = NewTempDirectory();
        try
        {
            var a = Path.Combine(dir, "A.cs");
            var b = Path.Combine(dir, "B.cs");
            File.WriteAllText(a, """
                public class A { public string Name() => "name of a"; }
                """);
            File.WriteAllText(b, """
                public class B { public string Name() => "name of b"; }
                """);
            var output = Path.Combine(dir, "out");

            await using var container = BuildContainer();
            var service = container.Resolve<IObfuscationService>();
            (await service.ObfuscateSourcesAsync([a, b], output, OnlySettings(strings: true))).Success.ShouldBeTrue();
            (await service.ObfuscateSourcesAsync([a, b], output, OnlySettings(strings: true))).Success.ShouldBeTrue();

            var files = Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories);
            CountHelperTypes(files).ShouldBe(1);
            ShouldCompile(files);
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }
}

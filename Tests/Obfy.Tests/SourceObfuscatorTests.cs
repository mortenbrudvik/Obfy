using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Source;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class SourceObfuscatorTests
{
    [Fact]
    public async Task SourceStringEncryptor_EncryptsStringLiterals()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public string GetMessage() => "Hello World";
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceStringEncryptor>>();
        var encryptor = new SourceStringEncryptor(logger.Object);

        var settings = new ObfySettings { StringEncryption = { Enabled = true, MinStringLength = 3 } };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await encryptor.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBeGreaterThan(0);

        // Verify the string is no longer in plain text
        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldNotContain("\"Hello World\"");
        newSource.ShouldContain("__ObfyStringDecryptor");
    }

    [Fact]
    public async Task SourceStringEncryptor_SkipsShortStrings()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public string GetX() => "X";
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceStringEncryptor>>();
        var encryptor = new SourceStringEncryptor(logger.Object);

        var settings = new ObfySettings { StringEncryption = { Enabled = true, MinStringLength = 3 } };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await encryptor.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBe(0);

        // Short string should still be there
        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldContain("\"X\"");
    }

    [Fact]
    public async Task SourceSymbolRenamer_RenamesClassNames()
    {
        // Arrange
        var sourceCode = """
            class MyTestClass
            {
                public void DoSomething() { }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceSymbolRenamer>>();
        var nameGenerator = new NameGenerator();
        var renamer = new SourceSymbolRenamer(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameTypes = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await renamer.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.TypesRenamed.ShouldBeGreaterThan(0);

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldNotContain("MyTestClass");
    }

    [Fact]
    public async Task SourceSymbolRenamer_PreservesPublicApi()
    {
        // Arrange
        var sourceCode = """
            public class PublicClass
            {
                public void PublicMethod() { }
                private void PrivateMethod() { }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceSymbolRenamer>>();
        var nameGenerator = new NameGenerator();
        var renamer = new SourceSymbolRenamer(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, PreservePublicApi = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await renamer.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldContain("PublicClass");
        newSource.ShouldContain("PublicMethod");
        newSource.ShouldNotContain("PrivateMethod");
    }

    [Fact]
    public async Task SourceControlFlowObfuscator_InsertsOpaquePredicates()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public int Calculate(int x)
                {
                    var a = x + 1;
                    var b = a * 2;
                    var c = b - 3;
                    var d = c + 4;
                    return d;
                }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceControlFlowObfuscator>>();
        var obfuscator = new SourceControlFlowObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.OpaquePredicate, Intensity = 100 }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsControlFlowObfuscated.ShouldBeGreaterThan(0);

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        // Should contain if statements from opaque predicates
        newSource.ShouldContain("if");
    }

    [Fact]
    public async Task SourceControlFlowObfuscator_ConvertsToSwitchDispatcher()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public int Calculate(int x)
                {
                    var a = x + 1;
                    var b = a * 2;
                    var c = b - 3;
                    var d = c + 4;
                    return d;
                }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceControlFlowObfuscator>>();
        var obfuscator = new SourceControlFlowObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsControlFlowObfuscated.ShouldBeGreaterThan(0);

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        // Should contain switch and while from dispatcher
        newSource.ShouldContain("switch");
        newSource.ShouldContain("while");
    }

    [Fact]
    public async Task SourceObfuscators_SupportsCorrectTargetType()
    {
        // Arrange
        var stringEncryptor = new SourceStringEncryptor(Mock.Of<ILogger<SourceStringEncryptor>>());
        var symbolRenamer = new SourceSymbolRenamer(new NameGenerator(), Mock.Of<ILogger<SourceSymbolRenamer>>());
        var controlFlow = new SourceControlFlowObfuscator(Mock.Of<ILogger<SourceControlFlowObfuscator>>());

        // Assert
        stringEncryptor.SupportsTargetType(TargetType.SourceCode).ShouldBeTrue();
        stringEncryptor.SupportsTargetType(TargetType.Assembly).ShouldBeFalse();

        symbolRenamer.SupportsTargetType(TargetType.SourceCode).ShouldBeTrue();
        symbolRenamer.SupportsTargetType(TargetType.Assembly).ShouldBeFalse();

        controlFlow.SupportsTargetType(TargetType.SourceCode).ShouldBeTrue();
        controlFlow.SupportsTargetType(TargetType.Assembly).ShouldBeFalse();
    }

    [Fact]
    public void SourceObfuscators_HaveCorrectPriority()
    {
        // Arrange
        var stringEncryptor = new SourceStringEncryptor(Mock.Of<ILogger<SourceStringEncryptor>>());
        var symbolRenamer = new SourceSymbolRenamer(new NameGenerator(), Mock.Of<ILogger<SourceSymbolRenamer>>());
        var controlFlow = new SourceControlFlowObfuscator(Mock.Of<ILogger<SourceControlFlowObfuscator>>());

        // Assert - same priorities as assembly obfuscators
        stringEncryptor.Priority.ShouldBe(10);
        controlFlow.Priority.ShouldBe(30);
        symbolRenamer.Priority.ShouldBe(50);
    }

    private static CSharpCompilation CreateCompilation(string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

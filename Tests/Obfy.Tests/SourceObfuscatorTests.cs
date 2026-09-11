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
                    x = x + 1;
                    x = x * 2;
                    x = x - 3;
                    x = x + 4;
                    return x;
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
                    x = x + 1;
                    x = x * 2;
                    x = x - 3;
                    x = x + 4;
                    return x;
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

    #region Additional SourceStringEncryptor Tests

    [Fact]
    public async Task SourceStringEncryptor_EncryptsVerbatimStrings()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public string GetPath() => @"C:\Users\Test\Documents";
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

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldNotContain(@"@""C:\Users\Test\Documents""");
    }

    [Fact]
    public async Task SourceStringEncryptor_HandlesSpecialCharacters()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public string GetSpecial() => "Hello\nWorld\t\"Test\"";
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
    }

    [Fact]
    public async Task SourceStringEncryptor_SkipsEmptyStrings()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public string GetEmpty() => "";
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceStringEncryptor>>();
        var encryptor = new SourceStringEncryptor(logger.Object);

        var settings = new ObfySettings { StringEncryption = { Enabled = true, MinStringLength = 1 } };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await encryptor.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBe(0);

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldContain("\"\"");
    }

    [Fact]
    public async Task SourceStringEncryptor_EncryptsMultipleStrings()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public string GetA() => "First String";
                public string GetB() => "Second String";
                public string GetC() => "Third String";
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
        result.Statistics.StringsEncrypted.ShouldBe(3);
    }

    #endregion

    #region Additional SourceSymbolRenamer Tests

    [Fact]
    public async Task SourceSymbolRenamer_RenamesNestedClasses()
    {
        // Arrange
        var sourceCode = """
            class OuterClass
            {
                class InnerClass
                {
                    public void InnerMethod() { }
                }
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
        result.Statistics.TypesRenamed.ShouldBeGreaterThanOrEqualTo(2);

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldNotContain("OuterClass");
        newSource.ShouldNotContain("InnerClass");
    }

    [Fact]
    public async Task SourceSymbolRenamer_RenamesPrivateMethods()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                private void PrivateHelper() { }
                private int AnotherPrivate(int x) => x * 2;
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceSymbolRenamer>>();
        var nameGenerator = new NameGenerator();
        var renamer = new SourceSymbolRenamer(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameMethods = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await renamer.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsRenamed.ShouldBeGreaterThan(0);

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldNotContain("PrivateHelper");
        newSource.ShouldNotContain("AnotherPrivate");
    }

    [Fact]
    public async Task SourceSymbolRenamer_RenamesMethodParameters()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                private int Calculate(int inputValue, int multiplier)
                {
                    return inputValue * multiplier;
                }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceSymbolRenamer>>();
        var nameGenerator = new NameGenerator();
        var renamer = new SourceSymbolRenamer(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameParameters = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await renamer.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ParametersRenamed.ShouldBeGreaterThan(0);

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldNotContain("inputValue");
        newSource.ShouldNotContain("multiplier");
    }

    [Fact]
    public async Task SourceSymbolRenamer_HandlesGenericTypes()
    {
        // Arrange
        var sourceCode = """
            class GenericClass<T>
            {
                private T internalValue;
                public T GetValue() => internalValue;
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceSymbolRenamer>>();
        var nameGenerator = new NameGenerator();
        var renamer = new SourceSymbolRenamer(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameTypes = true, RenameFields = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await renamer.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldNotContain("GenericClass");
        newSource.ShouldNotContain("internalValue");
    }

    [Fact]
    public async Task SourceSymbolRenamer_RenamesFields()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                private int privateField = 10;
                private string anotherField = "test";
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceSymbolRenamer>>();
        var nameGenerator = new NameGenerator();
        var renamer = new SourceSymbolRenamer(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameFields = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await renamer.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.FieldsRenamed.ShouldBeGreaterThan(0);

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldNotContain("privateField");
        newSource.ShouldNotContain("anotherField");
    }

    #endregion

    #region Additional SourceControlFlowObfuscator Tests

    [Fact]
    public async Task SourceControlFlowObfuscator_PreservesReturnStatements()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public int Calculate(int x)
                {
                    if (x > 0)
                        return x * 2;
                    return x;
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

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        // Should still contain return statements
        newSource.ShouldContain("return");
    }

    [Fact]
    public async Task SourceControlFlowObfuscator_SkipsSimpleMethods()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public int Simple() => 42;
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
        // Simple expression-bodied methods should be skipped
        result.Statistics.MethodsControlFlowObfuscated.ShouldBe(0);
    }

    [Fact]
    public async Task SourceControlFlowObfuscator_HandlesLoops()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public int Sum(int n)
                {
                    int result = 0;
                    for (int i = 0; i < n; i++)
                    {
                        result += i;
                    }
                    return result;
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
    }

    [Fact]
    public async Task SourceControlFlowObfuscator_CombinedMode_AppliesBothTransformations()
    {
        // Arrange
        var sourceCode = """
            class Test
            {
                public int Process(int x)
                {
                    x = x + 1;
                    x = x * 2;
                    x = x - 3;
                    x = x + 4;
                    x = x * 5;
                    return x;
                }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var logger = new Mock<ILogger<SourceControlFlowObfuscator>>();
        var obfuscator = new SourceControlFlowObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Combined, Intensity = 100 }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsControlFlowObfuscated.ShouldBeGreaterThan(0);

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        // Combined mode should add both switch and if statements
        newSource.ShouldContain("switch");
    }

    [Fact]
    public void SourceControlFlowObfuscator_IsEnabled_RespectsSettings()
    {
        var logger = new Mock<ILogger<SourceControlFlowObfuscator>>();
        var obfuscator = new SourceControlFlowObfuscator(logger.Object);

        var enabledSettings = new ObfySettings { ControlFlow = { Enabled = true } };
        var disabledSettings = new ObfySettings { ControlFlow = { Enabled = false } };

        obfuscator.IsEnabled(enabledSettings).ShouldBeTrue();
        obfuscator.IsEnabled(disabledSettings).ShouldBeFalse();
    }

    #endregion

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

using System.Reflection;
using System.Runtime.Loader;
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

    private static CSharpCompilation RecompileWithFullReferences(CSharpCompilation compilation)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        return CSharpCompilation.Create(
            "VerifyAssembly",
            compilation.SyntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IReadOnlyList<Diagnostic> GetErrors(CSharpCompilation compilation) =>
        RecompileWithFullReferences(compilation)
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

    private static object? EmitAndInvoke(CSharpCompilation compilation, string typeName, string methodName, params object[] args)
    {
        var full = RecompileWithFullReferences(compilation);
        using var ms = new MemoryStream();
        var emit = full.Emit(ms);
        emit.Success.ShouldBeTrue(
            "Obfuscated code failed to compile:\n" +
            string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        ms.Position = 0;
        var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var asm = alc.LoadFromStream(ms);
            var type = asm.GetTypes().First(t => t.Name == typeName);
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            var instance = method!.IsStatic ? null : Activator.CreateInstance(type);
            return method.Invoke(instance, args.Length == 0 ? null : args);
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public async Task SourceControlFlow_SwitchMode_ProducesCompilableRunnableCode()
    {
        // Switch flattening previously left value-returning methods with no return after the loop
        // (CS0161). Compile and run the flattened method to prove it is valid and behavior-preserving.
        var sourceCode = """
            public class Calc
            {
                public int Compute(int x)
                {
                    x = x + 5;
                    x = x * 3;
                    x = x - 2;
                    return x;
                }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var obfuscator = new SourceControlFlowObfuscator(new Mock<ILogger<SourceControlFlowObfuscator>>().Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsControlFlowObfuscated.ShouldBe(1);

        GetErrors(context.Compilation!).ShouldBeEmpty();
        EmitAndInvoke(context.Compilation!, "Calc", "Compute", 10).ShouldBe(((10 + 5) * 3) - 2);
    }

    [Fact]
    public async Task SourceControlFlow_OpaquePredicate_DoesNotScopeAwayDeclarations()
    {
        // Opaque predicates previously wrapped local declarations in an if-block, hiding the local
        // from the rest of the method. Compile and run to prove declarations stay in scope.
        var sourceCode = """
            public class Calc
            {
                public int Compute(int seed)
                {
                    int a = seed + 1;
                    int b = a * 2;
                    int c = b - 3;
                    return a + b + c;
                }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var obfuscator = new SourceControlFlowObfuscator(new Mock<ILogger<SourceControlFlowObfuscator>>().Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.OpaquePredicate, Intensity = 100 }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();

        GetErrors(context.Compilation!).ShouldBeEmpty();
        // a=11, b=22, c=19 => 52
        EmitAndInvoke(context.Compilation!, "Calc", "Compute", 10).ShouldBe(52);
    }

    [Fact]
    public async Task SourceSymbolRenamer_SemanticRename_CompilesAndPreservesBehavior()
    {
        // Two locals named 'x' in different methods, plus a renamed private method reference. A
        // textual renamer would corrupt these; the semantic renamer must keep them independent and
        // preserve behavior, while leaving the public API (Widget.Result) intact.
        var sourceCode = """
            public class Widget
            {
                private int Seed() { int x = 21; return x; }
                public int Result()
                {
                    int x = Seed();
                    return x + 21;
                }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var renamer = new SourceSymbolRenamer(new NameGenerator(), new Mock<ILogger<SourceSymbolRenamer>>().Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            SymbolRenaming =
            {
                Enabled = true,
                RenameMethods = true,
                RenameFields = true,
                RenameParameters = true,
                PreservePublicApi = true,
                Mode = NamingMode.Sequential
            }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        var result = await renamer.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsRenamed.ShouldBe(1); // Seed renamed, public Result preserved

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldNotContain("Seed");
        newSource.ShouldContain("Result"); // public API preserved

        GetErrors(context.Compilation!).ShouldBeEmpty();
        EmitAndInvoke(context.Compilation!, "Widget", "Result").ShouldBe(42);
    }

    [Fact]
    public async Task SourceControlFlow_SwitchMode_DoesNotFlattenOutVarDeclarations()
    {
        // Flattening used to split `out var x` from later uses (CS0103). CanFlatten must refuse.
        var sourceCode = """
            public class Parser
            {
                public int Parse(string s)
                {
                    int.TryParse(s, out var x);
                    x = x + 1;
                    return x;
                }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var obfuscator = new SourceControlFlowObfuscator(new Mock<ILogger<SourceControlFlowObfuscator>>().Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();

        GetErrors(context.Compilation!).ShouldBeEmpty();
        EmitAndInvoke(context.Compilation!, "Parser", "Parse", "41").ShouldBe(42);
    }

    [Fact]
    public async Task SourceControlFlow_SwitchMode_DoesNotFlattenLocalFunctions()
    {
        // A local function in case 0 is invisible to calls in later cases (CS0103).
        var sourceCode = """
            public class Calc
            {
                public int Compute(int x)
                {
                    int Local(int n) => n + 1;
                    x = Local(x);
                    x = x + 2;
                    return x;
                }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var obfuscator = new SourceControlFlowObfuscator(new Mock<ILogger<SourceControlFlowObfuscator>>().Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();

        GetErrors(context.Compilation!).ShouldBeEmpty();
        EmitAndInvoke(context.Compilation!, "Calc", "Compute", 10).ShouldBe(13);
    }

    [Fact]
    public async Task SourceSymbolRenamer_RewritesAttributeTypeNames()
    {
        // `[Marker]` binds to the constructor, not the type. The type must be renamed and the
        // attribute use-site must follow, or the result fails with CS0246.
        var sourceCode = """
            using System;
            class MarkerAttribute : Attribute {}
            [Marker]
            public class Widget
            {
                public static int Result() => 1;
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var renamer = new SourceSymbolRenamer(new NameGenerator(), new Mock<ILogger<SourceSymbolRenamer>>().Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            SymbolRenaming =
            {
                Enabled = true,
                RenameTypes = true,
                PreservePublicApi = true,
                Mode = NamingMode.Sequential
            }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        var result = await renamer.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.TypesRenamed.ShouldBeGreaterThan(0);

        var newSource = context.Compilation!.SyntaxTrees.First().ToString();
        newSource.ShouldNotContain("MarkerAttribute");
        newSource.ShouldNotContain("[Marker]");
        newSource.ShouldContain("Widget");

        GetErrors(context.Compilation!).ShouldBeEmpty();
        EmitAndInvoke(context.Compilation!, "Widget", "Result").ShouldBe(1);
    }

    [Fact]
    public async Task SourceSymbolRenamer_RenamesRecordPositionalParametersAndPropertyUses()
    {
        // `record Person(string Name)` is one parameter token that also declares a property.
        // Renaming the parameter without the property use-site (`p.Name`) produces CS1061.
        var sourceCode = """
            record Person(string Name);
            public class Use
            {
                public static string Run()
                {
                    var p = new Person("Ada");
                    return p.Name;
                }
            }
            """;

        var compilation = CreateCompilation(sourceCode);
        var renamer = new SourceSymbolRenamer(new NameGenerator(), new Mock<ILogger<SourceSymbolRenamer>>().Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            SymbolRenaming =
            {
                Enabled = true,
                RenameTypes = true,
                RenameParameters = true,
                RenameProperties = true,
                PreservePublicApi = true,
                Mode = NamingMode.Sequential
            }
        };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        var result = await renamer.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();

        GetErrors(context.Compilation!).ShouldBeEmpty();
        EmitAndInvoke(context.Compilation!, "Use", "Run").ShouldBe("Ada");
    }
}

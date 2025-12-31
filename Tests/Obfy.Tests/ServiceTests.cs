using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Tests for service layer classes.
/// </summary>
public class ServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public ServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"ObfyTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region AssemblyProcessor Tests

    [Fact]
    public async Task AssemblyProcessor_Load_LoadsValidAssembly()
    {
        // Arrange
        var assemblyPath = CreateTestAssembly("TestAssembly.dll");
        var logger = new Mock<ILogger<AssemblyProcessor>>();
        var processor = new AssemblyProcessor(logger.Object);
        var settings = new ObfySettings();

        // Act
        var context = await processor.LoadAsync(assemblyPath, settings);

        // Assert
        context.ShouldNotBeNull();
        context.Module.ShouldNotBeNull();
        context.TargetType.ShouldBe(TargetType.Assembly);
        context.InputPath.ShouldBe(assemblyPath);
    }

    [Fact]
    public async Task AssemblyProcessor_Load_ThrowsOnInvalidFile()
    {
        // Arrange
        var invalidPath = Path.Combine(_tempDirectory, "invalid.dll");
        await File.WriteAllTextAsync(invalidPath, "not a valid assembly");

        var logger = new Mock<ILogger<AssemblyProcessor>>();
        var processor = new AssemblyProcessor(logger.Object);
        var settings = new ObfySettings();

        // Act & Assert
        await Should.ThrowAsync<BadImageFormatException>(async () =>
            await processor.LoadAsync(invalidPath, settings));
    }

    [Fact]
    public async Task AssemblyProcessor_Save_CreatesOutputDirectory()
    {
        // Arrange
        var assemblyPath = CreateTestAssembly("TestAssembly.dll");
        var outputPath = Path.Combine(_tempDirectory, "output", "subdir", "output.dll");

        var logger = new Mock<ILogger<AssemblyProcessor>>();
        var processor = new AssemblyProcessor(logger.Object);
        var settings = new ObfySettings();

        var context = await processor.LoadAsync(assemblyPath, settings);

        // Act
        await processor.SaveAsync(context, outputPath);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();
    }

    [Fact]
    public async Task AssemblyProcessor_Save_ThrowsOnNullModule()
    {
        // Arrange
        var logger = new Mock<ILogger<AssemblyProcessor>>();
        var processor = new AssemblyProcessor(logger.Object);

        var context = new PipelineContext
        {
            TargetType = TargetType.Assembly,
            Module = null,
            Settings = new ObfySettings()
        };

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await processor.SaveAsync(context, "output.dll"));
    }

    [Fact]
    public async Task AssemblyProcessor_Save_DisablesPdbWhenConfigured()
    {
        // Arrange
        var assemblyPath = CreateTestAssembly("TestAssembly.dll");
        var outputPath = Path.Combine(_tempDirectory, "output_no_pdb.dll");

        var logger = new Mock<ILogger<AssemblyProcessor>>();
        var processor = new AssemblyProcessor(logger.Object);
        var settings = new ObfySettings { Metadata = { RemoveDebugInfo = true } };

        var context = await processor.LoadAsync(assemblyPath, settings);

        // Act
        await processor.SaveAsync(context, outputPath);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();
        // PDB file should not be created
        var pdbPath = Path.ChangeExtension(outputPath, ".pdb");
        File.Exists(pdbPath).ShouldBeFalse();
    }

    #endregion

    #region SourceProcessor Tests

    [Fact]
    public async Task SourceProcessor_Load_LoadsSingleFile()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "Test.cs");
        await File.WriteAllTextAsync(sourcePath, @"
using System;
namespace Test
{
    public class MyClass
    {
        public void MyMethod() { }
    }
}");

        var logger = new Mock<ILogger<SourceProcessor>>();
        var processor = new SourceProcessor(logger.Object);
        var settings = new ObfySettings();

        // Act
        var context = await processor.LoadAsync(sourcePath, settings);

        // Assert
        context.ShouldNotBeNull();
        context.Compilation.ShouldNotBeNull();
        context.TargetType.ShouldBe(TargetType.SourceCode);
        context.Compilation!.SyntaxTrees.Count().ShouldBe(1);
    }

    [Fact]
    public async Task SourceProcessor_Load_LoadsDirectory()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "sources");
        Directory.CreateDirectory(sourceDir);

        await File.WriteAllTextAsync(Path.Combine(sourceDir, "File1.cs"), "class File1 { }");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "File2.cs"), "class File2 { }");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "File3.cs"), "class File3 { }");

        var logger = new Mock<ILogger<SourceProcessor>>();
        var processor = new SourceProcessor(logger.Object);
        var settings = new ObfySettings();

        // Act
        var context = await processor.LoadAsync(sourceDir, settings);

        // Assert
        context.Compilation.ShouldNotBeNull();
        context.Compilation!.SyntaxTrees.Count().ShouldBe(3);
    }

    [Fact]
    public async Task SourceProcessor_Load_ThrowsOnInvalidPath()
    {
        // Arrange
        var invalidPath = Path.Combine(_tempDirectory, "nonexistent.cs");

        var logger = new Mock<ILogger<SourceProcessor>>();
        var processor = new SourceProcessor(logger.Object);
        var settings = new ObfySettings();

        // Act & Assert
        await Should.ThrowAsync<FileNotFoundException>(async () =>
            await processor.LoadAsync(invalidPath, settings));
    }

    [Fact]
    public async Task SourceProcessor_Save_SavesSingleFile()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "Input.cs");
        var outputPath = Path.Combine(_tempDirectory, "Output.cs");

        await File.WriteAllTextAsync(sourcePath, "class Test { }");

        var logger = new Mock<ILogger<SourceProcessor>>();
        var processor = new SourceProcessor(logger.Object);
        var settings = new ObfySettings();

        var context = await processor.LoadAsync(sourcePath, settings);

        // Act
        await processor.SaveAsync(context, outputPath);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.ShouldContain("class Test");
    }

    [Fact]
    public async Task SourceProcessor_Save_SavesMultipleFilesToDirectory()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "input");
        var outputDir = Path.Combine(_tempDirectory, "output_sources");
        Directory.CreateDirectory(sourceDir);

        await File.WriteAllTextAsync(Path.Combine(sourceDir, "A.cs"), "class A { }");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "B.cs"), "class B { }");

        var logger = new Mock<ILogger<SourceProcessor>>();
        var processor = new SourceProcessor(logger.Object);
        var settings = new ObfySettings();

        var context = await processor.LoadAsync(sourceDir, settings);

        // Act
        await processor.SaveAsync(context, outputDir);

        // Assert
        Directory.Exists(outputDir).ShouldBeTrue();
        Directory.GetFiles(outputDir, "*.cs").Length.ShouldBe(2);
    }

    [Fact]
    public async Task SourceProcessor_Save_ThrowsOnNullCompilation()
    {
        // Arrange
        var logger = new Mock<ILogger<SourceProcessor>>();
        var processor = new SourceProcessor(logger.Object);

        var context = new PipelineContext
        {
            TargetType = TargetType.SourceCode,
            Compilation = null,
            Settings = new ObfySettings()
        };

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await processor.SaveAsync(context, "output.cs"));
    }

    [Fact]
    public async Task SourceProcessor_Save_CreatesOutputDirectory()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "Input2.cs");
        var outputPath = Path.Combine(_tempDirectory, "nested", "deep", "Output.cs");

        await File.WriteAllTextAsync(sourcePath, "class Test { }");

        var logger = new Mock<ILogger<SourceProcessor>>();
        var processor = new SourceProcessor(logger.Object);
        var settings = new ObfySettings();

        var context = await processor.LoadAsync(sourcePath, settings);

        // Act
        await processor.SaveAsync(context, outputPath);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();
    }

    #endregion

    #region ObfuscationService Tests

    [Fact]
    public async Task ObfuscationService_Obfuscate_FailsOnMissingFile()
    {
        // Arrange
        var assemblyProcessor = new Mock<IAssemblyProcessor>();
        var sourceProcessor = new Mock<ISourceProcessor>();
        var pipeline = new Mock<IObfuscationPipeline>();
        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            logger.Object);

        var settings = new ObfySettings();

        // Act
        var result = await service.ObfuscateAsync(
            Path.Combine(_tempDirectory, "nonexistent.dll"),
            null,
            settings);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("not found");
    }

    [Fact]
    public async Task ObfuscationService_Obfuscate_CallsAssemblyProcessor()
    {
        // Arrange
        var assemblyPath = CreateTestAssembly("ServiceTest.dll");
        var outputPath = Path.Combine(_tempDirectory, "ServiceOutput.dll");

        var mockContext = PipelineContext.ForAssembly(
            ModuleDefMD.Load(assemblyPath),
            new ObfySettings());

        var assemblyProcessor = new Mock<IAssemblyProcessor>();
        assemblyProcessor
            .Setup(p => p.LoadAsync(assemblyPath, It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext);
        assemblyProcessor
            .Setup(p => p.SaveAsync(It.IsAny<PipelineContext>(), outputPath, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sourceProcessor = new Mock<ISourceProcessor>();
        var pipeline = new Mock<IObfuscationPipeline>();
        pipeline
            .Setup(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics()));

        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            logger.Object);

        var settings = new ObfySettings();

        // Act
        var result = await service.ObfuscateAsync(assemblyPath, outputPath, settings);

        // Assert
        result.Success.ShouldBeTrue();
        assemblyProcessor.Verify(p => p.LoadAsync(assemblyPath, It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()), Times.Once);
        assemblyProcessor.Verify(p => p.SaveAsync(It.IsAny<PipelineContext>(), outputPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ObfuscationService_Obfuscate_CallsSourceProcessor()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "ServiceSource.cs");
        var outputPath = Path.Combine(_tempDirectory, "ServiceSourceOutput.cs");

        await File.WriteAllTextAsync(sourcePath, "class Test { }");

        var syntaxTree = CSharpSyntaxTree.ParseText("class Test { }");
        var compilation = CSharpCompilation.Create("Test", new[] { syntaxTree });
        var mockContext = PipelineContext.ForSourceCode(compilation, new ObfySettings());

        var assemblyProcessor = new Mock<IAssemblyProcessor>();
        var sourceProcessor = new Mock<ISourceProcessor>();
        sourceProcessor
            .Setup(p => p.LoadAsync(sourcePath, It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext);
        sourceProcessor
            .Setup(p => p.SaveAsync(It.IsAny<PipelineContext>(), outputPath, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var pipeline = new Mock<IObfuscationPipeline>();
        pipeline
            .Setup(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics()));

        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            logger.Object);

        var settings = new ObfySettings();

        // Act
        var result = await service.ObfuscateAsync(sourcePath, outputPath, settings);

        // Assert
        result.Success.ShouldBeTrue();
        sourceProcessor.Verify(p => p.LoadAsync(sourcePath, It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()), Times.Once);
        sourceProcessor.Verify(p => p.SaveAsync(It.IsAny<PipelineContext>(), outputPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ObfuscationService_BatchObfuscate_ProcessesMultipleFiles()
    {
        // Arrange
        var assembly1 = CreateTestAssembly("Batch1.dll");
        var assembly2 = CreateTestAssembly("Batch2.dll");
        var outputDir = Path.Combine(_tempDirectory, "batch_output");
        Directory.CreateDirectory(outputDir);

        var mockContext = PipelineContext.ForAssembly(
            ModuleDefMD.Load(assembly1),
            new ObfySettings());

        var assemblyProcessor = new Mock<IAssemblyProcessor>();
        assemblyProcessor
            .Setup(p => p.LoadAsync(It.IsAny<string>(), It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext);
        assemblyProcessor
            .Setup(p => p.SaveAsync(It.IsAny<PipelineContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sourceProcessor = new Mock<ISourceProcessor>();
        var pipeline = new Mock<IObfuscationPipeline>();
        pipeline
            .Setup(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics()));

        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            logger.Object);

        var settings = new ObfySettings();

        // Act
        var results = await service.ObfuscateBatchAsync(
            new[] { assembly1, assembly2 },
            outputDir,
            settings);

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldAllBe(r => r.Success);
    }

    [Fact]
    public async Task ObfuscationService_WriteSymbolMap_WritesJsonFile()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDirectory, "symbolmap.json");
        var symbolMap = new Dictionary<string, string>
        {
            { "Type:MyClass", "a" },
            { "Method:MyMethod", "b" }
        };

        var assemblyProcessor = new Mock<IAssemblyProcessor>();
        var sourceProcessor = new Mock<ISourceProcessor>();
        var pipeline = new Mock<IObfuscationPipeline>();
        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            logger.Object);

        // Act
        await service.WriteSymbolMapAsync(symbolMap, outputPath);

        // Assert
        File.Exists(outputPath).ShouldBeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.ShouldContain("Type:MyClass");
        content.ShouldContain("Method:MyMethod");
    }

    [Fact]
    public async Task ObfuscationService_Obfuscate_ReturnsFailureOnLoadError()
    {
        // Arrange
        var assemblyPath = CreateTestAssembly("FailTest.dll");

        var assemblyProcessor = new Mock<IAssemblyProcessor>();
        assemblyProcessor
            .Setup(p => p.LoadAsync(assemblyPath, It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Load failed"));

        var sourceProcessor = new Mock<ISourceProcessor>();
        var pipeline = new Mock<IObfuscationPipeline>();
        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            logger.Object);

        var settings = new ObfySettings();

        // Act
        var result = await service.ObfuscateAsync(assemblyPath, null, settings);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Failed to load");
    }

    [Fact]
    public async Task ObfuscationService_Obfuscate_ReturnsFailureOnPipelineError()
    {
        // Arrange
        var assemblyPath = CreateTestAssembly("PipelineFailTest.dll");

        var mockContext = PipelineContext.ForAssembly(
            ModuleDefMD.Load(assemblyPath),
            new ObfySettings());

        var assemblyProcessor = new Mock<IAssemblyProcessor>();
        assemblyProcessor
            .Setup(p => p.LoadAsync(assemblyPath, It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockContext);

        var sourceProcessor = new Mock<ISourceProcessor>();
        var pipeline = new Mock<IObfuscationPipeline>();
        pipeline
            .Setup(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Failed("Pipeline failed"));

        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            logger.Object);

        var settings = new ObfySettings();

        // Act
        var result = await service.ObfuscateAsync(assemblyPath, null, settings);

        // Assert
        result.Success.ShouldBeFalse();
    }

    #endregion

    #region Helper Methods

    private string CreateTestAssembly(string name)
    {
        var path = Path.Combine(_tempDirectory, name);

        // Create a minimal valid .NET assembly using dnlib
        var module = new ModuleDefUser(name, Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var assembly = new AssemblyDefUser(Path.GetFileNameWithoutExtension(name), new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);

        // Add a simple type
        var typeDef = new TypeDefUser("TestNamespace", "TestClass", module.CorLibTypes.Object.TypeDefOrRef);
        typeDef.Attributes = TypeAttributes.Public | TypeAttributes.Class;
        module.Types.Add(typeDef);

        // Add a simple method
        var method = new MethodDefUser(
            "TestMethod",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody();
        body.Instructions.Add(dnlib.DotNet.Emit.Instruction.Create(dnlib.DotNet.Emit.OpCodes.Ret));
        method.Body = body;
        typeDef.Methods.Add(method);

        module.Write(path);
        return path;
    }

    #endregion
}

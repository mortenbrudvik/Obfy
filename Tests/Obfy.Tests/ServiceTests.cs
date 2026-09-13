using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    public async Task AssemblyProcessor_Save_ConvertsShortBranchesThatAreTooFar()
    {
        var assemblyPath = CreateTestAssembly("ShortBranch.dll");
        var outputPath = Path.Combine(_tempDirectory, "short-branch.dll");
        var processor = new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object);
        var context = await processor.LoadAsync(assemblyPath, new ObfySettings());

        var method = context.Module!.Types.Single(t => t.Name == "TestClass").Methods
            .Single(m => m.Name == "TestMethod");
        var ret = Instruction.Create(OpCodes.Ret);
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Br_S, ret));
        for (var i = 0; i < 200; i++)
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
        method.Body.Instructions.Add(ret);

        await processor.SaveAsync(context, outputPath);
        File.Exists(outputPath).ShouldBeTrue();
        File.ReadAllBytes(outputPath).Length.ShouldBeGreaterThan(0);
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

    [Fact]
    public async Task AssemblyProcessor_Save_SigningWithoutKeyFile_Fails()
    {
        var assemblyPath = CreateTestAssembly("Unsigned.dll");
        var outputPath = Path.Combine(_tempDirectory, "signed-missing.dll");
        var processor = new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object);
        var settings = new ObfySettings { Signing = { Enabled = true } };
        var context = await processor.LoadAsync(assemblyPath, settings);

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await processor.SaveAsync(context, outputPath));
        ex.Message.ShouldContain("key file");
    }

    [Fact]
    public async Task AssemblyProcessor_Save_ResignsWithSnk()
    {
        var snkPath = Path.Combine(_tempDirectory, "test.snk");
#pragma warning disable SYSLIB0028
        var cspParams = new CspParameters { KeyNumber = (int)KeyNumber.Signature };
        using (var csp = new RSACryptoServiceProvider(1024, cspParams))
            File.WriteAllBytes(snkPath, csp.ExportCspBlob(includePrivateParameters: true));
#pragma warning restore SYSLIB0028

        var assemblyPath = CreateTestAssembly("ToSign.dll");
        var outputPath = Path.Combine(_tempDirectory, "signed.dll");
        var processor = new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object);
        var settings = new ObfySettings { Signing = { Enabled = true, KeyFile = snkPath } };
        var context = await processor.LoadAsync(assemblyPath, settings);

        await processor.SaveAsync(context, outputPath);

        File.Exists(outputPath).ShouldBeTrue();
        using var signed = ModuleDefMD.Load(File.ReadAllBytes(outputPath));
        signed.IsStrongNameSigned.ShouldBeTrue();
        signed.Assembly.PublicKey.ShouldNotBeNull();
        signed.Assembly.PublicKey.Data.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task AssemblyProcessor_Save_MissingKeyFilePath_Fails()
    {
        var assemblyPath = CreateTestAssembly("Unsigned2.dll");
        var outputPath = Path.Combine(_tempDirectory, "signed-missing-path.dll");
        var processor = new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object);
        var settings = new ObfySettings
        {
            Signing = { Enabled = true, KeyFile = Path.Combine(_tempDirectory, "no-such.snk") }
        };
        var context = await processor.LoadAsync(assemblyPath, settings);

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await processor.SaveAsync(context, outputPath));
        ex.Message.ShouldContain("not found");
    }

    [Fact]
    public async Task AssemblyProcessor_Save_PfxWithoutPasswordEnv_Fails()
    {
        var pfxPath = Path.Combine(_tempDirectory, "key.pfx");
        WritePfx(pfxPath, "secret");
        var assemblyPath = CreateTestAssembly("PfxNoPass.dll");
        var outputPath = Path.Combine(_tempDirectory, "pfx-nopass.dll");
        var processor = new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object);
        var settings = new ObfySettings
        {
            Signing = { Enabled = true, KeyFile = pfxPath }
        };
        var context = await processor.LoadAsync(assemblyPath, settings);

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await processor.SaveAsync(context, outputPath));
        ex.Message.ShouldContain("PasswordEnvironmentVariable");
    }

    [Fact]
    public async Task AssemblyProcessor_Save_PfxWithPassword_Signs()
    {
        var pfxPath = Path.Combine(_tempDirectory, "ok.pfx");
        WritePfx(pfxPath, "secret");
        const string env = "OBFY_TEST_PFX_PASSWORD";
        Environment.SetEnvironmentVariable(env, "secret");
        try
        {
            var assemblyPath = CreateTestAssembly("PfxOk.dll");
            var outputPath = Path.Combine(_tempDirectory, "pfx-ok.dll");
            var processor = new AssemblyProcessor(new Mock<ILogger<AssemblyProcessor>>().Object);
            var settings = new ObfySettings
            {
                Signing =
                {
                    Enabled = true,
                    KeyFile = pfxPath,
                    PasswordEnvironmentVariable = env
                }
            };
            var context = await processor.LoadAsync(assemblyPath, settings);
            await processor.SaveAsync(context, outputPath);

            using var signed = ModuleDefMD.Load(File.ReadAllBytes(outputPath));
            signed.IsStrongNameSigned.ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(env, null);
        }
    }

    private static void WritePfx(string path, string password)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=ObfyTest", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
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
    public async Task SourceProcessor_Save_PreservesRelativeSubdirectories()
    {
        var sourceDir = Path.Combine(_tempDirectory, "srcroot");
        var nested = Path.Combine(sourceDir, "Nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Root.cs"), "class Root { }");
        await File.WriteAllTextAsync(Path.Combine(nested, "Child.cs"), "class Child { }");

        var processor = new SourceProcessor(new Mock<ILogger<SourceProcessor>>().Object);
        var context = await processor.LoadAsync(sourceDir, new ObfySettings());
        var outputDir = Path.Combine(_tempDirectory, "outroot");
        await processor.SaveAsync(context, outputDir);

        File.Exists(Path.Combine(outputDir, "Root.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "Nested", "Child.cs")).ShouldBeTrue();
    }

    [Fact]
    public async Task SourceProcessor_Load_IncludesTrustedPlatformAssemblies()
    {
        var sourcePath = Path.Combine(_tempDirectory, "Http.cs");
        await File.WriteAllTextAsync(sourcePath, """
            using System.Net.Http;
            class C { HttpClient? Client; }
            """);
        var processor = new SourceProcessor(new Mock<ILogger<SourceProcessor>>().Object);
        var context = await processor.LoadAsync(sourcePath, new ObfySettings());
        context.Compilation.ShouldNotBeNull();
        context.Compilation!.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ShouldBeEmpty();
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
        var assemblyMerger = new Mock<IAssemblyMerger>();
        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            assemblyMerger.Object,
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

        var assemblyMerger = new Mock<IAssemblyMerger>();
        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            assemblyMerger.Object,
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

        var assemblyMerger = new Mock<IAssemblyMerger>();
        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            assemblyMerger.Object,
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

        var assemblyMerger = new Mock<IAssemblyMerger>();
        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            assemblyMerger.Object,
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
        var assemblyMerger = new Mock<IAssemblyMerger>();
        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            assemblyMerger.Object,
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
        var assemblyMerger = new Mock<IAssemblyMerger>();
        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            assemblyMerger.Object,
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

        var assemblyMerger = new Mock<IAssemblyMerger>();
        var logger = new Mock<ILogger<ObfuscationService>>();

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            sourceProcessor.Object,
            pipeline.Object,
            assemblyMerger.Object,
            logger.Object);

        var settings = new ObfySettings();

        // Act
        var result = await service.ObfuscateAsync(assemblyPath, null, settings);

        // Assert
        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task ObfuscationService_CopiesSkippedItemsAndWarningsOntoResult()
    {
        var assemblyPath = CreateTestAssembly("SkipWarn.dll");
        var outputPath = Path.Combine(_tempDirectory, "SkipWarn.out.dll");

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

        var pipeline = new Mock<IObfuscationPipeline>();
        pipeline
            .Setup(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .Callback<PipelineContext, CancellationToken>((ctx, _) =>
            {
                ctx.SkippedItems.Add(SkippedItem.UnsupportedMethod("Foo::Bar", "Exception handlers"));
                ctx.Warnings.Add("protection ineffective");
            })
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics { StringsEncrypted = 1 }));

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            new Mock<ISourceProcessor>().Object,
            pipeline.Object,
            new Mock<IAssemblyMerger>().Object,
            new Mock<ILogger<ObfuscationService>>().Object);

        var result = await service.ObfuscateAsync(assemblyPath, outputPath, new ObfySettings { Level = ObfuscationLevel.Custom });

        result.Success.ShouldBeTrue();
        result.SkippedItems.ShouldContain(s => s.ItemName == "Foo::Bar");
        result.Warnings.ShouldContain("protection ineffective");
    }

    [Fact]
    public async Task ObfuscationService_FailsOnInvalidSettings()
    {
        var assemblyPath = CreateTestAssembly("InvalidSettings.dll");
        var service = new ObfuscationService(
            new Mock<IAssemblyProcessor>().Object,
            new Mock<ISourceProcessor>().Object,
            new Mock<IObfuscationPipeline>().Object,
            new Mock<IAssemblyMerger>().Object,
            new Mock<ILogger<ObfuscationService>>().Object);

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            ControlFlow = { Intensity = 101 }
        };

        var result = await service.ObfuscateAsync(assemblyPath, null, settings);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Invalid settings");
    }

    [Fact]
    public async Task ObfuscationService_DoesNotWarnThatAntiDumpIsUnimplemented()
    {
        var assemblyPath = CreateTestAssembly("AntiDump.dll");
        var outputPath = Path.Combine(_tempDirectory, "AntiDump.out.dll");

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

        var pipeline = new Mock<IObfuscationPipeline>();
        pipeline
            .Setup(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics()));

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            new Mock<ISourceProcessor>().Object,
            pipeline.Object,
            new Mock<IAssemblyMerger>().Object,
            new Mock<ILogger<ObfuscationService>>().Object);

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            Protection = { AntiDump = true }
        };

        var result = await service.ObfuscateAsync(assemblyPath, outputPath, settings);

        result.Success.ShouldBeTrue();
        result.Warnings.ShouldNotContain(w => w.Contains("Anti-dump is not implemented"));
    }

    [Fact]
    public async Task ObfuscationService_DoesNotMutateCallerSettings()
    {
        var assemblyPath = CreateTestAssembly("CloneSettings.dll");
        var outputPath = Path.Combine(_tempDirectory, "CloneSettings.out.dll");

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

        var pipeline = new Mock<IObfuscationPipeline>();
        pipeline
            .Setup(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics()));

        var service = new ObfuscationService(
            assemblyProcessor.Object,
            new Mock<ISourceProcessor>().Object,
            pipeline.Object,
            new Mock<IAssemblyMerger>().Object,
            new Mock<ILogger<ObfuscationService>>().Object);

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Aggressive,
            StringEncryption = { Enabled = false }
        };

        var result = await service.ObfuscateAsync(assemblyPath, outputPath, settings);

        result.Success.ShouldBeTrue();
        settings.StringEncryption.Enabled.ShouldBeFalse();
        assemblyProcessor.Verify(p => p.LoadAsync(
            assemblyPath,
            It.Is<ObfySettings>(s => s.StringEncryption.Enabled && !ReferenceEquals(s, settings)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AssemblyMerger Tests

    [Fact]
    public async Task AssemblyMerger_FailsWithLessThanTwoAssemblies()
    {
        // Arrange
        var logger = new Mock<ILogger<AssemblyMerger>>();
        var merger = new AssemblyMerger(logger.Object);

        var assembly1 = CreateTestAssembly("Single.dll");
        var settings = new AssemblyMergeSettings();

        // Act
        var result = await merger.MergeAsync(
            new[] { assembly1 },
            Path.Combine(_tempDirectory, "merged.dll"),
            settings);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("At least two assemblies");
    }

    [Fact]
    public async Task AssemblyMerger_FailsWithMissingFile()
    {
        // Arrange
        var logger = new Mock<ILogger<AssemblyMerger>>();
        var merger = new AssemblyMerger(logger.Object);

        var assembly1 = CreateTestAssembly("Exists.dll");
        var settings = new AssemblyMergeSettings();

        // Act
        var result = await merger.MergeAsync(
            new[] { assembly1, Path.Combine(_tempDirectory, "DoesNotExist.dll") },
            Path.Combine(_tempDirectory, "merged.dll"),
            settings);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("not found");
    }

    [Fact]
    public async Task AssemblyMerger_ExcludesPatternMatches()
    {
        // Arrange
        var logger = new Mock<ILogger<AssemblyMerger>>();
        var merger = new AssemblyMerger(logger.Object);

        var assembly1 = CreateTestAssembly("Main.dll");
        var assembly2 = CreateTestAssembly("System.Helper.dll");
        var settings = new AssemblyMergeSettings
        {
            ExcludePatterns = new List<string> { "System.*" }
        };

        // Act
        var result = await merger.MergeAsync(
            new[] { assembly1, assembly2 },
            Path.Combine(_tempDirectory, "merged_exclude.dll"),
            settings);

        // Assert - should fail because after exclusion only one assembly remains
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("Less than two assemblies remain");
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

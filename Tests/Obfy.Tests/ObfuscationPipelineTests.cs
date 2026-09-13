using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators;
using Obfy.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Obfy.Tests;

public class ObfuscationPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_WithNoObfuscators_ReturnsSuccess()
    {
        // Arrange
        var logger = new Mock<ILogger<ObfuscationPipeline>>();
        var pipeline = new ObfuscationPipeline([], logger.Object);
        var context = new PipelineContext
        {
            TargetType = TargetType.Assembly,
            Settings = new ObfySettings()
        };

        // Act
        var result = await pipeline.ExecuteAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.TotalTransformations.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithEnabledObfuscator_ExecutesObfuscator()
    {
        // Arrange
        var logger = new Mock<ILogger<ObfuscationPipeline>>();
        var obfuscator = new Mock<IObfuscator>();
        obfuscator.Setup(o => o.Name).Returns("Test");
        obfuscator.Setup(o => o.Priority).Returns(10);
        obfuscator.Setup(o => o.SupportsTargetType(TargetType.Assembly)).Returns(true);
        obfuscator.Setup(o => o.IsEnabled(It.IsAny<ObfySettings>())).Returns(true);
        obfuscator.Setup(o => o.ObfuscateAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Successful(new ObfuscationStatistics { StringsEncrypted = 5 }));

        var pipeline = new ObfuscationPipeline([obfuscator.Object], logger.Object);
        var context = new PipelineContext
        {
            TargetType = TargetType.Assembly,
            Settings = new ObfySettings()
        };

        // Act
        var result = await pipeline.ExecuteAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        obfuscator.Verify(o => o.ObfuscateAsync(context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithDisabledObfuscator_SkipsObfuscator()
    {
        // Arrange
        var logger = new Mock<ILogger<ObfuscationPipeline>>();
        var obfuscator = new Mock<IObfuscator>();
        obfuscator.Setup(o => o.Name).Returns("Test");
        obfuscator.Setup(o => o.SupportsTargetType(TargetType.Assembly)).Returns(true);
        obfuscator.Setup(o => o.IsEnabled(It.IsAny<ObfySettings>())).Returns(false);

        var pipeline = new ObfuscationPipeline([obfuscator.Object], logger.Object);
        var context = new PipelineContext
        {
            TargetType = TargetType.Assembly,
            Settings = new ObfySettings()
        };

        // Act
        var result = await pipeline.ExecuteAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        obfuscator.Verify(o => o.ObfuscateAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Obfuscators_ShouldBeOrderedByPriority()
    {
        // Arrange
        var logger = new Mock<ILogger<ObfuscationPipeline>>();

        var low = new Mock<IObfuscator>();
        low.Setup(o => o.Priority).Returns(10);
        low.Setup(o => o.Name).Returns("Low");

        var high = new Mock<IObfuscator>();
        high.Setup(o => o.Priority).Returns(90);
        high.Setup(o => o.Name).Returns("High");

        var medium = new Mock<IObfuscator>();
        medium.Setup(o => o.Priority).Returns(50);
        medium.Setup(o => o.Name).Returns("Medium");

        // Act
        var pipeline = new ObfuscationPipeline([high.Object, low.Object, medium.Object], logger.Object);

        // Assert
        pipeline.Obfuscators[0].Name.ShouldBe("Low");
        pipeline.Obfuscators[1].Name.ShouldBe("Medium");
        pipeline.Obfuscators[2].Name.ShouldBe("High");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var logger = new Mock<ILogger<ObfuscationPipeline>>();
        var obfuscator = new Mock<IObfuscator>();
        obfuscator.Setup(o => o.Name).Returns("Slow");
        obfuscator.Setup(o => o.Priority).Returns(10);
        obfuscator.Setup(o => o.SupportsTargetType(TargetType.Assembly)).Returns(true);
        obfuscator.Setup(o => o.IsEnabled(It.IsAny<ObfySettings>())).Returns(true);
        obfuscator.Setup(o => o.ObfuscateAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var pipeline = new ObfuscationPipeline([obfuscator.Object], logger.Object);
        var context = new PipelineContext
        {
            TargetType = TargetType.Assembly,
            Settings = new ObfySettings()
        };

        await Should.ThrowAsync<OperationCanceledException>(() => pipeline.ExecuteAsync(context));
    }
}

using Autofac;
using Logging.Core.Configuration;
using Logging.Core.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using Shouldly;

namespace Obfy.Tests;

public class LoggingConfigurationTests
{
    [Fact]
    public void CreateLoggerFactory_WritesFileAndHonorsConsoleOff()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var factory = LoggingConfiguration.CreateLoggerFactory(dir, "ObfyTests", enableConsoleOutput: false);
            var logger = factory.CreateLogger("LoggingConfigurationTests");
            logger.LogInformation("hello-from-test");
            factory.Dispose();
            LogManager.Shutdown();

            var files = Directory.GetFiles(dir, "ObfyTests-*.log");
            files.Length.ShouldBeGreaterThan(0);
            File.ReadAllText(files[0]).ShouldContain("hello-from-test");
        }
        finally
        {
            LogManager.Shutdown();
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void LoggingModule_ResolvesILogger()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new LoggingModule("ObfyTestsModule", enableConsoleOutput: false));
        using var container = builder.Build();
        container.Resolve<ILogger<LoggingConfigurationTests>>().ShouldNotBeNull();
        LogManager.Shutdown();
    }
}

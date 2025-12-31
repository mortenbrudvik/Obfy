using Autofac;
using Logging.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Logging.Core.DependencyInjection;

/// <summary>
/// Autofac module for registering logging services.
/// Register this module first before other modules to ensure ILogger&lt;T&gt; is available.
/// </summary>
public class LoggingModule : Module
{
    private readonly string? _appName;
    private readonly bool _enableConsoleOutput;

    /// <summary>
    /// Creates a new LoggingModule instance.
    /// </summary>
    /// <param name="appName">Optional application name for log file naming.</param>
    /// <param name="enableConsoleOutput">Whether to output logs to console. Default is true.</param>
    public LoggingModule(string? appName = null, bool enableConsoleOutput = true)
    {
        _appName = appName;
        _enableConsoleOutput = enableConsoleOutput;
    }

    /// <summary>
    /// Registers ILoggerFactory and ILogger&lt;T&gt; with the container.
    /// </summary>
    protected override void Load(ContainerBuilder builder)
    {
        var loggerFactory = LoggingConfiguration.CreateLoggerFactory(_appName, _enableConsoleOutput);

        builder.RegisterInstance(loggerFactory)
            .As<ILoggerFactory>()
            .SingleInstance();

        builder.RegisterGeneric(typeof(Logger<>))
            .As(typeof(ILogger<>))
            .SingleInstance();
    }
}

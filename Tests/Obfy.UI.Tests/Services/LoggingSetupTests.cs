using System.IO;
using Logging.Core.Configuration;
using Microsoft.Extensions.Logging;
using NLog;
using Shouldly;

namespace Obfy.UI.Tests.Services;

/// <summary>
/// The log file is written through an asynchronous NLog target, so a crash handler that only logs can lose the crash; and a
/// log folder that cannot be created used to be reported on Console.Error, which a WPF app does not have, while the failure
/// panel still said "the log has the details".
/// </summary>
public class LoggingSetupTests
{
    [Fact]
    public void FlushWithATimeout_WritesWhatIsQueued_BeforeReturning()
    {
        var folder = Path.Combine(Path.GetTempPath(), "obfy-logflush-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            const int lines = 3000;
            using var factory = LoggingConfiguration.CreateLoggerFactory(folder, "FlushTest", enableConsoleOutput: false);
            var logger = factory.CreateLogger("FlushTest");
            for (var i = 0; i < lines; i++)
                logger.LogError("crash line {Index}", i);

            // What the crash handler does as the process is going down: nothing may still be in the queue afterwards.
            LoggingConfiguration.Flush(TimeSpan.FromSeconds(10));

            var file = Directory.GetFiles(folder, "FlushTest-*.log").ShouldHaveSingleItem();
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var written = 0;
            while (reader.ReadLine() is { } line)
            {
                if (line.Contains("crash line "))
                    written++;
            }

            written.ShouldBe(lines);
        }
        finally
        {
            LogManager.Shutdown();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ALogFolderThatCannotBeCreated_IsReportedInTheStatus_AndNamesNoPath()
    {
        // A folder cannot be created beneath a file.
        var blocker = Path.Combine(Path.GetTempPath(), "obfy-logblock-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "not a folder");
        try
        {
            using var factory = LoggingConfiguration.CreateLoggerFactory(Path.Combine(blocker, "Logs"), "StatusTest", enableConsoleOutput: false);

            var status = LoggingConfiguration.Status;
            status.FileLoggingAvailable.ShouldBeFalse();
            status.FailureType.ShouldNotBeNullOrWhiteSpace();
            status.ToString().ShouldNotContain(blocker);
        }
        finally
        {
            LogManager.Shutdown();
            File.Delete(blocker);
        }
    }

    [Fact]
    public void AWritableLogFolder_IsReportedAvailable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "obfy-logok-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var factory = LoggingConfiguration.CreateLoggerFactory(folder, "StatusTest", enableConsoleOutput: false);

            LoggingConfiguration.Status.FileLoggingAvailable.ShouldBeTrue();
            LoggingConfiguration.Status.FailureType.ShouldBeNull();
        }
        finally
        {
            LogManager.Shutdown();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}

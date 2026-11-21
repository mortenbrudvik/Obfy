using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Obfy.UI.Services;
using Shouldly;

namespace Obfy.UI.Tests.Services;

/// <summary>
/// The shell's unhandled-exception handlers wrote <c>LogError(ex, ...)</c>, which puts the exception's message in the log
/// file. A message names what failed: a file the user picked, a folder they typed. The log keeps the type and the stack.
/// </summary>
public class CrashLogTests
{
    private sealed class CapturingLogger : ILogger
    {
        public readonly List<string> Lines = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }

    private static Exception Thrown(Exception ex)
    {
        try
        {
            throw ex;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    [Fact]
    public void Describe_HasTheTypes_AndTheStack_ButNotTheMessages()
    {
        var inner = Thrown(new FileNotFoundException("Could not find C:\\Users\\someone\\Secret Project\\app.dll"));
        var outer = Thrown(new TargetInvocationException("outer says Secret Project too", inner));

        var text = CrashLog.Describe(outer);

        text.ShouldContain(nameof(TargetInvocationException));
        text.ShouldContain(nameof(FileNotFoundException));
        text.ShouldNotContain("Secret Project");
        // The innermost exception's stack is the useful one: where it was thrown.
        text.ShouldContain(nameof(Thrown));
    }

    [Fact]
    public void Error_LogsTheMessage_AndTheDescription_NotTheExceptionText()
    {
        var logger = new CapturingLogger();

        CrashLog.Error(logger, Thrown(new IOException("C:\\Users\\someone\\Secret Project\\x")), "Unhandled UI exception");

        var line = logger.Lines.ShouldHaveSingleItem();
        line.ShouldContain("Unhandled UI exception");
        line.ShouldContain(nameof(IOException));
        line.ShouldNotContain("Secret Project");
    }
}

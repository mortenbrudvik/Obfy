using System.IO;
using Mediator;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.App.Features.Files;
using Obfy.App.Models;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.UI.Services;
using Shouldly;

namespace Obfy.UI.Tests.Services;

/// <summary>
/// What the user is told when start-up state or a dropped file cannot be applied. Both used to be a log line and nothing
/// else, and the preferences and the command line shared one try, so a failing preferences file also dropped the files,
/// the -o folder and the configuration the process was started with.
/// </summary>
public class StartupAndDropFailureTests
{
    private sealed class CapturingLogger : ILogger
    {
        public readonly List<string> Lines = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // What a file sink would write: the message, then the exception with its text.
            Lines.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
        }
    }

    private static InputSession NewSession()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupAllProperties();
        return new InputSession(settings.Object, preferenceDelay: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task APreferencesFailure_StillApplies_TheStartupArguments_AndTellsTheUser()
    {
        var applied = 0;
        var notices = new List<(string Title, string Message)>();

        await StartupState.ApplyAsync(
            loadPreferences: () => throw new IOException("locked: C:\\Users\\someone\\ui-preferences.json"),
            applyArguments: () =>
            {
                applied++;
                return Task.CompletedTask;
            },
            notify: (title, message) => notices.Add((title, message)),
            logger: new CapturingLogger());

        applied.ShouldBe(1);
        notices.Count.ShouldBe(1);
        notices[0].Title.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AnArgumentsFailure_TellsTheUser_AfterThePreferencesWereLoaded()
    {
        var loaded = 0;
        var notices = new List<(string Title, string Message)>();

        await StartupState.ApplyAsync(
            loadPreferences: () =>
            {
                loaded++;
                return Task.CompletedTask;
            },
            applyArguments: () => throw new InvalidOperationException("bad"),
            notify: (title, message) => notices.Add((title, message)),
            logger: new CapturingLogger());

        loaded.ShouldBe(1);
        notices.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenBothFail_BothAreReported_AndTheNoticesDiffer()
    {
        var notices = new List<(string Title, string Message)>();

        await StartupState.ApplyAsync(
            () => throw new IOException(),
            () => throw new InvalidOperationException(),
            (title, message) => notices.Add((title, message)),
            new CapturingLogger());

        notices.Count.ShouldBe(2);
        notices[0].Title.ShouldNotBe(notices[1].Title);
    }

    [Fact]
    public async Task WhenNothingFails_NobodyIsTold()
    {
        var notices = 0;

        await StartupState.ApplyAsync(
            () => Task.CompletedTask, () => Task.CompletedTask, (_, _) => notices++, new CapturingLogger());

        notices.ShouldBe(0);
    }

    [Fact]
    public async Task TheLog_KeepsTheExceptionType_NotItsMessage()
    {
        // The message of a failed preferences read or of a bad start-up argument names a file the user picked or typed.
        var logger = new CapturingLogger();
        const string secret = "C:\\Users\\someone\\Secret Project\\app.dll";

        await StartupState.ApplyAsync(
            () => throw new IOException(secret), () => throw new FileNotFoundException(secret), (_, _) => { }, logger);

        logger.Lines.ShouldNotBeEmpty();
        logger.Lines.ShouldAllBe(line => !line.Contains("Secret Project"));
        logger.Lines.ShouldContain(line => line.Contains(nameof(IOException)));
    }

    [Fact]
    public async Task ADropThatFails_TellsTheUser_AndTheLogHasNoPath()
    {
        const string secret = "C:\\Users\\someone\\Secret Project\\app.dll";
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<DropPathsRequest>(), It.IsAny<CancellationToken>()))
            .Throws(new FileNotFoundException("not found: " + secret));
        var input = NewSession();
        var logger = new CapturingLogger();

        await new DroppedFilesHandler(mediator.Object, input, logger).HandleAsync([secret]);

        var notice = input.GetSnapshot().Notices.ShouldHaveSingleItem();
        notice.Level.ShouldBe(NoticeLevel.Error);
        notice.Message.ShouldNotContain("Secret Project");
        logger.Lines.ShouldNotBeEmpty();
        logger.Lines.ShouldAllBe(line => !line.Contains("Secret Project"));
    }

    [Fact]
    public async Task ADropThatWorks_NoticesNothing()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<DropPathsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Unit.Value);
        var input = NewSession();

        await new DroppedFilesHandler(mediator.Object, input, new CapturingLogger()).HandleAsync(["C:\\x\\a.dll"]);

        input.GetSnapshot().Notices.ShouldBeEmpty();
    }
}

/// <summary>
/// A start-up exception before the try (the WebView2 runtime probe throws DllNotFoundException or COMException when the
/// loader is broken) escaped the async-void OnStartup to the dispatcher handler, which marks it handled: no window, no
/// message, no Shutdown, an invisible process that never exits.
/// </summary>
public class StartupGuardTests
{
    [Fact]
    public async Task ARuntimeProbeThatThrows_IsAStartFailure_NotAnEscape()
    {
        Exception? reported = null;
        var started = false;

        var result = await StartupState.StartAsync(
            runtimeInstalled: () => throw new DllNotFoundException("WebView2Loader.dll"),
            startHost: () =>
            {
                started = true;
                return Task.CompletedTask;
            },
            onFailure: ex => reported = ex);

        result.ShouldBe(StartupState.StartResult.Failed);
        reported.ShouldBeOfType<DllNotFoundException>();
        started.ShouldBeFalse();
    }

    [Fact]
    public async Task AMissingRuntime_IsReportedAsSuch_AndNothingStarts()
    {
        var started = false;

        var result = await StartupState.StartAsync(
            () => false, () =>
            {
                started = true;
                return Task.CompletedTask;
            }, _ => throw new InvalidOperationException("not a failure"));

        result.ShouldBe(StartupState.StartResult.RuntimeMissing);
        started.ShouldBeFalse();
    }

    [Fact]
    public async Task AHostThatFailsToStart_IsAStartFailure()
    {
        Exception? reported = null;

        var result = await StartupState.StartAsync(
            () => true, () => throw new InvalidOperationException("boom"), ex => reported = ex);

        result.ShouldBe(StartupState.StartResult.Failed);
        reported.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task AHealthyStart_IsStarted()
    {
        var result = await StartupState.StartAsync(() => true, () => Task.CompletedTask, _ => { });

        result.ShouldBe(StartupState.StartResult.Started);
    }
}

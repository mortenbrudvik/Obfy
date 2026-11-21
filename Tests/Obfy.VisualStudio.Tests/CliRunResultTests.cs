using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

public class CliRunResultTests
{
    private const string Input = @"C:\src\App\bin\Release\App.dll";
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(30);

    [Fact]
    public void Cancelled_IsReportedAsCancelled_NotAsAFailureOfTheObfuscation()
    {
        var run = new CliRunResult(CliRunStatus.Cancelled, -1, "partial output", string.Empty);

        var result = run.ToObfuscationResult(Input, Limit);

        result.Success.ShouldBeFalse();
        result.StopReason.ShouldBe(ObfuscationStopReason.Cancelled);
        result.ErrorMessage.ShouldBe("Obfuscation was cancelled.");
        result.OutputPath.ShouldBe(Input);
    }

    [Fact]
    public void TimedOut_IsReportedAsTimedOut_WithTheLimitAndWhereToChangeIt()
    {
        var run = new CliRunResult(CliRunStatus.TimedOut, -1, string.Empty, string.Empty);

        var result = run.ToObfuscationResult(Input, Limit);

        result.Success.ShouldBeFalse();
        result.StopReason.ShouldBe(ObfuscationStopReason.TimedOut);
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("timed out after 30 minutes");
        result.ErrorMessage.ShouldContain("Run Timeout (minutes)");
        result.ErrorMessage.ShouldNotBe(ObfuscationResult.CancelledMessage);
    }

    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(5, "5 minutes")]
    public void TimedOut_NamesTheLimitInMinutes(int minutes, string expected)
    {
        var result = ObfuscationResult.TimedOut(Input, TimeSpan.FromMinutes(minutes));

        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("timed out after " + expected + " ");
    }

    [Fact]
    public void TimedOut_NamesALimitUnderAMinuteInSeconds()
    {
        var result = ObfuscationResult.TimedOut(Input, TimeSpan.FromSeconds(30));

        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("timed out after 30 seconds ");
    }

    [Fact]
    public void Completed_WithExitCodeZero_IsASuccessWithStatistics()
    {
        var run = new CliRunResult(CliRunStatus.Completed, 0, "Strings encrypted: 4" + Environment.NewLine, string.Empty);

        var result = run.ToObfuscationResult(Input, Limit);

        result.Success.ShouldBeTrue();
        result.StopReason.ShouldBe(ObfuscationStopReason.None);
        result.Statistics.StringsEncrypted.ShouldBe(4);
    }

    [Fact]
    public void Completed_WithANonZeroExitCode_IsAFailureWithTheCliMessage_AndNotAStop()
    {
        var run = new CliRunResult(CliRunStatus.Completed, 1, "Error: boom" + Environment.NewLine, string.Empty);

        var result = run.ToObfuscationResult(Input, Limit);

        result.Success.ShouldBeFalse();
        result.StopReason.ShouldBe(ObfuscationStopReason.None);
        result.ErrorMessage.ShouldBe("boom");
    }

    [Fact]
    public void Cancelled_WhenTheProcessDidNotExit_SaysItMayStillBeRunning()
    {
        var run = new CliRunResult(CliRunStatus.Cancelled, -1, string.Empty, string.Empty, processStillRunning: true);

        var result = run.ToObfuscationResult(Input, Limit);

        result.StopReason.ShouldBe(ObfuscationStopReason.Cancelled);
        result.ErrorMessage.ShouldBe(ObfuscationResult.CancelledButStillRunningMessage);
        result.ErrorMessage.ShouldContain("may still be running");
    }

    [Fact]
    public void TimedOut_WhenTheProcessDidNotExit_DoesNotClaimItWasStopped()
    {
        var run = new CliRunResult(CliRunStatus.TimedOut, -1, string.Empty, string.Empty, processStillRunning: true);

        var result = run.ToObfuscationResult(Input, Limit);

        result.StopReason.ShouldBe(ObfuscationStopReason.TimedOut);
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("timed out after 30 minutes");
        result.ErrorMessage.ShouldContain("did not exit when it was stopped and may still be running");
        result.ErrorMessage.ShouldNotContain("the Obfy CLI was stopped");
        result.ErrorMessage.ShouldContain("Run Timeout (minutes)");
    }

    [Theory]
    [InlineData(true, false, CliRunStatus.Completed)]
    [InlineData(true, true, CliRunStatus.Completed)]
    [InlineData(false, true, CliRunStatus.Cancelled)]
    [InlineData(false, false, CliRunStatus.TimedOut)]
    public void Classify_ReportsAProcessThatFinishedByItselfAsCompleted(
        bool exitedByItself, bool callerCancelled, CliRunStatus expected)
    {
        // A stop requested in the very instant the process exited must not turn its result into a stop.
        CliProcessRunner.Classify(exitedByItself, callerCancelled).ShouldBe(expected);
    }

    [Fact]
    public void Failure_IsNotAStop()
    {
        ObfuscationResult.Failure(Input, "x").StopReason.ShouldBe(ObfuscationStopReason.None);
    }
}

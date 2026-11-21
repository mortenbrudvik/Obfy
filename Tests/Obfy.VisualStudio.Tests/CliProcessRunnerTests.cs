using System.Diagnostics;
using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

/// <summary>
/// Runs real short-lived child processes. The "long-running CLI" is a copy of ping.exe under a unique name, so the
/// tests can tell whether exactly that process (a grandchild of the runner, started through cmd.exe) is still alive.
/// </summary>
public class CliProcessRunnerTests : IDisposable
{
    // Far below the helper's own run time (about a minute), far above what a kill needs on a loaded machine.
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(20);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obfy-runner-" + Guid.NewGuid().ToString("N"));
    private readonly string _sleeperName = "obfy-sleeper-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private string SleeperPath => Path.Combine(_dir, _sleeperName + ".exe");

    public CliProcessRunnerTests()
    {
        Directory.CreateDirectory(_dir);
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), SleeperPath);
    }

    public void Dispose()
    {
        foreach (var leftover in Process.GetProcessesByName(_sleeperName))
        {
            try { leftover.Kill(); } catch { /* already gone */ }
            leftover.Dispose();
        }

        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [WindowsFact]
    public async Task NormalRun_ReportsExitCodeAndBothStreams()
    {
        var seenOutput = new List<string>();
        var seenError = new List<string>();

        var result = await Run(
            "echo hello& echo oops>&2& exit /b 3",
            TimeSpan.FromMinutes(1),
            seenOutput.Add,
            seenError.Add,
            CancellationToken.None);

        result.Status.ShouldBe(CliRunStatus.Completed);
        result.ExitCode.ShouldBe(3);
        result.Output.ShouldContain("hello");
        result.Error.ShouldContain("oops");
        seenOutput.ShouldContain("hello");
        seenError.ShouldContain("oops");
    }

    [WindowsFact]
    public async Task NormalRun_WithExitCodeZero_IsCompleted()
    {
        var result = await Run("echo done", TimeSpan.FromMinutes(1), null, null, CancellationToken.None);

        result.Status.ShouldBe(CliRunStatus.Completed);
        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("done");
    }

    [WindowsFact]
    public async Task Cancel_EndsALongRunningChildPromptly_AndReportsCancelled_KeepingTheOutputSoFar()
    {
        var sawLine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var run = Run(
            $"echo before-kill& \"{SleeperPath}\" -n 60 127.0.0.1 > nul",
            TimeSpan.FromMinutes(10),
            line =>
            {
                if (line.Contains("before-kill"))
                    sawLine.TrySetResult();
            },
            null,
            cts.Token);

        await sawLine.Task.WaitAsync(Generous);
        (await WaitUntil(() => SleeperCount() > 0)).ShouldBeTrue("the long-running helper child never started");

        var stopwatch = Stopwatch.StartNew();
        cts.Cancel();
        var result = await run.WaitAsync(Generous);
        stopwatch.Stop();

        result.Status.ShouldBe(CliRunStatus.Cancelled);
        result.Output.ShouldContain("before-kill");
        stopwatch.Elapsed.ShouldBeLessThan(Generous);

        // cmd.exe is the process the runner started; the helper is its child. The whole tree has to go.
        (await WaitUntil(() => SleeperCount() == 0)).ShouldBeTrue("the child of the killed process is still running");
    }

    [WindowsFact]
    public async Task Timeout_EndsALongRunningChildPromptly_AndReportsTimedOut()
    {
        var stopwatch = Stopwatch.StartNew();

        var result = await Run(
                $"\"{SleeperPath}\" -n 30 127.0.0.1 > nul",
                TimeSpan.FromSeconds(1),
                null,
                null,
                CancellationToken.None)
            .WaitAsync(Generous);
        stopwatch.Stop();

        result.Status.ShouldBe(CliRunStatus.TimedOut);
        stopwatch.Elapsed.ShouldBeLessThan(Generous);
    }

    [WindowsFact]
    public async Task AlreadyCancelledToken_DoesNotStartTheProcess()
    {
        var marker = Path.Combine(_dir, "started.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Run($"echo started> \"{marker}\"", TimeSpan.FromMinutes(1), null, null, cts.Token);

        result.Status.ShouldBe(CliRunStatus.Cancelled);
        File.Exists(marker).ShouldBeFalse();
    }

    [WindowsFact]
    public async Task CancelAfterTheRunHasFinished_IsHarmless()
    {
        using var cts = new CancellationTokenSource();

        var result = await Run("echo done", TimeSpan.FromMinutes(1), null, null, cts.Token);
        cts.Cancel();

        result.Status.ShouldBe(CliRunStatus.Completed);
    }

    [Fact]
    public void ResolveTimeout_UsesTheDefault_ForMissingOrNonPositiveValues()
    {
        var expected = TimeSpan.FromMinutes(CliProcessRunner.DefaultTimeoutMinutes);

        CliProcessRunner.ResolveTimeout(null).ShouldBe(expected);
        CliProcessRunner.ResolveTimeout(0).ShouldBe(expected);
        CliProcessRunner.ResolveTimeout(-5).ShouldBe(expected);
    }

    [Fact]
    public void ResolveTimeout_UsesTheConfiguredMinutes_UpToTheMaximum()
    {
        CliProcessRunner.ResolveTimeout(5).ShouldBe(TimeSpan.FromMinutes(5));
        CliProcessRunner.ResolveTimeout(int.MaxValue)
            .ShouldBe(TimeSpan.FromMinutes(CliProcessRunner.MaxTimeoutMinutes));
    }

    [Fact]
    public void DefaultTimeout_IsGenerousEnoughForLargeSolutions()
    {
        CliProcessRunner.DefaultTimeoutMinutes.ShouldBeGreaterThanOrEqualTo(10);
    }

    private static Task<CliRunResult> Run(
        string script,
        TimeSpan timeout,
        Action<string>? onOutput,
        Action<string>? onError,
        CancellationToken cancellationToken) =>
        CliProcessRunner.RunAsync("cmd.exe", "/d /c \"" + script + "\"", timeout, onOutput, onError, cancellationToken);

    private int SleeperCount()
    {
        var processes = Process.GetProcessesByName(_sleeperName);
        try
        {
            return processes.Length;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Generous;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }

        return condition();
    }
}

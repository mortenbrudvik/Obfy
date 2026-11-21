using System.Diagnostics;
using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

/// <summary>
/// Runs real short-lived child processes. The "CLI" is a renamed copy of cmd.exe, and the "long-running work" is a
/// renamed copy of ping.exe, both under unique names. That lets the tests tell exactly which processes (the root and
/// its grandchildren, started through the root) are still alive, and lets the cleanup kill them by name even when a
/// test failed half way and the runner never got to.
/// </summary>
public class CliProcessRunnerTests : IDisposable
{
    // Far below the helpers' own run time (about a minute), far above what a kill needs on a loaded machine.
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(20);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obfy-runner-" + Guid.NewGuid().ToString("N"));
    private readonly string _unique = Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly string _rootName;
    private readonly string _sleeperName;

    public CliProcessRunnerTests()
    {
        _rootName = "obfy-root-" + _unique;
        _sleeperName = "obfy-sleeper-" + _unique;
        Directory.CreateDirectory(_dir);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), RootPath);
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), SleeperPath);
    }

    private string RootPath => Path.Combine(_dir, _rootName + ".exe");
    private string SleeperPath => Path.Combine(_dir, _sleeperName + ".exe");

    public void Dispose()
    {
        // Root first, so a still-running loop cannot start more helpers while they are being killed.
        KillAllAndWait(_rootName);
        KillAllAndWait(_sleeperName);

        for (var attempt = 0; attempt < 50 && Directory.Exists(_dir); attempt++)
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void KillAllAndWait(string processName)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
                return;

            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
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
        (await WaitUntil(() => Count(_sleeperName) > 0)).ShouldBeTrue("the long-running helper child never started");

        var stopwatch = Stopwatch.StartNew();
        cts.Cancel();
        var result = await run.WaitAsync(Generous);
        stopwatch.Stop();

        result.Status.ShouldBe(CliRunStatus.Cancelled);
        result.Output.ShouldContain("before-kill");
        stopwatch.Elapsed.ShouldBeLessThan(Generous);

        // The root is the process the runner started; the helper is its child. The whole tree has to go.
        (await WaitUntil(() => Count(_sleeperName) == 0)).ShouldBeTrue("the child of the killed process is still running");
    }

    [WindowsFact]
    public async Task Timeout_EndsALongRunningChildPromptly_AndReportsTimedOut_KillingTheTree()
    {
        // The limit has to outlast the start of the helper, or there would be no child to find. Starting a
        // process takes milliseconds; four seconds is far beyond that even on a loaded machine.
        var stopwatch = Stopwatch.StartNew();
        var run = Run(
            $"\"{SleeperPath}\" -n 60 127.0.0.1 > nul",
            TimeSpan.FromSeconds(4),
            null,
            null,
            CancellationToken.None);

        (await WaitUntil(() => Count(_sleeperName) > 0)).ShouldBeTrue("the long-running helper child never started");
        var result = await run.WaitAsync(Generous);
        stopwatch.Stop();

        result.Status.ShouldBe(CliRunStatus.TimedOut);
        stopwatch.Elapsed.ShouldBeLessThan(Generous);
        (await WaitUntil(() => Count(_sleeperName) == 0)).ShouldBeTrue("the child of the timed-out process is still running");
    }

    [WindowsFact]
    public Task Cancel_WithAWideProcessTree_KillsEveryDescendantPromptly() =>
        CancelAWideTree(CliProcessRunner.ProcessTreeKill.JobObject);

    [WindowsFact]
    public Task Cancel_WithAWideProcessTree_KillsEveryDescendantPromptly_WhenOnlyTaskkillIsUsed() =>
        CancelAWideTree(CliProcessRunner.ProcessTreeKill.TaskkillOnly);

    private async Task CancelAWideTree(CliProcessRunner.ProcessTreeKill kill)
    {
        // taskkill prints about 85 bytes per killed process; with the pipe nobody reads, it blocks on the write once
        // roughly 45 processes are listed. Sixty children and the one the root waits for are well past that.
        const int children = 60;
        using var cts = new CancellationTokenSource();

        var run = Run(WideTreeScript(children), TimeSpan.FromMinutes(10), null, null, cts.Token, kill);

        (await WaitUntil(() => Count(_sleeperName) >= children + 1, TimeSpan.FromSeconds(60)))
            .ShouldBeTrue($"only {Count(_sleeperName)} of {children + 1} helpers started");

        var stopwatch = Stopwatch.StartNew();
        cts.Cancel();
        var result = await run.WaitAsync(Generous);
        stopwatch.Stop();

        result.Status.ShouldBe(CliRunStatus.Cancelled);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(8));
        (await WaitUntil(() => Count(_sleeperName) == 0))
            .ShouldBeTrue($"{Count(_sleeperName)} descendants of the killed process are still running");
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
    public async Task NormalRun_LeavesAHelperTheCliStartedRunning()
    {
        // A CLI may leave a helper behind on purpose (a build server). The job object must not kill it when the
        // run ends normally; before the job existed, nothing did. The helper inherits the CLI's pipes whatever it
        // redirects, so the run ends when the bounded wait for end of stream (5 s) elapses, not when the helper does.
        var result = await Run(
            $"start \"\" /b \"{SleeperPath}\" -n 60 127.0.0.1 > nul & echo started",
            TimeSpan.FromMinutes(1),
            null,
            null,
            CancellationToken.None);

        result.Status.ShouldBe(CliRunStatus.Completed);
        Count(_sleeperName).ShouldBe(1);
    }

    [WindowsFact]
    public async Task CancelAfterTheRunHasFinished_IsHarmless()
    {
        using var cts = new CancellationTokenSource();

        var result = await Run("echo done", TimeSpan.FromMinutes(1), null, null, cts.Token);
        cts.Cancel();

        result.Status.ShouldBe(CliRunStatus.Completed);
    }

    /// <summary>
    /// The root starts <paramref name="children"/> sleepers with <c>start /b</c> (so they are its children and share
    /// its stderr pipe), then waits for one more itself so it stays alive while they run.
    /// </summary>
    private string WideTreeScript(int children) =>
        $"(for /L %i in (1,1,{children}) do @start \"\" /b \"{SleeperPath}\" -n 60 127.0.0.1 > nul)" +
        $" & \"{SleeperPath}\" -n 60 127.0.0.1 > nul";

    private Task<CliRunResult> Run(
        string script,
        TimeSpan timeout,
        Action<string>? onOutput,
        Action<string>? onError,
        CancellationToken cancellationToken,
        CliProcessRunner.ProcessTreeKill kill = CliProcessRunner.ProcessTreeKill.JobObject) =>
        CliProcessRunner.RunAsync(
            RootPath, "/d /c \"" + script + "\"", timeout, onOutput, onError, cancellationToken, kill);

    private static int Count(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
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

    private static Task<bool> WaitUntil(Func<bool> condition) => WaitUntil(condition, Generous);

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }

        return condition();
    }
}

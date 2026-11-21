using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Obfy.VisualStudio.Services;

/// <summary>How a CLI run ended.</summary>
public enum CliRunStatus
{
    /// <summary>The process exited on its own; see <see cref="CliRunResult.ExitCode"/>.</summary>
    Completed,

    /// <summary>The caller's token was cancelled and the process (tree) was stopped.</summary>
    Cancelled,

    /// <summary>The time limit passed and the process (tree) was stopped.</summary>
    TimedOut
}

/// <summary>What a CLI run produced. <see cref="Output"/> and <see cref="Error"/> hold what was read before the run ended.</summary>
public sealed class CliRunResult
{
    public CliRunResult(CliRunStatus status, int exitCode, string output, string error)
    {
        Status = status;
        ExitCode = exitCode;
        Output = output;
        Error = error;
    }

    public CliRunStatus Status { get; }

    /// <summary>The exit code; only meaningful when <see cref="Status"/> is <see cref="CliRunStatus.Completed"/>.</summary>
    public int ExitCode { get; }

    public string Output { get; }
    public string Error { get; }

    /// <summary>
    /// Maps the run to the result the commands show. A cancelled or timed-out run is not reported as a failure
    /// of the obfuscation itself: <see cref="ObfuscationResult.StopReason"/> says why it ended.
    /// </summary>
    public ObfuscationResult ToObfuscationResult(string inputPath, TimeSpan timeout)
    {
        switch (Status)
        {
            case CliRunStatus.Cancelled:
                return ObfuscationResult.Cancelled(inputPath);
            case CliRunStatus.TimedOut:
                return ObfuscationResult.TimedOut(inputPath, timeout);
        }

        var result = new ObfuscationResult { Success = ExitCode == 0 };
        if (!result.Success)
        {
            // The CLI reports failures on stdout ("Error: ..."), so stderr alone is usually empty.
            result.ErrorMessage = CliArgumentBuilder.ExtractErrorMessage(Output, Error, ExitCode);
        }
        else
        {
            result.Statistics = CliArgumentBuilder.ParseStatistics(Output);
        }

        return result;
    }
}

/// <summary>
/// Runs the Obfy CLI as a child process and makes sure a run always ends: when the token is cancelled or the time
/// limit passes, the whole process tree is killed and the call returns promptly, whatever the CLI is doing.
/// </summary>
public static class CliProcessRunner
{
    /// <summary>
    /// How long one CLI run may take before it is stopped. Large solutions can legitimately take several minutes,
    /// so this is generous; Tools > Options > Obfy > General can change it.
    /// </summary>
    public const int DefaultTimeoutMinutes = 30;

    /// <summary>Upper bound for the configured limit (one day); <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> caps out near 24 days.</summary>
    public const int MaxTimeoutMinutes = 24 * 60;

    /// <summary>How long to wait for a killed process to be gone before giving up on it.</summary>
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long to wait for the output readers to reach end of stream after the process is gone. A grandchild that
    /// outlived it can keep the pipes open indefinitely; the run must not wait for that.
    /// </summary>
    private static readonly TimeSpan DrainWait = TimeSpan.FromSeconds(5);

    /// <summary>The configured limit in minutes, or the default for a missing or non-positive value.</summary>
    public static TimeSpan ResolveTimeout(int? minutes)
    {
        if (minutes is null or <= 0)
            return TimeSpan.FromMinutes(DefaultTimeoutMinutes);
        return TimeSpan.FromMinutes(Math.Min(minutes.Value, MaxTimeoutMinutes));
    }

    /// <summary>
    /// Starts <paramref name="fileName"/> and waits for it. Ends when the process exits, <paramref name="cancellationToken"/>
    /// is cancelled (<see cref="CliRunStatus.Cancelled"/>) or <paramref name="timeout"/> passes
    /// (<see cref="CliRunStatus.TimedOut"/>). A token that is already cancelled never starts the process.
    /// <paramref name="onOutput"/> and <paramref name="onError"/> get each non-empty line as it arrives, from a
    /// thread-pool thread, and never after this call has returned.
    /// </summary>
    public static async Task<CliRunResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        Action<string>? onOutput,
        Action<string>? onError,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return new CliRunResult(CliRunStatus.Cancelled, -1, string.Empty, string.Empty);

        var gate = new object();
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        var closed = false;

        var exited = NewSignal();
        var outputDone = NewSignal();
        var errorDone = NewSignal();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.Exited += (sender, e) => exited.TrySetResult(true);
        process.OutputDataReceived += (sender, e) => OnLine(e.Data, outputBuilder, onOutput, outputDone);
        process.ErrorDataReceived += (sender, e) => OnLine(e.Data, errorBuilder, onError, errorDone);

        void OnLine(string? line, StringBuilder builder, Action<string>? callback, TaskCompletionSource<bool> endOfStream)
        {
            // A null line is the end of the stream.
            if (line is null)
            {
                endOfStream.TrySetResult(true);
                return;
            }

            if (line.Length == 0)
                return;

            lock (gate)
            {
                if (closed)
                    return;
                builder.AppendLine(line);
            }

            callback?.Invoke(line);
        }

        // The limit and the caller's token end the run the same way. Cancelling only signals; the kill is done
        // below, off the thread that cancelled (which can be the UI thread when the solution closes).
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopRequested = NewSignal();

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (timeout != Timeout.InfiniteTimeSpan)
            stop.CancelAfter(timeout);

        using (stop.Token.Register(() => stopRequested.TrySetResult(true)))
        {
            await Task.WhenAny(exited.Task, stopRequested.Task).ConfigureAwait(false);
        }

        var status = CliRunStatus.Completed;
        if (!exited.Task.IsCompleted)
        {
            // A caller that cancelled and hit the limit in the same moment cancelled.
            status = cancellationToken.IsCancellationRequested ? CliRunStatus.Cancelled : CliRunStatus.TimedOut;

            await Task.Run(() => KillProcessTree(process)).ConfigureAwait(false);
            var gone = await Task.WhenAny(exited.Task, Task.Delay(StopWait)).ConfigureAwait(false);
            if (gone != exited.Task)
                onError?.Invoke("The Obfy CLI process did not exit after it was asked to stop.");
        }

        // Let the readers deliver the tail of the output, but never wait on them indefinitely.
        await Task.WhenAny(Task.WhenAll(outputDone.Task, errorDone.Task), Task.Delay(DrainWait)).ConfigureAwait(false);

        string output;
        string error;
        lock (gate)
        {
            closed = true;
            output = outputBuilder.ToString();
            error = errorBuilder.ToString();
        }

        var exitCode = status == CliRunStatus.Completed ? process.ExitCode : -1;
        return new CliRunResult(status, exitCode, output, error);
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Kills the process and everything it started. <see cref="Process.Kill()"/> alone leaves children running, and
    /// .NET Framework (the extension's runtime) has no overload that takes the tree, so taskkill does it. If taskkill
    /// cannot run, the process itself is still killed.
    /// </summary>
    private static void KillProcessTree(Process process)
    {
        int pid;
        try
        {
            if (process.HasExited)
                return;
            pid = process.Id;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            using var taskkill = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                Arguments = "/PID " + pid + " /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (taskkill != null && !taskkill.WaitForExit((int)StopWait.TotalMilliseconds))
                taskkill.Kill();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Fall through to killing the process itself.
        }

        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Already gone, or access denied; the wait for exit reports the latter.
        }
    }
}

using System.Diagnostics;
using Obfy.VisualStudio.Services;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

/// <summary>
/// The job object itself, against a renamed copy of ping.exe (one process that runs about a minute). The cleanup kills
/// whatever is left by name and waits, so a failing test cannot leave a process or its folder behind.
/// </summary>
public class ProcessJobTests : IDisposable
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(20);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obfy-job-" + Guid.NewGuid().ToString("N"));
    private readonly string _sleeperName = "obfy-jobsleeper-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    public ProcessJobTests()
    {
        Directory.CreateDirectory(_dir);
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), Path.Combine(_dir, _sleeperName + ".exe"));
    }

    public void Dispose()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var left = Process.GetProcessesByName(_sleeperName);
            foreach (var process in left)
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

            if (left.Length == 0)
                break;
        }

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

    [WindowsFact]
    public void LimitInformation_HasTheNativeSize()
    {
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION: 144 bytes on x64, 112 on x86. A wrong layout would make
        // SetInformationJobObject fail or set garbage limits.
        ProcessJob.LimitInformationSize.ShouldBe(IntPtr.Size == 8 ? 144 : 112);
    }

    [WindowsFact]
    public void Terminate_KillsTheProcess()
    {
        using var process = StartSleeper();
        using var job = ProcessJob.TryCreateFor(process);
        job.ShouldNotBeNull("the process could not be put in a job");

        job.Terminate().ShouldBeTrue();

        process.WaitForExit((int)Generous.TotalMilliseconds).ShouldBeTrue("the process survived TerminateJobObject");
    }

    [WindowsFact]
    public void ClosingTheJobKillsTheProcess_AsWhenVisualStudioCrashes()
    {
        using var process = StartSleeper();
        var job = ProcessJob.TryCreateFor(process);
        job.ShouldNotBeNull("the process could not be put in a job");

        // No Terminate: this is what Windows does to the handle when the owning process dies.
        job.Dispose();

        process.WaitForExit((int)Generous.TotalMilliseconds).ShouldBeTrue("the process survived the closing of its job");
    }

    [WindowsFact]
    public void ClosingADisarmedJob_LeavesTheProcessRunning()
    {
        using var process = StartSleeper();
        var job = ProcessJob.TryCreateFor(process);
        job.ShouldNotBeNull("the process could not be put in a job");

        job.Disarm().ShouldBeTrue();
        job.Dispose();

        // The cleanup in Dispose() kills it.
        process.WaitForExit(1000).ShouldBeFalse("a disarmed job must not kill the process when it is closed");
    }

    private Process StartSleeper()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(_dir, _sleeperName + ".exe"),
            Arguments = "-n 60 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        });
        process.ShouldNotBeNull();
        return process;
    }
}

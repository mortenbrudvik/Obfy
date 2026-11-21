using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Obfy.VisualStudio.Services;

/// <summary>
/// A Windows job object that holds a child process and everything it starts, so the whole tree can be killed in one
/// call (<see cref="Terminate"/>) however wide it is. It is created with "kill on job close": if the handle goes away
/// while the tree runs (this object is disposed, or Visual Studio itself crashes and Windows closes its handles) the
/// tree is killed too. <see cref="Disarm"/> turns that off for a run that ended normally, so a helper the CLI
/// meant to leave running is not killed by closing the handle.
/// </summary>
/// <remarks>
/// The process is assigned right after it has started, so a grandchild created in the first instants of the process
/// would not be in the job. Job objects nest on Windows 8 and later; where the assignment is refused,
/// <see cref="TryCreateFor"/> returns null and the caller falls back to killing the tree by other means.
/// </remarks>
internal sealed class ProcessJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private readonly SafeJobHandle _handle;

    private ProcessJob(SafeJobHandle handle)
    {
        _handle = handle;
    }

    /// <summary>Size of the native structure this class passes; pinned by a test (144 bytes on x64, 112 on x86).</summary>
    internal static int LimitInformationSize => Marshal.SizeOf(typeof(JobObjectExtendedLimitInfo));

    /// <summary>
    /// Puts <paramref name="process"/> in a new kill-on-close job. Null when that is not possible (not Windows, the
    /// process already ended, assignment refused); the process is then simply not in a job.
    /// </summary>
    public static ProcessJob? TryCreateFor(Process process)
    {
        SafeJobHandle? handle = null;
        try
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle.IsInvalid)
                return null;

            if (!SetLimitFlags(handle, JobObjectLimitKillOnJobClose))
                return DisposeAndReturnNull(handle);

            if (!AssignProcessToJobObject(handle, process.Handle))
                return DisposeAndReturnNull(handle);

            return new ProcessJob(handle);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            handle?.Dispose();
            return null;
        }
    }

    /// <summary>Kills every process in the job. False when the call failed.</summary>
    public bool Terminate() => TerminateJobObject(_handle, 1);

    /// <summary>Stops closing the handle from killing what is left in the job. False when the call failed.</summary>
    public bool Disarm() => SetLimitFlags(_handle, 0);

    public void Dispose() => _handle.Dispose();

    private static ProcessJob? DisposeAndReturnNull(SafeJobHandle handle)
    {
        handle.Dispose();
        return null;
    }

    private static bool SetLimitFlags(SafeJobHandle handle, uint flags)
    {
        var info = new JobObjectExtendedLimitInfo();
        info.BasicLimitInformation.LimitFlags = flags;
        return SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf(typeof(JobObjectExtendedLimitInfo)));
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        // Created by the marshaller for CreateJobObject.
        public SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInfo
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInfo
    {
        public JobObjectBasicLimitInfo BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeJobHandle job,
        int jobObjectInformationClass,
        ref JobObjectExtendedLimitInfo information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

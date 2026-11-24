using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Obfy.Runtime;

/// <summary>
/// In-memory payload loader invoked from the native host via <c>UnmanagedCallersOnly</c>.
/// Uses <see cref="AssemblyLoadContext.Default"/> (hostfxr component hosting; matches the
/// anti-tamper <c>LoadFromStream</c> skip). Command-line args come from
/// <see cref="Environment.GetCommandLineArgs"/>; the C <c>wmain</c> ignores argc/argv.
/// </summary>
public static class PackedBootstrap
{
    private static int _resolvingHooked;

    [UnmanagedCallersOnly(EntryPoint = "ObfyPackedRun")]
    public static int ObfyPackedRun(IntPtr payload, int length)
    {
        try
        {
            if (payload == IntPtr.Zero || length <= 0)
            {
                Console.Error.WriteLine("Packed host: invalid payload.");
                return 1;
            }

            var bytes = new byte[length];
            Marshal.Copy(payload, bytes, 0, length);
            // Overlay decryption has already produced the managed PE. Method IL is still XOR'd,
            // and LoadFromStream does not map a module the in-assembly decryptor can patch.
            MethodImageDecryptor.Decrypt(bytes);

            var alc = AssemblyLoadContext.Default;
            RegisterDefaultResolvers(alc);

            var assembly = alc.LoadFromStream(new MemoryStream(bytes));
            var entry = assembly.EntryPoint;
            if (entry == null)
            {
                Console.Error.WriteLine("Packed host: payload assembly has no entry point.");
                return 1;
            }

            // Do NOT set the entry assembly here. Assembly.GetEntryAssembly() stays null for a packed payload on
            // purpose: the anti-tamper check treats "no entry assembly and an empty Location" as a packed app and
            // skips; with an entry assembly set it would hash the native stub's process path instead and fail
            // every packed app that has AntiTamper enabled at startup.
            var all = Environment.GetCommandLineArgs();
            var args = all.Length <= 1 ? Array.Empty<string>() : all[1..];
            return InvokeEntryPoint(entry, args);
        }
        catch (TargetInvocationException ex)
        {
            Console.Error.WriteLine(ex.InnerException ?? ex);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Packed host failed: " + ex);
            return 1;
        }
    }

    internal static bool RegisterDefaultResolvers(AssemblyLoadContext alc)
    {
        if (Interlocked.Exchange(ref _resolvingHooked, 1) != 0)
            return false;

        alc.Resolving += ResolveSibling;
        alc.ResolvingUnmanagedDll += ResolveUnmanagedSibling;
        return true;
    }

    /// <summary>
    /// Runs the payload's entry point and maps its result to the process exit code the way the
    /// runtime's own app host does: an <c>int</c> / <c>Task&lt;int&gt;</c> is the code, and a
    /// <c>void</c> / <c>Task</c> Main exits with <see cref="Environment.ExitCode"/> (0 unless the
    /// payload set it). On Windows, entry points marked with <see cref="STAThreadAttribute"/> are
    /// dispatched to an STA thread when the current thread is not already STA.
    /// </summary>
    internal static int InvokeEntryPoint(MethodBase entry, string[] args)
    {
        if (OperatingSystem.IsWindows()
            && entry.GetCustomAttributes(typeof(STAThreadAttribute), false).Length > 0
            && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            var exitCode = 1;
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    exitCode = InvokeEntryPointCore(entry, args);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
            return exitCode;
        }

        return InvokeEntryPointCore(entry, args);
    }

    private static int InvokeEntryPointCore(MethodBase entry, string[] args)
    {
        object?[]? invokeArgs = entry.GetParameters().Length == 0 ? null : new object[] { args };
        var result = entry.Invoke(null, invokeArgs);

        if (result is Task<int> ti)
            return ti.GetAwaiter().GetResult();
        if (result is Task t)
        {
            t.GetAwaiter().GetResult();
            return Environment.ExitCode;
        }

        return result is int code ? code : Environment.ExitCode;
    }

    private static Assembly? ResolveSibling(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrEmpty(name.Name))
            return null;
        var fileName = name.Name + ".dll";
        var probe = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(probe) && !string.IsNullOrEmpty(name.CultureName))
            probe = Path.Combine(AppContext.BaseDirectory, name.CultureName, fileName);
        return File.Exists(probe) ? context.LoadFromAssemblyPath(probe) : null;
    }

    private static IntPtr ResolveUnmanagedSibling(Assembly requestingAssembly, string unmanagedDllName) =>
        ResolveUnmanagedFromBaseDirectory(AppContext.BaseDirectory, unmanagedDllName);

    internal static IntPtr ResolveUnmanagedFromBaseDirectory(string baseDirectory, string unmanagedDllName)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory)
            || string.IsNullOrWhiteSpace(unmanagedDllName)
            || Path.IsPathRooted(unmanagedDllName))
        {
            return IntPtr.Zero;
        }

        var handle = TryLoadUnmanagedFromDirectory(baseDirectory, unmanagedDllName);
        if (handle != IntPtr.Zero)
            return handle;

        return TryLoadUnmanagedFromDirectory(
            Path.Combine(baseDirectory, "runtimes", "win-x64", "native"),
            unmanagedDllName);
    }

    private static IntPtr TryLoadUnmanagedFromDirectory(string directory, string unmanagedDllName)
    {
        string fullDir;
        string fullCandidate;
        try
        {
            fullDir = Path.GetFullPath(directory);
            if (!fullDir.EndsWith(Path.DirectorySeparatorChar))
                fullDir += Path.DirectorySeparatorChar;

            fullCandidate = Path.GetFullPath(Path.Combine(fullDir, unmanagedDllName));
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException)
        {
            return IntPtr.Zero;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullCandidate.StartsWith(fullDir, comparison) || Path.EndsInDirectorySeparator(fullCandidate))
            return IntPtr.Zero;

        if (NativeLibrary.TryLoad(fullCandidate, out var handle))
            return handle;

        if (!unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            && NativeLibrary.TryLoad(fullCandidate + ".dll", out handle))
        {
            return handle;
        }

        return IntPtr.Zero;
    }
}

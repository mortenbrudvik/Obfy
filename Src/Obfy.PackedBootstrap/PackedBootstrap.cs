using System.Reflection;
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

            var alc = AssemblyLoadContext.Default;
            if (Interlocked.Exchange(ref _resolvingHooked, 1) == 0)
                alc.Resolving += ResolveSibling;

            var assembly = alc.LoadFromStream(new MemoryStream(bytes));
            var entry = assembly.EntryPoint;
            if (entry == null)
            {
                Console.Error.WriteLine("Packed host: payload assembly has no entry point.");
                return 1;
            }

            var all = Environment.GetCommandLineArgs();
            var args = all.Length <= 1 ? Array.Empty<string>() : all[1..];
            object?[]? invokeArgs = entry.GetParameters().Length == 0 ? null : new object[] { args };
            var result = entry.Invoke(null, invokeArgs);

            if (result is Task<int> ti)
                return ti.GetAwaiter().GetResult();
            if (result is Task t)
            {
                t.GetAwaiter().GetResult();
                return 0;
            }

            return result is int code ? code : 0;
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
}

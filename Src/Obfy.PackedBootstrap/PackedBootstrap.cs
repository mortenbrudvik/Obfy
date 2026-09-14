using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Obfy.Runtime;

public static class PackedBootstrap
{
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
            alc.Resolving += static (context, name) =>
            {
                if (string.IsNullOrEmpty(name.Name))
                    return null;
                var fileName = name.Name + ".dll";
                var probe = Path.Combine(AppContext.BaseDirectory, fileName);
                if (!File.Exists(probe) && !string.IsNullOrEmpty(name.CultureName))
                    probe = Path.Combine(AppContext.BaseDirectory, name.CultureName, fileName);
                return File.Exists(probe) ? context.LoadFromAssemblyPath(probe) : null;
            };

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
            Console.Error.WriteLine("Packed host failed: " + ex.Message);
            return 1;
        }
    }
}

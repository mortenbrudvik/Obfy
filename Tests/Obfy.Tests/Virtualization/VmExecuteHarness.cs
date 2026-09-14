using System.Reflection;
using System.Runtime.Loader;
using dnlib.DotNet;

namespace Obfy.Tests.Virtualization;

/// <summary>
/// Writes a prepared module and invokes a method from a collectible load context.
/// </summary>
public static class VmExecuteHarness
{
    public static object Invoke(ModuleDef module, string typeName, string methodName, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(methodName);

        var dir = Path.Combine(Path.GetTempPath(), "obfy-vm-exec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "exec.dll");
        var alc = new AssemblyLoadContext("vm-exec-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            module.Write(path);
            var assembly = alc.LoadFromAssemblyPath(path);
            var type = assembly.GetType(typeName)
                ?? throw new InvalidOperationException($"Type '{typeName}' was not found.");
            var method = type.GetMethod(methodName)
                ?? throw new InvalidOperationException($"Method '{typeName}.{methodName}' was not found.");
            try
            {
                return method.Invoke(null, args)!;
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }
        finally
        {
            alc.Unload();
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}

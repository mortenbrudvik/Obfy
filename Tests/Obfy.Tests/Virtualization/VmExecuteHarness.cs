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
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(methodName);
        return Invoke(module, assembly =>
        {
            var type = GetType(assembly, typeName);
            var method = type.GetMethod(methodName)
                ?? throw new InvalidOperationException($"Method '{typeName}.{methodName}' was not found.");
            return method.Invoke(null, args);
        });
    }

    public static object Invoke(ModuleDef module, Func<Assembly, object?> invoke)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(invoke);

        var dir = Path.Combine(Path.GetTempPath(), "obfy-vm-exec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "exec.dll");
        var alc = new AssemblyLoadContext("vm-exec-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            module.Write(path);
            var assembly = alc.LoadFromAssemblyPath(path);
            try
            {
                return invoke(assembly)!;
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

    public static object CreateInstance(Assembly assembly, string typeName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(typeName);
        var type = GetType(assembly, typeName);
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Failed to create instance of '{typeName}'.");
    }

    public static Type GetType(Assembly assembly, string typeName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(typeName);
        return assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"Type '{typeName}' was not found.");
    }
}

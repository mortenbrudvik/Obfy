using System;
using System.Reflection;

namespace Obfy.Runtime;

public static class Vm
{
    static byte[] _code = Array.Empty<byte>();
    static int[] _starts = Array.Empty<int>();
    static byte[] _opMap = Array.Empty<byte>();
    static byte[] _xorKey = Array.Empty<byte>();
    static MethodBase[] _methods = Array.Empty<MethodBase>();
    static FieldInfo[] _fields = Array.Empty<FieldInfo>();
    static Type[] _types = Array.Empty<Type>();
    static Type[] _returnTypes = Array.Empty<Type>();

    public static object Run(int id, object[] args)
    {
        throw new NotSupportedException("Obfy VM: interpreter not implemented");
    }

    internal static void Init(
        byte[] code,
        int[] starts,
        byte[] opMap,
        byte[] xorKey,
        MethodBase[] methods,
        FieldInfo[] fields,
        Type[] types,
        Type[] returnTypes)
    {
        _code = code ?? throw new ArgumentNullException(nameof(code));
        _starts = starts ?? throw new ArgumentNullException(nameof(starts));
        _opMap = opMap ?? throw new ArgumentNullException(nameof(opMap));
        _xorKey = xorKey ?? throw new ArgumentNullException(nameof(xorKey));
        _methods = methods ?? throw new ArgumentNullException(nameof(methods));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _returnTypes = returnTypes ?? throw new ArgumentNullException(nameof(returnTypes));
    }
}

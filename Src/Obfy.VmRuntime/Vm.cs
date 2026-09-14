using System;
using System.Reflection;

namespace Obfy.Runtime;

public static class Vm
{
    static byte[] _code;
    static int[] _starts;
    static byte[] _opMap;
    static byte[] _xorKey;
    static MethodBase[] _methods;
    static FieldInfo[] _fields;
    static Type[] _types;

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
        Type[] types)
    {
        _code = code ?? throw new ArgumentNullException(nameof(code));
        _starts = starts ?? throw new ArgumentNullException(nameof(starts));
        _opMap = opMap ?? throw new ArgumentNullException(nameof(opMap));
        _xorKey = xorKey ?? throw new ArgumentNullException(nameof(xorKey));
        _methods = methods ?? throw new ArgumentNullException(nameof(methods));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _types = types ?? throw new ArgumentNullException(nameof(types));
    }
}

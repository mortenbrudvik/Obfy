using dnlib.DotNet;

namespace Obfy.Core.Virtualization;

/// <summary>
/// Interned member tokens written into VM bytecode as u16 indices.
/// </summary>
public sealed class VmMemberTables
{
    private readonly Dictionary<string, ushort> _methodKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ushort> _fieldKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ushort> _typeKeys = new(StringComparer.Ordinal);

    public List<IMethod> Methods { get; } = new();
    public List<IField> Fields { get; } = new();
    public List<ITypeDefOrRef> Types { get; } = new();

    public ushort AddMethod(IMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Add(Methods, _methodKeys, method, method.MDToken, method.FullName);
    }

    public ushort AddField(IField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Add(Fields, _fieldKeys, field, field.MDToken, field.FullName);
    }

    public ushort AddType(ITypeDefOrRef type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Add(Types, _typeKeys, type, type.MDToken, type.FullName);
    }

    internal void Rollback(int methodCount, int fieldCount, int typeCount)
    {
        Truncate(Methods, _methodKeys, methodCount);
        Truncate(Fields, _fieldKeys, fieldCount);
        Truncate(Types, _typeKeys, typeCount);
    }

    private static ushort Add<T>(
        List<T> list,
        Dictionary<string, ushort> keys,
        T item,
        MDToken token,
        string fullName)
    {
        if (token.Rid != 0 && keys.TryGetValue(TokenKey(token), out var id))
            return id;
        if (keys.TryGetValue(NameKey(fullName), out id))
            return id;
        if (list.Count > ushort.MaxValue)
            throw new InvalidOperationException("VM member table exceeds 65535 entries.");

        id = (ushort)list.Count;
        list.Add(item);
        if (token.Rid != 0)
            keys[TokenKey(token)] = id;
        keys[NameKey(fullName)] = id;
        return id;
    }

    private static void Truncate<T>(List<T> list, Dictionary<string, ushort> keys, int count)
    {
        if (list.Count > count)
            list.RemoveRange(count, list.Count - count);

        if (keys.Count == 0)
            return;

        var stale = new List<string>();
        foreach (var pair in keys)
        {
            if (pair.Value >= count)
                stale.Add(pair.Key);
        }

        foreach (var key in stale)
            keys.Remove(key);
    }

    private static string TokenKey(MDToken token) => "t:" + token.Raw.ToString("X8");

    private static string NameKey(string fullName) => "n:" + fullName;
}

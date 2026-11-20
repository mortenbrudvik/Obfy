using Obfy.Core.Models;

namespace Obfy.App.Sessions;

/// <summary>
/// One editable option of <see cref="ObfySettings"/>: its patch key, whether a level preset governs it, and how to read,
/// convert and write it. The session's only list of options: the patch applier, the snapshot and the "differs from level"
/// check all walk <see cref="SettingFields.All"/>.
/// </summary>
internal abstract class SettingField
{
    /// <summary>The technique or group the option belongs to; empty for a top-level option.</summary>
    public required string Group { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// True when a level preset never changes the option, so editing it keeps the level and a Custom restore leaves it alone.
    /// Packing, incremental cache and virtualization are governed: the engine re-applies the level on every run and turns
    /// them off for any non-Custom level, so enabling one must make the level Custom for the engine to honour it.
    /// </summary>
    public required bool LevelIndependent { get; init; }

    public string Key => Group.Length == 0 ? Name : $"{Group}.{Name}";

    /// <summary>The value as the page sees it: bool, int, string or a string list.</summary>
    public abstract object? Read(ObfySettings settings);

    /// <summary>Writes a value from a patch. Returns whether the stored value changed.</summary>
    /// <exception cref="ObfyApiException">The value has the wrong type or is not one of the allowed names.</exception>
    public abstract bool Write(ObfySettings settings, object? raw);

    public abstract bool SameValue(ObfySettings a, ObfySettings b);
}

internal sealed class SettingField<T>(
    Func<ObfySettings, T> get,
    Action<ObfySettings, T> set,
    Func<SettingField, object?, T> convert,
    Func<T, object?> present,
    IEqualityComparer<T> comparer) : SettingField
{
    public override object? Read(ObfySettings settings) => present(get(settings));

    public override bool Write(ObfySettings settings, object? raw)
    {
        var value = convert(this, raw);
        if (comparer.Equals(get(settings), value))
            return false;
        set(settings, value);
        return true;
    }

    public override bool SameValue(ObfySettings a, ObfySettings b) => comparer.Equals(get(a), get(b));
}

internal sealed class StringListComparer : IEqualityComparer<List<string>>
{
    public static readonly StringListComparer Instance = new();

    public bool Equals(List<string>? x, List<string>? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y, StringComparer.Ordinal));

    public int GetHashCode(List<string> obj) => obj.Count;
}

internal static class SettingFields
{
    public static IReadOnlyList<SettingField> All { get; } = Build();

    public static IReadOnlyDictionary<string, SettingField> ByKey { get; } = All.ToDictionary(f => f.Key, StringComparer.Ordinal);

    private static SettingField Bool(string group, string name, bool independent, Func<ObfySettings, bool> get, Action<ObfySettings, bool> set) =>
        new SettingField<bool>(get, set, (f, raw) => raw is bool b ? b : throw Fail(f, "expected true or false"), v => v, EqualityComparer<bool>.Default)
        { Group = group, Name = name, LevelIndependent = independent };

    private static SettingField Int(string group, string name, bool independent, int min, int max, Func<ObfySettings, int> get, Action<ObfySettings, int> set) =>
        new SettingField<int>(get, set, (f, raw) => Math.Clamp(ToInt(f, raw), min, max), v => v, EqualityComparer<int>.Default)
        { Group = group, Name = name, LevelIndependent = independent };

    /// <summary>A string option. Null reads and writes as empty; <paramref name="set"/> decides what empty means.</summary>
    private static SettingField Text(string group, string name, bool independent, Func<ObfySettings, string> get, Action<ObfySettings, string> set) =>
        new SettingField<string>(get, set, (f, raw) => raw switch { null => "", string s => s, _ => throw Fail(f, "expected text") }, v => v, StringComparer.Ordinal)
        { Group = group, Name = name, LevelIndependent = independent };

    private static SettingField Choice<T>(string group, string name, bool independent, Func<ObfySettings, T> get, Action<ObfySettings, T> set)
        where T : struct, Enum =>
        new SettingField<T>(get, set, (f, raw) => ToEnum<T>(f, raw), v => v.ToString(), EqualityComparer<T>.Default)
        { Group = group, Name = name, LevelIndependent = independent };

    /// <summary>A pattern list. Entries are trimmed, blanks and duplicates dropped, the rest kept in order.</summary>
    private static SettingField List(string group, string name, bool independent, Func<ObfySettings, List<string>> get, Action<ObfySettings, List<string>> set) =>
        new SettingField<List<string>>(
            s => get(s) ?? [], set, (f, raw) => ToList(f, raw), v => v.ToArray(), StringListComparer.Instance)
        { Group = group, Name = name, LevelIndependent = independent };

    private static ObfyApiException Fail(SettingField field, string reason) => new("settings.invalidValue", $"{field.Key}: {reason}");

    private static int ToInt(SettingField field, object? raw) => raw switch
    {
        int i => i,
        long l => (int)Math.Clamp(l, int.MinValue, int.MaxValue),
        short s => s,
        double d when d == Math.Floor(d) && Math.Abs(d) < int.MaxValue => (int)d,
        _ => throw Fail(field, "expected a whole number")
    };

    private static T ToEnum<T>(SettingField field, object? raw) where T : struct, Enum
    {
        if (raw is T value)
            return value;
        if (raw is string text)
        {
            var name = Enum.GetNames<T>().FirstOrDefault(n => n.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is not null)
                return Enum.Parse<T>(name);
        }

        throw Fail(field, $"expected one of {string.Join(", ", Enum.GetNames<T>())}");
    }

    private static List<string> ToList(SettingField field, object? raw)
    {
        if (raw is null)
            return [];
        if (raw is string or not System.Collections.IEnumerable)
            throw Fail(field, "expected a list of text");

        var result = new List<string>();
        foreach (var item in (System.Collections.IEnumerable)raw)
        {
            if (item is not string text)
                throw Fail(field, "expected a list of text");
            var trimmed = text.Trim();
            if (trimmed.Length > 0 && !result.Contains(trimmed, StringComparer.Ordinal))
                result.Add(trimmed);
        }

        return result;
    }

    private static List<SettingField> Build() =>
    [
        // Options below are governed by level presets (editing one of them makes the level Custom) unless marked independent.
        Bool("stringEncryption", "enabled", false, s => s.StringEncryption.Enabled, (s, v) => s.StringEncryption.Enabled = v),
        Choice("stringEncryption", "algorithm", false, s => s.StringEncryption.Algorithm, (s, v) => s.StringEncryption.Algorithm = v),
        Int("stringEncryption", "minStringLength", false, 1, 100, s => s.StringEncryption.MinStringLength, (s, v) => s.StringEncryption.MinStringLength = v),

        Bool("constantEncryption", "enabled", false, s => s.ConstantEncryption.Enabled, (s, v) => s.ConstantEncryption.Enabled = v),
        Choice("constantEncryption", "algorithm", false, s => s.ConstantEncryption.Algorithm, (s, v) => s.ConstantEncryption.Algorithm = v),
        Bool("constantEncryption", "encryptIntegers", false, s => s.ConstantEncryption.EncryptIntegers, (s, v) => s.ConstantEncryption.EncryptIntegers = v),
        Bool("constantEncryption", "encryptLongs", false, s => s.ConstantEncryption.EncryptLongs, (s, v) => s.ConstantEncryption.EncryptLongs = v),
        Bool("constantEncryption", "encryptFloats", false, s => s.ConstantEncryption.EncryptFloats, (s, v) => s.ConstantEncryption.EncryptFloats = v),
        Bool("constantEncryption", "encryptDoubles", false, s => s.ConstantEncryption.EncryptDoubles, (s, v) => s.ConstantEncryption.EncryptDoubles = v),
        Int("constantEncryption", "integerThreshold", false, 0, 1000, s => s.ConstantEncryption.IntegerThreshold, (s, v) => s.ConstantEncryption.IntegerThreshold = v),
        Bool("constantEncryption", "skipCommonFloats", false, s => s.ConstantEncryption.SkipCommonFloats, (s, v) => s.ConstantEncryption.SkipCommonFloats = v),
        Bool("constantEncryption", "skipCommonDoubles", false, s => s.ConstantEncryption.SkipCommonDoubles, (s, v) => s.ConstantEncryption.SkipCommonDoubles = v),

        Bool("resourceEncryption", "enabled", false, s => s.ResourceEncryption.Enabled, (s, v) => s.ResourceEncryption.Enabled = v),
        Choice("resourceEncryption", "algorithm", false, s => s.ResourceEncryption.Algorithm, (s, v) => s.ResourceEncryption.Algorithm = v),

        Bool("controlFlow", "enabled", false, s => s.ControlFlow.Enabled, (s, v) => s.ControlFlow.Enabled = v),
        Choice("controlFlow", "mode", false, s => s.ControlFlow.Mode, (s, v) => s.ControlFlow.Mode = v),
        Int("controlFlow", "intensity", false, 0, 100, s => s.ControlFlow.Intensity, (s, v) => s.ControlFlow.Intensity = v),

        Bool("symbolRenaming", "enabled", false, s => s.SymbolRenaming.Enabled, (s, v) => s.SymbolRenaming.Enabled = v),
        Choice("symbolRenaming", "mode", false, s => s.SymbolRenaming.Mode, (s, v) => s.SymbolRenaming.Mode = v),
        Bool("symbolRenaming", "renameTypes", false, s => s.SymbolRenaming.RenameTypes, (s, v) => s.SymbolRenaming.RenameTypes = v),
        Bool("symbolRenaming", "renameMethods", false, s => s.SymbolRenaming.RenameMethods, (s, v) => s.SymbolRenaming.RenameMethods = v),
        Bool("symbolRenaming", "renameFields", false, s => s.SymbolRenaming.RenameFields, (s, v) => s.SymbolRenaming.RenameFields = v),
        Bool("symbolRenaming", "renameProperties", false, s => s.SymbolRenaming.RenameProperties, (s, v) => s.SymbolRenaming.RenameProperties = v),
        Bool("symbolRenaming", "renameParameters", false, s => s.SymbolRenaming.RenameParameters, (s, v) => s.SymbolRenaming.RenameParameters = v),
        Bool("symbolRenaming", "renameEvents", false, s => s.SymbolRenaming.RenameEvents, (s, v) => s.SymbolRenaming.RenameEvents = v),
        Bool("symbolRenaming", "renameNamespaces", false, s => s.SymbolRenaming.RenameNamespaces, (s, v) => s.SymbolRenaming.RenameNamespaces = v),
        Bool("symbolRenaming", "preservePublicApi", false, s => s.SymbolRenaming.PreservePublicApi, (s, v) => s.SymbolRenaming.PreservePublicApi = v),
        Bool("symbolRenaming", "preserveXaml", false, s => s.SymbolRenaming.PreserveXaml, (s, v) => s.SymbolRenaming.PreserveXaml = v),

        Bool("antiDebug", "enabled", false, s => s.Protection.AntiDebug, (s, v) => s.Protection.AntiDebug = v),

        Bool("antiTamper", "enabled", false, s => s.Protection.AntiTamper.Enabled, (s, v) => s.Protection.AntiTamper.Enabled = v),
        Bool("antiTamper", "checkEntryPoint", false, s => s.Protection.AntiTamper.CheckEntryPoint, (s, v) => s.Protection.AntiTamper.CheckEntryPoint = v),
        Bool("antiTamper", "checkModuleInitializer", false, s => s.Protection.AntiTamper.CheckModuleInitializer, (s, v) => s.Protection.AntiTamper.CheckModuleInitializer = v),

        Bool("antiDump", "enabled", false, s => s.Protection.AntiDump, (s, v) => s.Protection.AntiDump = v),

        Bool("antiDecompiler", "enabled", false, s => s.Protection.AntiDecompiler.Enabled, (s, v) => s.Protection.AntiDecompiler.Enabled = v),
        Bool("antiDecompiler", "injectJunkTypes", false, s => s.Protection.AntiDecompiler.InjectJunkTypes, (s, v) => s.Protection.AntiDecompiler.InjectJunkTypes = v),
        Bool("antiDecompiler", "addSuppressIldasmAttribute", false, s => s.Protection.AntiDecompiler.AddSuppressIldasmAttribute, (s, v) => s.Protection.AntiDecompiler.AddSuppressIldasmAttribute = v),
        Bool("antiDecompiler", "addDecoyAttributes", false, s => s.Protection.AntiDecompiler.AddDecoyAttributes, (s, v) => s.Protection.AntiDecompiler.AddDecoyAttributes = v),
        Int("antiDecompiler", "junkTypeCount", false, 1, 50, s => s.Protection.AntiDecompiler.JunkTypeCount, (s, v) => s.Protection.AntiDecompiler.JunkTypeCount = v),
        Int("antiDecompiler", "junkMethodsPerType", false, 1, 20, s => s.Protection.AntiDecompiler.JunkMethodsPerType, (s, v) => s.Protection.AntiDecompiler.JunkMethodsPerType = v),

        Bool("referenceProxy", "enabled", false, s => s.Protection.ReferenceProxy, (s, v) => s.Protection.ReferenceProxy = v),
        Bool("referenceProxy", "proxyExternalCalls", false, s => s.Protection.ProxyExternalCalls, (s, v) => s.Protection.ProxyExternalCalls = v),

        Bool("methodEncryption", "enabled", false, s => s.Protection.MethodEncryption, (s, v) => s.Protection.MethodEncryption = v),

        Bool("virtualization", "enabled", false, s => s.Virtualization.Enabled, (s, v) => s.Virtualization.Enabled = v),

        Bool("metadataRemoval", "removeDebugInfo", false, s => s.Metadata.RemoveDebugInfo, (s, v) => s.Metadata.RemoveDebugInfo = v),
        Bool("metadataRemoval", "removeAttributes", false, s => s.Metadata.RemoveAttributes, (s, v) => s.Metadata.RemoveAttributes = v),
        Bool("metadataRemoval", "stripDocumentation", false, s => s.Metadata.StripDocumentation, (s, v) => s.Metadata.StripDocumentation = v),

        // Independent of the level: presets do not own these, so editing them never makes the level Custom.
        // (packing.enabled and incremental.enabled below are the exception: governed, see SettingField.LevelIndependent.)
        Bool("dependencyEmbedding", "enabled", true, s => s.DependencyEmbedding.Enabled, (s, v) => s.DependencyEmbedding.Enabled = v),
        List("dependencyEmbedding", "includePatterns", true, s => s.DependencyEmbedding.IncludePatterns, (s, v) => s.DependencyEmbedding.IncludePatterns = v),
        List("dependencyEmbedding", "excludePatterns", true, s => s.DependencyEmbedding.ExcludePatterns, (s, v) => s.DependencyEmbedding.ExcludePatterns = v),

        Bool("assemblyMerge", "enabled", true, s => s.AssemblyMerge.Enabled, (s, v) => s.AssemblyMerge.Enabled = v),
        Bool("assemblyMerge", "internalize", true, s => s.AssemblyMerge.Internalize, (s, v) => s.AssemblyMerge.Internalize = v),

        Bool("watermark", "enabled", true, s => s.Watermark.Enabled, (s, v) => s.Watermark.Enabled = v),
        Text("watermark", "id", true, s => s.Watermark.Id ?? "", (s, v) => s.Watermark.Id = v),

        Bool("signing", "enabled", true, s => s.Signing.Enabled, (s, v) => s.Signing.Enabled = v),
        Text("signing", "keyFile", true, s => s.Signing.KeyFile ?? "", (s, v) => s.Signing.KeyFile = string.IsNullOrWhiteSpace(v) ? null : v),
        Text("signing", "passwordEnvironmentVariable", true, s => s.Signing.PasswordEnvironmentVariable ?? "",
            (s, v) => s.Signing.PasswordEnvironmentVariable = string.IsNullOrWhiteSpace(v) ? null : v),

        Bool("packing", "enabled", false, s => s.Packing.Enabled, (s, v) => s.Packing.Enabled = v),
        Text("packing", "rid", true, s => string.IsNullOrWhiteSpace(s.Packing.Rid) ? "win-x64" : s.Packing.Rid,
            (s, v) => s.Packing.Rid = string.IsNullOrWhiteSpace(v) ? "win-x64" : v.Trim()),

        Bool("incremental", "enabled", false, s => s.Incremental.Enabled, (s, v) => s.Incremental.Enabled = v),

        Choice("", "runtimeProfile", true, s => s.RuntimeProfile, (s, v) => s.RuntimeProfile = v),

        List("exclusions", "namespaces", true, s => s.Exclusions.Namespaces, (s, v) => s.Exclusions.Namespaces = v),
        List("exclusions", "types", true, s => s.Exclusions.Types, (s, v) => s.Exclusions.Types = v),
        List("exclusions", "methods", true, s => s.Exclusions.Methods, (s, v) => s.Exclusions.Methods = v),

        List("inclusions", "namespaces", true, s => s.Inclusions.Namespaces, (s, v) => s.Inclusions.Namespaces = v),
        List("inclusions", "types", true, s => s.Inclusions.Types, (s, v) => s.Inclusions.Types = v),
        List("inclusions", "methods", true, s => s.Inclusions.Methods, (s, v) => s.Inclusions.Methods = v),
    ];
}

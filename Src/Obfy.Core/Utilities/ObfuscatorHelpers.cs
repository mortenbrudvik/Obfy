using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Shared helpers for assembly obfuscators (exclusions, compiler-generated detection, IL emit, pinned attribute names).
/// </summary>
public static class ObfuscatorHelpers
{
    public static bool MatchesPattern(string? value, string pattern) => WildcardMatcher.IsMatch(value, pattern);

    /// <summary>
    /// Matches an attribute type against an exclusion pattern.
    /// Accepts short names (<c>SerializableAttribute</c>), names without the Attribute suffix,
    /// and full names (<c>System.SerializableAttribute</c>).
    /// </summary>
    public static bool MatchesAttribute(string typeFullName, string pattern)
    {
        if (string.IsNullOrEmpty(typeFullName) || string.IsNullOrEmpty(pattern))
            return false;

        if (MatchesPattern(typeFullName, pattern))
            return true;

        var shortName = typeFullName;
        var lastDot = typeFullName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < typeFullName.Length - 1)
            shortName = typeFullName[(lastDot + 1)..];

        if (MatchesPattern(shortName, pattern))
            return true;

        if (!pattern.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase) &&
            MatchesPattern(shortName, pattern + "Attribute"))
            return true;

        if (pattern.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
        {
            var withoutSuffix = pattern[..^"Attribute".Length];
            if (MatchesPattern(shortName, withoutSuffix))
                return true;
        }

        return false;
    }

    public static bool HasExcludedAttribute(IHasCustomAttribute provider, ExclusionRules exclusions)
    {
        if (exclusions.Attributes.Count == 0 || !provider.HasCustomAttributes)
            return false;

        foreach (var attr in provider.CustomAttributes)
        {
            var fullName = attr.TypeFullName;
            if (exclusions.Attributes.Any(pattern => MatchesAttribute(fullName, pattern)))
                return true;
        }

        return false;
    }

    public static bool IsRuntimeHelper(TypeDef type)
    {
        var ns = type.Namespace.String;
        return ns == "Obfy.Runtime" || ns.StartsWith("Obfy.Runtime.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Detector/lookup attribute names that must survive symbol renaming.
    /// Watermark lives in <c>Obfy.Runtime</c>; decoys are injected in the global namespace.
    /// </summary>
    public static class PinnedAttributeNames
    {
        public const string Watermark = "WatermarkAttribute";
        public const string WatermarkNamespace = "Obfy.Runtime";
        public const string ConfusedBy = "ConfusedByAttribute";
        public const string Dotfuscator = "DotfuscatorAttribute";
    }

    /// <summary>
    /// Attribute types whose names are a public contract (de4dot decoys, watermark lookup).
    /// Symbol renaming must leave the type name, members, and namespace intact.
    /// Matched by full identity: global <c>ConfusedByAttribute</c>/<c>DotfuscatorAttribute</c>,
    /// and <c>Obfy.Runtime.WatermarkAttribute</c>. A user type with the same simple name in
    /// another namespace is not pinned.
    /// </summary>
    public static bool IsPinnedAttributeType(TypeDef type)
    {
        var name = type.Name.String;
        var ns = type.Namespace.String;
        if (name is PinnedAttributeNames.ConfusedBy or PinnedAttributeNames.Dotfuscator)
            return ns.Length == 0;

        return name == PinnedAttributeNames.Watermark &&
               ns == PinnedAttributeNames.WatermarkNamespace;
    }

    public static bool IsRuntimeOrExcluded(TypeDef type, ExclusionRules exclusions)
    {
        if (IsRuntimeHelper(type) || type.Namespace == "Obfy.Core.Models")
            return true;

        if (exclusions.Namespaces.Any(n => MatchesPattern(type.Namespace, n)))
            return true;

        if (exclusions.Types.Any(t => MatchesPattern(type.Name, t)))
            return true;

        return HasExcludedAttribute(type, exclusions);
    }

    public static bool IsExcluded(TypeDef type, ExclusionRules exclusions)
    {
        if (type.Namespace == "Obfy.Core.Models")
            return true;

        if (exclusions.Namespaces.Any(n => MatchesPattern(type.Namespace, n)))
            return true;

        if (exclusions.Types.Any(t => MatchesPattern(type.Name, t)))
            return true;

        return HasExcludedAttribute(type, exclusions);
    }

    public static bool MethodMatchesExclusion(MethodDef method, ExclusionRules exclusions) =>
        exclusions.Methods.Any(m => MatchesPattern(method.Name, m));

    public static bool IsComVisibleTrue(IHasCustomAttribute provider)
    {
        if (!provider.HasCustomAttributes)
            return false;

        foreach (var attr in provider.CustomAttributes)
        {
            if (!attr.TypeFullName.EndsWith("ComVisibleAttribute", StringComparison.Ordinal))
                continue;
            if (attr.ConstructorArguments.Count == 0)
                continue;
            if (attr.ConstructorArguments[0].Value is bool visible && visible)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Heuristic for XAML-bindable types: name ends with ViewModel or View (ordinal-ignore-case,
    /// so Overview/Preview and mixed-case suffixes still match), an interface name contains INotifyPropertyChanged, a field type is
    /// DependencyProperty, or a resolvable base name contains DependencyObject. Framework WPF
    /// bases often fail <c>ResolveTypeDef()</c> and fall through to the suffix checks.
    /// </summary>
    public static bool LooksLikeXamlBindable(TypeDef type)
    {
        var name = type.Name.String;
        if (name.EndsWith("ViewModel", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("View", StringComparison.OrdinalIgnoreCase))
            return true;

        if (type.Interfaces.Any(i => i.Interface.Name.Contains("INotifyPropertyChanged")))
            return true;

        for (var current = type; current != null; current = current.BaseType?.ResolveTypeDef())
        {
            if (current.Name.Contains("DependencyObject"))
                return true;
            if (current.Fields.Any(f => f.FieldType?.TypeName == "DependencyProperty"))
                return true;
        }

        return false;
    }

    public static bool IsPrefix(Instruction instruction) =>
        instruction.OpCode.FlowControl == FlowControl.Meta;

    public static bool IsCompilerGenerated(TypeDef type)
    {
        if (type.CustomAttributes.Any(a => a.TypeFullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
            return true;

        var name = type.Name.String;
        if (name.StartsWith('<') || name.Contains(">d__") || name.Contains(">c__") ||
            name.Contains("<>c") || name.Contains("DisplayClass"))
            return true;

        if (type.Interfaces.Any(i => i.Interface.FullName == "System.Runtime.CompilerServices.IAsyncStateMachine"))
            return true;

        return false;
    }

    public static bool IsCompilerGeneratedMethod(MethodDef method)
    {
        if (method.CustomAttributes.Any(a => a.TypeFullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
            return true;

        var name = method.Name.String;
        if (name.StartsWith('<') || name.Contains(">b__") || name.Contains(">g__"))
            return true;

        return false;
    }

    public static void SetLdcI4(Instruction instruction, int value)
    {
        switch (value)
        {
            case -1:
                instruction.OpCode = OpCodes.Ldc_I4_M1;
                instruction.Operand = null;
                break;
            case 0:
                instruction.OpCode = OpCodes.Ldc_I4_0;
                instruction.Operand = null;
                break;
            case 1:
                instruction.OpCode = OpCodes.Ldc_I4_1;
                instruction.Operand = null;
                break;
            case 2:
                instruction.OpCode = OpCodes.Ldc_I4_2;
                instruction.Operand = null;
                break;
            case 3:
                instruction.OpCode = OpCodes.Ldc_I4_3;
                instruction.Operand = null;
                break;
            case 4:
                instruction.OpCode = OpCodes.Ldc_I4_4;
                instruction.Operand = null;
                break;
            case 5:
                instruction.OpCode = OpCodes.Ldc_I4_5;
                instruction.Operand = null;
                break;
            case 6:
                instruction.OpCode = OpCodes.Ldc_I4_6;
                instruction.Operand = null;
                break;
            case 7:
                instruction.OpCode = OpCodes.Ldc_I4_7;
                instruction.Operand = null;
                break;
            case 8:
                instruction.OpCode = OpCodes.Ldc_I4_8;
                instruction.Operand = null;
                break;
            default:
                if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
                {
                    instruction.OpCode = OpCodes.Ldc_I4_S;
                    instruction.Operand = (sbyte)value;
                }
                else
                {
                    instruction.OpCode = OpCodes.Ldc_I4;
                    instruction.Operand = value;
                }
                break;
        }
    }
}

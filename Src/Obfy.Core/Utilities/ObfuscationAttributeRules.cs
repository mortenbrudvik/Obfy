using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Honors <c>System.Reflection.ObfuscationAttribute</c> on types and members (assembly and source).
/// Attribute property defaults match the framework: Exclude=true, Feature="all", ApplyToMembers=true.
/// When no attribute is present, the member is not excluded.
/// Unknown <c>Feature</c> values are ignored (with an optional warning).
/// </summary>
public static class ObfuscationAttributeRules
{
    public static bool IsExcluded(IHasCustomAttribute provider, ObfuscationFeature feature, ICollection<string>? warnings = null)
    {
        bool? exact = null;
        bool? all = null;

        if (!provider.HasCustomAttributes)
            return false;

        var target = Describe(provider);
        foreach (var attr in provider.CustomAttributes)
        {
            if (!IsObfuscationAttribute(attr))
                continue;

            var featureName = ReadFeature(attr);
            NoteUnknownFeature(featureName, target, warnings);
            var exclude = ReadNamedBool(attr, "Exclude", defaultValue: true);

            if (IsAllFeature(featureName))
                all = exclude;
            else if (MatchesFeature(featureName, feature))
                exact = exclude;
        }

        if (exact.HasValue)
            return exact.Value;
        if (all.HasValue)
            return all.Value;
        return false;
    }

    /// <summary>
    /// Member-level <c>Exclude</c> wins. Otherwise the declaring type's directive applies
    /// only when that type's <c>ApplyToMembers</c> is true (framework default).
    /// </summary>
    public static bool IsExcluded(IMemberDef member, ObfuscationFeature feature, ICollection<string>? warnings = null)
    {
        if (TryGetDirective(member, feature, out var exclude, warnings))
            return exclude;

        var declaring = member.DeclaringType;
        if (declaring == null)
            return false;

        if (!IsExcluded(declaring, feature, warnings))
            return false;

        return ApplyToMembers(declaring, feature);
    }

    public static bool ApplyToMembers(TypeDef type, ObfuscationFeature feature)
    {
        bool? exact = null;
        bool? all = null;
        foreach (var attr in type.CustomAttributes)
        {
            if (!IsObfuscationAttribute(attr))
                continue;
            var featureName = ReadFeature(attr);
            var apply = ReadNamedBool(attr, "ApplyToMembers", defaultValue: true);
            if (IsAllFeature(featureName))
                all = apply;
            else if (MatchesFeature(featureName, feature))
                exact = apply;
        }

        if (exact.HasValue)
            return exact.Value;
        if (all.HasValue)
            return all.Value;
        return true;
    }

    public static bool MatchesInclusions(TypeDef type, MethodDef? method, InclusionRules inclusions)
    {
        if (!inclusions.HasAny)
            return true;

        if (inclusions.Namespaces.Any(n => ObfuscatorHelpers.MatchesPattern(type.Namespace, n)))
            return true;
        if (inclusions.Types.Any(t => ObfuscatorHelpers.MatchesPattern(type.Name, t)))
            return true;
        if (method != null && inclusions.Methods.Any(m => ObfuscatorHelpers.MatchesPattern(method.Name, m)))
            return true;

        return false;
    }

    /// <summary>
    /// Type-level allow-list: a type is a candidate if a namespace/type pattern matches, or
    /// (when only method patterns are set) if any of its methods match.
    /// </summary>
    public static bool TypeMayContainInclusions(TypeDef type, InclusionRules inclusions)
    {
        if (!inclusions.HasAny)
            return true;
        if (MatchesInclusions(type, null, inclusions))
            return true;
        if (inclusions.Methods.Count == 0)
            return false;
        return type.Methods.Any(m =>
            inclusions.Methods.Any(p => ObfuscatorHelpers.MatchesPattern(m.Name, p)));
    }

    /// <summary>
    /// Whether the type should be visited for member processing. A type-level exclude with
    /// <c>ApplyToMembers=true</c> skips the whole type; <c>ApplyToMembers=false</c> still
    /// visits members.
    /// </summary>
    public static bool AllowType(TypeDef type, ObfySettings settings, ObfuscationFeature feature, ICollection<string>? warnings = null)
    {
        if (ObfuscatorHelpers.IsRuntimeHelper(type) && feature != ObfuscationFeature.ControlFlow)
            return false;
        if (type.Namespace == "Obfy.Core.Models")
            return false;
        if (IsExcluded(type, feature, warnings) && ApplyToMembers(type, feature))
            return false;
        if (!TypeMayContainInclusions(type, settings.Inclusions))
            return false;
        return !ObfuscatorHelpers.IsExcluded(type, settings.Exclusions);
    }

    public static bool AllowMethod(MethodDef method, ObfySettings settings, ObfuscationFeature feature, ICollection<string>? warnings = null)
    {
        if (IsExcluded(method, feature, warnings))
            return false;
        if (!MatchesInclusions(method.DeclaringType, method, settings.Inclusions))
            return false;
        return !ObfuscatorHelpers.MethodMatchesExclusion(method, settings.Exclusions);
    }

    public static void WarnIfInclusionsMatchedNothing(ModuleDef module, ObfySettings settings, ICollection<string> warnings)
    {
        if (!settings.Inclusions.HasAny)
            return;
        if (module.GetTypes().Any(t => TypeMayContainInclusions(t, settings.Inclusions)))
            return;
        warnings.Add(
            "Inclusions matched 0 types/methods; nothing was obfuscated. Check namespaces/types/methods patterns.");
    }

    public static bool IsExcluded(ISymbol symbol, ObfuscationFeature feature)
    {
        if (TryGetSymbolDirective(symbol, feature, out var exclude))
            return exclude;

        var parent = symbol is INamedTypeSymbol named ? named.ContainingType : symbol.ContainingType;
        if (parent == null)
            return false;
        if (!IsExcluded(parent, feature))
            return false;
        return ReadSymbolApplyToMembers(parent, feature);
    }

    private static bool TryGetDirective(IHasCustomAttribute provider, ObfuscationFeature feature, out bool exclude, ICollection<string>? warnings)
    {
        exclude = false;
        if (!provider.HasCustomAttributes)
            return false;

        bool? exact = null;
        bool? all = null;
        var target = Describe(provider);
        var found = false;
        foreach (var attr in provider.CustomAttributes)
        {
            if (!IsObfuscationAttribute(attr))
                continue;
            found = true;
            var featureName = ReadFeature(attr);
            NoteUnknownFeature(featureName, target, warnings);
            var value = ReadNamedBool(attr, "Exclude", defaultValue: true);
            if (IsAllFeature(featureName))
                all = value;
            else if (MatchesFeature(featureName, feature))
                exact = value;
        }

        if (!found)
            return false;
        if (exact.HasValue)
        {
            exclude = exact.Value;
            return true;
        }
        if (all.HasValue)
        {
            exclude = all.Value;
            return true;
        }
        return false;
    }

    private static bool IsObfuscationAttribute(CustomAttribute attr)
    {
        var fullName = attr.TypeFullName;
        return fullName.Equals("System.Reflection.ObfuscationAttribute", StringComparison.Ordinal)
            || fullName.Equals("ObfuscationAttribute", StringComparison.Ordinal);
    }

    private static string ReadFeature(CustomAttribute attr)
    {
        foreach (var named in attr.NamedArguments)
        {
            if (named.Name != "Feature")
                continue;
            var text = ReadString(named.Argument.Value);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return "all";
    }

    private static string? ReadString(object? value) => value switch
    {
        string s => s,
        UTF8String u => UTF8String.ToSystemStringOrEmpty(u),
        _ => value?.ToString()
    };

    private static bool ReadNamedBool(CustomAttribute attr, string name, bool defaultValue)
    {
        foreach (var named in attr.NamedArguments)
        {
            if (named.Name == name && named.Argument.Value is bool b)
                return b;
        }

        return defaultValue;
    }

    private static bool IsAllFeature(string feature) =>
        string.IsNullOrWhiteSpace(feature) ||
        Normalize(feature).Equals("all", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFeature(string feature, ObfuscationFeature requested)
    {
        var normalized = Normalize(feature);
        return requested switch
        {
            ObfuscationFeature.Renaming => normalized is "renaming" or "rename" or "symbols",
            ObfuscationFeature.ControlFlow => normalized is "controlflow" or "cf",
            ObfuscationFeature.Strings => normalized is "strings" or "stringencryption",
            ObfuscationFeature.Constants => normalized is "constants" or "constantencryption",
            ObfuscationFeature.All => false,
            _ => false
        };
    }

    private static bool IsKnownFeature(string feature)
    {
        var normalized = Normalize(feature);
        return normalized is "all" or "renaming" or "rename" or "symbols"
            or "controlflow" or "cf"
            or "strings" or "stringencryption"
            or "constants" or "constantencryption";
    }

    private static string Normalize(string feature) =>
        feature.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

    private static void NoteUnknownFeature(string featureName, string target, ICollection<string>? warnings)
    {
        if (warnings == null || IsAllFeature(featureName) || IsKnownFeature(featureName))
            return;
        var msg =
            $"Unknown ObfuscationAttribute Feature '{featureName}' on {target}; supported: all, renaming, controlflow, strings, constants.";
        if (!warnings.Contains(msg))
            warnings.Add(msg);
    }

    private static string Describe(IHasCustomAttribute provider) => provider switch
    {
        TypeDef t => t.FullName,
        IMemberDef m => m.FullName,
        _ => provider.GetType().Name
    };

    private static bool TryGetSymbolDirective(ISymbol symbol, ObfuscationFeature feature, out bool exclude)
    {
        exclude = false;
        bool? exact = null;
        bool? all = null;
        var found = false;
        foreach (var attr in symbol.GetAttributes())
        {
            if (!IsObfuscationAttributeClass(attr.AttributeClass))
                continue;
            found = true;
            var featureName = "all";
            var value = true;
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Feature" && named.Value.Value is string s)
                    featureName = s;
                else if (named.Key == "Exclude" && named.Value.Value is bool b)
                    value = b;
            }

            if (IsAllFeature(featureName))
                all = value;
            else if (MatchesFeature(featureName, feature))
                exact = value;
        }

        if (!found)
            return false;
        if (exact.HasValue)
        {
            exclude = exact.Value;
            return true;
        }
        if (all.HasValue)
        {
            exclude = all.Value;
            return true;
        }
        return false;
    }

    private static bool ReadSymbolApplyToMembers(INamedTypeSymbol type, ObfuscationFeature feature)
    {
        bool? exact = null;
        bool? all = null;
        foreach (var attr in type.GetAttributes())
        {
            if (!IsObfuscationAttributeClass(attr.AttributeClass))
                continue;
            var featureName = "all";
            var apply = true;
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Feature" && named.Value.Value is string s)
                    featureName = s;
                else if (named.Key == "ApplyToMembers" && named.Value.Value is bool b)
                    apply = b;
            }

            if (IsAllFeature(featureName))
                all = apply;
            else if (MatchesFeature(featureName, feature))
                exact = apply;
        }

        if (exact.HasValue)
            return exact.Value;
        if (all.HasValue)
            return all.Value;
        return true;
    }

    private static bool IsObfuscationAttributeClass(INamedTypeSymbol? type)
    {
        if (type == null)
            return false;
        if (type.Name != "ObfuscationAttribute")
            return false;
        var ns = type.ContainingNamespace?.ToDisplayString();
        return ns is "System.Reflection" or null or "";
    }
}

public enum ObfuscationFeature
{
    Renaming,
    ControlFlow,
    Strings,
    Constants,
    All
}

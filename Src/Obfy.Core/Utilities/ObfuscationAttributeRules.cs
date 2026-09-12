using dnlib.DotNet;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Honors <see cref="System.Reflection.ObfuscationAttribute"/> on types and members.
/// Unknown <c>Feature</c> values are ignored. Defaults match the framework: Exclude=true,
/// Feature="all", ApplyToMembers=true.
/// </summary>
public static class ObfuscationAttributeRules
{
    public static bool IsExcluded(IHasCustomAttribute provider, ObfuscationFeature feature)
    {
        bool? exact = null;
        bool? all = null;

        if (!provider.HasCustomAttributes)
            return false;

        foreach (var attr in provider.CustomAttributes)
        {
            if (!IsObfuscationAttribute(attr))
                continue;

            var featureName = ReadFeature(attr);
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

    public static bool IsExcluded(IMemberDef member, ObfuscationFeature feature)
    {
        if (IsExcluded((IHasCustomAttribute)member, feature))
            return true;

        var declaring = member.DeclaringType;
        if (declaring == null)
            return false;

        if (!IsExcluded(declaring, feature))
            return false;

        return ReadApplyToMembers(declaring, feature);
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

    public static bool AllowType(TypeDef type, ObfySettings settings, ObfuscationFeature feature)
    {
        if (ObfuscatorHelpers.IsRuntimeHelper(type) && feature != ObfuscationFeature.ControlFlow)
            return false;
        if (type.Namespace == "Obfy.Core.Models")
            return false;
        if (IsExcluded(type, feature))
            return false;
        if (!MatchesInclusions(type, null, settings.Inclusions))
            return false;
        return !ObfuscatorHelpers.IsExcluded(type, settings.Exclusions);
    }

    public static bool AllowMethod(MethodDef method, ObfySettings settings, ObfuscationFeature feature)
    {
        if (IsExcluded(method, feature))
            return false;
        if (!MatchesInclusions(method.DeclaringType, method, settings.Inclusions))
            return false;
        return !ObfuscatorHelpers.MethodMatchesExclusion(method, settings.Exclusions);
    }

    private static bool ReadApplyToMembers(TypeDef type, ObfuscationFeature feature)
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

    private static bool IsObfuscationAttribute(CustomAttribute attr) =>
        attr.TypeFullName.EndsWith("ObfuscationAttribute", StringComparison.Ordinal);

    private static string ReadFeature(CustomAttribute attr)
    {
        foreach (var named in attr.NamedArguments)
        {
            if (named.Name == "Feature" && named.Argument.Value is string s)
                return s;
        }

        return "all";
    }

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
        feature.Equals("all", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesFeature(string feature, ObfuscationFeature requested)
    {
        var normalized = feature.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        return requested switch
        {
            ObfuscationFeature.Renaming => normalized.Equals("renaming", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("rename", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("symbols", StringComparison.OrdinalIgnoreCase),
            ObfuscationFeature.ControlFlow => normalized.Equals("controlflow", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("cf", StringComparison.OrdinalIgnoreCase),
            ObfuscationFeature.Strings => normalized.Equals("strings", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("stringencryption", StringComparison.OrdinalIgnoreCase),
            ObfuscationFeature.Constants => normalized.Equals("constants", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("constantencryption", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}

public enum ObfuscationFeature
{
    Renaming,
    ControlFlow,
    Strings,
    Constants
}

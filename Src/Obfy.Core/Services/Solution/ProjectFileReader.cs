using System.Xml.Linq;

namespace Obfy.Core.Services.Solution;

/// <summary>
/// Project metadata read from a SDK-style project file (no MSBuild evaluation).
/// </summary>
public sealed class ProjectFileInfo
{
    public required string Path { get; init; }
    public required string AssemblyName { get; init; }
    public required string OutputType { get; init; }
    public required IReadOnlyList<string> TargetFrameworks { get; init; }
    public bool UseWpf { get; init; }
    public bool UseWinForms { get; init; }
    public bool UseMaui { get; init; }
    public bool PublishAot { get; init; }
    public bool IsBlazorWasm { get; init; }
    public bool IsAspNetWeb { get; init; }
    public bool IsTest { get; init; }
    public bool ReferencesUnity { get; init; }
    public required IReadOnlyList<string> ProjectReferences { get; init; }
}

/// <summary>
/// Reads project hints from <c>.csproj</c> / <c>.vbproj</c> / <c>.fsproj</c> XML without MSBuild.
/// </summary>
public static class ProjectFileReader
{
    /// <summary>
    /// Parses <paramref name="projectPath"/> with <see cref="XDocument"/>. Property values use last-wins document order.
    /// </summary>
    public static ProjectFileInfo Read(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);

        if (!File.Exists(projectPath))
            throw new FileNotFoundException("Project file was not found.", projectPath);

        var document = XDocument.Load(projectPath);
        var root = document.Root
            ?? throw new InvalidOperationException($"Project file '{projectPath}' has no root element.");

        var sdk = GetAttributeValue(root, "Sdk") ?? string.Empty;
        var isBlazorWasm = sdk.Contains("BlazorWebAssembly", StringComparison.OrdinalIgnoreCase);
        var isAspNetWeb = !isBlazorWasm && sdk.Contains("Web", StringComparison.OrdinalIgnoreCase);

        string? outputType = null;
        string? assemblyName = null;
        string? targetFramework = null;
        string? targetFrameworks = null;
        var useWpf = false;
        var useWinForms = false;
        var useMaui = false;
        var publishAotProp = false;
        var isAotCompatible = false;
        var isTestProject = false;

        foreach (var propertyGroup in DescendantsByLocalName(root, "PropertyGroup"))
        {
            foreach (var property in propertyGroup.Elements())
            {
                var name = property.Name.LocalName;
                var value = property.Value.Trim();

                if (name.Equals("OutputType", StringComparison.OrdinalIgnoreCase))
                    outputType = value;
                else if (name.Equals("AssemblyName", StringComparison.OrdinalIgnoreCase))
                    assemblyName = value;
                else if (name.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
                {
                    targetFramework = value;
                    targetFrameworks = null;
                }
                else if (name.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                {
                    targetFrameworks = value;
                    targetFramework = null;
                }
                else if (name.Equals("UseWPF", StringComparison.OrdinalIgnoreCase))
                    useWpf = IsTrue(value);
                else if (name.Equals("UseWinForms", StringComparison.OrdinalIgnoreCase))
                    useWinForms = IsTrue(value);
                else if (name.Equals("UseMaui", StringComparison.OrdinalIgnoreCase))
                    useMaui = IsTrue(value);
                else if (name.Equals("PublishAot", StringComparison.OrdinalIgnoreCase))
                    publishAotProp = IsTrue(value);
                else if (name.Equals("IsAotCompatible", StringComparison.OrdinalIgnoreCase))
                    isAotCompatible = IsTrue(value);
                else if (name.Equals("IsTestProject", StringComparison.OrdinalIgnoreCase))
                    isTestProject = IsTrue(value);
            }
        }

        var publishAot = publishAotProp || isAotCompatible;

        var projectReferences = new List<string>();
        var hasTestPackage = false;
        var referencesUnity = false;

        foreach (var element in root.Descendants())
        {
            var localName = element.Name.LocalName;

            if (localName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase))
            {
                var include = GetAttributeValue(element, "Include");
                if (!string.IsNullOrEmpty(include))
                    projectReferences.Add(include);
            }
            else if (localName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase))
            {
                var include = GetAttributeValue(element, "Include") ?? string.Empty;
                if (IsTestPackage(include))
                    hasTestPackage = true;
                if (include.Contains("UnityEngine", StringComparison.OrdinalIgnoreCase)
                    || element.Value.Contains("UnityEngine", StringComparison.OrdinalIgnoreCase))
                    referencesUnity = true;
            }
            else if (localName.Equals("Reference", StringComparison.OrdinalIgnoreCase))
            {
                var include = GetAttributeValue(element, "Include") ?? string.Empty;
                if (include.Contains("UnityEngine", StringComparison.OrdinalIgnoreCase)
                    || element.Value.Contains("UnityEngine", StringComparison.OrdinalIgnoreCase))
                    referencesUnity = true;
            }
            else if (localName.Equals("HintPath", StringComparison.OrdinalIgnoreCase))
            {
                if (element.Value.Contains("UnityEngine", StringComparison.OrdinalIgnoreCase))
                    referencesUnity = true;
            }
        }

        var frameworks = ParseTargetFrameworks(targetFramework, targetFrameworks);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(projectPath);
        var resolvedAssemblyName = string.IsNullOrWhiteSpace(assemblyName)
            ? fileNameWithoutExtension
            : assemblyName;
        var resolvedOutputType = string.IsNullOrWhiteSpace(outputType) ? "Library" : outputType;

        var isTest = isTestProject
            || hasTestPackage
            || TestProjectClassifier.IsTestProjectName(fileNameWithoutExtension);

        return new ProjectFileInfo
        {
            Path = projectPath,
            AssemblyName = resolvedAssemblyName,
            OutputType = resolvedOutputType,
            TargetFrameworks = frameworks,
            UseWpf = useWpf,
            UseWinForms = useWinForms,
            UseMaui = useMaui,
            PublishAot = publishAot,
            IsBlazorWasm = isBlazorWasm,
            IsAspNetWeb = isAspNetWeb,
            IsTest = isTest,
            ReferencesUnity = referencesUnity,
            ProjectReferences = projectReferences,
        };
    }

    private static IReadOnlyList<string> ParseTargetFrameworks(string? targetFramework, string? targetFrameworks)
    {
        if (!string.IsNullOrWhiteSpace(targetFrameworks))
        {
            return targetFrameworks
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(targetFramework))
            return new[] { targetFramework.Trim() };

        return Array.Empty<string>();
    }

    private static bool IsTestPackage(string include)
    {
        if (string.IsNullOrEmpty(include))
            return false;

        if (include.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
            return true;
        if (include.Equals("MSTest.Sdk", StringComparison.OrdinalIgnoreCase))
            return true;
        if (include.Contains("xunit", StringComparison.OrdinalIgnoreCase))
            return true;
        if (include.Contains("nunit", StringComparison.OrdinalIgnoreCase))
            return true;
        if (include.Contains("MSTest", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsTrue(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string? GetAttributeValue(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(a => a.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static IEnumerable<XElement> DescendantsByLocalName(XElement root, string localName)
        => root.Descendants().Where(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
}

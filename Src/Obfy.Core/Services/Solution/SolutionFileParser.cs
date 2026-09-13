using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Obfy.Core.Services.Solution;

/// <summary>
/// Project entry as listed in a <c>.sln</c> / <c>.slnx</c> file (path not resolved).
/// </summary>
public readonly record struct SolutionProjectRef(string Name, string RelativePath);

/// <summary>
/// Parses project lists from Visual Studio solution files.
/// </summary>
public static class SolutionFileParser
{
    private static readonly Regex SlnProjectLine = new(
        @"^Project\(""[^""]+""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads project entries from <paramref name="solutionPath"/>. Paths are returned as written in the file.
    /// </summary>
    public static IReadOnlyList<SolutionProjectRef> Parse(string solutionPath)
    {
        ArgumentNullException.ThrowIfNull(solutionPath);

        if (!File.Exists(solutionPath))
            throw new FileNotFoundException("Solution file was not found.", solutionPath);

        var extension = Path.GetExtension(solutionPath);
        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
            return ParseSln(solutionPath);
        if (extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            return ParseSlnx(solutionPath);

        throw new ArgumentException(
            $"Unsupported solution file extension '{extension}'. Expected .sln or .slnx.",
            nameof(solutionPath));
    }

    private static IReadOnlyList<SolutionProjectRef> ParseSln(string solutionPath)
    {
        var results = new List<SolutionProjectRef>();

        foreach (var line in File.ReadAllLines(solutionPath))
        {
            var match = SlnProjectLine.Match(line);
            if (!match.Success)
                continue;

            var name = UnescapeQuotes(match.Groups[1].Value);
            var relativePath = UnescapeQuotes(match.Groups[2].Value);
            results.Add(new SolutionProjectRef(name, relativePath));
        }

        return results;
    }

    private static IReadOnlyList<SolutionProjectRef> ParseSlnx(string solutionPath)
    {
        var document = XDocument.Load(solutionPath);
        var results = new List<SolutionProjectRef>();

        foreach (var project in document.Descendants().Where(e => e.Name.LocalName == "Project"))
        {
            var pathAttr = project.Attributes()
                .FirstOrDefault(a => a.Name.LocalName.Equals("Path", StringComparison.OrdinalIgnoreCase));
            if (pathAttr is null)
                continue;

            var relativePath = pathAttr.Value;
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            var name = Path.GetFileNameWithoutExtension(relativePath);
            results.Add(new SolutionProjectRef(name, relativePath));
        }

        return results;
    }

    private static string UnescapeQuotes(string value) => value.Replace("\"\"", "\"", StringComparison.Ordinal);
}

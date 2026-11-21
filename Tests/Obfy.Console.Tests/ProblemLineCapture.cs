using System.Text.Json;
using System.Text.RegularExpressions;

namespace Obfy.Console.Tests;

/// <summary>One line the VS Code <c>obfy</c> problem matcher picked up, split into the groups it captures.</summary>
internal sealed record ProblemLine(string File, int Line, int Column, string Severity, string Message);

/// <summary>
/// Redirects <see cref="System.Console.Out"/> (where the CLI prints its problem-matcher lines) for the life of the
/// instance and reads them back with the regular expression from <c>Src/Obfy.VSCode/package.json</c>, so a line the
/// real matcher would not capture is not counted. Restores the previous writer on dispose. The test project runs its
/// collections serially, so swapping the static console is safe.
/// </summary>
internal sealed class ProblemLineCapture : IDisposable
{
    private readonly TextWriter _previous = System.Console.Out;
    private readonly StringWriter _writer = new();

    public ProblemLineCapture() => System.Console.SetOut(_writer);

    /// <summary>The problem matcher's own captures for every line written so far that it would match.</summary>
    public IReadOnlyList<ProblemLine> Problems
    {
        get
        {
            var pattern = MatcherPattern.Value;
            var problems = new List<ProblemLine>();
            foreach (var text in _writer.ToString().Split('\n'))
            {
                var match = pattern.Regex.Match(text.TrimEnd('\r'));
                if (!match.Success)
                    continue;

                problems.Add(new ProblemLine(
                    match.Groups[pattern.File].Value,
                    int.Parse(match.Groups[pattern.Line].Value),
                    int.Parse(match.Groups[pattern.Column].Value),
                    match.Groups[pattern.Severity].Value,
                    match.Groups[pattern.Message].Value));
            }

            return problems;
        }
    }

    public void Dispose() => System.Console.SetOut(_previous);

    private sealed record Pattern(Regex Regex, int File, int Line, int Column, int Severity, int Message);

    private static readonly Lazy<Pattern> MatcherPattern = new(LoadPattern);

    private static Pattern LoadPattern()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Obfy.sln")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("Obfy.sln was not found above the test output folder.");

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, "Src", "Obfy.VSCode", "package.json")));
        var pattern = doc.RootElement
            .GetProperty("contributes")
            .GetProperty("problemMatchers")[0]
            .GetProperty("pattern");

        return new Pattern(
            new Regex(pattern.GetProperty("regexp").GetString()!),
            pattern.GetProperty("file").GetInt32(),
            pattern.GetProperty("line").GetInt32(),
            pattern.GetProperty("column").GetInt32(),
            pattern.GetProperty("severity").GetInt32(),
            pattern.GetProperty("message").GetInt32());
    }
}

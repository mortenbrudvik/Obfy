using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// The tables in <c>FrameworkReferences</c> are the one place that knows where a framework type lives on each target.
/// That holds only while injected code cannot name a framework type another way, so a site that builds its own
/// <c>TypeRef</c> against the core library (the mistake behind the startup crashes on SDK-built apps) fails here.
/// </summary>
public class FrameworkReferenceChokePointTests
{
    private static readonly string[] Forbidden =
    {
        "CorLibTypes.AssemblyRef",
        "CorLibTypes.GetTypeRef(",
        "new TypeRefUser("
    };

    [Fact]
    public void ObfyCore_CreatesFrameworkTypeReferencesOnlyThroughFrameworkReferences()
    {
        var core = Path.Combine(FindRepoRoot(), "Src", "Obfy.Core");
        var separator = Path.DirectorySeparatorChar;

        var offenders = Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "FrameworkReferences.cs")
            .Where(f => !f.Contains($"{separator}obj{separator}") && !f.Contains($"{separator}bin{separator}"))
            .SelectMany(f => File.ReadLines(f).Select((line, index) => (File: Path.GetRelativePath(core, f), Line: index + 1, Text: line.Trim())))
            .Where(t => !t.Text.StartsWith("//", StringComparison.Ordinal) && Forbidden.Any(t.Text.Contains))
            .Select(t => $"{t.File}:{t.Line}: {t.Text}")
            .ToList();

        offenders.ShouldBeEmpty(
            "Use FrameworkReferences.Type (or Scope) for framework types, so the target-specific tables apply:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}

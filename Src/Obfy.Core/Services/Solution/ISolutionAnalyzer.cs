using Obfy.Core.Models.Solution;

namespace Obfy.Core.Services.Solution;

/// <summary>
/// Builds a <see cref="ProtectionSession"/> from a solution or project path (no MSBuild).
/// </summary>
public interface ISolutionAnalyzer
{
    /// <summary>
    /// Analyzes <paramref name="path"/> (<c>.sln</c> / <c>.slnx</c> / <c>.csproj</c> / <c>.vbproj</c> / <c>.fsproj</c>).
    /// </summary>
    ProtectionSession Analyze(string path);
}

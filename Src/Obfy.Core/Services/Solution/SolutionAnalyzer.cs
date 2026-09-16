using System.Xml;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;

namespace Obfy.Core.Services.Solution;

/// <summary>
/// Discovers projects, built outputs, and settings hints for a solution or project drop.
/// </summary>
public class SolutionAnalyzer : ISolutionAnalyzer
{
    private static readonly HashSet<string> SupportedProjectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj",
        ".vbproj",
        ".fsproj"
    };

    /// <inheritdoc/>
    public ProtectionSession Analyze(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var extension = Path.GetExtension(path);
        string baseDirectory;
        IReadOnlyList<SolutionProjectRef> projectRefs;

        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            projectRefs = SolutionFileParser.Parse(path);
            if (projectRefs.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No Project entries in {path}. Is this a valid .sln/.slnx?");
            }

            baseDirectory = GetDirectory(path);
        }
        else if (SupportedProjectExtensions.Contains(extension))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            projectRefs = new[] { new SolutionProjectRef(name, path) };
            baseDirectory = GetDirectory(path);
        }
        else
        {
            throw new ArgumentException(
                $"Unsupported path extension '{extension}'. Expected .sln, .slnx, .csproj, .vbproj, or .fsproj.",
                nameof(path));
        }

        var analyzed = new List<AnalyzedProject>(projectRefs.Count);
        foreach (var projectRef in projectRefs)
            analyzed.Add(AnalyzeRef(projectRef, baseDirectory));

        var included = analyzed
            .Where(static p => p.SkipReason == ProjectSkipReason.None && p.Info is not null)
            .ToList();

        var hasIncludedExe = included.Any(static p => IsExeOrWinExe(p.Info!.OutputType));
        var referencedByIncludedExe = BuildReferencedByIncludedExe(included);

        var entries = new List<ProjectProtectionEntry>();
        foreach (var project in analyzed)
        {
            if (project.SkipReason != ProjectSkipReason.None
                || project.Info is null
                || project.Outputs.Count == 0)
            {
                entries.Add(ToSkippedEntry(project));
                continue;
            }

            var hints = BuildHints(project, hasIncludedExe, referencedByIncludedExe);
            var multiTfm = project.Outputs.Count > 1;
            foreach (var output in project.Outputs)
            {
                var name = multiTfm
                    ? $"{project.ProjectName} ({GetTfmLabel(output, project.Info)})"
                    : project.ProjectName;

                entries.Add(ProjectProtectionEntry.Included(
                    project.ProjectPath,
                    name,
                    output,
                    hints));
            }
        }

        return new ProtectionSession
        {
            SourcePath = path,
            Entries = entries
        };
    }

    private static AnalyzedProject AnalyzeRef(SolutionProjectRef projectRef, string baseDirectory)
    {
        var resolved = SolutionPath.Resolve(baseDirectory, projectRef.RelativePath);
        var extension = Path.GetExtension(resolved);

        if (!SupportedProjectExtensions.Contains(extension))
        {
            return new AnalyzedProject
            {
                ProjectPath = resolved,
                ProjectName = projectRef.Name,
                SkipReason = ProjectSkipReason.Unsupported,
                SkipMessage = string.IsNullOrEmpty(extension)
                    ? "Unsupported project type"
                    : $"Unsupported project type '{extension}'"
            };
        }

        if (!File.Exists(resolved))
        {
            return new AnalyzedProject
            {
                ProjectPath = resolved,
                ProjectName = projectRef.Name,
                SkipReason = ProjectSkipReason.MissingProject,
                SkipMessage = "Project file not found"
            };
        }

        ProjectFileInfo info;
        try
        {
            info = ProjectFileReader.Read(resolved);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new AnalyzedProject
            {
                ProjectPath = resolved,
                ProjectName = projectRef.Name,
                SkipReason = ProjectSkipReason.LoadFailed,
                SkipMessage = $"Failed to read project: {ex.Message}"
            };
        }

        if (info.IsTest)
        {
            return new AnalyzedProject
            {
                ProjectPath = resolved,
                ProjectName = projectRef.Name,
                Info = info,
                SkipReason = ProjectSkipReason.Test,
                SkipMessage = "Test project"
            };
        }

        var projectDirectory = Path.GetDirectoryName(resolved) ?? baseDirectory;
        var outputs = AssemblyOutputLocator.FindAll(projectDirectory, info.AssemblyName, info.TargetFrameworks);
        if (outputs.Count == 0)
        {
            return new AnalyzedProject
            {
                ProjectPath = resolved,
                ProjectName = projectRef.Name,
                Info = info,
                SkipReason = ProjectSkipReason.MissingOutput,
                SkipMessage = "No built output in bin/Release or bin/Debug (RID-specific and publish folders are not scanned)"
            };
        }

        return new AnalyzedProject
        {
            ProjectPath = resolved,
            ProjectName = projectRef.Name,
            Info = info,
            Outputs = outputs,
            SkipReason = ProjectSkipReason.None
        };
    }

    private static HashSet<string> BuildReferencedByIncludedExe(List<AnalyzedProject> included)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in included)
        {
            if (!IsExeOrWinExe(project.Info!.OutputType))
                continue;

            var projectDirectory = Path.GetDirectoryName(project.ProjectPath);
            if (projectDirectory is null)
                continue;

            foreach (var include in project.Info.ProjectReferences)
            {
                if (string.IsNullOrWhiteSpace(include))
                    continue;

                referenced.Add(SolutionPath.Resolve(projectDirectory, include));
            }
        }

        return referenced;
    }

    private static ProjectSettingsHints BuildHints(
        AnalyzedProject project,
        bool hasIncludedExe,
        HashSet<string> referencedByIncludedExe)
    {
        var info = project.Info!;
        var preservePublicApi = !IsExeOrWinExe(info.OutputType)
            && (!hasIncludedExe || !referencedByIncludedExe.Contains(project.ProjectPath));

        var runtimeProfile = RuntimeProfile.Default;
        if (info.PublishAot)
            runtimeProfile = RuntimeProfile.NativeAot;
        if (info.IsBlazorWasm || HasBrowserTfm(info.TargetFrameworks))
            runtimeProfile = RuntimeProfile.BlazorWasm;
        if (info.ReferencesUnity)
            runtimeProfile = RuntimeProfile.UnityIl2Cpp;

        return new ProjectSettingsHints
        {
            PreserveXaml = info.UseWpf || info.UseWinForms || info.UseMaui,
            PreservePublicApi = preservePublicApi,
            RuntimeProfile = runtimeProfile,
            AddUnityExcludes = info.ReferencesUnity,
            AddAspNetMvcExcludes = info.IsAspNetWeb && !info.IsBlazorWasm
        };
    }

    private static ProjectProtectionEntry ToSkippedEntry(AnalyzedProject project)
        => ProjectProtectionEntry.Skipped(
            project.ProjectPath,
            project.ProjectName,
            project.SkipReason == ProjectSkipReason.None ? ProjectSkipReason.MissingOutput : project.SkipReason,
            project.SkipMessage);

    private static bool IsExeOrWinExe(string outputType)
        => outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase)
            || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase);

    private static bool HasBrowserTfm(IReadOnlyList<string> targetFrameworks)
        => targetFrameworks.Any(static tfm => tfm.Contains("-browser", StringComparison.OrdinalIgnoreCase));

    private static string GetTfmLabel(string outputPath, ProjectFileInfo info)
    {
        var segments = outputPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var tfm in info.TargetFrameworks)
        {
            if (string.IsNullOrEmpty(tfm))
                continue;

            if (segments.Any(s => s.Equals(tfm, StringComparison.OrdinalIgnoreCase)))
                return tfm;
        }

        var folder = Path.GetFileName(Path.GetDirectoryName(outputPath));
        return string.IsNullOrEmpty(folder) ? "unknown" : folder;
    }

    private static string GetDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Path has no directory.", nameof(path));
    }

    private sealed class AnalyzedProject
    {
        public required string ProjectPath { get; init; }
        public required string ProjectName { get; init; }
        public ProjectFileInfo? Info { get; init; }
        public IReadOnlyList<string> Outputs { get; init; } = Array.Empty<string>();
        public ProjectSkipReason SkipReason { get; init; }
        public string? SkipMessage { get; init; }
    }
}

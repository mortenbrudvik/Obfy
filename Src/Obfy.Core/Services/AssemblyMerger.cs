using System.Diagnostics;
using System.Text.RegularExpressions;
using ILRepacking;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;

namespace Obfy.Core.Services;

/// <summary>
/// Merges multiple .NET assemblies into a single assembly using ILRepack.
/// </summary>
public class AssemblyMerger : IAssemblyMerger
{
    private readonly ILogger<AssemblyMerger> _logger;

    public AssemblyMerger(ILogger<AssemblyMerger> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<AssemblyMergeResult> MergeAsync(
        IEnumerable<string> inputPaths,
        string outputPath,
        AssemblyMergeSettings settings,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var inputList = inputPaths.ToList();

        if (inputList.Count < 2)
        {
            return Task.FromResult(new AssemblyMergeResult
            {
                Success = false,
                ErrorMessage = "At least two assemblies are required for merging",
                Duration = stopwatch.Elapsed
            });
        }

        // Validate input files exist
        var missingFiles = inputList.Where(p => !File.Exists(p)).ToList();
        if (missingFiles.Count > 0)
        {
            return Task.FromResult(new AssemblyMergeResult
            {
                Success = false,
                ErrorMessage = $"Input files not found: {string.Join(", ", missingFiles)}",
                Duration = stopwatch.Elapsed
            });
        }

        // Filter out excluded assemblies
        var assembliesToMerge = FilterAssemblies(inputList, settings.ExcludePatterns);
        if (assembliesToMerge.Count < 2)
        {
            return Task.FromResult(new AssemblyMergeResult
            {
                Success = false,
                ErrorMessage = "Less than two assemblies remain after applying exclusion patterns",
                Duration = stopwatch.Elapsed
            });
        }

        _logger.LogInformation("Merging {Count} assemblies into {Output}", assembliesToMerge.Count, outputPath);

        try
        {
            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Build search directories
            var searchDirs = new List<string>();
            searchDirs.AddRange(settings.SearchDirectories);

            // Add directories of input assemblies as search paths
            foreach (var inputPath in assembliesToMerge)
            {
                var dir = Path.GetDirectoryName(inputPath);
                if (!string.IsNullOrEmpty(dir) && !searchDirs.Contains(dir))
                {
                    searchDirs.Add(dir);
                }
            }

            var options = new RepackOptions
            {
                InputAssemblies = assembliesToMerge.ToArray(),
                OutputFile = outputPath,
                Internalize = settings.Internalize,
                DebugInfo = settings.PreserveDebugInfo,
                CopyAttributes = true,
                AllowMultipleAssemblyLevelAttributes = true,
                AllowDuplicateResources = true,
                SearchDirectories = searchDirs,
                Parallel = true,
                LogVerbose = false
            };

            var repackLogger = new RepackLogger(_logger);
            var repack = new ILRepack(options, repackLogger);

            _logger.LogDebug("Starting ILRepack merge");
            repack.Repack();
            _logger.LogDebug("ILRepack merge completed");

            stopwatch.Stop();

            // ILRepack surfaces some failures through its logger without throwing. If any error was
            // logged, the merged output cannot be trusted, so fail rather than obfuscate a bad merge.
            if (repackLogger.HadError)
            {
                return Task.FromResult(new AssemblyMergeResult
                {
                    Success = false,
                    ErrorMessage = repackLogger.FirstError ?? "ILRepack reported an error during merge",
                    Duration = stopwatch.Elapsed
                });
            }

            var result = new AssemblyMergeResult
            {
                Success = true,
                OutputPath = outputPath,
                MergedAssemblyCount = assembliesToMerge.Count,
                MergedAssemblies = assembliesToMerge.Select(Path.GetFileName).ToList()!,
                Duration = stopwatch.Elapsed
            };

            _logger.LogInformation(
                "Successfully merged {Count} assemblies in {Duration:F2}s",
                result.MergedAssemblyCount,
                result.Duration.TotalSeconds);

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to merge assemblies");

            return Task.FromResult(new AssemblyMergeResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            });
        }
    }

    private List<string> FilterAssemblies(List<string> inputPaths, List<string> excludePatterns)
    {
        if (excludePatterns.Count == 0)
        {
            return inputPaths;
        }

        var regexPatterns = excludePatterns
            .Select(p => new Regex(
                "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase))
            .ToList();

        return inputPaths
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                return !regexPatterns.Any(r => r.IsMatch(fileName));
            })
            .ToList();
    }

    /// <summary>
    /// Logger adapter for ILRepack.
    /// </summary>
    private class RepackLogger : ILRepacking.ILogger
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;

        public RepackLogger(Microsoft.Extensions.Logging.ILogger logger)
        {
            _logger = logger;
        }

        public bool ShouldLogVerbose { get; set; } = false;

        /// <summary>
        /// True if ILRepack logged at least one error (some failures are reported without throwing).
        /// </summary>
        public bool HadError { get; private set; }

        /// <summary>
        /// The first error message ILRepack logged, if any.
        /// </summary>
        public string? FirstError { get; private set; }

        public void DuplicateIgnored(string ignoredType, object ignoredObject)
        {
            _logger.LogDebug("Duplicate ignored: {Type}", ignoredType);
        }

        public void Error(string msg)
        {
            HadError = true;
            FirstError ??= msg;
            _logger.LogError("{Message}", msg);
        }

        public void Info(string msg)
        {
            _logger.LogInformation("{Message}", msg);
        }

        public void Log(object str)
        {
            _logger.LogDebug("{Message}", str);
        }

        public void Verbose(string msg)
        {
            if (ShouldLogVerbose)
            {
                _logger.LogDebug("{Message}", msg);
            }
        }

        public void Warn(string msg)
        {
            _logger.LogWarning("{Message}", msg);
        }
    }
}

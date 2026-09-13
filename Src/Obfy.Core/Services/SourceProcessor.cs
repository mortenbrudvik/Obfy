using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Services;

/// <summary>
/// Default implementation of source code processing using Roslyn.
/// </summary>
public class SourceProcessor : ISourceProcessor
{
    private readonly ILogger<SourceProcessor> _logger;

    public SourceProcessor(ILogger<SourceProcessor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PipelineContext> LoadAsync(string path, ObfySettings settings, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Loading source code from {Path}", path);

        var syntaxTrees = new List<SyntaxTree>();

        if (File.Exists(path))
        {
            // Single file
            var sourceText = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: path);
            syntaxTrees.Add(syntaxTree);
        }
        else if (Directory.Exists(path))
        {
            // Directory with multiple files
            var files = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var sourceText = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: file);
                syntaxTrees.Add(syntaxTree);
            }
        }
        else
        {
            throw new FileNotFoundException($"Source path not found: {path}");
        }

        if (syntaxTrees.Count == 0)
        {
            throw new InvalidOperationException($"No C# files found in {path}");
        }

        _logger.LogDebug("Loaded {Count} source files", syntaxTrees.Count);

        var compilation = CSharpCompilation.Create(
            "ObfuscatedAssembly",
            syntaxTrees,
            CreateRuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var context = PipelineContext.ForSourceCode(compilation, settings);
        context.InputPath = path;

        return context;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(PipelineContext context, string outputPath, CancellationToken cancellationToken = default)
    {
        if (context.Compilation == null)
        {
            throw new InvalidOperationException("No compilation in context");
        }

        _logger.LogDebug("Saving obfuscated source code to {Path}", outputPath);

        // Ensure output directory exists
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // If single file input, write to single file output
        if (context.Compilation.SyntaxTrees.Count() == 1)
        {
            var tree = context.Compilation.SyntaxTrees.First();
            var sourceText = tree.GetRoot(cancellationToken).ToFullString();
            await File.WriteAllTextAsync(outputPath, sourceText, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Multiple files - write to directory
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            var inputRoot = Directory.Exists(context.InputPath) ? context.InputPath : null;
            foreach (var tree in context.Compilation.SyntaxTrees)
            {
                var relative = RelativeSourcePath(tree.FilePath, inputRoot);
                var filePath = Path.Combine(outputPath, relative);
                var fileDirectory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(fileDirectory))
                    Directory.CreateDirectory(fileDirectory);
                var sourceText = tree.GetRoot(cancellationToken).ToFullString();
                await File.WriteAllTextAsync(filePath, sourceText, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogDebug("Source code saved successfully");
    }

    private static string RelativeSourcePath(string? treePath, string? inputRoot)
    {
        if (string.IsNullOrEmpty(treePath))
            return $"obfuscated_{Guid.NewGuid():N}.cs";

        if (!string.IsNullOrEmpty(inputRoot))
        {
            var relative = Path.GetRelativePath(inputRoot, treePath);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && relative != treePath)
                return relative;
        }

        return Path.GetFileName(treePath);
    }

    private static IReadOnlyList<MetadataReference> CreateRuntimeReferences()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrEmpty(tpa))
        {
            return tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(File.Exists)
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToList();
        }

        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
        ];
    }
}

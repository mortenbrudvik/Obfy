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
            var sourceText = await File.ReadAllTextAsync(path, cancellationToken);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: path);
            syntaxTrees.Add(syntaxTree);
        }
        else if (Directory.Exists(path))
        {
            // Directory with multiple files
            var files = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var sourceText = await File.ReadAllTextAsync(file, cancellationToken);
                var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: file);
                syntaxTrees.Add(syntaxTree);
            }
        }
        else
        {
            throw new FileNotFoundException($"Source path not found: {path}");
        }

        _logger.LogDebug("Loaded {Count} source files", syntaxTrees.Count);

        // Create compilation with basic references
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            "ObfuscatedAssembly",
            syntaxTrees,
            references,
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
            await File.WriteAllTextAsync(outputPath, sourceText, cancellationToken);
        }
        else
        {
            // Multiple files - write to directory
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            foreach (var tree in context.Compilation.SyntaxTrees)
            {
                var fileName = Path.GetFileName(tree.FilePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = $"obfuscated_{Guid.NewGuid():N}.cs";
                }

                var filePath = Path.Combine(outputPath, fileName);
                var sourceText = tree.GetRoot(cancellationToken).ToFullString();
                await File.WriteAllTextAsync(filePath, sourceText, cancellationToken);
            }
        }

        _logger.LogDebug("Source code saved successfully");
    }
}

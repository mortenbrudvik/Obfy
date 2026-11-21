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
        _logger.LogDebug("Loading source code");

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
        return CreateContext(syntaxTrees, settings, path);
    }

    /// <inheritdoc/>
    public async Task<PipelineContext> LoadFilesAsync(
        IReadOnlyList<string> paths,
        ObfySettings settings,
        CancellationToken cancellationToken = default)
    {
        if (paths == null || paths.Count == 0)
            throw new ArgumentException("No source files were provided.", nameof(paths));

        var syntaxTrees = new List<SyntaxTree>(paths.Count);
        foreach (var file in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(file))
                throw new FileNotFoundException($"Source path not found: {file}", file);

            var sourceText = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(sourceText, path: Path.GetFullPath(file)));
        }

        _logger.LogDebug("Loaded {Count} source files into one compilation", syntaxTrees.Count);
        return CreateContext(syntaxTrees, settings, CommonDirectory(paths));
    }

    private static PipelineContext CreateContext(
        IReadOnlyList<SyntaxTree> syntaxTrees,
        ObfySettings settings,
        string? inputPath)
    {
        var compilation = CSharpCompilation.Create(
            "ObfuscatedAssembly",
            syntaxTrees,
            CreateRuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var context = PipelineContext.ForSourceCode(compilation, settings);
        context.InputPath = inputPath;
        return context;
    }

    private static string? CommonDirectory(IReadOnlyList<string> paths)
    {
        string? root = null;
        foreach (var path in paths)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(dir))
                return null;

            if (root == null)
            {
                root = dir;
                continue;
            }

            while (!IsSameOrUnder(dir, root))
            {
                var parent = Path.GetDirectoryName(root);
                if (string.IsNullOrEmpty(parent) ||
                    string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                    return null;
                root = parent;
            }
        }

        return root;
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "."
            || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    /// <inheritdoc/>
    public async Task SaveAsync(PipelineContext context, string outputPath, CancellationToken cancellationToken = default)
    {
        if (context.Compilation == null)
        {
            throw new InvalidOperationException("No compilation in context");
        }

        _logger.LogDebug("Saving obfuscated source code");

        // Ensure output directory exists
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // One input file gives one output file. A directory input always gives a directory, even when
        // it held a single file (and the string-encryption helper lives in the file it serves, so the
        // tree count alone no longer tells the two apart).
        var directoryInput = !string.IsNullOrEmpty(context.InputPath) && Directory.Exists(context.InputPath);
        if (context.Compilation.SyntaxTrees.Count() == 1 && !directoryInput)
        {
            if (Directory.Exists(outputPath))
            {
                throw new IOException(
                    "The output path is an existing directory, but one source file is written as one file. " +
                    "Remove the directory or choose another output path.");
            }

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

        // A tree Obfy added (the string-decryption helper) carries a plain relative name. Keep it as is
        // rather than resolving it against the current directory.
        if (!Path.IsPathRooted(treePath) &&
            !treePath.Split('/', '\\').Contains(".."))
            return treePath;

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

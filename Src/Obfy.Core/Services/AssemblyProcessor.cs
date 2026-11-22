using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Services;

/// <summary>
/// Default implementation of assembly processing using dnlib.
/// </summary>
public class AssemblyProcessor : IAssemblyProcessor
{
    private readonly ILogger<AssemblyProcessor> _logger;
    private readonly IReadOnlyList<IPePostProcessor> _postProcessors;

    public AssemblyProcessor(ILogger<AssemblyProcessor> logger, IEnumerable<IPePostProcessor>? postProcessors = null)
    {
        _logger = logger;
        var resolved = postProcessors is null ? null : postProcessors as ICollection<IPePostProcessor> ?? postProcessors.ToList();
        _postProcessors = (resolved is null || resolved.Count == 0
            ?
            [
                new MethodEncryptionPePostProcessor(),
                new AntiTamperPePostProcessor()
            ]
            : resolved).OrderBy(p => p.Order).ToList();
    }

    /// <inheritdoc/>
    public async Task<PipelineContext> LoadAsync(string path, ObfySettings settings, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Loading assembly");
        cancellationToken.ThrowIfCancellationRequested();

        var moduleContext = ModuleDef.CreateModuleContext();
        // Load from an in-memory byte copy rather than the path directly: ModuleDefMD.Load(path)
        // memory-maps and locks the file for the module's lifetime, which blocks in-place output and
        // leaves the input locked if a later stage fails. Reading the bytes up front avoids the lock.
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var module = ModuleDefMD.Load(bytes, moduleContext);

        _logger.LogDebug("Loaded assembly with {TypeCount} types", module.Types.Count);

        var context = PipelineContext.ForAssembly(module, settings);
        context.InputPath = path;

        return context;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The assembly is written to a temporary file in the target directory, post-processed and signed
    /// there, and only then moved over <paramref name="outputPath"/>. A failure at any step deletes the
    /// temporary file and leaves an existing target (including an in-place output that is also the
    /// input) exactly as it was.
    /// </remarks>
    public Task SaveAsync(PipelineContext context, string outputPath, CancellationToken cancellationToken = default)
    {
        if (context.Module == null)
        {
            throw new InvalidOperationException("No module in context");
        }

        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Saving obfuscated assembly");

        var targetPath = Path.GetFullPath(outputPath);

        // Ensure output directory exists
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // StrongNameKey on the first write allocates the signature directory. After IL XOR,
        // AntiTamperPePostProcessor patches the integrity hash before AssemblySigner.SignInPlace
        // (zeroing both the hash slot and strong-name signature slot during hashing so signing
        // covers the patched hash).
        var strongNameKey = AssemblySigner.TryLoadKey(context.Settings.Signing);

        var writerOptions = new ModuleWriterOptions(context.Module)
        {
            // Keep the row ids of the tables whose tokens reflection can see (see OutputMetadataFlags) but let
            // dnlib rebuild #Strings, #US and #Blob. PreserveAll also copies the ORIGINAL heaps verbatim,
            // which leaves every renamed name and encrypted literal readable in the output bytes.
            MetadataOptions = { Flags = OutputMetadataFlags },
            StrongNameKey = strongNameKey
        };

        if (context.Settings.Metadata.RemoveDebugInfo)
        {
            writerOptions.WritePdb = false;
        }

        var antiTamper = context.Settings.Protection.AntiTamper.Enabled;
        if (antiTamper && context.AntiTamperMetadata is null)
        {
            throw new InvalidOperationException("Anti-tamper is enabled but the runtime type was not injected.");
        }

        var targetExisted = File.Exists(targetPath);
        var tempPath = Path.Combine(
            directory ?? string.Empty,
            $"{Path.GetFileName(targetPath)}.obfy-{Guid.NewGuid():N}.tmp");

        try
        {
            // Control-flow (and other IL rewrites) can leave br.s / brfalse.s whose
            // targets no longer fit in a signed byte. dnlib then throws on write.
            foreach (var type in context.Module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;
                    method.Body.SimplifyBranches();
                    method.Body.OptimizeBranches();
                }
            }

            context.Module.Write(tempPath, writerOptions);
            foreach (var processor in _postProcessors)
                processor.Process(tempPath, context);
            if (strongNameKey is not null)
                AssemblySigner.SignInPlace(tempPath, strongNameKey);

            ReplaceTarget(tempPath, targetPath, targetExisted);
        }
        catch (Exception ex) when (targetExisted && ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"{ex.Message} The existing output file was not modified.", ex);
        }
        finally
        {
            DeleteTempFile(tempPath);
        }

        if (context.Module is IDisposable disposable)
        {
            disposable.Dispose();
            context.Module = null;
        }

        _logger.LogDebug("Assembly saved successfully");

        return Task.CompletedTask;
    }

    /// <summary>
    /// The row ids of every table are preserved except MemberRef and MethodSpec; the string, user-string and blob
    /// heaps are not, so names and literals the pipeline removed or replaced are not carried into the output.
    /// <para>
    /// Preserving ids means the writer also emits, at each original id nothing claimed, the row it reads from the
    /// ORIGINAL metadata. A member reference whose signature mentions a generic parameter (<c>Box&lt;T&gt;.Item</c>
    /// used through <c>Box&lt;int&gt;</c>) is never claimed: dnlib hands every reader a fresh copy of it, so the
    /// renamer renames the copies the code uses and the writer adds the untouched originals next to them, still
    /// carrying their original names. Not preserving MemberRef ids drops those rows. A MethodSpec whose
    /// instantiation contains a generic parameter is a fresh copy as well. Preserving that id makes the writer
    /// emit the copy's member reference, and that reference still has the original name. MethodSpec rows hold no
    /// name themselves, but the member reference they point at does, so those ids are not preserved either.
    /// TypeSpec and StandAloneSig rows hold no name and do not point at one, so their ids stay. The tokens that
    /// survive are the ones token-based reflection can reach: TypeDef, Field, Method, Param, Event and Property
    /// (the members reflection hands out as <c>MetadataToken</c>, and what <c>Module.ResolveMethod(int)</c> and
    /// the like accept), plus TypeRef, TypeSpec and StandAloneSig, which only IL and signatures use.
    /// A MemberRef or MethodSpec token appears only in IL operands, where the writer writes the new row id
    /// itself; nothing in this tool keeps one as a number (the post-save steps find methods by full name and by
    /// RVA, the VM tables are indexes into helpers that load their members with <c>ldtoken</c>).
    /// </para>
    /// </summary>
    private const MetadataFlags OutputMetadataFlags =
        (MetadataFlags.PreserveRids
            & ~MetadataFlags.PreserveMemberRefRids
            & ~MetadataFlags.PreserveMethodSpecRids)
        | MetadataFlags.PreserveExtraSignatureData;

    private static void ReplaceTarget(string tempPath, string targetPath, bool targetExisted)
    {
        if (targetExisted && OperatingSystem.IsWindows())
            File.Replace(tempPath, targetPath, destinationBackupFileName: null);
        else
            File.Move(tempPath, targetPath, overwrite: true);
    }

    private void DeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The exception text carries the path, so log its type only.
            _logger.LogWarning("Failed to delete a temporary output file ({ExceptionType})", ex.GetType().Name);
        }
    }
}

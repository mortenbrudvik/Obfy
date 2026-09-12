using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Writes a customer/build identifier as an assembly-level custom attribute.
/// Enable with <c>watermark.enabled</c> and a non-whitespace <c>watermark.id</c>.
/// The type <c>Obfy.Runtime.WatermarkAttribute</c> is pinned against renaming; the id is stored as
/// a constructor argument and a public <c>Id</c> field (plaintext metadata, not encryption).
/// Re-obfuscating with the same id is a no-op (warning). A different requested id replaces
/// the constructor argument and is reported as a warning.
/// </summary>
public class WatermarkObfuscator : IObfuscator
{
    public const string AttributeTypeName = ObfuscatorHelpers.PinnedAttributeNames.Watermark;
    public const string AttributeNamespace = ObfuscatorHelpers.PinnedAttributeNames.WatermarkNamespace;
    public const string IdFieldName = "Id";

    private readonly ILogger<WatermarkObfuscator> _logger;

    public WatermarkObfuscator(ILogger<WatermarkObfuscator> logger)
    {
        _logger = logger;
    }

    public string Name => "Watermark";

    public int Priority => (int)ObfuscationPhase.Watermark;

    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    public bool IsEnabled(ObfySettings settings) =>
        settings.Watermark.Enabled && !string.IsNullOrWhiteSpace(settings.Watermark.Id);

    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var module = context.RequireModule();
        var id = context.Settings.Watermark.Id.Trim();
        var stats = new ObfuscationStatistics();

        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                const string error = "Watermark is enabled but no id was specified.";
                _logger.LogError(error);
                return Task.FromResult(ObfuscationResult.Failed(error));
            }

            var assembly = module.Assembly ?? throw new InvalidOperationException("Module has no assembly.");
            var existing = assembly.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.Name == AttributeTypeName);
            if (existing != null)
            {
                var existingId = existing.ConstructorArguments.Count > 0
                    ? existing.ConstructorArguments[0].Value?.ToString()
                    : null;
                if (string.Equals(existingId, id, StringComparison.Ordinal))
                {
                    var sameWarning = $"Watermark already present ('{id}'); left unchanged.";
                    context.Warnings.Add(sameWarning);
                    _logger.LogInformation("{Warning}", sameWarning);
                    return Task.FromResult(ObfuscationResult.Successful(stats));
                }

                assembly.CustomAttributes.Remove(existing);
                var replacement = new CustomAttribute(existing.Constructor);
                replacement.ConstructorArguments.Add(new CAArgument(module.CorLibTypes.String, id));
                assembly.CustomAttributes.Add(replacement);
                stats.ProtectionsApplied = 1;
                var warning =
                    $"Watermark requested id '{id}' but assembly already has WatermarkAttribute('{existingId ?? "<missing>"}'); replaced with '{id}'.";
                context.Warnings.Add(warning);
                _logger.LogWarning("{Warning}", warning);
                return Task.FromResult(ObfuscationResult.Successful(stats));
            }

            var existingType = module.Find($"{AttributeNamespace}.{AttributeTypeName}", isReflectionName: false)
                ?? module.Types.FirstOrDefault(t => t.Name == AttributeTypeName);
            MethodDef ctor;
            if (existingType != null)
            {
                ctor = existingType.FindMethod(".ctor")
                    ?? throw new InvalidOperationException(
                        $"Watermark type {existingType.FullName} already exists without a constructor.");
                var warning = $"Watermark type {existingType.FullName} already exists; reusing it.";
                context.Warnings.Add(warning);
                _logger.LogWarning("{Warning}", warning);
            }
            else
            {
                var attrType = new TypeDefUser(
                    AttributeNamespace,
                    AttributeTypeName,
                    new TypeRefUser(module, "System", "Attribute", module.CorLibTypes.AssemblyRef))
                {
                    Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit
                };
                var idField = new FieldDefUser(
                    IdFieldName,
                    new FieldSig(module.CorLibTypes.String),
                    FieldAttributes.Public);
                attrType.Fields.Add(idField);

                ctor = new MethodDefUser(
                    ".ctor",
                    MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String),
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
                ctor.Body = new CilBody();
                ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call,
                    new MemberRefUser(module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void),
                        new TypeRefUser(module, "System", "Attribute", module.CorLibTypes.AssemblyRef))));
                ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
                ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, idField));
                ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                attrType.Methods.Add(ctor);
                module.Types.Add(attrType);
            }

            var attr = new CustomAttribute(ctor);
            attr.ConstructorArguments.Add(new CAArgument(module.CorLibTypes.String, id));
            assembly.CustomAttributes.Add(attr);

            stats.ProtectionsApplied = 1;
            _logger.LogInformation("Embedded watermark");
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Watermark injection failed");
            return Task.FromResult(ObfuscationResult.Failed($"Watermark injection failed: {ex.Message}", ex));
        }
    }
}

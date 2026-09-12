using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Writes a customer/build identifier as an assembly-level custom attribute.
/// Enable with <c>watermark.enabled</c> and a non-whitespace <c>watermark.id</c>.
/// The type name <c>WatermarkAttribute</c> is pinned against renaming; the id is stored as
/// a constructor argument and a public <c>Id</c> field (plaintext metadata, not encryption).
/// </summary>
public class WatermarkObfuscator : IObfuscator
{
    public const string AttributeTypeName = "WatermarkAttribute";
    public const string AttributeNamespace = "Obfy.Runtime";
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
        var module = context.RequireModule();
        var id = context.Settings.Watermark.Id.Trim();
        var stats = new ObfuscationStatistics();

        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogWarning("Watermark id is empty; skipped");
                return Task.FromResult(ObfuscationResult.Successful(stats));
            }

            var assembly = module.Assembly ?? throw new InvalidOperationException("Module has no assembly.");
            if (assembly.CustomAttributes.Any(a => a.AttributeType.Name == AttributeTypeName))
            {
                _logger.LogDebug("Watermark attribute already present");
                return Task.FromResult(ObfuscationResult.Successful(stats));
            }

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

            var ctor = new MethodDefUser(
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

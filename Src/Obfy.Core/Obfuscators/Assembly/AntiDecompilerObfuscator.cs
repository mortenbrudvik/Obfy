using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Injects anti-decompiler protection to make reverse engineering harder.
/// </summary>
public class AntiDecompilerObfuscator : IObfuscator
{
    private readonly ILogger<AntiDecompilerObfuscator> _logger;
    private readonly Random _random = new();

    // Characters that look similar or are hard to read (for junk names)
    private static readonly char[] ConfusingChars = new[]
    {
        '\u200B', '\u200C', '\u200D', '\u2060', '\uFEFF',
        'l', '1', 'I', 'O', '0',
        '\u0131', '\u0399', '\u03B9'
    };

    public AntiDecompilerObfuscator(ILogger<AntiDecompilerObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "AntiDecompiler";

    /// <inheritdoc/>
    public int Priority => (int)ObfuscationPhase.AntiDecompiler;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.Protection.AntiDecompiler.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var settings = context.Settings.Protection.AntiDecompiler;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting anti-decompiler protection injection");

        try
        {
            // Add SuppressIldasm attribute
            if (settings.AddSuppressIldasmAttribute)
            {
                if (InjectSuppressIldasmAttribute(module))
                {
                    stats.ProtectionsApplied++;
                    _logger.LogDebug("Added SuppressIldasm attribute");
                }
            }

            // Inject junk types
            if (settings.InjectJunkTypes)
            {
                var junkCount = InjectJunkTypes(module, settings);
                stats.ProtectionsApplied += junkCount;
                _logger.LogDebug("Injected {Count} junk types", junkCount);
            }

            _logger.LogInformation("Applied {Count} anti-decompiler protections", stats.ProtectionsApplied);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anti-decompiler injection failed");
            return Task.FromResult(ObfuscationResult.Failed($"Anti-decompiler injection failed: {ex.Message}", ex));
        }
    }

    /// <summary>
    /// Adds the SuppressIldasm attribute to the assembly to block ILDasm.
    /// </summary>
    private bool InjectSuppressIldasmAttribute(ModuleDef module)
    {
        // Check if already present
        var existingAttr = module.Assembly.CustomAttributes
            .FirstOrDefault(a => a.TypeFullName == "System.Runtime.CompilerServices.SuppressIldasmAttribute");

        if (existingAttr != null)
        {
            _logger.LogDebug("SuppressIldasm attribute already present");
            return false;
        }

        // Create reference to SuppressIldasmAttribute
        var attrType = new TypeRefUser(
            module,
            "System.Runtime.CompilerServices",
            "SuppressIldasmAttribute",
            module.CorLibTypes.AssemblyRef);

        var ctor = new MemberRefUser(
            module,
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void),
            attrType);

        var attr = new CustomAttribute(ctor);
        module.Assembly.CustomAttributes.Add(attr);

        return true;
    }

    /// <summary>
    /// Injects junk types with confusing methods to clutter decompiler output.
    /// </summary>
    private int InjectJunkTypes(ModuleDef module, AntiDecompilerSettings settings)
    {
        var count = 0;

        for (int i = 0; i < settings.JunkTypeCount; i++)
        {
            var junkType = CreateJunkType(module, i, settings.JunkMethodsPerType);
            module.Types.Add(junkType);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Creates a junk type with obfuscated name and complex-looking methods.
    /// </summary>
    private TypeDef CreateJunkType(ModuleDef module, int index, int methodCount)
    {
        var typeName = GenerateConfusingName(index);

        var typeDef = new TypeDefUser(
            "Obfy.Internal",
            typeName,
            module.CorLibTypes.Object.TypeDefOrRef);

        // Make it internal and sealed
        typeDef.Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed;

        // Add junk fields
        AddJunkFields(typeDef, module);

        // Add junk methods
        for (int i = 0; i < methodCount; i++)
        {
            var method = CreateJunkMethod(module, i);
            typeDef.Methods.Add(method);
        }

        // Add a static constructor with some confusing code
        var cctor = CreateJunkStaticConstructor(module);
        typeDef.Methods.Add(cctor);

        return typeDef;
    }

    /// <summary>
    /// Adds junk fields to confuse analysis.
    /// </summary>
    private void AddJunkFields(TypeDef type, ModuleDef module)
    {
        // Add a few confusing static fields
        var field1 = new FieldDefUser(
            GenerateConfusingName(100),
            new FieldSig(module.CorLibTypes.Int32),
            FieldAttributes.Private | FieldAttributes.Static);
        type.Fields.Add(field1);

        var field2 = new FieldDefUser(
            GenerateConfusingName(101),
            new FieldSig(module.CorLibTypes.String),
            FieldAttributes.Private | FieldAttributes.Static);
        type.Fields.Add(field2);

        var field3 = new FieldDefUser(
            GenerateConfusingName(102),
            new FieldSig(new SZArraySig(module.CorLibTypes.Byte)),
            FieldAttributes.Private | FieldAttributes.Static);
        type.Fields.Add(field3);
    }

    /// <summary>
    /// Creates a junk method with complex-looking but valid IL.
    /// </summary>
    private MethodDef CreateJunkMethod(ModuleDef module, int index)
    {
        var methodName = GenerateConfusingName(index + 200);

        var method = new MethodDefUser(
            methodName,
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.Int32, module.CorLibTypes.Int32),
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        // Add several local variables
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // loc_0
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // loc_1
        body.Variables.Add(new Local(module.CorLibTypes.Int32)); // loc_2
        body.Variables.Add(new Local(module.CorLibTypes.Boolean)); // loc_3

        var instructions = body.Instructions;

        // Generate complex-looking but valid IL
        // This creates a method that does some math operations
        // but is never called, just adds noise

        // Initialize locals
        instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        instructions.Add(Instruction.Create(OpCodes.Stloc_0));
        instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        instructions.Add(Instruction.Create(OpCodes.Stloc_1));

        // Create a loop-like structure
        var loopStart = Instruction.Create(OpCodes.Ldloc_0);
        var loopEnd = Instruction.Create(OpCodes.Ldloc_2);

        instructions.Add(loopStart);
        instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        instructions.Add(Instruction.Create(OpCodes.Bge_S, loopEnd));

        // Loop body - some math operations
        instructions.Add(Instruction.Create(OpCodes.Ldloc_1));
        instructions.Add(Instruction.Create(OpCodes.Ldloc_0));
        instructions.Add(Instruction.Create(OpCodes.Add));
        instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        instructions.Add(Instruction.Create(OpCodes.Xor));
        instructions.Add(Instruction.Create(OpCodes.Stloc_1));

        // Increment loop counter
        instructions.Add(Instruction.Create(OpCodes.Ldloc_0));
        instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        instructions.Add(Instruction.Create(OpCodes.Add));
        instructions.Add(Instruction.Create(OpCodes.Stloc_0));

        // Loop back
        instructions.Add(Instruction.Create(OpCodes.Br_S, loopStart));

        // After loop
        instructions.Add(loopEnd);
        instructions.Add(Instruction.Create(OpCodes.Ldloc_1));
        instructions.Add(Instruction.Create(OpCodes.Stloc_2));

        // Some conditional logic
        instructions.Add(Instruction.Create(OpCodes.Ldloc_2));
        instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 0xFF));
        instructions.Add(Instruction.Create(OpCodes.And));

        // Return result
        instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        body.OptimizeBranches();

        return method;
    }

    /// <summary>
    /// Creates a static constructor with confusing initialization code.
    /// </summary>
    private MethodDef CreateJunkStaticConstructor(ModuleDef module)
    {
        var cctor = new MethodDefUser(
            ".cctor",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName |
            MethodAttributes.RTSpecialName);

        var body = new CilBody { InitLocals = true };
        cctor.Body = body;

        body.Variables.Add(new Local(module.CorLibTypes.Int32));

        var instructions = body.Instructions;

        // Some confusing but valid initialization
        instructions.Add(Instruction.Create(OpCodes.Ldc_I4, _random.Next(1000, 9999)));
        instructions.Add(Instruction.Create(OpCodes.Ldc_I4, _random.Next(100, 999)));
        instructions.Add(Instruction.Create(OpCodes.Xor));
        instructions.Add(Instruction.Create(OpCodes.Stloc_0));

        // Another operation
        instructions.Add(Instruction.Create(OpCodes.Ldloc_0));
        instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 0x5A5A));
        instructions.Add(Instruction.Create(OpCodes.And));
        instructions.Add(Instruction.Create(OpCodes.Pop));

        instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();

        return cctor;
    }

    /// <summary>
    /// Generates a confusing name using zero-width and similar-looking characters.
    /// </summary>
    private string GenerateConfusingName(int seed)
    {
        var length = _random.Next(6, 12);
        var chars = new char[length];

        // First character must be a valid identifier start
        chars[0] = '_';

        // Use seed to add some variation
        var localRandom = new Random(seed + _random.Next());

        for (var i = 1; i < length; i++)
        {
            chars[i] = ConfusingChars[localRandom.Next(ConfusingChars.Length)];
        }

        return new string(chars);
    }
}

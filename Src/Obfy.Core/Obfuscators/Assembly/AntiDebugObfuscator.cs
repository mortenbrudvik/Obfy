using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Injects anti-debugging protection into assemblies.
/// </summary>
public class AntiDebugObfuscator : IObfuscator
{
    private readonly ILogger<AntiDebugObfuscator> _logger;

    public AntiDebugObfuscator(ILogger<AntiDebugObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "AntiDebug";

    /// <inheritdoc/>
    public int Priority => 70;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.Protection.AntiDebug;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.Module!;
        var settings = context.Settings.Protection;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting anti-debug protection injection");

        try
        {
            if (settings.AntiDebug)
            {
                // Inject anti-debug type
                var antiDebugType = InjectAntiDebugType(module);

                // Add check to entry point
                if (module.EntryPoint != null)
                {
                    InjectDebuggerCheck(module.EntryPoint, antiDebugType);
                    stats.ProtectionsApplied++;
                }

                var moduleInitializer = FindModuleInitializer(module);
                if (moduleInitializer == null && module.EntryPoint == null)
                {
                    moduleInitializer = CreateModuleInitializer(module);
                }

                if (moduleInitializer != null)
                {
                    InjectDebuggerCheck(moduleInitializer, antiDebugType);
                    stats.ProtectionsApplied++;
                }
            }

            _logger.LogInformation("Applied {Count} anti-debug protections", stats.ProtectionsApplied);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anti-debug injection failed");
            return Task.FromResult(ObfuscationResult.Failed($"Anti-debug injection failed: {ex.Message}", ex));
        }
    }

    private TypeDef InjectAntiDebugType(ModuleDef module)
    {
        // Create internal static class for anti-debug
        var typeDef = new TypeDefUser(
            "Obfy.Runtime",
            "<AntiDebug>",
            module.CorLibTypes.Object.TypeDefOrRef);

        typeDef.Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract;

        // Add CheckDebugger method (self-contained: detects a debugger and exits the process)
        var checkMethod = CreateCheckDebuggerMethod(module);
        typeDef.Methods.Add(checkMethod);

        module.Types.Add(typeDef);

        return typeDef;
    }

    private MethodDef CreateCheckDebuggerMethod(ModuleDef module)
    {
        var method = new MethodDefUser(
            "Check",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;

        // Get Debugger.IsAttached property
        var debuggerType = module.CorLibTypes.GetTypeRef("System.Diagnostics", "Debugger");
        var isAttachedGetter = new MemberRefUser(
            module,
            "get_IsAttached",
            MethodSig.CreateStatic(module.CorLibTypes.Boolean),
            debuggerType);

        // Get Environment.Exit method
        var environmentType = module.CorLibTypes.GetTypeRef("System", "Environment");
        var exitMethod = new MemberRefUser(
            module,
            "Exit",
            MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.Int32),
            environmentType);

        // Create instructions: if (Debugger.IsAttached) Environment.Exit(1);
        var skipExit = Instruction.Create(OpCodes.Ret);

        body.Instructions.Add(Instruction.Create(OpCodes.Call, isAttachedGetter));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse_S, skipExit));
        body.Instructions.Add(Instruction.CreateLdcI4(1));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, exitMethod));
        body.Instructions.Add(skipExit);

        body.UpdateInstructionOffsets();

        return method;
    }

    private void InjectDebuggerCheck(MethodDef method, TypeDef antiDebugType)
    {
        if (!method.HasBody)
            return;

        var checkMethod = antiDebugType.FindMethod("Check");
        if (checkMethod == null)
            return;

        var body = method.Body;
        var instructions = body.Instructions;

        // Insert call to Check at the beginning
        instructions.Insert(0, Instruction.Create(OpCodes.Call, checkMethod));

        body.UpdateInstructionOffsets();
    }

    private MethodDef? FindModuleInitializer(ModuleDef module)
    {
        var globalType = module.GlobalType;
        if (globalType == null)
            return null;

        return globalType.Methods.FirstOrDefault(m =>
            m.IsStaticConstructor ||
            m.Name == ".cctor");
    }

    private static MethodDef CreateModuleInitializer(ModuleDef module)
    {
        var globalType = module.GlobalType;
        if (globalType == null)
        {
            globalType = new TypeDefUser("", "<Module>", null)
            {
                Attributes = TypeAttributes.NotPublic
            };
            module.Types.Insert(0, globalType);
        }

        var cctor = new MethodDefUser(
            ".cctor",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName |
            MethodAttributes.RTSpecialName);

        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        cctor.Body = body;
        globalType.Methods.Add(cctor);
        return cctor;
    }
}

using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Obfy.Core.Utilities;

namespace Obfy.Core.Pipeline;

/// <summary>
/// Per-helper policy for types injected into <c>Obfy.Runtime</c>. Defaults match historical
/// behavior: flatten and rename helpers, never encrypt their IL, and never flatten the VM.
/// </summary>
public sealed class RuntimeHelperOptions
{
    public bool FlattenControlFlow { get; init; } = true;
    public bool Rename { get; init; } = true;
    public bool EncryptIl { get; init; }

    public static RuntimeHelperOptions Default { get; } = new();

    public static RuntimeHelperOptions Interpreter { get; } = new()
    {
        FlattenControlFlow = false,
        Rename = true,
        EncryptIl = false
    };
}

/// <summary>
/// Registers injected runtime helper types on <see cref="PipelineContext"/> and answers
/// flatten/rename/encrypt questions without stringly-typed type names.
/// </summary>
public static class RuntimeInjection
{
    public static void Register(PipelineContext context, TypeDef type, RuntimeHelperOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(type);
        context.InjectedHelpers[type] = options ?? RuntimeHelperOptions.Default;
    }

    public static void AddType(PipelineContext context, TypeDef type, RuntimeHelperOptions? options = null)
    {
        context.RequireModule().Types.Add(type);
        Register(context, type, options);
    }

    public static void PrependModuleInitializerCall(ModuleDef module, MethodDef method, bool requireBody = false)
    {
        var initializer = ObfuscatorHelpers.FindOrCreateModuleInitializer(module, requireBody);
        initializer.Body!.Instructions.Insert(0, Instruction.Create(OpCodes.Call, method));
        initializer.Body.UpdateInstructionOffsets();
    }

    public static bool ShouldFlattenControlFlow(PipelineContext context, TypeDef type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (context.InjectedHelpers.TryGetValue(current, out var options))
                return options.FlattenControlFlow;
        }

        return !ObfuscatorHelpers.IsRuntimeHelper(type) || type.Name != "<Vm>";
    }

    public static bool ShouldEncryptIl(PipelineContext context, TypeDef type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (context.InjectedHelpers.TryGetValue(current, out var options))
                return options.EncryptIl;
        }

        return !ObfuscatorHelpers.IsRuntimeHelper(type);
    }

    public static bool ShouldRename(PipelineContext context, TypeDef type)
    {
        if (ObfuscatorHelpers.IsPinnedAttributeType(type))
            return false;

        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (context.InjectedHelpers.TryGetValue(current, out var options))
                return options.Rename;
        }

        return true;
    }
}

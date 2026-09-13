using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Obfy.Core.Utilities;

namespace Obfy.Core.Pipeline;

/// <summary>
/// Per-helper flatten/rename/encrypt-IL policy. Default: flatten and rename, do not encrypt IL.
/// <see cref="Interpreter"/> is the same but does not flatten. <see cref="Pinned"/> leaves the
/// type untouched (watermark / detector-bait attributes).
/// </summary>
public readonly record struct RuntimeHelperOptions
{
    public bool FlattenControlFlow { get; init; } = true;
    public bool Rename { get; init; } = true;
    public bool EncryptIl { get; init; }

    public RuntimeHelperOptions()
    {
    }

    public static RuntimeHelperOptions Default { get; } = new();

    public static RuntimeHelperOptions Interpreter { get; } = new()
    {
        FlattenControlFlow = false,
        Rename = true,
        EncryptIl = false
    };

    public static RuntimeHelperOptions Pinned { get; } = new()
    {
        FlattenControlFlow = false,
        Rename = false,
        EncryptIl = false
    };
}

/// <summary>
/// Registers injected runtime helper types on <see cref="PipelineContext"/> and answers
/// flatten/rename/encrypt questions from that registry. Lookups walk
/// <see cref="TypeDef.DeclaringType"/> so nested types inherit the parent's policy.
/// Unregistered <c>Obfy.Runtime</c> types (including nested types of helpers) do not flatten
/// or encrypt IL — a missed <see cref="Register"/> cannot flatten the VM or XOR a decryptor.
/// </summary>
public static class RuntimeInjection
{
    public static void Register(PipelineContext context, TypeDef type, RuntimeHelperOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(type);
        var resolved = options ?? RuntimeHelperOptions.Default;
        if (context.InjectedHelperMap.TryGetValue(type, out var existing))
        {
            if (existing != resolved)
            {
                throw new InvalidOperationException(
                    $"Runtime helper '{type.FullName}' was already registered with different policy.");
            }

            return;
        }

        context.InjectedHelperMap.Add(type, resolved);
    }

    public static void AddType(PipelineContext context, TypeDef type, RuntimeHelperOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(type);
        context.RequireModule().Types.Add(type);
        Register(context, type, options);
    }

    public static void PrependModuleInitializerCall(ModuleDef module, MethodDef method)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(method);
        var initializer = ObfuscatorHelpers.FindOrCreateModuleInitializer(module, requireBody: true);
        if (initializer.Body is null)
        {
            throw new InvalidOperationException(
                "Cannot inject runtime helper: module initializer has no IL body (native or abstract .cctor).");
        }

        initializer.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, method));
        initializer.Body.UpdateInstructionOffsets();
    }

    public static bool ShouldFlattenControlFlow(PipelineContext context, TypeDef type)
    {
        if (TryGetOptions(context, type, out var options))
            return options.FlattenControlFlow;

        return !IsRuntimeHelperFamily(type);
    }

    public static bool ShouldEncryptIl(PipelineContext context, TypeDef type)
    {
        if (TryGetOptions(context, type, out var options))
            return options.EncryptIl;

        return !IsRuntimeHelperFamily(type);
    }

    public static bool ShouldRename(PipelineContext context, TypeDef type)
    {
        if (ObfuscatorHelpers.IsPinnedAttributeType(type))
            return false;

        if (TryGetOptions(context, type, out var options))
            return options.Rename;

        return true;
    }

    private static bool TryGetOptions(PipelineContext context, TypeDef type, out RuntimeHelperOptions options)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (context.InjectedHelperMap.TryGetValue(current, out options))
                return true;
        }

        options = default;
        return false;
    }

    private static bool IsRuntimeHelperFamily(TypeDef type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (ObfuscatorHelpers.IsRuntimeHelper(current))
                return true;
        }

        return false;
    }
}

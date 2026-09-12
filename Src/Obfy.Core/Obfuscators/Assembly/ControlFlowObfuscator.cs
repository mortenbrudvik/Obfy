using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Obfuscates control flow with basic-block / linear-chunk state machines and opaque predicates.
/// </summary>
public class ControlFlowObfuscator : IObfuscator
{
    private readonly ILogger<ControlFlowObfuscator> _logger;
    private readonly Random _random = new();

    public ControlFlowObfuscator(ILogger<ControlFlowObfuscator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "ControlFlow";

    /// <inheritdoc/>
    public int Priority => (int)ObfuscationPhase.ControlFlow;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.ControlFlow.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var settings = context.Settings.ControlFlow;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting control flow obfuscation with mode {Mode}, intensity {Intensity}",
            settings.Mode, settings.Intensity);

        try
        {
            foreach (var type in module.GetTypes())
            {
                var isHelper = ObfuscatorHelpers.IsRuntimeHelper(type);
                if (!isHelper && !ObfuscationAttributeRules.AllowType(type, context.Settings, ObfuscationFeature.ControlFlow, context.Warnings))
                    continue;

                var intensity = isHelper ? 100 : settings.Intensity;

                foreach (var method in type.Methods)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!CanObfuscateMethod(method))
                        continue;
                    if (!isHelper && !ObfuscationAttributeRules.AllowMethod(method, context.Settings, ObfuscationFeature.ControlFlow, context.Warnings))
                        continue;

                    try
                    {
                        var obfuscated = settings.Mode switch
                        {
                            ControlFlowMode.Switch => ApplySwitchFlattening(method, intensity, isHelper, context),
                            ControlFlowMode.OpaquePredicate => ApplyOpaquePredicates(method, intensity),
                            ControlFlowMode.Combined => ApplyCombined(method, intensity, isHelper, context),
                            _ => throw new ArgumentOutOfRangeException(
                                nameof(settings.Mode), settings.Mode, "Unknown ControlFlowMode.")
                        };

                        if (obfuscated)
                        {
                            stats.MethodsControlFlowObfuscated++;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Flattening mutates the body in place (Clear + rebuild, or incremental
                        // inserts). A throw mid-mutation can leave unverifiable IL; do not report
                        // Success with a "skipped" method whose body is half-rewritten.
                        _logger.LogError(ex, "Failed to obfuscate method {Method}", method.FullName);
                        return Task.FromResult(ObfuscationResult.Failed(
                            $"Control flow obfuscation failed on {method.FullName}: {ex.Message}", ex));
                    }
                }
            }

            _logger.LogInformation("Obfuscated control flow in {Count} methods", stats.MethodsControlFlowObfuscated);

            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Control flow obfuscation failed");
            return Task.FromResult(ObfuscationResult.Failed($"Control flow obfuscation failed: {ex.Message}", ex));
        }
    }

    private bool CanObfuscateMethod(MethodDef method)
    {
        if (!method.HasBody)
            return false;

        if (method.Body.Instructions.Count < 5)
            return false;

        if (method.IsConstructor || method.IsStaticConstructor)
            return false;

        if (method.IsPinvokeImpl)
            return false;

        // calli trampolines (reference proxies) must stay a ldftn+calli+ret shape.
        foreach (var instr in method.Body.Instructions)
        {
            if (instr.OpCode == OpCodes.Calli)
                return false;
        }

        return true;
    }

    private bool ApplySwitchFlattening(MethodDef method, int intensity, bool isHelper, PipelineContext context)
    {
        if (method.Body.HasExceptionHandlers)
        {
            // Flattening rebuilds the body and would invalidate EH ranges. Opaque predicates
            // still raise the bar for try/catch/using/await methods.
            return ApplyOpaquePredicates(method, intensity);
        }

        if (method.Body.Instructions.Count < 10)
            return isHelper && ApplyOpaquePredicates(method, intensity);

        if (_random.Next(100) > intensity)
            return false;

        if (ControlFlowFlattener.Flatten(method, _random))
            return true;

        var instructions = method.Body.Instructions;
        var lastIndex = instructions.Count - 1;
        for (var i = 0; i < lastIndex; i++)
        {
            if (instructions[i].OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch
                or FlowControl.Return or FlowControl.Throw)
            {
                return isHelper && ApplyOpaquePredicates(method, intensity);
            }
        }

        return FlattenLinearMethod(method);
    }

    private bool FlattenLinearMethod(MethodDef method)
    {
        var body = method.Body;
        var instructions = body.Instructions;
        if (instructions.Count < 8)
            return false;

        var ret = instructions.Last();
        if (ret.OpCode != OpCodes.Ret)
            return false;

        const int chunkSize = 4;
        var work = instructions.Take(instructions.Count - 1).ToList();
        if (work.Count < 4)
            return false;

        // The dispatcher is re-entered via `br dispatcher` between chunks, so every chunk boundary
        // that precedes such a branch MUST sit on an empty evaluation stack; otherwise the dispatcher
        // is reachable at differing stack depths and the JIT rejects the body as unverifiable.
        // Compute the running stack depth after each instruction and split only at depth 0.
        var depthAfter = new int[work.Count];
        var depth = 0;
        foreach (var (instr, index) in work.Select((instr, index) => (instr, index)))
        {
            instr.CalculateStackUsage(out var pushes, out var pops);
            depth = depth - pops + pushes;
            if (depth < 0)
                return false; // unexpected underflow — refuse rather than emit invalid IL
            depthAfter[index] = depth;
        }

        // Greedily group instructions into chunks that each end on an empty stack (targeting chunkSize).
        // The final trailing segment falls through to `ret` and need not end on an empty stack (it may
        // carry the single return value that ret consumes). Prefix opcodes (volatile., constrained.,
        // tail., ...) have stack delta 0, so never split after one — the prefix must stay glued to
        // the instruction it modifies.
        var chunks = new List<List<Instruction>>();
        var current = new List<Instruction>();
        for (var i = 0; i < work.Count; i++)
        {
            current.Add(work[i]);
            if (depthAfter[i] == 0 &&
                current.Count >= chunkSize &&
                work[i].OpCode.FlowControl != FlowControl.Meta)
            {
                chunks.Add(current);
                current = new List<Instruction>();
            }
        }
        if (current.Count > 0)
            chunks.Add(current);

        // Flattening only adds value with multiple chunks. Leave the method untouched unless there
        // are at least two chunks (an empty-stack boundary at or after chunkSize instructions).
        if (chunks.Count < 2)
            return false;

        var states = new int[chunks.Count];
        var used = new HashSet<int>();
        for (var i = 0; i < chunks.Count; i++)
        {
            int state;
            do
            {
                state = _random.Next(1, 1_000_000);
            } while (!used.Add(state));
            states[i] = state;
        }

        var stateVar = new Local(method.Module.CorLibTypes.Int32);
        body.Variables.Add(stateVar);
        body.InitLocals = true;

        var dispatcher = Instruction.Create(OpCodes.Nop);
        var blockHeads = chunks.Select(_ => Instruction.Create(OpCodes.Nop)).ToList();

        var rebuilt = new List<Instruction>
        {
            Instruction.CreateLdcI4(states[0]),
            Instruction.Create(OpCodes.Stloc, stateVar),
            dispatcher
        };

        for (var c = 0; c < chunks.Count; c++)
        {
            rebuilt.Add(Instruction.Create(OpCodes.Ldloc, stateVar));
            rebuilt.Add(Instruction.CreateLdcI4(states[c]));
            rebuilt.Add(Instruction.Create(OpCodes.Beq, blockHeads[c]));
        }

        rebuilt.Add(Instruction.Create(OpCodes.Br, blockHeads[0]));

        for (var c = 0; c < chunks.Count; c++)
        {
            rebuilt.Add(blockHeads[c]);
            rebuilt.AddRange(chunks[c]);
            if (c + 1 < chunks.Count)
            {
                rebuilt.Add(Instruction.CreateLdcI4(states[c + 1]));
                rebuilt.Add(Instruction.Create(OpCodes.Stloc, stateVar));
                rebuilt.Add(Instruction.Create(OpCodes.Br, dispatcher));
            }
        }

        rebuilt.Add(Instruction.Create(OpCodes.Ret));
        instructions.Clear();
        foreach (var instr in rebuilt)
            instructions.Add(instr);

        body.KeepOldMaxStack = true;
        body.MaxStack = (ushort)Math.Max(body.MaxStack, (ushort)8);
        body.UpdateInstructionOffsets();
        return true;
    }

    private bool ApplyOpaquePredicates(MethodDef method, int intensity)
    {
        var body = method.Body;
        var instructions = body.Instructions;
        var insertCount = 0;

        // Insert opaque predicates at random positions
        var positions = new List<int>();
        for (var i = 0; i < instructions.Count - 1; i++)
        {
            if (i > 0 && ObfuscatorHelpers.IsPrefix(instructions[i - 1]))
                continue;

            if (IsExceptionHandlerBoundary(body, instructions[i]))
                continue;

            if (_random.Next(100) < Math.Clamp(intensity, 1, 100))
            {
                positions.Add(i);
            }
        }

        // Insert in reverse order to maintain indices
        positions.Reverse();

        foreach (var pos in positions)
        {
            if (pos >= instructions.Count)
                continue;

            InsertOpaquePredicate(method, pos);
            insertCount++;
        }

        body.KeepOldMaxStack = true;
        body.MaxStack = (ushort)Math.Max(body.MaxStack, (ushort)8);
        body.UpdateInstructionOffsets();

        return insertCount > 0;
    }

    private bool ApplyCombined(MethodDef method, int intensity, bool isHelper, PipelineContext context)
    {
        var result1 = ApplySwitchFlattening(method, intensity, isHelper, context);
        var result2 = ApplyOpaquePredicates(method, isHelper ? intensity : intensity / 2);
        return result1 || result2;
    }

    private static bool IsExceptionHandlerBoundary(CilBody body, Instruction instruction)
    {
        foreach (var eh in body.ExceptionHandlers)
        {
            if (eh.TryStart == instruction || eh.TryEnd == instruction ||
                eh.HandlerStart == instruction || eh.HandlerEnd == instruction ||
                eh.FilterStart == instruction)
            {
                return true;
            }
        }

        return false;
    }

    private void InsertOpaquePredicate(MethodDef method, int position)
    {
        var instructions = method.Body.Instructions;
        var target = instructions[position];
        var module = method.Module;

        var env = module.CorLibTypes.GetTypeRef("System", "Environment");
        var getTickCount = new MemberRefUser(
            module,
            "get_TickCount",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            env);
        var getProcessorCount = new MemberRefUser(
            module,
            "get_ProcessorCount",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            env);
        var getThreadId = new MemberRefUser(
            module,
            "get_CurrentManagedThreadId",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            env);
        MemberRefUser? getTickCount64 = null;
        var hasTickCount64 = FrameworkReferences.SupportsTickCount64(module);
        if (hasTickCount64)
        {
            getTickCount64 = new MemberRefUser(
                module,
                "get_TickCount64",
                MethodSig.CreateStatic(module.CorLibTypes.Int64),
                env);
        }

        var gcType = new TypeRefUser(module, "System", "GC", module.CorLibTypes.AssemblyRef);
        var getMaxGeneration = new MemberRefUser(
            module,
            "get_MaxGeneration",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            gcType);

        var dead = new[]
        {
            Instruction.CreateLdcI4(_random.Next(1, 1000)),
            Instruction.Create(OpCodes.Pop),
            Instruction.Create(OpCodes.Br, target)
        };

        // Runtime values so they are not compile-time constants. A decompiler may still see them as opaque.
        var formCount = hasTickCount64 ? 8 : 7;
        Instruction[] predicate = _random.Next(formCount) switch
        {
            0 =>
            [
                Instruction.Create(OpCodes.Call, getTickCount),
                Instruction.Create(OpCodes.Dup),
                Instruction.Create(OpCodes.Not),
                Instruction.Create(OpCodes.Or),
                Instruction.Create(OpCodes.Ldc_I4_M1),
                Instruction.Create(OpCodes.Ceq),
                Instruction.Create(OpCodes.Brtrue, target)
            ],
            1 =>
            [
                Instruction.Create(OpCodes.Call, getTickCount),
                Instruction.Create(OpCodes.Dup),
                Instruction.Create(OpCodes.Xor),
                Instruction.Create(OpCodes.Brfalse, target)
            ],
            2 =>
            [
                Instruction.Create(OpCodes.Call, getTickCount),
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Or),
                Instruction.Create(OpCodes.Brtrue, target)
            ],
            3 =>
            [
                Instruction.Create(OpCodes.Call, getTickCount),
                Instruction.Create(OpCodes.Dup),
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Add),
                Instruction.Create(OpCodes.Mul),
                Instruction.Create(OpCodes.Ldc_I4_2),
                Instruction.Create(OpCodes.Rem),
                Instruction.Create(OpCodes.Brfalse, target)
            ],
            4 =>
            [
                Instruction.Create(OpCodes.Call, getProcessorCount),
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Or),
                Instruction.Create(OpCodes.Brtrue, target)
            ],
            5 =>
            [
                Instruction.Create(OpCodes.Call, getThreadId),
                Instruction.Create(OpCodes.Dup),
                Instruction.Create(OpCodes.Xor),
                Instruction.Create(OpCodes.Brfalse, target)
            ],
            6 when hasTickCount64 =>
            [
                Instruction.Create(OpCodes.Call, getTickCount64!),
                Instruction.Create(OpCodes.Dup),
                Instruction.Create(OpCodes.Xor),
                Instruction.Create(OpCodes.Brfalse, target)
            ],
            _ =>
            [
                Instruction.Create(OpCodes.Call, getMaxGeneration),
                Instruction.Create(OpCodes.Ldc_I4_0),
                Instruction.Create(OpCodes.Clt),
                Instruction.Create(OpCodes.Brfalse, target)
            ]
        };

        var inserted = predicate.Concat(dead).ToArray();
        for (var i = inserted.Length - 1; i >= 0; i--)
            instructions.Insert(position, inserted[i]);
    }
}

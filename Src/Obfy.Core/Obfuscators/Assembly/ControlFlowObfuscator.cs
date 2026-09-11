using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Obfuscates control flow by converting linear code to state machines.
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
                if (ObfuscatorHelpers.IsRuntimeOrExcluded(type, context.Settings.Exclusions))
                    continue;

                foreach (var method in type.Methods)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!CanObfuscateMethod(method))
                    {
                        // Eligible-looking methods with exception handlers are left in the clear;
                        // record the skip so CLI/UI do not report a clean run.
                        if (method.HasBody &&
                            method.Body.Instructions.Count >= 5 &&
                            !method.IsConstructor &&
                            !method.IsStaticConstructor &&
                            method.Body.HasExceptionHandlers)
                        {
                            context.SkippedItems.Add(
                                SkippedItem.UnsupportedMethod(method.FullName, "Exception handlers"));
                        }
                        continue;
                    }

                    try
                    {
                        var obfuscated = settings.Mode switch
                        {
                            ControlFlowMode.Switch => ApplySwitchFlattening(method, settings.Intensity),
                            ControlFlowMode.OpaquePredicate => ApplyOpaquePredicates(method, settings.Intensity),
                            ControlFlowMode.Combined => ApplyCombined(method, settings.Intensity),
                            _ => false
                        };

                        if (obfuscated)
                        {
                            stats.MethodsControlFlowObfuscated++;
                        }
                    }
                    catch (Exception ex)
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
        catch (Exception ex)
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

        // Skip methods with exception handlers (complex to transform)
        if (method.Body.HasExceptionHandlers)
            return false;

        // Skip constructors
        if (method.IsConstructor || method.IsStaticConstructor)
            return false;

        return true;
    }

    private bool ApplySwitchFlattening(MethodDef method, int intensity)
    {
        var body = method.Body;
        var instructions = body.Instructions;

        if (instructions.Count < 10)
            return false;

        if (_random.Next(100) > intensity)
            return false;

        // Only straight-line code can be safely flattened. Bail on anything that branches, throws,
        // or returns early (other than the trailing ret handled below): those create extra edges or
        // dead code that would invalidate the linear stack-depth analysis in FlattenLinearMethod.
        var lastIndex = instructions.Count - 1;
        for (var i = 0; i < lastIndex; i++)
        {
            if (instructions[i].OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch
                or FlowControl.Return or FlowControl.Throw)
            {
                return false;
            }
        }

        return FlattenLinearMethod(method);
    }

    private static bool FlattenLinearMethod(MethodDef method)
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

        var stateVar = new Local(method.Module.CorLibTypes.Int32);
        body.Variables.Add(stateVar);
        body.InitLocals = true;

        var dispatcher = Instruction.Create(OpCodes.Ldloc, stateVar);
        var targets = chunks.Select(_ => Instruction.Create(OpCodes.Nop)).ToList();
        var switchInstr = Instruction.Create(OpCodes.Switch, targets.ToArray());

        var rebuilt = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stloc, stateVar),
            dispatcher,
            switchInstr
        };

        for (var c = 0; c < chunks.Count; c++)
        {
            rebuilt.Add(targets[c]);
            rebuilt.AddRange(chunks[c]);
            if (c + 1 < chunks.Count)
            {
                rebuilt.Add(Instruction.CreateLdcI4(c + 1));
                rebuilt.Add(Instruction.Create(OpCodes.Stloc, stateVar));
                rebuilt.Add(Instruction.Create(OpCodes.Br, dispatcher));
            }
        }

        rebuilt.Add(Instruction.Create(OpCodes.Ret));
        instructions.Clear();
        foreach (var instr in rebuilt)
            instructions.Add(instr);

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
            if (_random.Next(100) < intensity / 5) // Scale down intensity
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

            InsertOpaquePredicate(body, pos);
            insertCount++;
        }

        body.UpdateInstructionOffsets();

        return insertCount > 0;
    }

    private bool ApplyCombined(MethodDef method, int intensity)
    {
        var result1 = ApplySwitchFlattening(method, intensity);
        var result2 = ApplyOpaquePredicates(method, intensity / 2);
        return result1 || result2;
    }

    private void InsertOpaquePredicate(CilBody body, int position)
    {
        var instructions = body.Instructions;
        var target = instructions[position];

        // Create an opaque predicate: (x * x >= 0) is always true
        // This adds complexity without changing behavior

        var skipLabel = Instruction.Create(OpCodes.Nop);

        // Insert: if ((random_constant * random_constant) >= 0) goto original
        // This is always true for any real number
        var constant = _random.Next(1, 100);

        var newInstructions = new[]
        {
            Instruction.CreateLdcI4(constant),
            Instruction.CreateLdcI4(constant),
            Instruction.Create(OpCodes.Mul),
            Instruction.CreateLdcI4(0),
            Instruction.Create(OpCodes.Bge_S, target)
        };

        for (var i = newInstructions.Length - 1; i >= 0; i--)
        {
            instructions.Insert(position, newInstructions[i]);
        }
    }

}

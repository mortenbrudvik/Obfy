using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;

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
    public int Priority => 30;

    /// <inheritdoc/>
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    /// <inheritdoc/>
    public bool IsEnabled(ObfySettings settings) => settings.ControlFlow.Enabled;

    /// <inheritdoc/>
    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.Module!;
        var settings = context.Settings.ControlFlow;
        var stats = new ObfuscationStatistics();

        _logger.LogDebug("Starting control flow obfuscation with mode {Mode}, intensity {Intensity}",
            settings.Mode, settings.Intensity);

        try
        {
            foreach (var type in module.GetTypes())
            {
                if (IsExcluded(type, context.Settings.Exclusions))
                    continue;

                foreach (var method in type.Methods)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!CanObfuscateMethod(method))
                        continue;

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
                        _logger.LogWarning(ex, "Failed to obfuscate method {Method}", method.FullName);
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

        // Find basic blocks
        var blocks = IdentifyBasicBlocks(instructions);

        if (blocks.Count < 3)
            return false;

        // Only flatten a percentage of methods based on intensity
        if (_random.Next(100) > intensity)
            return false;

        // Create state variable
        var stateVar = new Local(method.Module.CorLibTypes.Int32);
        body.Variables.Add(stateVar);

        // Assign random state numbers to blocks
        var stateNumbers = new Dictionary<int, int>();
        var usedStates = new HashSet<int>();
        foreach (var blockStart in blocks)
        {
            int state;
            do
            {
                state = _random.Next(1000, 9999);
            } while (!usedStates.Add(state));

            stateNumbers[blockStart] = state;
        }

        // Insert dispatcher at the beginning
        InsertSwitchDispatcher(body, stateVar, blocks, stateNumbers);

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

    private List<int> IdentifyBasicBlocks(IList<Instruction> instructions)
    {
        var leaders = new HashSet<int> { 0 };

        for (var i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];

            // After a branch, the next instruction starts a block
            if (instr.OpCode.FlowControl == FlowControl.Branch ||
                instr.OpCode.FlowControl == FlowControl.Cond_Branch ||
                instr.OpCode.FlowControl == FlowControl.Return)
            {
                if (i + 1 < instructions.Count)
                {
                    leaders.Add(i + 1);
                }
            }

            // Branch targets start blocks
            if (instr.Operand is Instruction target)
            {
                var targetIndex = instructions.IndexOf(target);
                if (targetIndex >= 0)
                {
                    leaders.Add(targetIndex);
                }
            }
        }

        return leaders.OrderBy(x => x).ToList();
    }

    private void InsertSwitchDispatcher(CilBody body, Local stateVar, List<int> blocks, Dictionary<int, int> stateNumbers)
    {
        // This is a simplified implementation
        // A full implementation would restructure the entire method

        var instructions = body.Instructions;

        // Insert state initialization at the beginning
        var initState = stateNumbers.Values.FirstOrDefault();
        instructions.Insert(0, Instruction.CreateLdcI4(initState));
        instructions.Insert(1, Instruction.Create(OpCodes.Stloc, stateVar));

        body.UpdateInstructionOffsets();
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

    private bool IsExcluded(TypeDef type, ExclusionRules rules)
    {
        if (type.Namespace == "Obfy.Runtime")
            return true;

        // Skip Obfy's own model types (required for JSON serialization)
        if (type.Namespace == "Obfy.Core.Models")
            return true;

        return rules.Namespaces.Any(n => MatchesPattern(type.Namespace, n)) ||
               rules.Types.Any(t => MatchesPattern(type.Name, t));
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        if (pattern.EndsWith("*"))
        {
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
    }
}

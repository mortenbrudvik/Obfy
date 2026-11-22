using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Obfy.Core.Utilities;

/// <summary>
/// Switch-dispatches a method's basic blocks. Requires empty evaluation stack at every block entry
/// (verifiable IL merge points) and no exception handlers.
/// </summary>
internal static class ControlFlowFlattener
{
    public static bool Flatten(MethodDef method, Random random)
    {
        var body = method.Body;
        if (body == null || body.HasExceptionHandlers)
            return false;

        var instructions = body.Instructions;
        if (instructions.Count < 8)
            return false;

        var depthBefore = ComputeDepthBefore(method);
        if (depthBefore == null)
            return false;

        var leaders = CollectLeaders(instructions);
        var leaderList = leaders.ToList();
        if (leaderList.Count < 2)
            return false;

        var blocks = new List<(int Start, int End)>();
        for (var i = 0; i < leaderList.Count; i++)
        {
            var start = leaderList[i];
            var end = i + 1 < leaderList.Count ? leaderList[i + 1] : instructions.Count;
            if (end <= start)
                return false;
            blocks.Add((start, end));
        }

        foreach (var (start, end) in blocks)
        {
            if (depthBefore[start] != 0)
                return false;
            if (ObfuscatorHelpers.IsPrefix(instructions[end - 1]))
                return false;
        }

        for (var b = 0; b < blocks.Count; b++)
        {
            var last = instructions[blocks[b].End - 1];
            if (last.OpCode == OpCodes.Switch || last.Operand is IList<Instruction>)
                return false;
        }

        var states = new int[blocks.Count];
        var used = new HashSet<int>();
        for (var i = 0; i < blocks.Count; i++)
        {
            int state;
            do
            {
                state = random.Next(1, 1_000_000);
            } while (!used.Add(state));
            states[i] = state;
        }

        var instrToBlock = new Dictionary<Instruction, int>();
        for (var b = 0; b < blocks.Count; b++)
        {
            for (var i = blocks[b].Start; i < blocks[b].End; i++)
                instrToBlock[instructions[i]] = b;
        }

        var stateVar = new Local(method.Module.CorLibTypes.Int32);
        body.Variables.Add(stateVar);
        body.InitLocals = true;

        var dispatcher = Instruction.Create(OpCodes.Nop);
        var blockHeads = blocks.Select(_ => Instruction.Create(OpCodes.Nop)).ToList();

        var rebuilt = new List<Instruction>
        {
            Instruction.CreateLdcI4(states[0]),
            Instruction.Create(OpCodes.Stloc, stateVar),
            dispatcher
        };

        for (var b = 0; b < blocks.Count; b++)
        {
            rebuilt.Add(Instruction.Create(OpCodes.Ldloc, stateVar));
            rebuilt.Add(Instruction.CreateLdcI4(states[b]));
            rebuilt.Add(Instruction.Create(OpCodes.Beq, blockHeads[b]));
        }

        // Unknown state is unreachable; branch to the first block so value-returning methods
        // stay verifiable (a bare ret would be an empty-stack return of int/object/etc.).
        rebuilt.Add(Instruction.Create(OpCodes.Br, blockHeads[0]));

        for (var b = 0; b < blocks.Count; b++)
        {
            rebuilt.Add(blockHeads[b]);
            var (start, end) = blocks[b];
            var last = instructions[end - 1];
            var flow = last.OpCode.FlowControl;
            var copyEnd = flow is FlowControl.Branch or FlowControl.Cond_Branch ? end - 1 : end;

            for (var i = start; i < copyEnd; i++)
                rebuilt.Add(instructions[i]);

            if (flow is FlowControl.Return or FlowControl.Throw)
            {
                if (copyEnd != end)
                    rebuilt.Add(last);
            }
            else if (flow == FlowControl.Branch && last.Operand is Instruction target)
            {
                if (!instrToBlock.TryGetValue(target, out var tb))
                    return false;
                rebuilt.Add(Instruction.CreateLdcI4(states[tb]));
                rebuilt.Add(Instruction.Create(OpCodes.Stloc, stateVar));
                rebuilt.Add(Instruction.Create(OpCodes.Br, dispatcher));
            }
            else if (flow == FlowControl.Cond_Branch && last.Operand is Instruction condTarget)
            {
                if (!instrToBlock.TryGetValue(condTarget, out var tb))
                    return false;
                var fallthrough = b + 1;
                if (fallthrough >= blocks.Count)
                    return false;

                var trueHead = Instruction.Create(OpCodes.Nop);
                rebuilt.Add(Instruction.Create(last.OpCode, trueHead));
                rebuilt.Add(Instruction.CreateLdcI4(states[fallthrough]));
                rebuilt.Add(Instruction.Create(OpCodes.Stloc, stateVar));
                rebuilt.Add(Instruction.Create(OpCodes.Br, dispatcher));
                rebuilt.Add(trueHead);
                rebuilt.Add(Instruction.CreateLdcI4(states[tb]));
                rebuilt.Add(Instruction.Create(OpCodes.Stloc, stateVar));
                rebuilt.Add(Instruction.Create(OpCodes.Br, dispatcher));
            }
            else if (flow is not FlowControl.Branch and not FlowControl.Cond_Branch)
            {
                if (b + 1 < blocks.Count)
                {
                    rebuilt.Add(Instruction.CreateLdcI4(states[b + 1]));
                    rebuilt.Add(Instruction.Create(OpCodes.Stloc, stateVar));
                    rebuilt.Add(Instruction.Create(OpCodes.Br, dispatcher));
                }
                else if (last.OpCode != OpCodes.Ret)
                {
                    rebuilt.Add(Instruction.Create(OpCodes.Ret));
                }
            }
            else
            {
                return false;
            }
        }

        instructions.Clear();
        foreach (var instr in rebuilt)
            instructions.Add(instr);

        if (body.MaxStack == 0)
        {
            body.KeepOldMaxStack = false;
        }
        else if (body.KeepOldMaxStack)
        {
            body.MaxStack = (ushort)Math.Max((int)body.MaxStack, 8);
        }
        body.UpdateInstructionOffsets();
        return true;
    }

    private static SortedSet<int> CollectLeaders(IList<Instruction> instructions)
    {
        var leaders = new SortedSet<int> { 0 };
        for (var i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            var flow = instr.OpCode.FlowControl;
            if (flow is FlowControl.Branch or FlowControl.Cond_Branch)
            {
                switch (instr.Operand)
                {
                    case Instruction target:
                    {
                        var targetIndex = instructions.IndexOf(target);
                        if (targetIndex >= 0)
                            leaders.Add(targetIndex);
                        break;
                    }
                    case IList<Instruction> targets:
                        foreach (var target in targets)
                        {
                            if (target == null)
                                continue;
                            var targetIndex = instructions.IndexOf(target);
                            if (targetIndex >= 0)
                                leaders.Add(targetIndex);
                        }
                        break;
                }

                if (i + 1 < instructions.Count)
                    leaders.Add(i + 1);
            }
            else if (flow is FlowControl.Return or FlowControl.Throw)
            {
                if (i + 1 < instructions.Count)
                    leaders.Add(i + 1);
            }
        }

        return leaders;
    }

    internal const int UnreachableDepth = -1;

    internal static int[]? ComputeDepthBefore(MethodDef method)
    {
        var body = method.Body;
        if (body == null)
            return null;

        var instructions = body.Instructions;
        if (instructions.Count == 0)
            return Array.Empty<int>();

        var indexOf = new Dictionary<Instruction, int>(instructions.Count);
        for (var i = 0; i < instructions.Count; i++)
            indexOf[instructions[i]] = i;

        var depthBefore = new int[instructions.Count];
        Array.Fill(depthBefore, UnreachableDepth);

        var queue = new Queue<int>();

        bool Enqueue(int index, int depth)
        {
            if (index < 0 || index >= instructions.Count)
                return false;

            if (depthBefore[index] == UnreachableDepth)
            {
                depthBefore[index] = depth;
                queue.Enqueue(index);
                return true;
            }

            return depthBefore[index] == depth;
        }

        if (!Enqueue(0, 0))
            return null;

        if (body.HasExceptionHandlers)
        {
            foreach (var eh in body.ExceptionHandlers)
            {
                if (eh.TryStart != null && eh.TryStart != eh.TryEnd &&
                    indexOf.TryGetValue(eh.TryStart, out var tryIdx) && !Enqueue(tryIdx, 0))
                    return null;

                if (eh.FilterStart != null &&
                    indexOf.TryGetValue(eh.FilterStart, out var filterIdx) && !Enqueue(filterIdx, 1))
                    return null;

                if (eh.HandlerStart != null && eh.HandlerStart != eh.HandlerEnd &&
                    indexOf.TryGetValue(eh.HandlerStart, out var handlerIdx))
                {
                    var handlerDepth = eh.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter
                        ? 1
                        : 0;
                    if (!Enqueue(handlerIdx, handlerDepth))
                        return null;
                }
            }
        }

        var hasReturnValue = method.ReturnType?.RemovePinnedAndModifiers()?.ElementType is { } retType &&
                             retType != ElementType.Void;

        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            var instr = instructions[idx];
            var depth = depthBefore[idx];

            instr.CalculateStackUsage(hasReturnValue, out var pushes, out var pops);
            int nextDepth;
            if (pops < 0)
            {
                // StackBehaviour.PopAll (leave, leave.s, endfinally) clears the evaluation stack.
                nextDepth = pushes;
            }
            else
            {
                nextDepth = depth - pops + pushes;
                if (nextDepth < 0)
                    return null;
            }

            switch (instr.OpCode.FlowControl)
            {
                case FlowControl.Branch:
                    if (instr.Operand is not Instruction branchTarget ||
                        !indexOf.TryGetValue(branchTarget, out var branchIdx) ||
                        !Enqueue(branchIdx, nextDepth))
                        return null;
                    break;

                case FlowControl.Cond_Branch:
                    if (idx + 1 < instructions.Count && !Enqueue(idx + 1, nextDepth))
                        return null;

                    switch (instr.Operand)
                    {
                        case Instruction condTarget:
                            if (!indexOf.TryGetValue(condTarget, out var condIdx) || !Enqueue(condIdx, nextDepth))
                                return null;
                            break;
                        case IList<Instruction> switchTargets:
                            foreach (var target in switchTargets)
                            {
                                if (target == null ||
                                    !indexOf.TryGetValue(target, out var switchIdx) ||
                                    !Enqueue(switchIdx, nextDepth))
                                    return null;
                            }
                            break;
                    }
                    break;

                case FlowControl.Return:
                case FlowControl.Throw:
                    break;

                case FlowControl.Call:
                    if (instr.OpCode.Code != Code.Jmp && idx + 1 < instructions.Count && !Enqueue(idx + 1, nextDepth))
                        return null;
                    break;

                default:
                    if (idx + 1 < instructions.Count && !Enqueue(idx + 1, nextDepth))
                        return null;
                    break;
            }
        }

        return depthBefore;
    }
}

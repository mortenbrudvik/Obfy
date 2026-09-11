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

        var depthBefore = ComputeDepthBefore(instructions);
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

        body.KeepOldMaxStack = true;
        body.MaxStack = (ushort)Math.Max(body.MaxStack, (ushort)8);
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
                        leaders.Add(instructions.IndexOf(target));
                        break;
                    case IList<Instruction> targets:
                        foreach (var target in targets)
                            leaders.Add(instructions.IndexOf(target));
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

    private static int[]? ComputeDepthBefore(IList<Instruction> instructions)
    {
        var depthBefore = new int[instructions.Count];
        var depth = 0;
        for (var i = 0; i < instructions.Count; i++)
        {
            depthBefore[i] = depth;
            instructions[i].CalculateStackUsage(out var pushes, out var pops);
            depth = depth - pops + pushes;
            if (depth < 0)
                return null;
        }

        return depthBefore;
    }
}

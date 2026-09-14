using System.Buffers.Binary;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Obfy.Core.Virtualization;

/// <summary>
/// Encodes eligible CIL into the Obfy VM ISA. Does not mutate method bodies.
/// </summary>
public static class VmEncoder
{
    private const string IsByRefLikeAttribute = "System.Runtime.CompilerServices.IsByRefLikeAttribute";

    public static IReadOnlyList<MethodDef> Select(IEnumerable<MethodDef> candidates, int maxMethods)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (maxMethods <= 0)
            return Array.Empty<MethodDef>();

        var tables = new VmMemberTables();
        var ids = new Dictionary<MethodDef, int>();
        var selected = new List<MethodDef>();
        foreach (var method in candidates
                     .Where(static m => m is not null)
                     .OrderBy(static m => m.DeclaringType?.FullName ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(static m => m.FullName, StringComparer.Ordinal))
        {
            if (selected.Count >= maxMethods)
                break;
            if (TryEncode(method, ids, tables, out _, out _))
                selected.Add(method);
        }

        return selected;
    }

    public static bool TryEncode(
        MethodDef method,
        IReadOnlyDictionary<MethodDef, int> virtualizedIds,
        VmMemberTables tables,
        out byte[] code,
        out string? skipReason)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(virtualizedIds);
        ArgumentNullException.ThrowIfNull(tables);

        code = Array.Empty<byte>();
        skipReason = null;

        if (method.Body is null || !method.HasBody || method.IsAbstract || method.IsPinvokeImpl
            || method.IsNative || method.IsRuntime)
        {
            skipReason = VmSkipReasons.NoBody;
            return false;
        }

        if (method.IsConstructor)
        {
            skipReason = VmSkipReasons.Constructor;
            return false;
        }

        if (method.HasGenericParameters || (method.DeclaringType?.HasGenericParameters ?? false))
        {
            skipReason = VmSkipReasons.Generic;
            return false;
        }

        var body = method.Body;
        if (body.HasExceptionHandlers)
        {
            skipReason = VmSkipReasons.ExceptionHandlers;
            return false;
        }

        if (IsForbiddenByRef(method.MethodSig?.RetType))
        {
            skipReason = VmSkipReasons.ByRef;
            return false;
        }

        if (IsNonPrimitiveValueType(method.MethodSig?.RetType))
        {
            skipReason = VmSkipReasons.NonPrimitiveValuetypeLocal;
            return false;
        }

        foreach (var param in method.Parameters)
        {
            if (IsForbiddenByRef(param.Type))
            {
                skipReason = VmSkipReasons.ByRef;
                return false;
            }

            if (IsNonPrimitiveValueType(param.Type))
            {
                skipReason = VmSkipReasons.NonPrimitiveValuetypeLocal;
                return false;
            }
        }

        foreach (var local in body.Variables)
        {
            if (IsForbiddenByRef(local.Type))
            {
                skipReason = VmSkipReasons.ByRef;
                return false;
            }

            if (IsNonPrimitiveValueType(local.Type))
            {
                skipReason = VmSkipReasons.NonPrimitiveValuetypeLocal;
                return false;
            }
        }

        if (method.Parameters.Count > 256 || body.Variables.Count > 256)
        {
            skipReason = VmSkipReasons.IndexOutOfRange;
            return false;
        }

        var methodMark = tables.Methods.Count;
        var fieldMark = tables.Fields.Count;
        var typeMark = tables.Types.Count;
        var buffer = new List<byte>();
        var offsets = new Dictionary<Instruction, int>();
        var heightAt = new Dictionary<Instruction, int>();
        var fixups = new List<(int OperandIndex, Instruction Target)>();
        int? height = 0;
        VmOp? lastOp = null;

        foreach (var instr in body.Instructions)
        {
            if (heightAt.TryGetValue(instr, out var recorded))
            {
                if (height is int fallthrough && fallthrough != recorded)
                {
                    skipReason = VmSkipReasons.StackHeightMismatch;
                    tables.Rollback(methodMark, fieldMark, typeMark);
                    return false;
                }

                height = recorded;
            }
            else if (height is int fallthrough)
            {
                heightAt[instr] = fallthrough;
            }

            offsets[instr] = buffer.Count;

            var codeName = instr.OpCode.Code;
            if (codeName is Code.Nop or Code.Volatile or Code.Unaligned)
                continue;

            if (codeName is Code.Switch)
            {
                skipReason = VmSkipReasons.Switch;
                tables.Rollback(methodMark, fieldMark, typeMark);
                return false;
            }

            if (!TryWrite(
                    instr,
                    method,
                    virtualizedIds,
                    tables,
                    buffer,
                    out var pop,
                    out var push,
                    out var terminal,
                    out var target,
                    out var writeReason,
                    out var op))
            {
                skipReason = writeReason;
                tables.Rollback(methodMark, fieldMark, typeMark);
                return false;
            }

            lastOp = op;
            if (target is not null)
                fixups.Add((buffer.Count - 2, target));

            if (height is int current)
            {
                var next = current - pop + push;
                if (target is not null)
                {
                    if (heightAt.TryGetValue(target, out var existing))
                    {
                        if (existing != next)
                        {
                            skipReason = VmSkipReasons.StackHeightMismatch;
                            tables.Rollback(methodMark, fieldMark, typeMark);
                            return false;
                        }
                    }
                    else
                    {
                        heightAt[target] = next;
                    }
                }

                height = terminal ? null : next;
            }
        }

        foreach (var (operandIndex, target) in fixups)
        {
            if (!offsets.TryGetValue(target, out var dest))
            {
                skipReason = VmSkipReasons.InvalidBytecode;
                tables.Rollback(methodMark, fieldMark, typeMark);
                return false;
            }

            if (dest > ushort.MaxValue)
            {
                skipReason = VmSkipReasons.IndexOutOfRange;
                tables.Rollback(methodMark, fieldMark, typeMark);
                return false;
            }

            WriteU16At(buffer, operandIndex, (ushort)dest);
        }

        if (lastOp is not (VmOp.Ret or VmOp.Throw))
        {
            skipReason = VmSkipReasons.InvalidBytecode;
            tables.Rollback(methodMark, fieldMark, typeMark);
            return false;
        }

        code = buffer.ToArray();
        skipReason = null;
        return true;
    }

    private static bool TryWrite(
        Instruction instr,
        MethodDef method,
        IReadOnlyDictionary<MethodDef, int> virtualizedIds,
        VmMemberTables tables,
        List<byte> buffer,
        out int pop,
        out int push,
        out bool terminal,
        out Instruction? target,
        out string? skipReason,
        out VmOp op)
    {
        pop = 0;
        push = 0;
        terminal = false;
        target = null;
        skipReason = null;
        op = default;
        var codeName = instr.OpCode.Code;

        if (TryReadLdcI4(instr, out var i4))
            return WriteSimple(buffer, VmOp.LdcI4, 0, 1, out pop, out push, out op, WriteI32Action(i4));

        if (codeName is Code.Ldc_I8)
        {
            if (!TryReadI8(instr, out var i8))
                return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);
            return WriteSimple(buffer, VmOp.LdcI8, 0, 1, out pop, out push, out op, b => WriteI64(b, i8));
        }

        if (codeName is Code.Ldc_R4)
        {
            if (!TryReadR4(instr, out var r4))
                return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);
            return WriteSimple(buffer, VmOp.LdcR4, 0, 1, out pop, out push, out op, b => WriteI32(b, BitConverter.SingleToInt32Bits(r4)));
        }

        if (codeName is Code.Ldc_R8)
        {
            if (!TryReadR8(instr, out var r8))
                return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);
            return WriteSimple(buffer, VmOp.LdcR8, 0, 1, out pop, out push, out op, b => WriteI64(b, BitConverter.DoubleToInt64Bits(r8)));
        }

        if (codeName is Code.Ldnull)
            return WriteSimple(buffer, VmOp.Ldnull, 0, 1, out pop, out push, out op);

        if (codeName is Code.Ldstr)
        {
            var text = instr.Operand switch
            {
                string s => s,
                UTF8String utf => utf.String,
                _ => null
            };
            if (text is null)
                return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);

            var utf8 = Encoding.UTF8.GetBytes(text);
            if (utf8.Length > ushort.MaxValue)
                return Fail(VmSkipReasons.IndexOutOfRange, out skipReason);

            buffer.Add((byte)VmOp.Ldstr);
            WriteU16(buffer, (ushort)utf8.Length);
            buffer.AddRange(utf8);
            pop = 0;
            push = 1;
            op = VmOp.Ldstr;
            return true;
        }

        if (TryReadLdarg(instr, out var arg))
        {
            if (arg is < 0 or > 255)
                return Fail(VmSkipReasons.IndexOutOfRange, out skipReason);
            return WriteIndex(buffer, VmOp.Ldarg, arg, 0, 1, out pop, out push, out op);
        }

        if (TryReadStarg(instr, out arg))
        {
            if (arg is < 0 or > 255)
                return Fail(VmSkipReasons.IndexOutOfRange, out skipReason);
            return WriteIndex(buffer, VmOp.Starg, arg, 1, 0, out pop, out push, out op);
        }

        if (TryReadLdloc(instr, out var loc))
        {
            if (loc is < 0 or > 255)
                return Fail(VmSkipReasons.IndexOutOfRange, out skipReason);
            return WriteIndex(buffer, VmOp.Ldloc, loc, 0, 1, out pop, out push, out op);
        }

        if (TryReadStloc(instr, out loc))
        {
            if (loc is < 0 or > 255)
                return Fail(VmSkipReasons.IndexOutOfRange, out skipReason);
            return WriteIndex(buffer, VmOp.Stloc, loc, 1, 0, out pop, out push, out op);
        }

        if (TryBranch(codeName, out op, out pop, out terminal))
        {
            if (instr.Operand is not Instruction dest)
                return Fail(VmSkipReasons.InvalidBytecode, out skipReason);
            buffer.Add((byte)op);
            WriteU16(buffer, 0);
            target = dest;
            push = 0;
            return true;
        }

        if (TryBinary(codeName, out op))
            return WriteSimple(buffer, op, 2, 1, out pop, out push, out op);

        if (TryUnary(codeName, out op))
            return WriteSimple(buffer, op, 1, 1, out pop, out push, out op);

        switch (codeName)
        {
            case Code.Dup:
                return WriteSimple(buffer, VmOp.Dup, 0, 1, out pop, out push, out op);
            case Code.Pop:
                return WriteSimple(buffer, VmOp.Pop, 1, 0, out pop, out push, out op);
            case Code.Ldlen:
                return WriteSimple(buffer, VmOp.Ldlen, 1, 1, out pop, out push, out op);
            case Code.Throw:
                buffer.Add((byte)VmOp.Throw);
                pop = 1;
                push = 0;
                terminal = true;
                op = VmOp.Throw;
                return true;
            case Code.Ret:
                buffer.Add((byte)VmOp.Ret);
                pop = ReturnsVoid(method) ? 0 : 1;
                push = 0;
                terminal = true;
                op = VmOp.Ret;
                return true;
            case Code.Call:
                return TryWriteCall(instr, virtualizedIds, tables, isCallvirt: false, buffer,
                    out pop, out push, out skipReason, out op);
            case Code.Callvirt:
                return TryWriteCall(instr, virtualizedIds, tables, isCallvirt: true, buffer,
                    out pop, out push, out skipReason, out op);
            case Code.Newobj:
                return TryWriteNewobj(instr, tables, buffer, out pop, out push, out skipReason, out op);
            case Code.Ldfld:
                return TryWriteField(instr, tables, VmOp.Ldfld, 1, 1, buffer, out pop, out push, out skipReason, out op);
            case Code.Stfld:
                return TryWriteField(instr, tables, VmOp.Stfld, 2, 0, buffer, out pop, out push, out skipReason, out op);
            case Code.Ldsfld:
                return TryWriteField(instr, tables, VmOp.Ldsfld, 0, 1, buffer, out pop, out push, out skipReason, out op);
            case Code.Stsfld:
                return TryWriteField(instr, tables, VmOp.Stsfld, 1, 0, buffer, out pop, out push, out skipReason, out op);
            case Code.Box:
                return TryWriteType(instr, tables, VmOp.Box, 1, 1, buffer, out pop, out push, out skipReason, out op);
            case Code.Unbox_Any:
                return TryWriteType(instr, tables, VmOp.UnboxAny, 1, 1, buffer, out pop, out push, out skipReason, out op);
            case Code.Castclass:
                return TryWriteType(instr, tables, VmOp.Castclass, 1, 1, buffer, out pop, out push, out skipReason, out op);
            case Code.Isinst:
                return TryWriteType(instr, tables, VmOp.Isinst, 1, 1, buffer, out pop, out push, out skipReason, out op);
            case Code.Newarr:
                return TryWriteType(instr, tables, VmOp.Newarr, 1, 1, buffer, out pop, out push, out skipReason, out op);
            case Code.Ldelem_I4:
                return WriteSimple(buffer, VmOp.LdelemI4, 2, 1, out pop, out push, out op);
            case Code.Ldelem_I8:
                return WriteSimple(buffer, VmOp.LdelemI8, 2, 1, out pop, out push, out op);
            case Code.Ldelem_R4:
                return WriteSimple(buffer, VmOp.LdelemR4, 2, 1, out pop, out push, out op);
            case Code.Ldelem_R8:
                return WriteSimple(buffer, VmOp.LdelemR8, 2, 1, out pop, out push, out op);
            case Code.Ldelem_Ref:
                return WriteSimple(buffer, VmOp.LdelemRef, 2, 1, out pop, out push, out op);
            case Code.Stelem_I4:
                return WriteSimple(buffer, VmOp.StelemI4, 3, 0, out pop, out push, out op);
            case Code.Stelem_I8:
                return WriteSimple(buffer, VmOp.StelemI8, 3, 0, out pop, out push, out op);
            case Code.Stelem_R4:
                return WriteSimple(buffer, VmOp.StelemR4, 3, 0, out pop, out push, out op);
            case Code.Stelem_R8:
                return WriteSimple(buffer, VmOp.StelemR8, 3, 0, out pop, out push, out op);
            case Code.Stelem_Ref:
                return WriteSimple(buffer, VmOp.StelemRef, 3, 0, out pop, out push, out op);
            default:
                return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);
        }
    }

    private static bool TryWriteCall(
        Instruction instr,
        IReadOnlyDictionary<MethodDef, int> virtualizedIds,
        VmMemberTables tables,
        bool isCallvirt,
        List<byte> buffer,
        out int pop,
        out int push,
        out string? skipReason,
        out VmOp op)
    {
        pop = 0;
        push = 0;
        skipReason = null;
        op = default;

        if (instr.Operand is MethodSpec)
            return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);
        if (instr.Operand is not IMethod called)
            return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);

        var argc = ParameterCount(called);
        if (argc > 255)
            return Fail(VmSkipReasons.IndexOutOfRange, out skipReason);

        pop = argc;
        push = ReturnsVoid(called) ? 0 : 1;

        if (!isCallvirt && called is MethodDef def && virtualizedIds.TryGetValue(def, out var id))
        {
            if (id is < 0 or > ushort.MaxValue)
                return Fail(VmSkipReasons.IndexOutOfRange, out skipReason);

            op = VmOp.CallVm;
            buffer.Add((byte)VmOp.CallVm);
            WriteU16(buffer, (ushort)id);
            buffer.Add((byte)argc);
            return true;
        }

        op = isCallvirt ? VmOp.Callvirt : VmOp.Call;
        buffer.Add((byte)op);
        WriteU16(buffer, tables.AddMethod(called));
        return true;
    }

    private static bool TryWriteNewobj(
        Instruction instr,
        VmMemberTables tables,
        List<byte> buffer,
        out int pop,
        out int push,
        out string? skipReason,
        out VmOp op)
    {
        pop = 0;
        push = 0;
        skipReason = null;
        op = default;

        if (instr.Operand is MethodSpec)
            return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);
        if (instr.Operand is not IMethod ctor)
            return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);
        if (IsValueType(ctor.DeclaringType))
            return Fail(VmSkipReasons.ValuetypeNewobj, out skipReason);

        pop = ctor.MethodSig?.Params.Count ?? 0;
        push = 1;
        op = VmOp.Newobj;
        buffer.Add((byte)VmOp.Newobj);
        WriteU16(buffer, tables.AddMethod(ctor));
        return true;
    }

    private static bool TryWriteField(
        Instruction instr,
        VmMemberTables tables,
        VmOp opcode,
        int popCount,
        int pushCount,
        List<byte> buffer,
        out int pop,
        out int push,
        out string? skipReason,
        out VmOp op)
    {
        pop = popCount;
        push = pushCount;
        skipReason = null;
        op = opcode;
        if (instr.Operand is not IField field)
            return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);
        buffer.Add((byte)opcode);
        WriteU16(buffer, tables.AddField(field));
        return true;
    }

    private static bool TryWriteType(
        Instruction instr,
        VmMemberTables tables,
        VmOp opcode,
        int popCount,
        int pushCount,
        List<byte> buffer,
        out int pop,
        out int push,
        out string? skipReason,
        out VmOp op)
    {
        pop = popCount;
        push = pushCount;
        skipReason = null;
        op = opcode;
        if (instr.Operand is not ITypeDefOrRef type)
            return Fail(VmSkipReasons.UnsupportedOpcode, out skipReason);
        buffer.Add((byte)opcode);
        WriteU16(buffer, tables.AddType(type));
        return true;
    }

    private static bool WriteSimple(
        List<byte> buffer,
        VmOp opcode,
        int popCount,
        int pushCount,
        out int pop,
        out int push,
        out VmOp op,
        Action<List<byte>>? extra = null)
    {
        buffer.Add((byte)opcode);
        extra?.Invoke(buffer);
        pop = popCount;
        push = pushCount;
        op = opcode;
        return true;
    }

    private static Action<List<byte>> WriteI32Action(int value) => buffer => WriteI32(buffer, value);

    private static bool WriteIndex(
        List<byte> buffer,
        VmOp opcode,
        int index,
        int popCount,
        int pushCount,
        out int pop,
        out int push,
        out VmOp op)
    {
        buffer.Add((byte)opcode);
        buffer.Add((byte)index);
        pop = popCount;
        push = pushCount;
        op = opcode;
        return true;
    }

    private static bool Fail(string reason, out string? skipReason)
    {
        skipReason = reason;
        return false;
    }

    private static bool TryBinary(Code code, out VmOp op)
    {
        op = code switch
        {
            Code.Add => VmOp.Add,
            Code.Sub => VmOp.Sub,
            Code.Mul => VmOp.Mul,
            Code.Div => VmOp.Div,
            Code.Rem => VmOp.Rem,
            Code.Div_Un => VmOp.DivUn,
            Code.Rem_Un => VmOp.RemUn,
            Code.And => VmOp.And,
            Code.Or => VmOp.Or,
            Code.Xor => VmOp.Xor,
            Code.Shl => VmOp.Shl,
            Code.Shr => VmOp.Shr,
            Code.Shr_Un => VmOp.ShrUn,
            Code.Ceq => VmOp.Ceq,
            Code.Cgt => VmOp.Cgt,
            Code.Cgt_Un => VmOp.CgtUn,
            Code.Clt => VmOp.Clt,
            Code.Clt_Un => VmOp.CltUn,
            _ => default
        };
        return op != default;
    }

    private static bool TryUnary(Code code, out VmOp op)
    {
        op = code switch
        {
            Code.Not => VmOp.Not,
            Code.Neg => VmOp.Neg,
            Code.Conv_I4 => VmOp.ConvI4,
            Code.Conv_I8 => VmOp.ConvI8,
            Code.Conv_R4 => VmOp.ConvR4,
            Code.Conv_R8 => VmOp.ConvR8,
            Code.Conv_U4 => VmOp.ConvU4,
            Code.Conv_U8 => VmOp.ConvU8,
            _ => default
        };
        return op != default;
    }

    private static bool TryBranch(Code code, out VmOp op, out int pops, out bool terminal)
    {
        terminal = false;
        pops = 0;
        switch (code)
        {
            case Code.Br or Code.Br_S:
                op = VmOp.Br;
                terminal = true;
                return true;
            case Code.Brtrue or Code.Brtrue_S:
                op = VmOp.Brtrue;
                pops = 1;
                return true;
            case Code.Brfalse or Code.Brfalse_S:
                op = VmOp.Brfalse;
                pops = 1;
                return true;
            case Code.Beq or Code.Beq_S:
                op = VmOp.Beq;
                pops = 2;
                return true;
            case Code.Bne_Un or Code.Bne_Un_S:
                op = VmOp.Bne;
                pops = 2;
                return true;
            case Code.Blt or Code.Blt_S:
                op = VmOp.Blt;
                pops = 2;
                return true;
            case Code.Ble or Code.Ble_S:
                op = VmOp.Ble;
                pops = 2;
                return true;
            case Code.Bgt or Code.Bgt_S:
                op = VmOp.Bgt;
                pops = 2;
                return true;
            case Code.Bge or Code.Bge_S:
                op = VmOp.Bge;
                pops = 2;
                return true;
            case Code.Blt_Un or Code.Blt_Un_S:
                op = VmOp.BltUn;
                pops = 2;
                return true;
            case Code.Ble_Un or Code.Ble_Un_S:
                op = VmOp.BleUn;
                pops = 2;
                return true;
            case Code.Bgt_Un or Code.Bgt_Un_S:
                op = VmOp.BgtUn;
                pops = 2;
                return true;
            case Code.Bge_Un or Code.Bge_Un_S:
                op = VmOp.BgeUn;
                pops = 2;
                return true;
            default:
                op = default;
                return false;
        }
    }

    private static bool TryReadLdcI4(Instruction instr, out int value)
    {
        value = 0;
        if (instr.OpCode == OpCodes.Ldc_I4_M1)
        {
            value = -1;
            return true;
        }

        if (instr.OpCode.Code is >= Code.Ldc_I4_0 and <= Code.Ldc_I4_8)
        {
            value = instr.OpCode.Code - Code.Ldc_I4_0;
            return true;
        }

        if (instr.OpCode == OpCodes.Ldc_I4_S && instr.Operand is sbyte sb)
        {
            value = sb;
            return true;
        }

        if (instr.OpCode == OpCodes.Ldc_I4)
        {
            switch (instr.Operand)
            {
                case int i:
                    value = i;
                    return true;
                case uint u:
                    value = unchecked((int)u);
                    return true;
                case sbyte s:
                    value = s;
                    return true;
                case byte b:
                    value = b;
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryReadI8(Instruction instr, out long value)
    {
        switch (instr.Operand)
        {
            case long l:
                value = l;
                return true;
            case ulong ul:
                value = unchecked((long)ul);
                return true;
            case int i:
                value = i;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryReadR4(Instruction instr, out float value)
    {
        switch (instr.Operand)
        {
            case float f:
                value = f;
                return true;
            case int bits:
                value = BitConverter.Int32BitsToSingle(bits);
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryReadR8(Instruction instr, out double value)
    {
        switch (instr.Operand)
        {
            case double d:
                value = d;
                return true;
            case float f:
                value = f;
                return true;
            case long bits:
                value = BitConverter.Int64BitsToDouble(bits);
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryReadLdarg(Instruction instr, out int index)
    {
        index = 0;
        if (instr.OpCode.Code is >= Code.Ldarg_0 and <= Code.Ldarg_3)
        {
            index = instr.OpCode.Code - Code.Ldarg_0;
            return true;
        }

        if (instr.OpCode is OpCode { Code: Code.Ldarg or Code.Ldarg_S })
            return TryReadIndexOperand(instr.Operand, out index);
        return false;
    }

    private static bool TryReadStarg(Instruction instr, out int index)
    {
        index = 0;
        if (instr.OpCode is OpCode { Code: Code.Starg or Code.Starg_S })
            return TryReadIndexOperand(instr.Operand, out index);
        return false;
    }

    private static bool TryReadLdloc(Instruction instr, out int index)
    {
        index = 0;
        if (instr.OpCode.Code is >= Code.Ldloc_0 and <= Code.Ldloc_3)
        {
            index = instr.OpCode.Code - Code.Ldloc_0;
            return true;
        }

        if (instr.OpCode is OpCode { Code: Code.Ldloc or Code.Ldloc_S } && instr.Operand is Local local)
        {
            index = local.Index;
            return true;
        }

        if (instr.OpCode is OpCode { Code: Code.Ldloc or Code.Ldloc_S })
            return TryReadIndexOperand(instr.Operand, out index);
        return false;
    }

    private static bool TryReadStloc(Instruction instr, out int index)
    {
        index = 0;
        if (instr.OpCode.Code is >= Code.Stloc_0 and <= Code.Stloc_3)
        {
            index = instr.OpCode.Code - Code.Stloc_0;
            return true;
        }

        if (instr.OpCode is OpCode { Code: Code.Stloc or Code.Stloc_S } && instr.Operand is Local local)
        {
            index = local.Index;
            return true;
        }

        if (instr.OpCode is OpCode { Code: Code.Stloc or Code.Stloc_S })
            return TryReadIndexOperand(instr.Operand, out index);
        return false;
    }

    private static bool TryReadIndexOperand(object? operand, out int index)
    {
        switch (operand)
        {
            case Parameter p:
                index = p.Index;
                return true;
            case Local local:
                index = local.Index;
                return true;
            case byte b:
                index = b;
                return true;
            case ushort us:
                index = us;
                return true;
            case int i:
                index = i;
                return true;
            default:
                index = 0;
                return false;
        }
    }

    private static bool IsForbiddenByRef(TypeSig? sig)
    {
        sig = sig?.RemovePinnedAndModifiers();
        if (sig is null)
            return false;
        if (sig.ElementType is ElementType.ByRef or ElementType.Ptr or ElementType.FnPtr or ElementType.TypedByRef)
            return true;
        return IsByRefLike(sig);
    }

    private static bool IsByRefLike(TypeSig sig)
    {
        sig = sig.RemovePinnedAndModifiers();
        if (sig is null)
            return false;

        // Match on the sig itself so GenericInst (System.Span`1<T>) is caught even
        // when GenericType.TypeDefOrRef is a TypeSpec with an empty Name.
        if (IsByRefLikeName(sig.TypeName) || IsByRefLikeFullName(sig.FullName))
            return true;

        if (sig is GenericInstSig generic)
            return IsByRefLike(generic.GenericType.TypeDefOrRef);

        if (sig is TypeDefOrRefSig tdr)
            return IsByRefLike(tdr.TypeDefOrRef);

        return false;
    }

    private static bool IsByRefLike(ITypeDefOrRef? typeRef)
    {
        if (typeRef is null)
            return false;

        if (typeRef is TypeSpec spec)
        {
            var inner = spec.TypeSig?.RemovePinnedAndModifiers();
            return inner is not null && IsByRefLike(inner);
        }

        if (IsByRefLikeName(UTF8String.ToSystemStringOrEmpty(typeRef.Name)) ||
            IsByRefLikeFullName(typeRef.FullName))
            return true;

        var type = typeRef.ResolveTypeDef();
        if (type is null)
            return false;
        foreach (var attr in type.CustomAttributes)
        {
            if (attr.TypeFullName == IsByRefLikeAttribute)
                return true;
        }

        return false;
    }

    private static bool IsByRefLikeName(string? name) =>
        name is "Span`1" or "ReadOnlySpan`1";

    private static bool IsByRefLikeFullName(string? fullName) =>
        fullName is not null &&
        (fullName.StartsWith("System.Span`1", StringComparison.Ordinal) ||
         fullName.StartsWith("System.ReadOnlySpan`1", StringComparison.Ordinal));

    private static bool IsNonPrimitiveValueType(TypeSig? sig)
    {
        sig = sig?.RemovePinnedAndModifiers();
        if (sig is null)
            return false;
        if (sig.ElementType is ElementType.Void)
            return false;
        if (IsPrimitiveFamily(sig.ElementType))
            return false;
        if (sig.ElementType is ElementType.Class or ElementType.Object or ElementType.String
            or ElementType.SZArray or ElementType.Array or ElementType.Var or ElementType.MVar)
            return false;
        if (sig is GenericInstSig generic)
            return generic.GenericType.IsValueType;
        return sig.IsValueType;
    }

    private static bool IsPrimitiveFamily(ElementType elementType) =>
        elementType is ElementType.I1 or ElementType.U1
            or ElementType.I2 or ElementType.U2
            or ElementType.I4 or ElementType.U4
            or ElementType.I8 or ElementType.U8
            or ElementType.R4 or ElementType.R8
            or ElementType.Boolean or ElementType.Char;

    private static bool IsValueType(ITypeDefOrRef? type)
    {
        if (type is null)
            return false;
        if (type.IsValueType)
            return true;
        return type is TypeSpec spec && spec.TypeSig is GenericInstSig generic && generic.GenericType.IsValueType;
    }

    private static int ParameterCount(IMethod method)
    {
        if (method is MethodDef def)
            return def.Parameters.Count;
        var sig = method.MethodSig;
        if (sig is null)
            return 0;
        var count = sig.Params.Count;
        if (sig.HasThis && !sig.ExplicitThis)
            count++;
        return count;
    }

    private static bool ReturnsVoid(IMethod method) =>
        method.MethodSig?.RetType.RemovePinnedAndModifiers()?.ElementType == ElementType.Void;

    private static void WriteU16(List<byte> buffer, ushort value)
    {
        Span<byte> tmp = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(tmp, value);
        buffer.Add(tmp[0]);
        buffer.Add(tmp[1]);
    }

    private static void WriteU16At(List<byte> buffer, int index, ushort value)
    {
        Span<byte> tmp = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(tmp, value);
        buffer[index] = tmp[0];
        buffer[index + 1] = tmp[1];
    }

    private static void WriteI32(List<byte> buffer, int value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(tmp, value);
        buffer.Add(tmp[0]);
        buffer.Add(tmp[1]);
        buffer.Add(tmp[2]);
        buffer.Add(tmp[3]);
    }

    private static void WriteI64(List<byte> buffer, long value)
    {
        Span<byte> tmp = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(tmp, value);
        buffer.Add(tmp[0]);
        buffer.Add(tmp[1]);
        buffer.Add(tmp[2]);
        buffer.Add(tmp[3]);
        buffer.Add(tmp[4]);
        buffer.Add(tmp[5]);
        buffer.Add(tmp[6]);
        buffer.Add(tmp[7]);
    }
}

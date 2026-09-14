using System;
using System.Reflection;
using System.Text;

namespace Obfy.Runtime;

public static class Vm
{
    const int LocalSlots = 256;
    const int StackSlots = 64;
    const int InitialFrames = 4;

    static byte[] _code = Array.Empty<byte>();
    static int[] _starts = Array.Empty<int>();
    static byte[] _opMap = Array.Empty<byte>();
    static byte[] _xorKey = Array.Empty<byte>();
    static MethodBase[] _methods = Array.Empty<MethodBase>();
    static FieldInfo[] _fields = Array.Empty<FieldInfo>();
    static Type[] _types = Array.Empty<Type>();
    static Type[] _returnTypes = Array.Empty<Type>();

    enum VmType : byte { I4, I8, R4, R8, O }

    struct VmValue
    {
        public VmType Type;
        public long Bits;
        public object Ref;
    }

    struct Frame
    {
        public int MethodId;
        public int Ip;
        public VmValue[] Args;
        public VmValue[] Locals;
        public VmValue[] Stack;
        public int Sp;
    }

    public static object Run(int id, object[] args)
    {
        if (_xorKey.Length == 0)
            throw Fault("invalid xor key");
        if (id < 0 || id >= _starts.Length)
            throw Fault("invalid method id");
        if (args == null)
            args = new object[0];

        var frames = new Frame[InitialFrames];
        var depth = 0;
        frames[0] = CreateFrame(id, args);

        while (depth >= 0)
        {
            ref var f = ref frames[depth];
            var op = Fetch(ref f);
            switch (op)
            {
                case 1: // LdcI4
                    Push(ref f, I4(ReadI32(ref f)));
                    break;
                case 2: // LdcI8
                    Push(ref f, I8(ReadI64(ref f)));
                    break;
                case 3: // LdcR4
                    Push(ref f, new VmValue { Type = VmType.R4, Bits = ReadI32(ref f) });
                    break;
                case 4: // LdcR8
                    Push(ref f, new VmValue { Type = VmType.R8, Bits = ReadI64(ref f) });
                    break;
                case 5: // Ldnull
                    Push(ref f, O(null!));
                    break;
                case 6: // Ldstr
                    Push(ref f, O(ReadUtf8(ref f)));
                    break;
                case 7: // Ldarg
                    Push(ref f, Arg(ref f, ReadU8(ref f)));
                    break;
                case 8: // Starg
                    SetArg(ref f, ReadU8(ref f), Pop(ref f));
                    break;
                case 9: // Ldloc
                    Push(ref f, Loc(ref f, ReadU8(ref f)));
                    break;
                case 10: // Stloc
                    SetLoc(ref f, ReadU8(ref f), Pop(ref f));
                    break;
                case 11: // Dup
                    Dup(ref f);
                    break;
                case 12: // Pop
                    Pop(ref f);
                    break;
                case 13: // Add
                case 14: // Sub
                case 15: // Mul
                case 16: // Div
                case 17: // Rem
                case 18: // DivUn
                case 19: // RemUn
                case 20: // And
                case 21: // Or
                case 22: // Xor
                    Bin(ref f, op);
                    break;
                case 23: // Not
                    UnaryNot(ref f);
                    break;
                case 24: // Neg
                    UnaryNeg(ref f);
                    break;
                case 25: // Shl
                case 26: // Shr
                case 27: // ShrUn
                    Shift(ref f, op);
                    break;
                case 28: // ConvI4
                case 29: // ConvI8
                case 30: // ConvR4
                case 31: // ConvR8
                case 32: // ConvU4
                case 33: // ConvU8
                    Conv(ref f, op);
                    break;
                case 34: // Ceq
                case 35: // Cgt
                case 36: // CgtUn
                case 37: // Clt
                case 38: // CltUn
                    Compare(ref f, op);
                    break;
                case 39: // Br
                    Branch(ref f, ReadU16(ref f));
                    break;
                case 40: // Brtrue
                    CondBranch(ref f, IsTrue(Pop(ref f)));
                    break;
                case 41: // Brfalse
                    CondBranch(ref f, !IsTrue(Pop(ref f)));
                    break;
                case 42: // Beq
                    BinBranch(ref f, Equal(PopPair(ref f, out var beqR), beqR));
                    break;
                case 43: // Bne
                    BinBranch(ref f, !Equal(PopPair(ref f, out var bneR), bneR));
                    break;
                case 44: // Blt
                    BinBranch(ref f, Less(PopPair(ref f, out var bltR), bltR, unsigned: false));
                    break;
                case 45: // Ble
                    BinBranch(ref f, !Greater(PopPair(ref f, out var bleR), bleR, unsigned: false));
                    break;
                case 46: // Bgt
                    BinBranch(ref f, Greater(PopPair(ref f, out var bgtR), bgtR, unsigned: false));
                    break;
                case 47: // Bge
                    BinBranch(ref f, !Less(PopPair(ref f, out var bgeR), bgeR, unsigned: false));
                    break;
                case 48: // BltUn
                    BinBranch(ref f, Less(PopPair(ref f, out var bltuR), bltuR, unsigned: true));
                    break;
                case 49: // BleUn
                    BinBranch(ref f, !Greater(PopPair(ref f, out var bleuR), bleuR, unsigned: true));
                    break;
                case 50: // BgtUn
                    BinBranch(ref f, Greater(PopPair(ref f, out var bgtuR), bgtuR, unsigned: true));
                    break;
                case 51: // BgeUn
                    BinBranch(ref f, !Less(PopPair(ref f, out var bgeuR), bgeuR, unsigned: true));
                    break;
                case 52: // Newobj
                    NewObj(ref f);
                    break;
                case 53: // Call
                case 54: // Callvirt
                    Call(ref f);
                    break;
                case 56: // Ldfld
                    LdFld(ref f, isStatic: false);
                    break;
                case 57: // Stfld
                    StFld(ref f, isStatic: false);
                    break;
                case 58: // Ldsfld
                    LdFld(ref f, isStatic: true);
                    break;
                case 59: // Stsfld
                    StFld(ref f, isStatic: true);
                    break;
                case 77: // Ret
                {
                    var boxed = DoRet(ref frames, ref depth, out var done);
                    if (done)
                        return boxed;
                    break;
                }
                default:
                    throw Fault("invalid opcode");
            }
        }

        return null!;
    }

    internal static void Init(
        byte[] code,
        int[] starts,
        byte[] opMap,
        byte[] xorKey,
        MethodBase[] methods,
        FieldInfo[] fields,
        Type[] types,
        Type[] returnTypes)
    {
        _code = code ?? throw new ArgumentNullException(nameof(code));
        _starts = starts ?? throw new ArgumentNullException(nameof(starts));
        _opMap = opMap ?? throw new ArgumentNullException(nameof(opMap));
        _xorKey = xorKey ?? throw new ArgumentNullException(nameof(xorKey));
        _methods = methods ?? throw new ArgumentNullException(nameof(methods));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _returnTypes = returnTypes ?? throw new ArgumentNullException(nameof(returnTypes));
    }

    static Frame CreateFrame(int id, object[] args)
    {
        var vmArgs = new VmValue[args.Length];
        for (var i = 0; i < args.Length; i++)
            vmArgs[i] = UnboxArg(args[i]);
        return new Frame
        {
            MethodId = id,
            Ip = _starts[id],
            Args = vmArgs,
            Locals = new VmValue[LocalSlots],
            Stack = new VmValue[StackSlots],
            Sp = 0
        };
    }

    static VmValue UnboxArg(object arg)
    {
        if (arg == null)
            return O(null!);
        switch (Type.GetTypeCode(arg.GetType()))
        {
            case TypeCode.Boolean:
                return I4((bool)arg ? 1 : 0);
            case TypeCode.Char:
                return I4((char)arg);
            case TypeCode.SByte:
                return I4((sbyte)arg);
            case TypeCode.Byte:
                return I4((byte)arg);
            case TypeCode.Int16:
                return I4((short)arg);
            case TypeCode.UInt16:
                return I4((ushort)arg);
            case TypeCode.Int32:
                return I4((int)arg);
            case TypeCode.UInt32:
                return I4(unchecked((int)(uint)arg));
            case TypeCode.Int64:
                return I8((long)arg);
            case TypeCode.UInt64:
                return I8(unchecked((long)(ulong)arg));
            case TypeCode.Single:
                return R4((float)arg);
            case TypeCode.Double:
                return R8((double)arg);
            default:
                return O(arg);
        }
    }

    static object BoxReturn(VmValue v, Type rt)
    {
        if (rt == null || rt == typeof(void))
            return null!;
        switch (Type.GetTypeCode(rt))
        {
            case TypeCode.Boolean:
                return ((int)v.Bits) != 0;
            case TypeCode.Char:
                return (char)(int)v.Bits;
            case TypeCode.SByte:
                return (sbyte)(int)v.Bits;
            case TypeCode.Byte:
                return (byte)(int)v.Bits;
            case TypeCode.Int16:
                return (short)(int)v.Bits;
            case TypeCode.UInt16:
                return (ushort)(int)v.Bits;
            case TypeCode.Int32:
                return (int)v.Bits;
            case TypeCode.UInt32:
                return unchecked((uint)(int)v.Bits);
            case TypeCode.Int64:
                return v.Bits;
            case TypeCode.UInt64:
                return unchecked((ulong)v.Bits);
            case TypeCode.Single:
                return ToR4(v);
            case TypeCode.Double:
                return ToR8(v);
            default:
                return v.Ref;
        }
    }

    static object DoRet(ref Frame[] frames, ref int depth, out bool done)
    {
        ref var f = ref frames[depth];
        var id = f.MethodId;
        if (id < 0 || id >= _returnTypes.Length)
            throw Fault("invalid method id");
        var rt = _returnTypes[id];
        var isVoid = rt == null || rt == typeof(void);
        VmValue value = default;
        if (!isVoid)
            value = Pop(ref f);
        depth--;
        if (depth < 0)
        {
            done = true;
            return isVoid ? null! : BoxReturn(value, rt!);
        }

        if (!isVoid)
            Push(ref frames[depth], value);
        done = false;
        return null!;
    }

    static void NewObj(ref Frame f)
    {
        var method = ReadMethod(ref f);
        var args = PopArgs(ref f, method.GetParameters());
        Push(ref f, UnboxArg(Invoke(method, null!, args)));
    }

    static void Call(ref Frame f)
    {
        var method = ReadMethod(ref f);
        var args = PopArgs(ref f, method.GetParameters());
        object target = null!;
        if (!method.IsStatic)
            target = ToClr(Pop(ref f), method.DeclaringType!);
        var result = Invoke(method, target, args);
        var info = method as MethodInfo;
        if (info == null || info.ReturnType == typeof(void))
            return;
        Push(ref f, UnboxArg(result!));
    }

    static void LdFld(ref Frame f, bool isStatic)
    {
        var field = ReadField(ref f);
        object receiver = null!;
        if (!isStatic)
            receiver = ToClr(Pop(ref f), field.DeclaringType!);
        Push(ref f, UnboxArg(field.GetValue(receiver)!));
    }

    static void StFld(ref Frame f, bool isStatic)
    {
        var field = ReadField(ref f);
        var value = ToClr(Pop(ref f), field.FieldType);
        object receiver = null!;
        if (!isStatic)
            receiver = ToClr(Pop(ref f), field.DeclaringType!);
        field.SetValue(receiver, value);
    }

    static object[] PopArgs(ref Frame f, ParameterInfo[] parameters)
    {
        var args = new object[parameters.Length];
        for (var i = parameters.Length - 1; i >= 0; i--)
            args[i] = ToClr(Pop(ref f), parameters[i].ParameterType!);
        return args;
    }

    static MethodBase ReadMethod(ref Frame f)
    {
        var index = ReadU16(ref f);
        if ((uint)index >= (uint)_methods.Length)
            throw Fault("invalid method");
        var method = _methods[index];
        if (method == null)
            throw Fault("invalid method");
        return method;
    }

    static FieldInfo ReadField(ref Frame f)
    {
        var index = ReadU16(ref f);
        if ((uint)index >= (uint)_fields.Length)
            throw Fault("invalid field");
        var field = _fields[index];
        if (field == null)
            throw Fault("invalid field");
        return field;
    }

    static object Invoke(MethodBase method, object target, object[] args)
    {
        try
        {
            var ctor = method as ConstructorInfo;
            if (ctor != null)
                return ctor.Invoke(args)!;
            return method.Invoke(target, args)!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }

    static object ToClr(VmValue v, Type expected)
    {
        if (expected == null || !expected.IsPrimitive)
            return v.Ref!;
        var boxed = BoxReturn(v, expected);
        if (boxed == null)
            throw Fault("type mismatch");
        return Convert.ChangeType(boxed, expected)!;
    }

    static byte Fetch(ref Frame f)
    {
        var ip = f.Ip;
        var start = _starts[f.MethodId];
        var end = MethodEnd(f.MethodId);
        if (ip < start || ip >= end)
            throw Fault("unexpected end of code");
        var idx = (byte)(_code[ip] ^ XorAt(ip));
        f.Ip = ip + 1;
        if (idx >= _opMap.Length)
            throw Fault("invalid opcode");
        var mapped = _opMap[idx];
        if (mapped == 0xFF)
            throw Fault("invalid opcode");
        return mapped;
    }

    static int MethodEnd(int id) =>
        id + 1 < _starts.Length ? _starts[id + 1] : _code.Length;

    static byte XorAt(int ip) => _xorKey[ip % _xorKey.Length];

    static byte ReadU8(ref Frame f)
    {
        var ip = f.Ip;
        if ((uint)ip >= (uint)_code.Length)
            throw Fault("unexpected end of code");
        var b = (byte)(_code[ip] ^ XorAt(ip));
        f.Ip = ip + 1;
        return b;
    }

    static int ReadU16(ref Frame f)
    {
        var b0 = ReadU8(ref f);
        var b1 = ReadU8(ref f);
        return b0 | (b1 << 8);
    }

    static int ReadI32(ref Frame f)
    {
        var b0 = ReadU8(ref f);
        var b1 = ReadU8(ref f);
        var b2 = ReadU8(ref f);
        var b3 = ReadU8(ref f);
        return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
    }

    static long ReadI64(ref Frame f)
    {
        var lo = (uint)ReadI32(ref f);
        var hi = (uint)ReadI32(ref f);
        return (long)lo | ((long)hi << 32);
    }

    static string ReadUtf8(ref Frame f)
    {
        var len = ReadU16(ref f);
        var bytes = new byte[len];
        for (var i = 0; i < len; i++)
            bytes[i] = ReadU8(ref f);
        return Encoding.UTF8.GetString(bytes);
    }

    static void Push(ref Frame f, VmValue v)
    {
        if (f.Sp >= f.Stack.Length)
            throw Fault("stack overflow");
        f.Stack[f.Sp] = v;
        f.Sp++;
    }

    static VmValue Pop(ref Frame f)
    {
        if (f.Sp <= 0)
            throw Fault("stack underflow");
        f.Sp--;
        return f.Stack[f.Sp];
    }

    static VmValue PopPair(ref Frame f, out VmValue right)
    {
        right = Pop(ref f);
        return Pop(ref f);
    }

    static void Dup(ref Frame f)
    {
        if (f.Sp <= 0)
            throw Fault("stack underflow");
        Push(ref f, f.Stack[f.Sp - 1]);
    }

    static VmValue Arg(ref Frame f, int index)
    {
        if ((uint)index >= (uint)f.Args.Length)
            throw Fault("argument index out of range");
        return f.Args[index];
    }

    static void SetArg(ref Frame f, int index, VmValue value)
    {
        if ((uint)index >= (uint)f.Args.Length)
            throw Fault("argument index out of range");
        f.Args[index] = value;
    }

    static VmValue Loc(ref Frame f, int index)
    {
        if ((uint)index >= (uint)f.Locals.Length)
            throw Fault("local index out of range");
        return f.Locals[index];
    }

    static void SetLoc(ref Frame f, int index, VmValue value)
    {
        if ((uint)index >= (uint)f.Locals.Length)
            throw Fault("local index out of range");
        f.Locals[index] = value;
    }

    static void CondBranch(ref Frame f, bool take)
    {
        var offset = ReadU16(ref f);
        if (take)
            Branch(ref f, offset);
    }

    static void BinBranch(ref Frame f, bool take) => CondBranch(ref f, take);

    static void Branch(ref Frame f, int offset)
    {
        var start = _starts[f.MethodId];
        var end = MethodEnd(f.MethodId);
        var target = start + offset;
        if (target < start || target >= end)
            throw Fault("branch out of range");
        f.Ip = target;
    }

    static bool IsTrue(VmValue v)
    {
        switch (v.Type)
        {
            case VmType.I4:
                return (int)v.Bits != 0;
            case VmType.I8:
                return v.Bits != 0;
            case VmType.R4:
                return ToR4(v) != 0;
            case VmType.R8:
                return ToR8(v) != 0;
            case VmType.O:
                return v.Ref != null;
            default:
                throw Fault("type mismatch");
        }
    }

    static bool Equal(VmValue l, VmValue r)
    {
        if (l.Type != r.Type)
            throw Fault("type mismatch");
        switch (l.Type)
        {
            case VmType.I4:
                return (int)l.Bits == (int)r.Bits;
            case VmType.I8:
                return l.Bits == r.Bits;
            case VmType.R4:
                return ToR4(l) == ToR4(r);
            case VmType.R8:
                return ToR8(l) == ToR8(r);
            case VmType.O:
                return ReferenceEquals(l.Ref, r.Ref);
            default:
                throw Fault("type mismatch");
        }
    }

    static bool Greater(VmValue l, VmValue r, bool unsigned)
    {
        if (l.Type != r.Type)
            throw Fault("type mismatch");
        if (unsigned)
        {
            if (l.Type == VmType.I4)
                return (uint)(int)l.Bits > (uint)(int)r.Bits;
            return (ulong)l.Bits > (ulong)r.Bits;
        }

        switch (l.Type)
        {
            case VmType.I4:
                return (int)l.Bits > (int)r.Bits;
            case VmType.I8:
                return l.Bits > r.Bits;
            case VmType.R4:
                return ToR4(l) > ToR4(r);
            case VmType.R8:
                return ToR8(l) > ToR8(r);
            default:
                throw Fault("type mismatch");
        }
    }

    static bool Less(VmValue l, VmValue r, bool unsigned)
    {
        if (l.Type != r.Type)
            throw Fault("type mismatch");
        if (unsigned)
        {
            if (l.Type == VmType.I4)
                return (uint)(int)l.Bits < (uint)(int)r.Bits;
            return (ulong)l.Bits < (ulong)r.Bits;
        }

        switch (l.Type)
        {
            case VmType.I4:
                return (int)l.Bits < (int)r.Bits;
            case VmType.I8:
                return l.Bits < r.Bits;
            case VmType.R4:
                return ToR4(l) < ToR4(r);
            case VmType.R8:
                return ToR8(l) < ToR8(r);
            default:
                throw Fault("type mismatch");
        }
    }

    static void Compare(ref Frame f, byte op)
    {
        var r = Pop(ref f);
        var l = Pop(ref f);
        bool result;
        switch (op)
        {
            case 34:
                result = Equal(l, r);
                break;
            case 35:
                result = Greater(l, r, unsigned: false);
                break;
            case 36:
                result = Greater(l, r, unsigned: true);
                break;
            case 37:
                result = Less(l, r, unsigned: false);
                break;
            default:
                result = Less(l, r, unsigned: true);
                break;
        }

        Push(ref f, I4(result ? 1 : 0));
    }

    static void Bin(ref Frame f, byte op)
    {
        var r = Pop(ref f);
        var l = Pop(ref f);
        if (l.Type != r.Type)
            throw Fault("type mismatch");
        Push(ref f, BinValue(l, r, op));
    }

    static VmValue BinValue(VmValue l, VmValue r, byte op)
    {
        switch (op)
        {
            case 13:
                return Add(l, r);
            case 14:
                return Sub(l, r);
            case 15:
                return Mul(l, r);
            case 16:
                return Div(l, r, unsigned: false);
            case 17:
                return Rem(l, r, unsigned: false);
            case 18:
                return Div(l, r, unsigned: true);
            case 19:
                return Rem(l, r, unsigned: true);
            case 20:
                return Bit(l, r, 20);
            case 21:
                return Bit(l, r, 21);
            default:
                return Bit(l, r, 22);
        }
    }

    static VmValue Add(VmValue l, VmValue r)
    {
        switch (l.Type)
        {
            case VmType.I4:
                return I4(unchecked((int)l.Bits + (int)r.Bits));
            case VmType.I8:
                return I8(unchecked(l.Bits + r.Bits));
            case VmType.R4:
                return R4(ToR4(l) + ToR4(r));
            case VmType.R8:
                return R8(ToR8(l) + ToR8(r));
            default:
                throw Fault("type mismatch");
        }
    }

    static VmValue Sub(VmValue l, VmValue r)
    {
        switch (l.Type)
        {
            case VmType.I4:
                return I4(unchecked((int)l.Bits - (int)r.Bits));
            case VmType.I8:
                return I8(unchecked(l.Bits - r.Bits));
            case VmType.R4:
                return R4(ToR4(l) - ToR4(r));
            case VmType.R8:
                return R8(ToR8(l) - ToR8(r));
            default:
                throw Fault("type mismatch");
        }
    }

    static VmValue Mul(VmValue l, VmValue r)
    {
        switch (l.Type)
        {
            case VmType.I4:
                return I4(unchecked((int)l.Bits * (int)r.Bits));
            case VmType.I8:
                return I8(unchecked(l.Bits * r.Bits));
            case VmType.R4:
                return R4(ToR4(l) * ToR4(r));
            case VmType.R8:
                return R8(ToR8(l) * ToR8(r));
            default:
                throw Fault("type mismatch");
        }
    }

    static VmValue Div(VmValue l, VmValue r, bool unsigned)
    {
        if (unsigned)
        {
            if (l.Type == VmType.I4)
                return I4(unchecked((int)((uint)(int)l.Bits / (uint)(int)r.Bits)));
            if (l.Type == VmType.I8)
                return I8(unchecked((long)((ulong)l.Bits / (ulong)r.Bits)));
            throw Fault("type mismatch");
        }

        switch (l.Type)
        {
            case VmType.I4:
                return I4((int)l.Bits / (int)r.Bits);
            case VmType.I8:
                return I8(l.Bits / r.Bits);
            case VmType.R4:
                return R4(ToR4(l) / ToR4(r));
            case VmType.R8:
                return R8(ToR8(l) / ToR8(r));
            default:
                throw Fault("type mismatch");
        }
    }

    static VmValue Rem(VmValue l, VmValue r, bool unsigned)
    {
        if (unsigned)
        {
            if (l.Type == VmType.I4)
                return I4(unchecked((int)((uint)(int)l.Bits % (uint)(int)r.Bits)));
            if (l.Type == VmType.I8)
                return I8(unchecked((long)((ulong)l.Bits % (ulong)r.Bits)));
            throw Fault("type mismatch");
        }

        switch (l.Type)
        {
            case VmType.I4:
                return I4((int)l.Bits % (int)r.Bits);
            case VmType.I8:
                return I8(l.Bits % r.Bits);
            case VmType.R4:
                return R4(ToR4(l) % ToR4(r));
            case VmType.R8:
                return R8(ToR8(l) % ToR8(r));
            default:
                throw Fault("type mismatch");
        }
    }

    static VmValue Bit(VmValue l, VmValue r, byte op)
    {
        if (l.Type == VmType.I4)
        {
            var a = (int)l.Bits;
            var b = (int)r.Bits;
            if (op == 20) return I4(a & b);
            if (op == 21) return I4(a | b);
            return I4(a ^ b);
        }

        if (l.Type == VmType.I8)
        {
            if (op == 20) return I8(l.Bits & r.Bits);
            if (op == 21) return I8(l.Bits | r.Bits);
            return I8(l.Bits ^ r.Bits);
        }

        throw Fault("type mismatch");
    }

    static void UnaryNot(ref Frame f)
    {
        var v = Pop(ref f);
        if (v.Type == VmType.I4)
            Push(ref f, I4(~(int)v.Bits));
        else if (v.Type == VmType.I8)
            Push(ref f, I8(~v.Bits));
        else
            throw Fault("type mismatch");
    }

    static void UnaryNeg(ref Frame f)
    {
        var v = Pop(ref f);
        switch (v.Type)
        {
            case VmType.I4:
                Push(ref f, I4(unchecked(-(int)v.Bits)));
                break;
            case VmType.I8:
                Push(ref f, I8(unchecked(-v.Bits)));
                break;
            case VmType.R4:
                Push(ref f, R4(-ToR4(v)));
                break;
            case VmType.R8:
                Push(ref f, R8(-ToR8(v)));
                break;
            default:
                throw Fault("type mismatch");
        }
    }

    static void Shift(ref Frame f, byte op)
    {
        var countVal = Pop(ref f);
        var value = Pop(ref f);
        if (countVal.Type != VmType.I4)
            throw Fault("type mismatch");
        var count = (int)countVal.Bits;
        if (value.Type == VmType.I4)
        {
            var a = (int)value.Bits;
            if (op == 25) Push(ref f, I4(a << count));
            else if (op == 26) Push(ref f, I4(a >> count));
            else Push(ref f, I4(unchecked((int)((uint)a >> count))));
            return;
        }

        if (value.Type == VmType.I8)
        {
            if (op == 25) Push(ref f, I8(value.Bits << count));
            else if (op == 26) Push(ref f, I8(value.Bits >> count));
            else Push(ref f, I8(unchecked((long)((ulong)value.Bits >> count))));
            return;
        }

        throw Fault("type mismatch");
    }

    static void Conv(ref Frame f, byte op)
    {
        var v = Pop(ref f);
        switch (op)
        {
            case 28:
                Push(ref f, I4(ToI4(v)));
                break;
            case 29:
                Push(ref f, I8(ToI8(v, unsigned: false)));
                break;
            case 30:
                Push(ref f, R4(ToFloat(v)));
                break;
            case 31:
                Push(ref f, R8(ToDouble(v)));
                break;
            case 32:
                Push(ref f, I4(ToI4(v)));
                break;
            default:
                Push(ref f, I8(ToI8(v, unsigned: true)));
                break;
        }
    }

    static int ToI4(VmValue v)
    {
        switch (v.Type)
        {
            case VmType.I4:
                return (int)v.Bits;
            case VmType.I8:
                return (int)v.Bits;
            case VmType.R4:
                return (int)ToR4(v);
            case VmType.R8:
                return (int)ToR8(v);
            default:
                throw Fault("type mismatch");
        }
    }

    static long ToI8(VmValue v, bool unsigned)
    {
        switch (v.Type)
        {
            case VmType.I4:
                return unsigned ? (long)(uint)(int)v.Bits : (int)v.Bits;
            case VmType.I8:
                return v.Bits;
            case VmType.R4:
                return unsigned ? unchecked((long)(ulong)ToR4(v)) : (long)ToR4(v);
            case VmType.R8:
                return unsigned ? unchecked((long)(ulong)ToR8(v)) : (long)ToR8(v);
            default:
                throw Fault("type mismatch");
        }
    }

    static float ToFloat(VmValue v)
    {
        switch (v.Type)
        {
            case VmType.I4:
                return (int)v.Bits;
            case VmType.I8:
                return v.Bits;
            case VmType.R4:
                return ToR4(v);
            case VmType.R8:
                return (float)ToR8(v);
            default:
                throw Fault("type mismatch");
        }
    }

    static double ToDouble(VmValue v)
    {
        switch (v.Type)
        {
            case VmType.I4:
                return (int)v.Bits;
            case VmType.I8:
                return v.Bits;
            case VmType.R4:
                return ToR4(v);
            case VmType.R8:
                return ToR8(v);
            default:
                throw Fault("type mismatch");
        }
    }

    static VmValue I4(int v) => new VmValue { Type = VmType.I4, Bits = v };
    static VmValue I8(long v) => new VmValue { Type = VmType.I8, Bits = v };
    static VmValue R4(float v) => new VmValue { Type = VmType.R4, Bits = FloatBits(v) };
    static VmValue R8(double v) => new VmValue { Type = VmType.R8, Bits = DoubleBits(v) };
    static VmValue O(object v) => new VmValue { Type = VmType.O, Ref = v };

    static float ToR4(VmValue v) => Int32BitsToSingle((int)v.Bits);
    static double ToR8(VmValue v) => Int64BitsToDouble(v.Bits);

    static int FloatBits(float v) => BitConverter.ToInt32(BitConverter.GetBytes(v), 0);
    static long DoubleBits(double v) => BitConverter.ToInt64(BitConverter.GetBytes(v), 0);
    static float Int32BitsToSingle(int bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
    static double Int64BitsToDouble(long bits) => BitConverter.ToDouble(BitConverter.GetBytes(bits), 0);

    static InvalidOperationException Fault(string message) =>
        new InvalidOperationException("Obfy VM: " + message);
}

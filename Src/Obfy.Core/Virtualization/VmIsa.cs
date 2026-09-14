using System.Buffers.Binary;

namespace Obfy.Core.Virtualization;

/// <summary>
/// Internal VM opcodes. These numbers are not the on-disk bytes (Task 6 permutes those).
/// </summary>
public enum VmOp : byte
{
    LdcI4 = 1, LdcI8 = 2, LdcR4 = 3, LdcR8 = 4, Ldnull = 5, Ldstr = 6,
    Ldarg = 7, Starg = 8, Ldloc = 9, Stloc = 10, Dup = 11, Pop = 12,
    Add = 13, Sub = 14, Mul = 15, Div = 16, Rem = 17, DivUn = 18, RemUn = 19,
    And = 20, Or = 21, Xor = 22, Not = 23, Neg = 24, Shl = 25, Shr = 26, ShrUn = 27,
    ConvI4 = 28, ConvI8 = 29, ConvR4 = 30, ConvR8 = 31, ConvU4 = 32, ConvU8 = 33,
    Ceq = 34, Cgt = 35, CgtUn = 36, Clt = 37, CltUn = 38,
    Br = 39, Brtrue = 40, Brfalse = 41, Beq = 42, Bne = 43,
    Blt = 44, Ble = 45, Bgt = 46, Bge = 47,
    BltUn = 48, BleUn = 49, BgtUn = 50, BgeUn = 51,
    Newobj = 52, Call = 53, Callvirt = 54, CallVm = 55,
    Ldfld = 56, Stfld = 57, Ldsfld = 58, Stsfld = 59,
    Box = 60, UnboxAny = 61, Castclass = 62, Isinst = 63,
    Newarr = 64, Ldlen = 65,
    LdelemI4 = 66, LdelemI8 = 67, LdelemR4 = 68, LdelemR8 = 69, LdelemRef = 70,
    StelemI4 = 71, StelemI8 = 72, StelemR4 = 73, StelemR8 = 74, StelemRef = 75,
    Throw = 76, Ret = 77
}

/// <summary>
/// Encoded widths for internal VM opcodes. Used to walk a blob before permutation/XOR.
/// </summary>
public static class VmIsa
{
    public static int EncodedSize(byte[] blob, int offset)
    {
        ArgumentNullException.ThrowIfNull(blob);
        if ((uint)offset >= (uint)blob.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        switch ((VmOp)blob[offset])
        {
            case VmOp.Ldstr:
                if (offset + 3 > blob.Length)
                    throw new ArgumentOutOfRangeException(nameof(offset));
                return 3 + BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(offset + 1));
            case VmOp.CallVm:
                return 4;
            case VmOp.Br:
            case VmOp.Brtrue:
            case VmOp.Brfalse:
            case VmOp.Beq:
            case VmOp.Bne:
            case VmOp.Blt:
            case VmOp.Ble:
            case VmOp.Bgt:
            case VmOp.Bge:
            case VmOp.BltUn:
            case VmOp.BleUn:
            case VmOp.BgtUn:
            case VmOp.BgeUn:
            case VmOp.Newobj:
            case VmOp.Call:
            case VmOp.Callvirt:
            case VmOp.Ldfld:
            case VmOp.Stfld:
            case VmOp.Ldsfld:
            case VmOp.Stsfld:
            case VmOp.Box:
            case VmOp.UnboxAny:
            case VmOp.Castclass:
            case VmOp.Isinst:
            case VmOp.Newarr:
                return 3;
            case VmOp.LdcI8:
            case VmOp.LdcR8:
                return 9;
            case VmOp.LdcI4:
            case VmOp.LdcR4:
                return 5;
            case VmOp.Ldarg:
            case VmOp.Starg:
            case VmOp.Ldloc:
            case VmOp.Stloc:
                return 2;
            case VmOp.Ldnull:
            case VmOp.Dup:
            case VmOp.Pop:
            case VmOp.Add:
            case VmOp.Sub:
            case VmOp.Mul:
            case VmOp.Div:
            case VmOp.Rem:
            case VmOp.DivUn:
            case VmOp.RemUn:
            case VmOp.And:
            case VmOp.Or:
            case VmOp.Xor:
            case VmOp.Not:
            case VmOp.Neg:
            case VmOp.Shl:
            case VmOp.Shr:
            case VmOp.ShrUn:
            case VmOp.ConvI4:
            case VmOp.ConvI8:
            case VmOp.ConvR4:
            case VmOp.ConvR8:
            case VmOp.ConvU4:
            case VmOp.ConvU8:
            case VmOp.Ceq:
            case VmOp.Cgt:
            case VmOp.CgtUn:
            case VmOp.Clt:
            case VmOp.CltUn:
            case VmOp.Ldlen:
            case VmOp.LdelemI4:
            case VmOp.LdelemI8:
            case VmOp.LdelemR4:
            case VmOp.LdelemR8:
            case VmOp.LdelemRef:
            case VmOp.StelemI4:
            case VmOp.StelemI8:
            case VmOp.StelemR4:
            case VmOp.StelemR8:
            case VmOp.StelemRef:
            case VmOp.Throw:
            case VmOp.Ret:
                return 1;
            default:
                throw new InvalidOperationException($"Unknown VM opcode at offset {offset}: {blob[offset]}.");
        }
    }
}

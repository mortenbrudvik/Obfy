using System.Buffers.Binary;

namespace Obfy.Core.Virtualization;

/// <summary>
/// Internal VM opcodes. On-disk bytes are permuted by <see cref="VmSeed"/>.
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
    public const int MaxEvalStack = 64;

    public static int EncodedSize(byte[] blob, int offset)
    {
        ArgumentNullException.ThrowIfNull(blob);
        if ((uint)offset >= (uint)blob.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var size = (VmOp)blob[offset] switch
        {
            VmOp.Ldarg or VmOp.Starg or VmOp.Ldloc or VmOp.Stloc => 2,
            VmOp.LdcI4 or VmOp.LdcR4 => 5,
            VmOp.LdcI8 or VmOp.LdcR8 => 9,
            VmOp.Ldstr => LdstrSize(blob, offset),
            VmOp.CallVm => 4,
            VmOp.Br or VmOp.Brtrue or VmOp.Brfalse or VmOp.Beq or VmOp.Bne
                or VmOp.Blt or VmOp.Ble or VmOp.Bgt or VmOp.Bge
                or VmOp.BltUn or VmOp.BleUn or VmOp.BgtUn or VmOp.BgeUn
                or VmOp.Newobj or VmOp.Call or VmOp.Callvirt
                or VmOp.Ldfld or VmOp.Stfld or VmOp.Ldsfld or VmOp.Stsfld
                or VmOp.Box or VmOp.UnboxAny or VmOp.Castclass or VmOp.Isinst
                or VmOp.Newarr => 3,
            _ => 1
        };

        if (offset > blob.Length - size)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return size;
    }

    private static int LdstrSize(byte[] blob, int offset)
    {
        if (blob.Length - offset < 3)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var utf8Length = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(offset + 1));
        return checked(3 + utf8Length);
    }
}

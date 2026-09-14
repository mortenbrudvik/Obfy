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

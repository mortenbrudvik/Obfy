namespace Obfy.Core.Virtualization;

public static class VmSkipReasons
{
    public const string NoBody = "no body";
    public const string Constructor = "constructor";
    public const string Generic = "generic";
    public const string ExceptionHandlers = "exception handlers";
    public const string ByRef = "byref";
    public const string NonPrimitiveValuetypeLocal = "non-primitive valuetype local";
    public const string UnsupportedOpcode = "unsupported opcode";
    public const string ValuetypeNewobj = "valuetype newobj";
    public const string Switch = "switch";
    public const string StackHeightMismatch = "stack height mismatch";
    public const string IndexOutOfRange = "index out of range";
    public const string InvalidBytecode = "invalid bytecode";
    public const string StackTooDeep = "eval stack exceeds VM limit";
    public const string VirtualBaseCall = "non-virtual call to virtual method";
}

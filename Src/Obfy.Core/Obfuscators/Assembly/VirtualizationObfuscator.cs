using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Replaces simple static int methods with a bytecode interpreter stub.
/// Encodes ldc.i4, ldarg, ldloc/stloc, add/sub/mul, ceq/cgt/clt, ret,
/// and br/brtrue/brfalse/blt/bgt/ble/bge/beq/bne (short forms included).
/// Static int methods only; 0–8 int parameters, ≤16 int-sized locals; no EH or generics.
/// Unsigned compare/branch opcodes are rejected so original IL is kept.
/// </summary>
public class VirtualizationObfuscator : IObfuscator
{
    private const byte OpLdcI4 = 1;
    private const byte OpLdarg = 2;
    private const byte OpAdd = 3;
    private const byte OpSub = 4;
    private const byte OpMul = 5;
    private const byte OpRet = 6;
    private const byte OpLdloc = 7;
    private const byte OpStloc = 8;
    private const byte OpBr = 9;
    private const byte OpBrtrue = 10;
    private const byte OpBrfalse = 11;
    private const byte OpBle = 12;
    private const byte OpBge = 13;
    private const byte OpBlt = 14;
    private const byte OpBgt = 15;
    private const byte OpBeq = 16;
    private const byte OpBne = 17;
    private const byte OpCeq = 18;
    private const byte OpCgt = 19;
    private const byte OpClt = 20;

    private readonly ILogger<VirtualizationObfuscator> _logger;

    public VirtualizationObfuscator(ILogger<VirtualizationObfuscator> logger) => _logger = logger;

    public string Name => "Virtualization";
    public int Priority => (int)ObfuscationPhase.Virtualization;
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;
    public bool IsEnabled(ObfySettings settings) => settings.Virtualization.Enabled;

    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var stats = new ObfuscationStatistics();
        var max = Math.Clamp(context.Settings.Virtualization.MaxMethods, 1, 256);
        var encoded = new List<(MethodDef Method, byte[] Code)>();
        var truncated = false;

        try
        {
            foreach (var type in module.GetTypes())
            {
                if (ObfuscatorHelpers.IsRuntimeHelper(type) || type.IsGlobalModuleType)
                    continue;
                if (!ObfuscationAttributeRules.AllowType(type, context.Settings, ObfuscationFeature.All, context.Warnings))
                    continue;
                foreach (var method in type.Methods)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ObfuscationAttributeRules.AllowMethod(method, context.Settings, ObfuscationFeature.All, context.Warnings))
                        continue;
                    if (encoded.Count >= max)
                    {
                        if (IsCandidate(method))
                            truncated = true;
                        continue;
                    }
                    if (!TryEncode(method, out var code, out var skipReason))
                    {
                        if (skipReason is not null)
                        {
                            context.SkippedItems.Add(SkippedItem.UnsupportedMethod(method.FullName, skipReason));
                            _logger.LogDebug("Virtualization skipped {Method}: {Reason}", method.FullName, skipReason);
                        }
                        continue;
                    }
                    encoded.Add((method, code));
                }
            }

            if (truncated)
            {
                var warning = $"Virtualization: maxMethods={max} reached; further eligible methods were skipped.";
                context.Warnings.Add(warning);
                _logger.LogWarning("{Warning}", warning);
            }

            if (encoded.Count == 0)
            {
                const string unused = "Virtualization was enabled but no eligible methods were encoded.";
                context.Warnings.Add(unused);
                _logger.LogWarning("{Warning}", unused);
                return Task.FromResult(ObfuscationResult.Successful(stats));
            }

            var execute = InjectVm(module, encoded);
            for (var i = 0; i < encoded.Count; i++)
                ReplaceWithStub(encoded[i].Method, execute, i);

            stats.ProtectionsApplied = encoded.Count;
            _logger.LogInformation("Virtualized {Count} methods", encoded.Count);
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Virtualization failed");
            return Task.FromResult(ObfuscationResult.Failed($"Virtualization failed: {ex.Message}", ex));
        }
    }

    private static bool IsCandidate(MethodDef method)
    {
        if (!method.IsStatic || !method.HasBody || method.IsConstructor || method.HasGenericParameters)
            return false;
        if (method.Body.HasExceptionHandlers)
            return false;
        if (method.MethodSig?.RetType.ElementType != ElementType.I4)
            return false;
        if (method.MethodSig.Params.Any(p => p.ElementType != ElementType.I4))
            return false;
        return method.Parameters.Count <= 8;
    }

    private static bool IsIntSized(TypeSig? type) =>
        type?.ElementType is ElementType.I4 or ElementType.U4 or ElementType.Boolean
            or ElementType.I1 or ElementType.U1 or ElementType.I2 or ElementType.U2
            or ElementType.Char;

    private static bool IsUnsignedCompare(Code codeName) =>
        codeName is Code.Cgt_Un or Code.Clt_Un
            or Code.Ble_Un or Code.Ble_Un_S
            or Code.Bge_Un or Code.Bge_Un_S
            or Code.Blt_Un or Code.Blt_Un_S
            or Code.Bgt_Un or Code.Bgt_Un_S;

    private static bool TryEncode(MethodDef method, out byte[] code, out string? skipReason)
    {
        code = Array.Empty<byte>();
        skipReason = null;
        if (!IsCandidate(method))
            return false;
        if (method.Body.Variables.Count > 16)
        {
            skipReason = "too many locals";
            return false;
        }
        if (method.Body.Variables.Any(v => !IsIntSized(v.Type)))
        {
            skipReason = "non-int local";
            return false;
        }

        var buffer = new List<byte>();
        var map = new Dictionary<Instruction, int>();
        var branches = new List<(int OperandIndex, Instruction Target)>();
        foreach (var instr in method.Body.Instructions)
        {
            map[instr] = buffer.Count;
            var codeName = instr.OpCode.Code;
            if (codeName is Code.Nop or Code.Conv_I4)
                continue;
            if (IsUnsignedCompare(codeName))
            {
                skipReason = "unsigned compare";
                return false;
            }
            if (TryReadLdcI4(instr, out var value))
            {
                buffer.Add(OpLdcI4);
                buffer.AddRange(BitConverter.GetBytes(value));
                continue;
            }
            if (TryReadLdarg(instr, out var arg))
            {
                buffer.Add(OpLdarg);
                buffer.Add((byte)arg);
                continue;
            }
            if (TryReadLdloc(instr, out var loc))
            {
                if (loc is < 0 or > 15)
                {
                    skipReason = "local index out of range";
                    return false;
                }
                buffer.Add(OpLdloc);
                buffer.Add((byte)loc);
                continue;
            }
            if (TryReadStloc(instr, out loc))
            {
                if (loc is < 0 or > 15)
                {
                    skipReason = "local index out of range";
                    return false;
                }
                buffer.Add(OpStloc);
                buffer.Add((byte)loc);
                continue;
            }

            if (TryBranchOp(codeName, out var brOp))
            {
                if (instr.Operand is not Instruction target)
                {
                    skipReason = "bad branch target";
                    return false;
                }
                buffer.Add(brOp);
                branches.Add((buffer.Count, target));
                buffer.Add(0);
                buffer.Add(0);
                continue;
            }

            buffer.Add(codeName switch
            {
                Code.Add => OpAdd,
                Code.Sub => OpSub,
                Code.Mul => OpMul,
                Code.Ret => OpRet,
                Code.Ceq => OpCeq,
                Code.Cgt => OpCgt,
                Code.Clt => OpClt,
                _ => (byte)0
            });
            if (buffer[^1] == 0)
            {
                skipReason = "unsupported opcode";
                return false;
            }
        }

        foreach (var (operandIndex, target) in branches)
        {
            if (!map.TryGetValue(target, out var dest))
            {
                skipReason = "bad branch target";
                return false;
            }
            var bytes = BitConverter.GetBytes((ushort)dest);
            buffer[operandIndex] = bytes[0];
            buffer[operandIndex + 1] = bytes[1];
        }

        if (buffer.Count == 0 || buffer[^1] != OpRet)
        {
            skipReason = "invalid bytecode";
            return false;
        }
        code = buffer.ToArray();
        return true;
    }

    private static bool TryBranchOp(Code codeName, out byte op)
    {
        op = codeName switch
        {
            Code.Br or Code.Br_S => OpBr,
            Code.Brtrue or Code.Brtrue_S => OpBrtrue,
            Code.Brfalse or Code.Brfalse_S => OpBrfalse,
            Code.Ble or Code.Ble_S => OpBle,
            Code.Bge or Code.Bge_S => OpBge,
            Code.Blt or Code.Blt_S => OpBlt,
            Code.Bgt or Code.Bgt_S => OpBgt,
            Code.Beq or Code.Beq_S => OpBeq,
            Code.Bne_Un or Code.Bne_Un_S => OpBne,
            _ => (byte)0
        };
        return op != 0;
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
        return false;
    }

    private static bool TryReadLdcI4(Instruction instr, out int value)
    {
        value = 0;
        if (instr.OpCode == OpCodes.Ldc_I4_M1) { value = -1; return true; }
        if (instr.OpCode.Code is >= Code.Ldc_I4_0 and <= Code.Ldc_I4_8)
        {
            value = instr.OpCode.Code - Code.Ldc_I4_0;
            return true;
        }
        if (instr.OpCode == OpCodes.Ldc_I4_S && instr.Operand is sbyte sb) { value = sb; return true; }
        if (instr.OpCode == OpCodes.Ldc_I4 && instr.Operand is int i) { value = i; return true; }
        return false;
    }

    private static bool TryReadLdarg(Instruction instr, out int index)
    {
        index = 0;
        if (instr.OpCode.Code is >= Code.Ldarg_0 and <= Code.Ldarg_3)
        {
            index = instr.OpCode.Code - Code.Ldarg_0;
            return true;
        }
        if (instr.OpCode == OpCodes.Ldarg_S && instr.Operand is Parameter p)
        {
            index = p.Index;
            return true;
        }
        if (instr.OpCode == OpCodes.Ldarg && instr.Operand is Parameter p2)
        {
            index = p2.Index;
            return true;
        }
        return false;
    }

    private static MethodDef InjectVm(ModuleDef module, List<(MethodDef Method, byte[] Code)> encoded)
    {
        var typeDef = new TypeDefUser("Obfy.Runtime", "<Vm>", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract
        };
        module.Types.Add(typeDef);

        var blobField = new FieldDefUser("b", new FieldSig(new SZArraySig(module.CorLibTypes.Byte)),
            FieldAttributes.Private | FieldAttributes.Static);
        var startsField = new FieldDefUser("s", new FieldSig(new SZArraySig(module.CorLibTypes.Int32)),
            FieldAttributes.Private | FieldAttributes.Static);
        typeDef.Fields.Add(blobField);
        typeDef.Fields.Add(startsField);

        var blob = new List<byte>();
        var starts = new int[encoded.Count];
        for (var i = 0; i < encoded.Count; i++)
        {
            starts[i] = blob.Count;
            blob.AddRange(encoded[i].Code);
        }

        var dataType = new TypeDefUser("D", new TypeRefUser(module, "System", "ValueType", module.CorLibTypes.AssemblyRef))
        {
            Attributes = TypeAttributes.NestedPrivate | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed
        };
        dataType.ClassLayout = new ClassLayoutUser(1, (uint)blob.Count);
        typeDef.NestedTypes.Add(dataType);
        var dataField = new FieldDefUser("r", new FieldSig(new ValueTypeSig(dataType)),
            FieldAttributes.Static | FieldAttributes.Assembly | FieldAttributes.HasFieldRVA)
        { InitialValue = blob.ToArray() };
        typeDef.Fields.Add(dataField);

        var cctor = new MethodDefUser(".cctor", MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig |
            MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        var initArray = new MemberRefUser(module, "InitializeArray",
            MethodSig.CreateStatic(module.CorLibTypes.Void, new ClassSig(new TypeRefUser(module, "System", "Array", module.CorLibTypes.AssemblyRef)),
                new ValueTypeSig(new TypeRefUser(module, "System", "RuntimeFieldHandle", module.CorLibTypes.AssemblyRef))),
            new TypeRefUser(module, "System.Runtime.CompilerServices", "RuntimeHelpers", module.CorLibTypes.AssemblyRef));
        var cb = new CilBody();
        cb.Instructions.Add(Instruction.CreateLdcI4(blob.Count));
        cb.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.ToTypeDefOrRef()));
        cb.Instructions.Add(Instruction.Create(OpCodes.Dup));
        cb.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, dataField));
        cb.Instructions.Add(Instruction.Create(OpCodes.Call, initArray));
        cb.Instructions.Add(Instruction.Create(OpCodes.Stsfld, blobField));
        cb.Instructions.Add(Instruction.CreateLdcI4(starts.Length));
        cb.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Int32.ToTypeDefOrRef()));
        for (var i = 0; i < starts.Length; i++)
        {
            cb.Instructions.Add(Instruction.Create(OpCodes.Dup));
            cb.Instructions.Add(Instruction.CreateLdcI4(i));
            cb.Instructions.Add(Instruction.CreateLdcI4(starts[i]));
            cb.Instructions.Add(Instruction.Create(OpCodes.Stelem_I4));
        }
        cb.Instructions.Add(Instruction.Create(OpCodes.Stsfld, startsField));
        cb.Instructions.Add(Instruction.Create(OpCodes.Ret));
        cctor.Body = cb;
        typeDef.Methods.Add(cctor);

        var execute = CreateExecute(module, blobField, startsField);
        typeDef.Methods.Add(execute);
        return execute;
    }

    private static MethodDef CreateExecute(ModuleDef module, FieldDef blobField, FieldDef startsField)
    {
        var method = new MethodDefUser("Execute",
            MethodSig.CreateStatic(module.CorLibTypes.Object, module.CorLibTypes.Int32, new SZArraySig(module.CorLibTypes.Object)),
            MethodAttributes.Assembly | MethodAttributes.Static);
        var body = new CilBody { InitLocals = true };
        method.Body = body;
        var code = new Local(new SZArraySig(module.CorLibTypes.Byte));
        var ip = new Local(module.CorLibTypes.Int32);
        var stack = new Local(new SZArraySig(module.CorLibTypes.Int32));
        var sp = new Local(module.CorLibTypes.Int32);
        var op = new Local(module.CorLibTypes.Int32);
        var start = new Local(module.CorLibTypes.Int32);
        var end = new Local(module.CorLibTypes.Int32);
        var vars = new Local(new SZArraySig(module.CorLibTypes.Int32));
        var tmp = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(code);
        body.Variables.Add(ip);
        body.Variables.Add(stack);
        body.Variables.Add(sp);
        body.Variables.Add(op);
        body.Variables.Add(start);
        body.Variables.Add(end);
        body.Variables.Add(vars);
        body.Variables.Add(tmp);

        var invalidOpCtor = new MemberRefUser(
            module,
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String),
            new TypeRefUser(module, "System", "InvalidOperationException", module.CorLibTypes.AssemblyRef));
        var throwRange = Instruction.Create(OpCodes.Ldstr, "Obfy VM: branch out of range");

        var loop = Instruction.Create(OpCodes.Nop);
        var doLdc = Instruction.Create(OpCodes.Nop);
        var doLdarg = Instruction.Create(OpCodes.Nop);
        var doAdd = Instruction.Create(OpCodes.Nop);
        var doSub = Instruction.Create(OpCodes.Nop);
        var doMul = Instruction.Create(OpCodes.Nop);
        var doRet = Instruction.Create(OpCodes.Nop);
        var doLdloc = Instruction.Create(OpCodes.Nop);
        var doStloc = Instruction.Create(OpCodes.Nop);
        var doBr = Instruction.Create(OpCodes.Nop);
        var doBrtrue = Instruction.Create(OpCodes.Nop);
        var doBrfalse = Instruction.Create(OpCodes.Nop);
        var doBle = Instruction.Create(OpCodes.Nop);
        var doBge = Instruction.Create(OpCodes.Nop);
        var doBlt = Instruction.Create(OpCodes.Nop);
        var doBgt = Instruction.Create(OpCodes.Nop);
        var doBeq = Instruction.Create(OpCodes.Nop);
        var doBne = Instruction.Create(OpCodes.Nop);
        var doCeq = Instruction.Create(OpCodes.Nop);
        var doCgt = Instruction.Create(OpCodes.Nop);
        var doClt = Instruction.Create(OpCodes.Nop);

        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, blobField));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, code));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, startsField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ip));
        body.Instructions.Add(Instruction.CreateLdcI4(32));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Int32.ToTypeDefOrRef()));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, stack));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, start));
        var useCodeLen = Instruction.Create(OpCodes.Nop);
        var haveEnd = Instruction.Create(OpCodes.Nop);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, startsField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Bge, useCodeLen));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, startsField));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, haveEnd));
        body.Instructions.Add(useCodeLen);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(haveEnd);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, end));
        body.Instructions.Add(Instruction.CreateLdcI4(16));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Int32.ToTypeDefOrRef()));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, vars));

        body.Instructions.Add(loop);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, op));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doLdc));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doLdarg));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_3));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doAdd));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_4));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doSub));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_5));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doMul));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_6));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doRet));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(7));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doLdloc));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(8));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doStloc));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(9));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doBr));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(10));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doBrtrue));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(11));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doBrfalse));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(12));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doBle));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(13));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doBge));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(14));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doBlt));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(15));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doBgt));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(16));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doBeq));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(17));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doBne));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(18));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doCeq));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(19));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doCgt));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, op));
        body.Instructions.Add(Instruction.CreateLdcI4(20));
        body.Instructions.Add(Instruction.Create(OpCodes.Beq, doClt));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "Obfy VM: invalid opcode"));
        body.Instructions.Add(Instruction.Create(OpCodes.Newobj, invalidOpCtor));
        body.Instructions.Add(Instruction.Create(OpCodes.Throw));

        // ldc.i4
        body.Instructions.Add(doLdc);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.CreateLdcI4(8));
        body.Instructions.Add(Instruction.Create(OpCodes.Shl));
        body.Instructions.Add(Instruction.Create(OpCodes.Or));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.CreateLdcI4(16));
        body.Instructions.Add(Instruction.Create(OpCodes.Shl));
        body.Instructions.Add(Instruction.Create(OpCodes.Or));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_3));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.CreateLdcI4(24));
        body.Instructions.Add(Instruction.Create(OpCodes.Shl));
        body.Instructions.Add(Instruction.Create(OpCodes.Or));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_4));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, loop));

        // ldarg
        body.Instructions.Add(doLdarg);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        body.Instructions.Add(Instruction.Create(OpCodes.Unbox_Any, module.CorLibTypes.Int32.ToTypeDefOrRef()));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, loop));

        void EmitBin(Instruction label, OpCode arith)
        {
            body.Instructions.Add(label);
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Sub));
            body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sp));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Sub));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Sub));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
            body.Instructions.Add(Instruction.Create(arith));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I4));
            body.Instructions.Add(Instruction.Create(OpCodes.Br, loop));
        }

        EmitBin(doAdd, OpCodes.Add);
        EmitBin(doSub, OpCodes.Sub);
        EmitBin(doMul, OpCodes.Mul);
        EmitBin(doCeq, OpCodes.Ceq);
        EmitBin(doCgt, OpCodes.Cgt);
        EmitBin(doClt, OpCodes.Clt);

        void EmitReadU16()
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
            body.Instructions.Add(Instruction.CreateLdcI4(8));
            body.Instructions.Add(Instruction.Create(OpCodes.Shl));
            body.Instructions.Add(Instruction.Create(OpCodes.Or));
            body.Instructions.Add(Instruction.Create(OpCodes.Stloc, tmp));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ip));
        }

        void EmitGotoTarget()
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, start));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, tmp));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ip));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, start));
            body.Instructions.Add(Instruction.Create(OpCodes.Blt, throwRange));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, end));
            body.Instructions.Add(Instruction.Create(OpCodes.Bge, throwRange));
            body.Instructions.Add(Instruction.Create(OpCodes.Br, loop));
        }

        body.Instructions.Add(doLdloc);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, vars));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, loop));

        body.Instructions.Add(doStloc);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Sub));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, vars));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, code));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_U1));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ip));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, loop));

        body.Instructions.Add(doBr);
        EmitReadU16();
        EmitGotoTarget();

        var skipTrue = Instruction.Create(OpCodes.Br, loop);
        body.Instructions.Add(doBrtrue);
        EmitReadU16();
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Sub));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, skipTrue));
        EmitGotoTarget();
        body.Instructions.Add(skipTrue);

        var skipFalse = Instruction.Create(OpCodes.Br, loop);
        body.Instructions.Add(doBrfalse);
        EmitReadU16();
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Sub));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, skipFalse));
        EmitGotoTarget();
        body.Instructions.Add(skipFalse);

        void EmitCmp(Instruction label, OpCode compare, bool invert)
        {
            var skip = Instruction.Create(OpCodes.Br, loop);
            body.Instructions.Add(label);
            EmitReadU16();
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
            body.Instructions.Add(Instruction.Create(OpCodes.Sub));
            body.Instructions.Add(Instruction.Create(OpCodes.Stloc, sp));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
            body.Instructions.Add(Instruction.Create(compare));
            body.Instructions.Add(Instruction.Create(invert ? OpCodes.Brtrue : OpCodes.Brfalse, skip));
            EmitGotoTarget();
            body.Instructions.Add(skip);
        }

        EmitCmp(doBlt, OpCodes.Clt, invert: false); // a < b
        EmitCmp(doBgt, OpCodes.Cgt, invert: false); // a > b
        EmitCmp(doBeq, OpCodes.Ceq, invert: false); // a == b
        EmitCmp(doBge, OpCodes.Clt, invert: true);  // a >= b via !(a < b)
        EmitCmp(doBle, OpCodes.Cgt, invert: true);  // a <= b via !(a > b)
        EmitCmp(doBne, OpCodes.Ceq, invert: true);  // a != b via !(a == b)

        body.Instructions.Add(doRet);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Sub));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Box, module.CorLibTypes.Int32.ToTypeDefOrRef()));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.Instructions.Add(throwRange);
        body.Instructions.Add(Instruction.Create(OpCodes.Newobj, invalidOpCtor));
        body.Instructions.Add(Instruction.Create(OpCodes.Throw));
        body.KeepOldMaxStack = true;
        body.MaxStack = 16;
        body.UpdateInstructionOffsets();
        return method;
    }

    private static void ReplaceWithStub(MethodDef method, MethodDef execute, int id)
    {
        var module = method.Module;
        var n = method.Parameters.Count;
        var rebuilt = new CilBody();
        rebuilt.Instructions.Add(Instruction.CreateLdcI4(id));
        rebuilt.Instructions.Add(Instruction.CreateLdcI4(n));
        rebuilt.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Object.ToTypeDefOrRef()));
        for (var i = 0; i < n; i++)
        {
            rebuilt.Instructions.Add(Instruction.Create(OpCodes.Dup));
            rebuilt.Instructions.Add(Instruction.CreateLdcI4(i));
            rebuilt.Instructions.Add(Instruction.Create(OpCodes.Ldarg, method.Parameters[i]));
            rebuilt.Instructions.Add(Instruction.Create(OpCodes.Box, module.CorLibTypes.Int32.ToTypeDefOrRef()));
            rebuilt.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }
        rebuilt.Instructions.Add(Instruction.Create(OpCodes.Call, execute));
        rebuilt.Instructions.Add(Instruction.Create(OpCodes.Unbox_Any, module.CorLibTypes.Int32.ToTypeDefOrRef()));
        rebuilt.Instructions.Add(Instruction.Create(OpCodes.Ret));
        rebuilt.MaxStack = 8;
        rebuilt.KeepOldMaxStack = true;
        method.Body = rebuilt;
    }
}

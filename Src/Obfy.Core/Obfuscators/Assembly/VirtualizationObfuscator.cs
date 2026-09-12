using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Replaces simple static int methods with a bytecode interpreter stub.
/// Supports ldc.i4, ldarg, add, sub, mul, and ret only.
/// </summary>
public class VirtualizationObfuscator : IObfuscator
{
    private const byte OpLdcI4 = 1;
    private const byte OpLdarg = 2;
    private const byte OpAdd = 3;
    private const byte OpSub = 4;
    private const byte OpMul = 5;
    private const byte OpRet = 6;

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

        try
        {
            foreach (var type in module.GetTypes())
            {
                if (ObfuscatorHelpers.IsRuntimeHelper(type) || type.IsGlobalModuleType)
                    continue;
                foreach (var method in type.Methods)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (encoded.Count >= max)
                        break;
                    if (!TryEncode(method, out var code))
                        continue;
                    encoded.Add((method, code));
                }
            }

            if (encoded.Count == 0)
                return Task.FromResult(ObfuscationResult.Successful(stats));

            var execute = InjectVm(module, encoded);
            for (var i = 0; i < encoded.Count; i++)
                ReplaceWithStub(encoded[i].Method, execute, i);

            stats.ProtectionsApplied = encoded.Count;
            _logger.LogInformation("Virtualized {Count} methods", encoded.Count);
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Virtualization failed");
            return Task.FromResult(ObfuscationResult.Failed($"Virtualization failed: {ex.Message}", ex));
        }
    }

    private static bool TryEncode(MethodDef method, out byte[] code)
    {
        code = Array.Empty<byte>();
        if (!method.IsStatic || !method.HasBody || method.IsConstructor || method.HasGenericParameters)
            return false;
        if (method.Body.HasExceptionHandlers)
            return false;
        if (method.MethodSig?.RetType.ElementType != ElementType.I4)
            return false;
        if (method.MethodSig.Params.Any(p => p.ElementType != ElementType.I4))
            return false;
        if (method.Parameters.Count is < 1 or > 8)
            return false;

        var buffer = new List<byte>();
        foreach (var instr in method.Body.Instructions)
        {
            var codeName = instr.OpCode.Code;
            if (codeName is Code.Nop or Code.Conv_I4)
                continue;
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

            buffer.Add(codeName switch
            {
                Code.Add => OpAdd,
                Code.Sub => OpSub,
                Code.Mul => OpMul,
                Code.Ret => OpRet,
                _ => (byte)0
            });
            if (buffer[^1] == 0)
                return false;
        }

        if (buffer.Count == 0 || buffer[^1] != OpRet)
            return false;
        code = buffer.ToArray();
        return true;
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
        body.Variables.Add(code);
        body.Variables.Add(ip);
        body.Variables.Add(stack);
        body.Variables.Add(sp);
        body.Variables.Add(op);

        var loop = Instruction.Create(OpCodes.Nop);
        var doLdc = Instruction.Create(OpCodes.Nop);
        var doLdarg = Instruction.Create(OpCodes.Nop);
        var doAdd = Instruction.Create(OpCodes.Nop);
        var doSub = Instruction.Create(OpCodes.Nop);
        var doMul = Instruction.Create(OpCodes.Nop);
        var doRet = Instruction.Create(OpCodes.Nop);

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
        body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

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

        body.Instructions.Add(doRet);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, stack));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, sp));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Sub));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldelem_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Box, module.CorLibTypes.Int32.ToTypeDefOrRef()));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.KeepOldMaxStack = true;
        body.MaxStack = 8;
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

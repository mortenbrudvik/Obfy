using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Obfy.Core.Pipeline;

namespace Obfy.Core.Virtualization;

/// <summary>
/// Copies <c>Obfy.Runtime.Vm</c> from the embedded runtime into a target module and emits its
/// table-initializing <c>.cctor</c>.
/// </summary>
public static class VmImporter
{
    public const string EmbeddedName = "Obfy.VmRuntime.dll";

    private const string VmFullName = "Obfy.Runtime.Vm";
    private const int StelemIntThreshold = 16;

    public static TypeDef Import(
        PipelineContext context,
        byte[] code,
        int[] starts,
        byte[] opMap,
        byte[] xorKey,
        IReadOnlyList<IMethod> methods,
        IReadOnlyList<IField> fields,
        IReadOnlyList<ITypeDefOrRef> types,
        IReadOnlyList<ITypeDefOrRef> returnTypes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(opMap);
        ArgumentNullException.ThrowIfNull(xorKey);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(returnTypes);

        var dest = context.RequireModule();
        var vmType = CopyVmType(dest);
        RetargetCorlib(vmType, dest);
        ReplaceCctor(vmType, dest, code, starts, opMap, xorKey, methods, fields, types, returnTypes);
        RuntimeInjection.Register(context, vmType, new RuntimeHelperOptions
        {
            FlattenControlFlow = true,
            Rename = true,
            EncryptIl = false
        });
        return vmType;
    }

    public static void WriteStub(MethodDef method, MethodDef run, int id)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(run);
        var module = method.Module
            ?? throw new InvalidOperationException("Virtualization failed: stub method has no module.");
        var argc = method.Parameters.Count;
        var body = new CilBody { MaxStack = 8 };
        body.Instructions.Add(Instruction.CreateLdcI4(id));
        body.Instructions.Add(Instruction.CreateLdcI4(argc));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Object.ToTypeDefOrRef()));
        for (var i = 0; i < argc; i++)
        {
            var param = method.Parameters[i];
            body.Instructions.Add(Instruction.Create(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg, param));
            var paramType = param.Type.RemovePinnedAndModifiers();
            if (paramType is not null && paramType.IsValueType)
                body.Instructions.Add(Instruction.Create(OpCodes.Box, paramType.ToTypeDefOrRef()));
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Call, run));
        var ret = method.MethodSig?.RetType.RemovePinnedAndModifiers();
        if (ret is null || ret.ElementType == ElementType.Void)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Pop));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
        else if (ret.IsValueType)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Unbox_Any, ret.ToTypeDefOrRef()));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
        else
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Castclass, ret.ToTypeDefOrRef()));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }

        body.UpdateInstructionOffsets();
        method.Body = body;
    }

    private static TypeDef CopyVmType(ModuleDef dest)
    {
        using var source = ModuleDefMD.Load(ReadEmbeddedBytes());
        var origin = source.Find(VmFullName, isReflectionName: false)
            ?? throw new InvalidOperationException(
                $"Virtualization failed: embedded {EmbeddedName} does not contain {VmFullName}.");

        var map = new Dictionary<IDnlibDef, IDnlibDef>();
        var cloned = CloneTypeSkeleton(origin, map);
        dest.Types.Add(cloned);

        var importer = new Importer(
            dest,
            ImporterOptions.TryToUseDefs,
            new GenericParamContext(),
            new CopiedMemberMapper(dest, map));

        CopyTypeContents(origin, cloned, importer, map);
        return cloned;
    }

    private static TypeDefUser CloneTypeSkeleton(TypeDef origin, Dictionary<IDnlibDef, IDnlibDef> map)
    {
        var cloned = new TypeDefUser(origin.Namespace, origin.Name)
        {
            Attributes = origin.Attributes
        };
        if (origin.ClassLayout is not null)
            cloned.ClassLayout = new ClassLayoutUser(origin.ClassLayout.PackingSize, origin.ClassLayout.ClassSize);

        foreach (var gp in origin.GenericParameters)
            cloned.GenericParameters.Add(new GenericParamUser(gp.Number, gp.Flags, gp.Name));

        map[origin] = cloned;
        foreach (var nested in origin.NestedTypes)
            cloned.NestedTypes.Add(CloneTypeSkeleton(nested, map));
        return cloned;
    }

    private static void CopyTypeContents(
        TypeDef origin,
        TypeDef cloned,
        Importer importer,
        Dictionary<IDnlibDef, IDnlibDef> map)
    {
        cloned.BaseType = origin.BaseType is null ? null : importer.Import(origin.BaseType);
        foreach (var iface in origin.Interfaces)
            cloned.Interfaces.Add(new InterfaceImplUser(importer.Import(iface.Interface)));

        foreach (var field in origin.Fields)
        {
            var clonedField = new FieldDefUser(
                field.Name,
                importer.Import(field.FieldSig),
                field.Attributes)
            {
                InitialValue = field.InitialValue is null ? null : (byte[])field.InitialValue.Clone()
            };
            map[field] = clonedField;
            cloned.Fields.Add(clonedField);
        }

        var methodsToCopy = new List<(MethodDef Origin, MethodDef Cloned)>();
        foreach (var method in origin.Methods)
        {
            if (method.IsStaticConstructor)
                continue;

            var clonedMethod = new MethodDefUser(
                method.Name,
                importer.Import(method.MethodSig),
                method.ImplAttributes,
                method.Attributes);
            foreach (var param in method.ParamDefs)
                clonedMethod.ParamDefs.Add(new ParamDefUser(param.Name, param.Sequence, param.Attributes));
            map[method] = clonedMethod;
            cloned.Methods.Add(clonedMethod);
            methodsToCopy.Add((method, clonedMethod));
        }

        foreach (var (originMethod, clonedMethod) in methodsToCopy)
            CopyMethodBody(originMethod, clonedMethod, importer, map);

        foreach (var nested in origin.NestedTypes)
            CopyTypeContents(nested, (TypeDef)map[nested], importer, map);
    }

    private static void CopyMethodBody(
        MethodDef origin,
        MethodDef cloned,
        Importer importer,
        Dictionary<IDnlibDef, IDnlibDef> map)
    {
        if (origin.Body is null)
            return;

        var body = origin.Body;
        var newBody = new CilBody
        {
            InitLocals = body.InitLocals,
            MaxStack = body.MaxStack
        };
        cloned.Body = newBody;

        var localMap = new Dictionary<Local, Local>();
        foreach (var local in body.Variables)
        {
            var clonedLocal = new Local(importer.Import(local.Type), local.Name);
            localMap[local] = clonedLocal;
            newBody.Variables.Add(clonedLocal);
        }

        var instrMap = new Dictionary<Instruction, Instruction>();
        foreach (var instr in body.Instructions)
        {
            var clonedInstr = new Instruction(instr.OpCode, instr.Operand);
            instrMap[instr] = clonedInstr;
            newBody.Instructions.Add(clonedInstr);
        }

        foreach (var clonedInstr in newBody.Instructions)
            clonedInstr.Operand = RemapOperand(clonedInstr.Operand, cloned, importer, map, localMap, instrMap);

        foreach (var eh in body.ExceptionHandlers)
        {
            newBody.ExceptionHandlers.Add(new ExceptionHandler(eh.HandlerType)
            {
                CatchType = eh.CatchType is null ? null : importer.Import(eh.CatchType),
                TryStart = MapInstruction(eh.TryStart, instrMap),
                TryEnd = MapInstruction(eh.TryEnd, instrMap),
                HandlerStart = MapInstruction(eh.HandlerStart, instrMap),
                HandlerEnd = MapInstruction(eh.HandlerEnd, instrMap),
                FilterStart = MapInstruction(eh.FilterStart, instrMap)
            });
        }

        newBody.UpdateInstructionOffsets();
    }

    private static Instruction? MapInstruction(Instruction? instr, Dictionary<Instruction, Instruction> instrMap) =>
        instr is null ? null : instrMap[instr];

    private static object? RemapOperand(
        object? operand,
        MethodDef clonedMethod,
        Importer importer,
        Dictionary<IDnlibDef, IDnlibDef> map,
        Dictionary<Local, Local> localMap,
        Dictionary<Instruction, Instruction> instrMap)
    {
        switch (operand)
        {
            case null:
                return null;
            case Instruction target:
                return instrMap[target];
            case IList<Instruction> targets:
                return targets.Select(t => instrMap[t]).ToArray();
            case Local local:
                return localMap[local];
            case Parameter param:
                return clonedMethod.Parameters[param.Index];
            case TypeDef typeDef:
                return map.TryGetValue(typeDef, out var mappedType)
                    ? (TypeDef)mappedType
                    : importer.Import(typeDef);
            case FieldDef fieldDef:
                return map.TryGetValue(fieldDef, out var mappedField)
                    ? (FieldDef)mappedField
                    : importer.Import(fieldDef);
            case MethodDef methodDef:
                return map.TryGetValue(methodDef, out var mappedMethod)
                    ? (MethodDef)mappedMethod
                    : importer.Import(methodDef);
            case TypeSpec typeSpec:
                return importer.Import(typeSpec);
            case TypeRef typeRef:
                return importer.Import(typeRef);
            case MemberRef memberRef:
                return importer.Import(memberRef);
            case MethodSpec methodSpec:
                return importer.Import(methodSpec);
            case MethodSig methodSig:
                return importer.Import(methodSig);
            case ITypeDefOrRef type:
                return importer.Import(type);
            case IMethod method:
                return importer.Import(method);
            case IField field:
                return importer.Import(field);
            default:
                return operand;
        }
    }

    private static void RetargetCorlib(TypeDef type, ModuleDef dest)
    {
        RetargetType(type.BaseType, dest);
        foreach (var iface in type.Interfaces)
            RetargetType(iface.Interface, dest);

        foreach (var field in type.Fields)
            RetargetSig(field.FieldType, dest);

        foreach (var method in type.Methods)
        {
            RetargetSig(method.MethodSig, dest);
            if (method.Body is null)
                continue;

            foreach (var local in method.Body.Variables)
                RetargetSig(local.Type, dest);

            foreach (var instr in method.Body.Instructions)
                RetargetOperand(instr.Operand, dest);

            foreach (var eh in method.Body.ExceptionHandlers)
                RetargetType(eh.CatchType, dest);
        }

        foreach (var nested in type.NestedTypes)
            RetargetCorlib(nested, dest);
    }

    private static void RetargetOperand(object? operand, ModuleDef dest)
    {
        switch (operand)
        {
            case TypeRef typeRef:
                RetargetTypeRef(typeRef, dest);
                break;
            case TypeSpec typeSpec:
                RetargetSig(typeSpec.TypeSig, dest);
                break;
            case MemberRef memberRef:
                RetargetType(memberRef.Class as ITypeDefOrRef, dest);
                RetargetSig(memberRef.MethodSig, dest);
                RetargetSig(memberRef.FieldSig, dest);
                break;
            case MethodSpec methodSpec:
                RetargetOperand(methodSpec.Method, dest);
                RetargetSig(methodSpec.GenericInstMethodSig, dest);
                break;
            case ITypeDefOrRef type:
                RetargetType(type, dest);
                break;
            case TypeSig sig:
                RetargetSig(sig, dest);
                break;
        }
    }

    private static void RetargetType(ITypeDefOrRef? type, ModuleDef dest)
    {
        switch (type)
        {
            case TypeRef typeRef:
                RetargetTypeRef(typeRef, dest);
                break;
            case TypeSpec typeSpec:
                RetargetSig(typeSpec.TypeSig, dest);
                break;
        }
    }

    private static void RetargetTypeRef(TypeRef typeRef, ModuleDef dest)
    {
        if (typeRef.ResolutionScope is TypeRef parent)
        {
            RetargetTypeRef(parent, dest);
            return;
        }

        if (typeRef.ResolutionScope is AssemblyRef assemblyRef && IsCorlibAssembly(assemblyRef.Name))
            typeRef.ResolutionScope = dest.CorLibTypes.AssemblyRef;
    }

    private static void RetargetSig(CallingConventionSig? sig, ModuleDef dest)
    {
        switch (sig)
        {
            case MethodSig methodSig:
                RetargetSig(methodSig.RetType, dest);
                foreach (var param in methodSig.Params)
                    RetargetSig(param, dest);
                if (methodSig.ParamsAfterSentinel is null)
                    break;
                foreach (var param in methodSig.ParamsAfterSentinel)
                    RetargetSig(param, dest);
                break;
            case FieldSig fieldSig:
                RetargetSig(fieldSig.Type, dest);
                break;
            case GenericInstMethodSig generic:
                foreach (var arg in generic.GenericArguments)
                    RetargetSig(arg, dest);
                break;
        }
    }

    private static void RetargetSig(TypeSig? sig, ModuleDef dest)
    {
        while (sig is not null)
        {
            switch (sig)
            {
                case TypeDefOrRefSig tdr:
                    RetargetType(tdr.TypeDefOrRef, dest);
                    break;
                case GenericInstSig generic:
                    RetargetSig(generic.GenericType, dest);
                    foreach (var arg in generic.GenericArguments)
                        RetargetSig(arg, dest);
                    break;
                case ModifierSig modifier:
                    RetargetType(modifier.Modifier, dest);
                    break;
            }

            sig = sig.Next;
        }
    }

    private static void ReplaceCctor(
        TypeDef vmType,
        ModuleDef dest,
        byte[] code,
        int[] starts,
        byte[] opMap,
        byte[] xorKey,
        IReadOnlyList<IMethod> methods,
        IReadOnlyList<IField> fields,
        IReadOnlyList<ITypeDefOrRef> types,
        IReadOnlyList<ITypeDefOrRef> returnTypes)
    {
        var existing = vmType.FindStaticConstructor();
        if (existing is not null)
            vmType.Methods.Remove(existing);

        var init = vmType.FindMethod("Init")
            ?? throw new InvalidOperationException(
                "Virtualization failed: Obfy.Runtime.Vm.Init was not found after import.");

        var initArray = new MemberRefUser(
            dest,
            "InitializeArray",
            MethodSig.CreateStatic(
                dest.CorLibTypes.Void,
                new ClassSig(CorlibRef(dest, "System", "Array")),
                new ValueTypeSig(CorlibRef(dest, "System", "RuntimeFieldHandle"))),
            CorlibRef(dest, "System.Runtime.CompilerServices", "RuntimeHelpers"));

        var methodBase = CorlibRef(dest, "System.Reflection", "MethodBase");
        var fieldInfo = CorlibRef(dest, "System.Reflection", "FieldInfo");
        var typeType = CorlibRef(dest, "System", "Type");
        var runtimeTypeHandle = new ValueTypeSig(CorlibRef(dest, "System", "RuntimeTypeHandle"));
        var getMethodFromHandle = new MemberRefUser(
            dest,
            "GetMethodFromHandle",
            MethodSig.CreateStatic(
                new ClassSig(methodBase),
                new ValueTypeSig(CorlibRef(dest, "System", "RuntimeMethodHandle")),
                runtimeTypeHandle),
            methodBase);
        var getFieldFromHandle = new MemberRefUser(
            dest,
            "GetFieldFromHandle",
            MethodSig.CreateStatic(
                new ClassSig(fieldInfo),
                new ValueTypeSig(CorlibRef(dest, "System", "RuntimeFieldHandle")),
                runtimeTypeHandle),
            fieldInfo);
        var getTypeFromHandle = new MemberRefUser(
            dest,
            "GetTypeFromHandle",
            MethodSig.CreateStatic(
                new ClassSig(typeType),
                new ValueTypeSig(CorlibRef(dest, "System", "RuntimeTypeHandle"))),
            typeType);

        var cctor = new MethodDefUser(
            ".cctor",
            MethodSig.CreateStatic(dest.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig |
            MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        var body = new CilBody();
        cctor.Body = body;

        EmitByteArray(body, dest, vmType, code, initArray, "rva_code");
        EmitIntArray(body, dest, vmType, starts, initArray, "rva_starts");
        EmitByteArray(body, dest, vmType, opMap, initArray, "rva_opMap");
        EmitByteArray(body, dest, vmType, xorKey, initArray, "rva_xorKey");
        EmitTokenArray(body, methods.Count, methodBase, i =>
        {
            var method = methods[i];
            body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, method));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, DeclaringType(method)));
            body.Instructions.Add(Instruction.Create(OpCodes.Call, getMethodFromHandle));
        });
        EmitTokenArray(body, fields.Count, fieldInfo, i =>
        {
            var field = fields[i];
            body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, field));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, DeclaringType(field)));
            body.Instructions.Add(Instruction.Create(OpCodes.Call, getFieldFromHandle));
        });
        EmitTokenArray(body, types.Count, typeType, i =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, types[i]));
            body.Instructions.Add(Instruction.Create(OpCodes.Call, getTypeFromHandle));
        });
        EmitTokenArray(body, returnTypes.Count, typeType, i =>
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, returnTypes[i]));
            body.Instructions.Add(Instruction.Create(OpCodes.Call, getTypeFromHandle));
        });
        body.Instructions.Add(Instruction.Create(OpCodes.Call, init));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
        vmType.Methods.Add(cctor);
    }

    private static void EmitByteArray(
        CilBody body,
        ModuleDef dest,
        TypeDef vmType,
        byte[] data,
        MemberRef initArray,
        string rvaName)
    {
        body.Instructions.Add(Instruction.CreateLdcI4(data.Length));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, dest.CorLibTypes.Byte.ToTypeDefOrRef()));
        if (data.Length == 0)
            return;

        body.Instructions.Add(Instruction.Create(OpCodes.Dup));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, CreateRvaField(vmType, dest, rvaName, data)));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, initArray));
    }

    private static void EmitIntArray(
        CilBody body,
        ModuleDef dest,
        TypeDef vmType,
        int[] data,
        MemberRef initArray,
        string rvaName)
    {
        body.Instructions.Add(Instruction.CreateLdcI4(data.Length));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, dest.CorLibTypes.Int32.ToTypeDefOrRef()));
        if (data.Length == 0)
            return;

        if (data.Length <= StelemIntThreshold)
        {
            for (var i = 0; i < data.Length; i++)
            {
                body.Instructions.Add(Instruction.Create(OpCodes.Dup));
                body.Instructions.Add(Instruction.CreateLdcI4(i));
                body.Instructions.Add(Instruction.CreateLdcI4(data[i]));
                body.Instructions.Add(Instruction.Create(OpCodes.Stelem_I4));
            }

            return;
        }

        var bytes = new byte[checked(data.Length * sizeof(int))];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        body.Instructions.Add(Instruction.Create(OpCodes.Dup));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, CreateRvaField(vmType, dest, rvaName, bytes)));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, initArray));
    }

    private static void EmitTokenArray(
        CilBody body,
        int count,
        ITypeDefOrRef elementType,
        Action<int> emitElement)
    {
        body.Instructions.Add(Instruction.CreateLdcI4(count));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, elementType));
        for (var i = 0; i < count; i++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Dup));
            body.Instructions.Add(Instruction.CreateLdcI4(i));
            emitElement(i);
            body.Instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }
    }

    private static FieldDef CreateRvaField(TypeDef vmType, ModuleDef dest, string name, byte[] data)
    {
        var dataType = new TypeDefUser(
            name + "Data",
            new TypeRefUser(dest, "System", "ValueType", dest.CorLibTypes.AssemblyRef))
        {
            Attributes = TypeAttributes.NestedPrivate | TypeAttributes.ExplicitLayout |
                         TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            ClassLayout = new ClassLayoutUser(1, (uint)data.Length)
        };
        vmType.NestedTypes.Add(dataType);

        var field = new FieldDefUser(
            name,
            new FieldSig(new ValueTypeSig(dataType)),
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.HasFieldRVA)
        {
            InitialValue = data
        };
        vmType.Fields.Add(field);
        return field;
    }

    private static ITypeDefOrRef DeclaringType(IMemberRef member) =>
        member.DeclaringType
        ?? throw new InvalidOperationException(
            $"Virtualization failed: '{member.FullName}' has no declaring type for Get*FromHandle.");

    private static TypeRef CorlibRef(ModuleDef dest, string ns, string name) =>
        dest.CorLibTypes.GetTypeRef(ns, name);

    private static byte[] ReadEmbeddedBytes()
    {
        using var stream = typeof(VmImporter).Assembly.GetManifestResourceStream(EmbeddedName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Virtualization failed: embedded resource '{EmbeddedName}' was not found.");
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool IsCorlibAssembly(UTF8String? name)
    {
        var simple = UTF8String.ToSystemStringOrEmpty(name);
        return simple is "netstandard" or "System.Runtime" or "mscorlib" or "System.Private.CoreLib";
    }

    private sealed class CopiedMemberMapper : ImportMapper
    {
        private readonly ModuleDef _dest;
        private readonly Dictionary<IDnlibDef, IDnlibDef> _map;

        public CopiedMemberMapper(ModuleDef dest, Dictionary<IDnlibDef, IDnlibDef> map)
        {
            _dest = dest;
            _map = map;
        }

        public override ITypeDefOrRef? Map(ITypeDefOrRef source)
        {
            if (source is TypeDef typeDef && _map.TryGetValue(typeDef, out var mapped))
                return (TypeDef)mapped;

            if (source is TypeRef typeRef && IsCorlibAssembly(typeRef.DefinitionAssembly?.Name))
                return _dest.CorLibTypes.GetTypeRef(typeRef.Namespace, typeRef.Name);

            return null;
        }

        public override IField? Map(FieldDef source) =>
            _map.TryGetValue(source, out var mapped) ? (FieldDef)mapped : null;

        public override IMethod? Map(MethodDef source) =>
            _map.TryGetValue(source, out var mapped) ? (MethodDef)mapped : null;
    }
}

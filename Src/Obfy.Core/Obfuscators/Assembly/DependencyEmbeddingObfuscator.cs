using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Embeds referenced assemblies next to the input as resources and loads them via AssemblyResolve.
/// </summary>
public class DependencyEmbeddingObfuscator : IObfuscator
{
    public const string ResourcePrefix = "Obfy.Embedded.";

    private readonly ILogger<DependencyEmbeddingObfuscator> _logger;

    public DependencyEmbeddingObfuscator(ILogger<DependencyEmbeddingObfuscator> logger)
    {
        _logger = logger;
    }

    public string Name => "DependencyEmbedding";

    public int Priority => (int)ObfuscationPhase.DependencyEmbedding;

    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;

    public bool IsEnabled(ObfySettings settings) =>
        settings.DependencyEmbedding.Enabled &&
        !RuntimeProfileGating.BlocksPeProtections(settings.RuntimeProfile);

    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var stats = new ObfuscationStatistics();
        var settings = context.Settings.DependencyEmbedding;

        try
        {
            var inputDir = Path.GetDirectoryName(context.InputPath);
            if (string.IsNullOrEmpty(inputDir) || !Directory.Exists(inputDir))
            {
                _logger.LogInformation("Dependency embedding: no input directory, nothing to embed");
                return Task.FromResult(ObfuscationResult.Successful(stats));
            }

            var selfName = Path.GetFileNameWithoutExtension(context.InputPath);
            var embedded = 0;

            foreach (var asmRef in module.GetAssemblyRefs())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var simple = asmRef.Name.String;
                if (string.Equals(simple, selfName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = simple + ".dll";
                if (!IsIncluded(fileName, settings))
                    continue;

                var path = Path.Combine(inputDir, fileName);
                if (!File.Exists(path))
                    continue;

                var resourceName = ResourcePrefix + simple + ".dll";
                if (module.Resources.Any(r => r.Name == resourceName))
                    continue;

                module.Resources.Add(new EmbeddedResource(resourceName, File.ReadAllBytes(path)));
                embedded++;
                stats.ProtectionsApplied++;
            }

            if (embedded > 0)
                InjectResolver(module);

            _logger.LogInformation("Embedded {Count} dependency assemblies", embedded);
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dependency embedding failed");
            return Task.FromResult(ObfuscationResult.Failed($"Dependency embedding failed: {ex.Message}", ex));
        }
    }

    private static bool IsIncluded(string fileName, DependencyEmbeddingSettings settings)
    {
        if (settings.ExcludePatterns.Any(p => WildcardMatcher.IsMatch(fileName, p)))
            return false;
        return settings.IncludePatterns.Count == 0 ||
               settings.IncludePatterns.Any(p => WildcardMatcher.IsMatch(fileName, p));
    }

    private static void InjectResolver(ModuleDef module)
    {
        var typeDef = new TypeDefUser(
            "Obfy.Runtime",
            "<Embed>",
            module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Abstract
        };
        module.Types.Add(typeDef);

        var resolve = CreateResolveMethod(module);
        typeDef.Methods.Add(resolve);

        var global = module.GlobalType;
        if (global == null)
        {
            global = new TypeDefUser("", "<Module>", null) { Attributes = TypeAttributes.NotPublic };
            module.Types.Insert(0, global);
        }

        var cctor = global.Methods.FirstOrDefault(m => m.IsStaticConstructor);
        if (cctor == null)
        {
            cctor = new MethodDefUser(
                ".cctor",
                MethodSig.CreateStatic(module.CorLibTypes.Void),
                MethodAttributes.Private | MethodAttributes.Static |
                MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
            cctor.Body = new CilBody();
            cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            global.Methods.Add(cctor);
        }

        var domain = new TypeRefUser(module, "System", "AppDomain", module.CorLibTypes.AssemblyRef);
        var handler = new TypeRefUser(module, "System", "ResolveEventHandler", module.CorLibTypes.AssemblyRef);
        var getDomain = new MemberRefUser(module, "get_CurrentDomain",
            MethodSig.CreateStatic(new ClassSig(domain)), domain);
        var handlerCtor = new MemberRefUser(module, ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Object, module.CorLibTypes.IntPtr),
            handler);
        var addResolve = new MemberRefUser(module, "add_AssemblyResolve",
            MethodSig.CreateInstance(module.CorLibTypes.Void, new ClassSig(handler)), domain);

        var body = cctor.Body!;
        var i = 0;
        body.Instructions.Insert(i++, Instruction.Create(OpCodes.Call, getDomain));
        body.Instructions.Insert(i++, Instruction.Create(OpCodes.Ldnull));
        body.Instructions.Insert(i++, Instruction.Create(OpCodes.Ldftn, resolve));
        body.Instructions.Insert(i++, Instruction.Create(OpCodes.Newobj, handlerCtor));
        body.Instructions.Insert(i, Instruction.Create(OpCodes.Callvirt, addResolve));
        body.UpdateInstructionOffsets();
    }

    private static MethodDef CreateResolveMethod(ModuleDef module)
    {
        var assemblyType = new TypeRefUser(module, "System.Reflection", "Assembly", module.CorLibTypes.AssemblyRef);
        var assemblyNameType = new TypeRefUser(module, "System.Reflection", "AssemblyName", module.CorLibTypes.AssemblyRef);
        var streamType = new TypeRefUser(module, "System.IO", "Stream", module.CorLibTypes.AssemblyRef);
        var argsType = new TypeRefUser(module, "System", "ResolveEventArgs", module.CorLibTypes.AssemblyRef);

        var method = new MethodDefUser(
            "Resolve",
            MethodSig.CreateStatic(
                new ClassSig(assemblyType),
                module.CorLibTypes.Object,
                new ClassSig(argsType)),
            MethodAttributes.Assembly | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        method.Body = body;
        var nameLocal = new Local(module.CorLibTypes.String);
        var streamLocal = new Local(new ClassSig(streamType));
        var bufLocal = new Local(new SZArraySig(module.CorLibTypes.Byte));
        body.Variables.Add(nameLocal);
        body.Variables.Add(streamLocal);
        body.Variables.Add(bufLocal);

        var getArgsName = new MemberRefUser(module, "get_Name",
            MethodSig.CreateInstance(module.CorLibTypes.String), argsType);
        var asmNameCtor = new MemberRefUser(module, ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String), assemblyNameType);
        var getAsmName = new MemberRefUser(module, "get_Name",
            MethodSig.CreateInstance(module.CorLibTypes.String), assemblyNameType);
        var getExecuting = new MemberRefUser(module, "GetExecutingAssembly",
            MethodSig.CreateStatic(new ClassSig(assemblyType)), assemblyType);
        var getStream = new MemberRefUser(module, "GetManifestResourceStream",
            MethodSig.CreateInstance(new ClassSig(streamType), module.CorLibTypes.String), assemblyType);
        var concat3 = new MemberRefUser(module, "Concat",
            MethodSig.CreateStatic(module.CorLibTypes.String, module.CorLibTypes.String, module.CorLibTypes.String, module.CorLibTypes.String),
            module.CorLibTypes.String.TypeDefOrRef);
        var getLength = new MemberRefUser(module, "get_Length",
            MethodSig.CreateInstance(module.CorLibTypes.Int64), streamType);
        var read = new MemberRefUser(module, "Read",
            MethodSig.CreateInstance(module.CorLibTypes.Int32, new SZArraySig(module.CorLibTypes.Byte),
                module.CorLibTypes.Int32, module.CorLibTypes.Int32), streamType);
        var load = new MemberRefUser(module, "Load",
            MethodSig.CreateStatic(new ClassSig(assemblyType), new SZArraySig(module.CorLibTypes.Byte)),
            assemblyType);

        var retNull = Instruction.Create(OpCodes.Ldnull);
        var haveStream = Instruction.Create(OpCodes.Stloc, streamLocal);

        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getArgsName));
        body.Instructions.Add(Instruction.Create(OpCodes.Newobj, asmNameCtor));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, getAsmName));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, nameLocal));

        body.Instructions.Add(Instruction.Create(OpCodes.Call, getExecuting));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, ResourcePrefix));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, nameLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, ".dll"));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, concat3));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getStream));
        body.Instructions.Add(Instruction.Create(OpCodes.Dup));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, haveStream));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(retNull);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.Instructions.Add(haveStream);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, streamLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getLength));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_Ovf_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, bufLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, streamLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bufLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bufLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, read));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bufLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, load));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.KeepOldMaxStack = true;
        body.MaxStack = 8;
        body.UpdateInstructionOffsets();
        return method;
    }
}

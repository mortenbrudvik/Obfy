using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Embeds sibling <c>{AssemblyRef.Name}.dll</c> files next to the input as
/// <c>Obfy.Embedded.{name}.dll</c> resources and loads them via
/// <c>AppDomain.CurrentDomain.AssemblyResolve</c>. Disabled on NativeAOT / Unity IL2CPP / Blazor WASM.
/// Does not probe NuGet, GAC, or <c>.exe</c> files, and does not strip assembly references.
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
        !RuntimeProfileGating.BlocksAssemblyResolve(settings.RuntimeProfile);

    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var stats = new ObfuscationStatistics();
        var settings = context.Settings.DependencyEmbedding;

        try
        {
            var inputPath = context.InputPath;
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                const string missingPath =
                    "Dependency embedding was enabled but the input path is missing; no assemblies were packed.";
                context.Warnings.Add(missingPath);
                _logger.LogWarning("{Warning}", missingPath);
                return Task.FromResult(ObfuscationResult.Successful(stats));
            }

            inputPath = Path.GetFullPath(inputPath);
            var inputDir = Path.GetDirectoryName(inputPath);
            if (string.IsNullOrEmpty(inputDir) || !Directory.Exists(inputDir))
            {
                var warning =
                    "Dependency embedding was enabled but the input directory is missing; no assemblies were packed.";
                context.Warnings.Add(warning);
                _logger.LogWarning("{Warning}", warning);
                return Task.FromResult(ObfuscationResult.Successful(stats));
            }

            var selfName = Path.GetFileNameWithoutExtension(inputPath);
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
                {
                    if (!IsFrameworkAssembly(simple))
                    {
                        context.Warnings.Add(
                            $"Dependency embedding: referenced assembly '{fileName}' was not found next to the input and was not packed.");
                    }
                    continue;
                }

                var resourceName = ResourcePrefix + simple + ".dll";
                if (module.Resources.Any(r => r.Name == resourceName))
                {
                    context.Warnings.Add(
                        $"Dependency embedding: resource '{resourceName}' already exists; not replaced.");
                    continue;
                }

                module.Resources.Add(new EmbeddedResource(resourceName, File.ReadAllBytes(path)));
                embedded++;
                stats.AssembliesEmbedded++;
            }

            if (embedded > 0)
                InjectResolver(module);
            else
            {
                const string unused =
                    "Dependency embedding was enabled but no referenced assemblies were packed. " +
                    "Refs must be '{Name}.dll' next to the input; framework assemblies are not embedded.";
                context.Warnings.Add(unused);
                _logger.LogWarning("{Warning}", unused);
            }

            _logger.LogInformation("Embedded {Count} dependency assemblies", embedded);
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Dependency embedding failed");
            return Task.FromResult(ObfuscationResult.Failed($"Dependency embedding failed: {ex.Message}", ex));
        }
    }

    private static bool IsIncluded(string fileName, DependencyEmbeddingSettings settings)
    {
        var excludes = settings.ExcludePatterns ?? new List<string>();
        var includes = settings.IncludePatterns ?? new List<string>();
        if (excludes.Any(p => WildcardMatcher.IsMatch(fileName, p)))
            return false;
        return includes.Count == 0 ||
               includes.Any(p => WildcardMatcher.IsMatch(fileName, p));
    }

    internal static bool IsFrameworkAssembly(string simpleName) =>
        simpleName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
        simpleName.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
        simpleName.Equals("System", StringComparison.OrdinalIgnoreCase) ||
        simpleName.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase) ||
        simpleName.Equals("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
        simpleName.Equals("PresentationFramework", StringComparison.OrdinalIgnoreCase) ||
        simpleName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
        simpleName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
        simpleName.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase);

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

        var cctor = ObfuscatorHelpers.FindOrCreateModuleInitializer(module);

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
        var offsetLocal = new Local(module.CorLibTypes.Int32);
        var nLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(nameLocal);
        body.Variables.Add(streamLocal);
        body.Variables.Add(bufLocal);
        body.Variables.Add(offsetLocal);
        body.Variables.Add(nLocal);

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
        var dispose = new MemberRefUser(module, "Dispose",
            MethodSig.CreateInstance(module.CorLibTypes.Void), streamType);

        var retNull = Instruction.Create(OpCodes.Ldnull);
        var haveStream = Instruction.Create(OpCodes.Stloc, streamLocal);
        var readLoop = Instruction.Create(OpCodes.Nop);
        var doneRead = Instruction.Create(OpCodes.Nop);
        var fail = Instruction.Create(OpCodes.Ldnull);

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
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, offsetLocal));

        body.Instructions.Add(readLoop);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bufLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Bge, doneRead));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, streamLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bufLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bufLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Sub));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, read));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, nLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, nLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, doneRead));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, nLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, readLoop));

        body.Instructions.Add(doneRead);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, streamLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, dispose));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, offsetLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bufLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldlen));
        body.Instructions.Add(Instruction.Create(OpCodes.Conv_I4));
        body.Instructions.Add(Instruction.Create(OpCodes.Bne_Un, fail));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, bufLocal));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, load));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.Instructions.Add(fail);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.KeepOldMaxStack = true;
        body.MaxStack = 8;
        body.UpdateInstructionOffsets();
        return method;
    }
}

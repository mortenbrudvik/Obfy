using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Regression tests for opaque-predicate insertion at non-empty evaluation-stack depth.
/// A predicate inserted between a <c>dup</c> and its consumer leaves unverifiable IL
/// (InvalidProgramException at JIT time).
/// </summary>
public class OpaquePredicateStackDepthTests
{
    private static ModuleDef CreateModule()
    {
        var module = new ModuleDefUser("StackDepthTest", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var assembly = new AssemblyDefUser("StackDepthTest", new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);
        return module;
    }

    /// <summary>
    /// Body shaped like a compiler-emitted dup site:
    ///   ldc.i4.1        depth 0 -&gt; 1
    ///   dup             depth 1 -&gt; 2
    ///   pop             depth 2 -&gt; 1   (the dup's consumer)
    ///   ... more depth-nonzero instructions ...
    ///   ret
    /// The only empty-stack insertable site is index 0. Every later position has depth &gt; 0.
    /// </summary>
    private static MethodDef CreateDupSiteMethod(TypeDef type)
    {
        var method = new MethodDefUser(
            "DupSite",
            MethodSig.CreateStatic(type.Module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1)); // 0 -> 1
        body.Instructions.Add(Instruction.Create(OpCodes.Dup));      // 1 -> 2
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));      // 2 -> 1
        for (var i = 0; i < 6; i++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1)); // 1 -> 2
            body.Instructions.Add(Instruction.Create(OpCodes.Pop));      // 2 -> 1
        }
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        method.Body = body;
        type.Methods.Add(method);
        return method;
    }

    private static async Task<ObfuscationResult> RunWithIntensity100(ModuleDef module)
    {
        var logger = new Mock<ILogger<ControlFlowObfuscator>>();
        var obfuscator = new ControlFlowObfuscator(logger.Object);
        var settings = new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.OpaquePredicate, Intensity = 100 }
        };
        return await obfuscator.ObfuscateAsync(PipelineContext.ForAssembly(module, settings));
    }

    [Fact]
    public async Task OpaquePredicates_AreNotInsertedBetweenDupAndItsConsumer()
    {
        var module = CreateModule();
        var type = new TypeDefUser("TestNamespace", "TestClass", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(type);
        var method = CreateDupSiteMethod(type);

        var result = await RunWithIntensity100(module);

        result.Success.ShouldBeTrue();

        // With the fix, the only empty-stack insertable site is index 0, so the dup/pop
        // pair remains adjacent. The buggy obfuscator never checks depth and inserts a
        // predicate at every eligible position, including immediately after dup.
        var instructions = method.Body.Instructions;
        var dupIndex = instructions.IndexOf(instructions.First(i => i.OpCode == OpCodes.Dup));
        instructions[dupIndex + 1].OpCode.ShouldBe(OpCodes.Pop,
            "no opaque predicate may be inserted between dup and its consumer");
    }

    [Fact]
    public async Task OpaquePredicates_OnlyInsertAtEmptyStackDepth()
    {
        // Every depth-nonzero site is skipped: only one predicate (at index 0) is emitted.
        var module = CreateModule();
        var type = new TypeDefUser("TestNamespace", "TestClass", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(type);
        var method = CreateDupSiteMethod(type);
        var originalCount = method.Body.Instructions.Count;

        var result = await RunWithIntensity100(module);

        result.Success.ShouldBeTrue();
        result.Statistics.MethodsControlFlowObfuscated.ShouldBe(1);
        // One opaque predicate expands to a fixed number of instructions; the buggy
        // implementation inserts many and grows the body substantially more.
        (method.Body.Instructions.Count - originalCount).ShouldBeLessThan(10,
            "only the single empty-stack insertion site may accept a predicate");
    }
}

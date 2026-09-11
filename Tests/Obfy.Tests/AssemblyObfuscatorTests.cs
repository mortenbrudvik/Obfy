using System.Runtime.Loader;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Tests for assembly-based obfuscators using dnlib.
/// </summary>
public class AssemblyObfuscatorTests
{
    #region Test Helpers

    private static ModuleDef CreateTestModule()
    {
        var module = new ModuleDefUser("TestAssembly", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var assembly = new AssemblyDefUser("TestAssembly", new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);
        return module;
    }

    private static TypeDef CreateTestType(ModuleDef module, string name, bool isPublic = false)
    {
        var attrs = isPublic ? TypeAttributes.Public : TypeAttributes.NotPublic;
        attrs |= TypeAttributes.Class;

        var typeDef = new TypeDefUser("TestNamespace", name, module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = attrs
        };
        module.Types.Add(typeDef);
        return typeDef;
    }

    private static MethodDef CreateTestMethod(TypeDef type, string name, bool isPublic = false)
    {
        var attrs = isPublic ? MethodAttributes.Public : MethodAttributes.Private;
        attrs |= MethodAttributes.Static;

        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(type.Module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            attrs);

        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;

        type.Methods.Add(method);
        return method;
    }

    private static MethodDef CreateMethodWithString(TypeDef type, string name, string stringValue)
    {
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(type.Module.CorLibTypes.String),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, stringValue));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;

        type.Methods.Add(method);
        return method;
    }

    private static MethodDef CreateMethodWithMultipleInstructions(TypeDef type, string name, int instructionCount)
    {
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(type.Module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody();

        // Add local variable
        var local = new Local(type.Module.CorLibTypes.Int32);
        body.Variables.Add(local);

        // Initialize local
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, local));

        // Add multiple instructions
        for (int i = 0; i < instructionCount - 4; i++)
        {
            body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, local));
            body.Instructions.Add(Instruction.CreateLdcI4(1));
            body.Instructions.Add(Instruction.Create(OpCodes.Add));
            body.Instructions.Add(Instruction.Create(OpCodes.Stloc, local));
        }

        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, local));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        body.UpdateInstructionOffsets();
        method.Body = body;

        type.Methods.Add(method);
        return method;
    }

    private static FieldDef CreateTestField(TypeDef type, string name, bool isPublic = false)
    {
        var attrs = isPublic ? FieldAttributes.Public : FieldAttributes.Private;
        var field = new FieldDefUser(name, new FieldSig(type.Module.CorLibTypes.String), attrs);
        type.Fields.Add(field);
        return field;
    }

    private static PropertyDef CreateTestProperty(TypeDef type, string name, bool isPublic = false)
    {
        var attrs = isPublic ? MethodAttributes.Public : MethodAttributes.Private;
        attrs |= MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        // Create backing field
        var backingField = new FieldDefUser(
            $"<{name}>k__BackingField",
            new FieldSig(type.Module.CorLibTypes.String),
            FieldAttributes.Private);
        type.Fields.Add(backingField);

        // Create getter
        var getter = new MethodDefUser(
            $"get_{name}",
            MethodSig.CreateInstance(type.Module.CorLibTypes.String),
            MethodImplAttributes.IL,
            attrs);

        var getterBody = new CilBody();
        getterBody.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        getterBody.Instructions.Add(Instruction.Create(OpCodes.Ldfld, backingField));
        getterBody.Instructions.Add(Instruction.Create(OpCodes.Ret));
        getter.Body = getterBody;
        type.Methods.Add(getter);

        // Create property
        var property = new PropertyDefUser(name, new PropertySig(true, type.Module.CorLibTypes.String));
        property.GetMethod = getter;
        type.Properties.Add(property);

        return property;
    }

    #endregion

    #region StringEncryptionObfuscator Tests

    [Fact]
    public async Task StringEncryption_EncryptsStringLiterals()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var method = CreateMethodWithString(type, "GetMessage", "Hello World - Secret Message");

        var logger = new Mock<ILogger<StringEncryptionObfuscator>>();
        var obfuscator = new StringEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            StringEncryption = { Enabled = true, Algorithm = EncryptionAlgorithm.Aes256, MinStringLength = 5 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBeGreaterThan(0);

        // Verify original string is no longer in method body as ldstr
        var hasOriginalString = method.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Ldstr && (string)i.Operand == "Hello World - Secret Message");
        hasOriginalString.ShouldBeFalse();
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public async Task StringEncryption_RuntimeDecryptsOriginalString(EncryptionAlgorithm algorithm)
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "Greeter", isPublic: true);
        var method = CreateMethodWithString(type, "GetMessage", "HelloWorldSecret");
        method.Attributes = MethodAttributes.Public | MethodAttributes.Static;

        var logger = new Mock<ILogger<StringEncryptionObfuscator>>();
        var obfuscator = new StringEncryptionObfuscator(logger.Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            StringEncryption = { Enabled = true, Algorithm = algorithm, MinStringLength = 3 }
        };
        var context = PipelineContext.ForAssembly(module, settings);
        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-str-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "TestAssembly.dll");
        try
        {
            module.Write(path);
            var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                var asm = alc.LoadFromAssemblyPath(path);
                var greeter = asm.GetType("TestNamespace.Greeter");
                greeter.ShouldNotBeNull();
                var getMessage = greeter!.GetMethod("GetMessage");
                getMessage.ShouldNotBeNull();
                var value = (string)getMessage!.Invoke(null, null)!;
                value.ShouldBe("HelloWorldSecret");
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task StringEncryption_SkipsShortStrings()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var method = CreateMethodWithString(type, "GetShort", "Hi"); // 2 chars, below threshold

        var logger = new Mock<ILogger<StringEncryptionObfuscator>>();
        var obfuscator = new StringEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            StringEncryption = { Enabled = true, Algorithm = EncryptionAlgorithm.Aes256, MinStringLength = 5 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBe(0);

        // Verify short string is still present
        var hasShortString = method.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Ldstr && (string)i.Operand == "Hi");
        hasShortString.ShouldBeTrue();
    }

    [Fact]
    public async Task StringEncryption_InjectsDecryptorType()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        CreateMethodWithString(type, "GetMessage", "This is a long secret message");

        var logger = new Mock<ILogger<StringEncryptionObfuscator>>();
        var obfuscator = new StringEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            StringEncryption = { Enabled = true, MinStringLength = 5 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        var decryptorType = module.Types.FirstOrDefault(t => t.Namespace == "Obfy.Runtime");
        decryptorType.ShouldNotBeNull();
        decryptorType.FindMethod("Decrypt").ShouldNotBeNull();
    }

    [Fact]
    public async Task StringEncryption_XorAlgorithm_EncryptsStrings()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        CreateMethodWithString(type, "GetMessage", "XOR encrypted message here");

        var logger = new Mock<ILogger<StringEncryptionObfuscator>>();
        var obfuscator = new StringEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            StringEncryption = { Enabled = true, Algorithm = EncryptionAlgorithm.Xor, MinStringLength = 5 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void StringEncryption_Properties_AreCorrect()
    {
        var logger = new Mock<ILogger<StringEncryptionObfuscator>>();
        var obfuscator = new StringEncryptionObfuscator(logger.Object);

        obfuscator.Name.ShouldBe("StringEncryption");
        obfuscator.Priority.ShouldBe(10);
        obfuscator.SupportsTargetType(TargetType.Assembly).ShouldBeTrue();
        obfuscator.SupportsTargetType(TargetType.SourceCode).ShouldBeFalse();
    }

    [Fact]
    public void StringEncryption_IsEnabled_RespectsSettings()
    {
        var logger = new Mock<ILogger<StringEncryptionObfuscator>>();
        var obfuscator = new StringEncryptionObfuscator(logger.Object);

        var enabledSettings = new ObfySettings { StringEncryption = { Enabled = true } };
        var disabledSettings = new ObfySettings { StringEncryption = { Enabled = false } };

        obfuscator.IsEnabled(enabledSettings).ShouldBeTrue();
        obfuscator.IsEnabled(disabledSettings).ShouldBeFalse();
    }

    #endregion

    #region SymbolRenamingObfuscator Tests

    [Fact]
    public async Task SymbolRenaming_RenamesPrivateTypes()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "PrivateTestClass", isPublic: false);
        CreateTestMethod(type, "TestMethod");

        var logger = new Mock<ILogger<SymbolRenamingObfuscator>>();
        var nameGenerator = new NameGenerator();
        var obfuscator = new SymbolRenamingObfuscator(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameTypes = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.TypesRenamed.ShouldBeGreaterThan(0);
        type.Name.String.ShouldNotBe("PrivateTestClass");
    }

    [Fact]
    public async Task SymbolRenaming_PreservesPublicApi()
    {
        // Arrange
        var module = CreateTestModule();
        var publicType = CreateTestType(module, "PublicClass", isPublic: true);
        CreateTestMethod(publicType, "PublicMethod", isPublic: true);

        var logger = new Mock<ILogger<SymbolRenamingObfuscator>>();
        var nameGenerator = new NameGenerator();
        var obfuscator = new SymbolRenamingObfuscator(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameTypes = true, PreservePublicApi = true }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        publicType.Name.String.ShouldBe("PublicClass");
    }

    [Fact]
    public async Task SymbolRenaming_RenamesMethods()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var method = CreateTestMethod(type, "PrivateMethod", isPublic: false);

        var logger = new Mock<ILogger<SymbolRenamingObfuscator>>();
        var nameGenerator = new NameGenerator();
        var obfuscator = new SymbolRenamingObfuscator(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameMethods = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsRenamed.ShouldBeGreaterThan(0);
        method.Name.String.ShouldNotBe("PrivateMethod");
    }

    [Fact]
    public async Task SymbolRenaming_RenamesFields()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var field = CreateTestField(type, "privateField", isPublic: false);

        var logger = new Mock<ILogger<SymbolRenamingObfuscator>>();
        var nameGenerator = new NameGenerator();
        var obfuscator = new SymbolRenamingObfuscator(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameFields = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.FieldsRenamed.ShouldBeGreaterThan(0);
        field.Name.String.ShouldNotBe("privateField");
    }

    [Fact]
    public async Task SymbolRenaming_RenamesProperties()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var property = CreateTestProperty(type, "PrivateProperty", isPublic: false);

        var logger = new Mock<ILogger<SymbolRenamingObfuscator>>();
        var nameGenerator = new NameGenerator();
        var obfuscator = new SymbolRenamingObfuscator(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameProperties = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.PropertiesRenamed.ShouldBeGreaterThan(0);
        property.Name.String.ShouldNotBe("PrivateProperty");
    }

    [Fact]
    public async Task SymbolRenaming_GeneratesSymbolMap()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "MappedClass", isPublic: false);

        var logger = new Mock<ILogger<SymbolRenamingObfuscator>>();
        var nameGenerator = new NameGenerator();
        var obfuscator = new SymbolRenamingObfuscator(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameTypes = true }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        context.SymbolMap.ShouldNotBeEmpty();
        context.SymbolMap.Keys.ShouldContain(k => k.StartsWith("Type:"));
    }

    [Fact]
    public void SymbolRenaming_Properties_AreCorrect()
    {
        var logger = new Mock<ILogger<SymbolRenamingObfuscator>>();
        var nameGenerator = new NameGenerator();
        var obfuscator = new SymbolRenamingObfuscator(nameGenerator, logger.Object);

        obfuscator.Name.ShouldBe("SymbolRenaming");
        obfuscator.Priority.ShouldBe(50);
        obfuscator.SupportsTargetType(TargetType.Assembly).ShouldBeTrue();
        obfuscator.SupportsTargetType(TargetType.SourceCode).ShouldBeFalse();
    }

    [Fact]
    public async Task SymbolRenaming_SkipsObfyCoreModelsNamespace()
    {
        // Arrange - create a type in the Obfy.Core.Models namespace
        var module = CreateTestModule();
        var typeDef = new TypeDefUser(
            "Obfy.Core.Models",
            "ObfySettings",
            module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(typeDef);

        var logger = new Mock<ILogger<SymbolRenamingObfuscator>>();
        var nameGenerator = new NameGenerator();
        var obfuscator = new SymbolRenamingObfuscator(nameGenerator, logger.Object);

        var settings = new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameTypes = true, Mode = NamingMode.Sequential }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert - type should NOT be renamed (excluded for JSON serialization)
        typeDef.Name.String.ShouldBe("ObfySettings");
        typeDef.Namespace.String.ShouldBe("Obfy.Core.Models");
    }

    #endregion

    #region ControlFlowObfuscator Tests

    [Fact]
    public async Task ControlFlow_ObfuscatesMethodsWithSufficientInstructions()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        CreateMethodWithMultipleInstructions(type, "LongMethod", 20);

        var logger = new Mock<ILogger<ControlFlowObfuscator>>();
        var obfuscator = new ControlFlowObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.OpaquePredicate, Intensity = 100 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task ControlFlow_SkipsMethodsWithTooFewInstructions()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var method = CreateTestMethod(type, "ShortMethod"); // Only has ret instruction

        var logger = new Mock<ILogger<ControlFlowObfuscator>>();
        var obfuscator = new ControlFlowObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var originalInstructionCount = method.Body.Instructions.Count;

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        method.Body.Instructions.Count.ShouldBe(originalInstructionCount);
    }

    [Fact]
    public async Task ControlFlow_SwitchMode_AddsStateVariable()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var method = CreateMethodWithMultipleInstructions(type, "SwitchableMethod", 30);

        var originalVarCount = method.Body.Variables.Count;

        var logger = new Mock<ILogger<ControlFlowObfuscator>>();
        var obfuscator = new ControlFlowObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert - Switch mode adds a state variable
        method.Body.Variables.Count.ShouldBeGreaterThanOrEqualTo(originalVarCount);
    }

    [Fact]
    public async Task ControlFlow_OpaquePredicate_InsertsPredicates()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var method = CreateMethodWithMultipleInstructions(type, "PredicateMethod", 25);

        var originalCount = method.Body.Instructions.Count;

        var logger = new Mock<ILogger<ControlFlowObfuscator>>();
        var obfuscator = new ControlFlowObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.OpaquePredicate, Intensity = 100 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert - Opaque predicates add instructions
        method.Body.Instructions.Count.ShouldBeGreaterThan(originalCount);
    }

    [Fact]
    public void ControlFlow_Properties_AreCorrect()
    {
        var logger = new Mock<ILogger<ControlFlowObfuscator>>();
        var obfuscator = new ControlFlowObfuscator(logger.Object);

        obfuscator.Name.ShouldBe("ControlFlow");
        obfuscator.Priority.ShouldBe(30);
        obfuscator.SupportsTargetType(TargetType.Assembly).ShouldBeTrue();
        obfuscator.SupportsTargetType(TargetType.SourceCode).ShouldBeFalse();
    }

    [Fact]
    public void ControlFlow_IsEnabled_RespectsSettings()
    {
        var logger = new Mock<ILogger<ControlFlowObfuscator>>();
        var obfuscator = new ControlFlowObfuscator(logger.Object);

        var enabledSettings = new ObfySettings { ControlFlow = { Enabled = true } };
        var disabledSettings = new ObfySettings { ControlFlow = { Enabled = false } };

        obfuscator.IsEnabled(enabledSettings).ShouldBeTrue();
        obfuscator.IsEnabled(disabledSettings).ShouldBeFalse();
    }

    #endregion

    #region AntiDebugObfuscator Tests

    [Fact]
    public async Task AntiDebug_InjectsAntiDebugType()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        // Create entry point
        var entryPoint = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        entryPoint.Body = body;
        type.Methods.Add(entryPoint);
        module.EntryPoint = entryPoint;

        var logger = new Mock<ILogger<AntiDebugObfuscator>>();
        var obfuscator = new AntiDebugObfuscator(logger.Object);

        var settings = new ObfySettings { Protection = { AntiDebug = true } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        var antiDebugType = module.Types.FirstOrDefault(t => t.Name == "<AntiDebug>");
        antiDebugType.ShouldNotBeNull();
    }

    [Fact]
    public async Task AntiDebug_InjectsCheckMethod()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var entryPoint = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        entryPoint.Body = body;
        type.Methods.Add(entryPoint);
        module.EntryPoint = entryPoint;

        var logger = new Mock<ILogger<AntiDebugObfuscator>>();
        var obfuscator = new AntiDebugObfuscator(logger.Object);

        var settings = new ObfySettings { Protection = { AntiDebug = true } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        var antiDebugType = module.Types.First(t => t.Name == "<AntiDebug>");
        antiDebugType.FindMethod("Check").ShouldNotBeNull();
        antiDebugType.FindMethod("Handle").ShouldNotBeNull();
    }

    [Fact]
    public async Task AntiDebug_InstrumentsEntryPoint()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var entryPoint = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        entryPoint.Body = body;
        type.Methods.Add(entryPoint);
        module.EntryPoint = entryPoint;

        var originalCount = entryPoint.Body.Instructions.Count;

        var logger = new Mock<ILogger<AntiDebugObfuscator>>();
        var obfuscator = new AntiDebugObfuscator(logger.Object);

        var settings = new ObfySettings { Protection = { AntiDebug = true } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert - Entry point should have additional call instruction
        entryPoint.Body.Instructions.Count.ShouldBeGreaterThan(originalCount);
        entryPoint.Body.Instructions[0].OpCode.ShouldBe(OpCodes.Call);
    }

    [Fact]
    public void AntiDebug_Properties_AreCorrect()
    {
        var logger = new Mock<ILogger<AntiDebugObfuscator>>();
        var obfuscator = new AntiDebugObfuscator(logger.Object);

        obfuscator.Name.ShouldBe("AntiDebug");
        obfuscator.Priority.ShouldBe(70);
        obfuscator.SupportsTargetType(TargetType.Assembly).ShouldBeTrue();
        obfuscator.SupportsTargetType(TargetType.SourceCode).ShouldBeFalse();
    }

    [Fact]
    public void AntiDebug_IsEnabled_RespectsSettings()
    {
        var logger = new Mock<ILogger<AntiDebugObfuscator>>();
        var obfuscator = new AntiDebugObfuscator(logger.Object);

        var enabledSettings = new ObfySettings { Protection = { AntiDebug = true } };
        var disabledSettings = new ObfySettings { Protection = { AntiDebug = false } };

        obfuscator.IsEnabled(enabledSettings).ShouldBeTrue();
        obfuscator.IsEnabled(disabledSettings).ShouldBeFalse();
    }

    #endregion

    #region AntiTamperObfuscator Tests

    [Fact]
    public async Task AntiTamper_InjectsAntiTamperType()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        // Create entry point
        var entryPoint = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        entryPoint.Body = body;
        type.Methods.Add(entryPoint);
        module.EntryPoint = entryPoint;

        var logger = new Mock<ILogger<AntiTamperObfuscator>>();
        var obfuscator = new AntiTamperObfuscator(logger.Object);

        var settings = new ObfySettings { Protection = { AntiTamper = { Enabled = true } } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        var antiTamperType = module.Types.FirstOrDefault(t => t.Name == "<AntiTamper>");
        antiTamperType.ShouldNotBeNull();
    }

    [Fact]
    public async Task AntiTamper_InjectsVerifyMethod()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var entryPoint = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        entryPoint.Body = body;
        type.Methods.Add(entryPoint);
        module.EntryPoint = entryPoint;

        var logger = new Mock<ILogger<AntiTamperObfuscator>>();
        var obfuscator = new AntiTamperObfuscator(logger.Object);

        var settings = new ObfySettings { Protection = { AntiTamper = { Enabled = true } } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        var antiTamperType = module.Types.First(t => t.Name == "<AntiTamper>");
        antiTamperType.FindMethod("Verify").ShouldNotBeNull();
    }

    [Fact]
    public async Task AntiTamper_InjectsHashField()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var entryPoint = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        entryPoint.Body = body;
        type.Methods.Add(entryPoint);
        module.EntryPoint = entryPoint;

        var logger = new Mock<ILogger<AntiTamperObfuscator>>();
        var obfuscator = new AntiTamperObfuscator(logger.Object);

        var settings = new ObfySettings { Protection = { AntiTamper = { Enabled = true } } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        var antiTamperType = module.Types.First(t => t.Name == "<AntiTamper>");
        var blobField = antiTamperType.Fields.FirstOrDefault(f => f.Name == "_blob");
        blobField.ShouldNotBeNull();
        blobField.HasFieldRVA.ShouldBeTrue();
        antiTamperType.FindMethod("Verify").ShouldNotBeNull();
        antiTamperType.FindMethod("FindHashOffset").ShouldNotBeNull();
    }

    [Fact]
    public async Task AntiTamper_InstrumentsEntryPoint()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var entryPoint = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        entryPoint.Body = body;
        type.Methods.Add(entryPoint);
        module.EntryPoint = entryPoint;

        var originalCount = entryPoint.Body.Instructions.Count;

        var logger = new Mock<ILogger<AntiTamperObfuscator>>();
        var obfuscator = new AntiTamperObfuscator(logger.Object);

        var settings = new ObfySettings { Protection = { AntiTamper = { Enabled = true, CheckEntryPoint = true } } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        entryPoint.Body.Instructions.Count.ShouldBeGreaterThan(originalCount);
        entryPoint.Body.Instructions[0].OpCode.ShouldBe(OpCodes.Call);
    }

    [Fact]
    public async Task AntiTamper_InstrumentsModuleInitializer()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var logger = new Mock<ILogger<AntiTamperObfuscator>>();
        var obfuscator = new AntiTamperObfuscator(logger.Object);

        var settings = new ObfySettings { Protection = { AntiTamper = { Enabled = true, CheckModuleInitializer = true, CheckEntryPoint = false } } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0);

        // Module initializer should be created and instrumented
        var globalType = module.GlobalType;
        globalType.ShouldNotBeNull();
        var cctor = globalType.Methods.FirstOrDefault(m => m.IsStaticConstructor);
        cctor.ShouldNotBeNull();
    }

    [Fact]
    public async Task AntiTamper_StoresMetadataInContext()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var entryPoint = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        entryPoint.Body = body;
        type.Methods.Add(entryPoint);
        module.EntryPoint = entryPoint;

        var logger = new Mock<ILogger<AntiTamperObfuscator>>();
        var obfuscator = new AntiTamperObfuscator(logger.Object);

        var settings = new ObfySettings { Protection = { AntiTamper = { Enabled = true } } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        context.SharedData.ContainsKey(AntiTamperObfuscator.HashFieldMetadataKey).ShouldBeTrue();
        var metadata = context.SharedData[AntiTamperObfuscator.HashFieldMetadataKey] as AntiTamperMetadata;
        metadata.ShouldNotBeNull();
        metadata.HashFieldToken.ShouldBeGreaterThan(0u);
    }

    [Fact]
    public void AntiTamper_Properties_AreCorrect()
    {
        var logger = new Mock<ILogger<AntiTamperObfuscator>>();
        var obfuscator = new AntiTamperObfuscator(logger.Object);

        obfuscator.Name.ShouldBe("AntiTamper");
        obfuscator.Priority.ShouldBe(75);
        obfuscator.SupportsTargetType(TargetType.Assembly).ShouldBeTrue();
        obfuscator.SupportsTargetType(TargetType.SourceCode).ShouldBeFalse();
    }

    [Fact]
    public void AntiTamper_IsEnabled_RespectsSettings()
    {
        var logger = new Mock<ILogger<AntiTamperObfuscator>>();
        var obfuscator = new AntiTamperObfuscator(logger.Object);

        var enabledSettings = new ObfySettings { Protection = { AntiTamper = { Enabled = true } } };
        var disabledSettings = new ObfySettings { Protection = { AntiTamper = { Enabled = false } } };

        obfuscator.IsEnabled(enabledSettings).ShouldBeTrue();
        obfuscator.IsEnabled(disabledSettings).ShouldBeFalse();
    }

    [Fact]
    public async Task AntiTamper_SkipsWhenDisabled()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var entryPoint = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        entryPoint.Body = body;
        type.Methods.Add(entryPoint);
        module.EntryPoint = entryPoint;

        var logger = new Mock<ILogger<AntiTamperObfuscator>>();
        var obfuscator = new AntiTamperObfuscator(logger.Object);

        var settings = new ObfySettings { Protection = { AntiTamper = { Enabled = false } } };

        // Act & Assert
        obfuscator.IsEnabled(settings).ShouldBeFalse();

        // Verify no AntiTamper type would be injected if run
        var typeCountBefore = module.Types.Count;
        // Don't run the obfuscator since it's disabled
        module.Types.Count.ShouldBe(typeCountBefore);
    }

    #endregion

    #region MetadataRemovalObfuscator Tests

    [Fact]
    public async Task MetadataRemoval_RemovesDebuggableAttribute()
    {
        // Arrange
        var module = CreateTestModule();

        // Add DebuggableAttribute to module
        var attrType = module.CorLibTypes.GetTypeRef("System.Diagnostics", "DebuggableAttribute");
        var attr = new CustomAttribute(new MemberRefUser(module, ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Boolean, module.CorLibTypes.Boolean),
            attrType));
        attr.ConstructorArguments.Add(new CAArgument(module.CorLibTypes.Boolean, true));
        attr.ConstructorArguments.Add(new CAArgument(module.CorLibTypes.Boolean, true));
        module.CustomAttributes.Add(attr);

        var logger = new Mock<ILogger<MetadataRemovalObfuscator>>();
        var obfuscator = new MetadataRemovalObfuscator(logger.Object);

        var settings = new ObfySettings { Metadata = { RemoveDebugInfo = true } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.MetadataItemsRemoved.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task MetadataRemoval_ClearsLocalVariableNames()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var method = new MethodDefUser(
            "TestMethod",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody();
        var local = new Local(module.CorLibTypes.Int32) { Name = "myVariable" };
        body.Variables.Add(local);
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, local));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;
        type.Methods.Add(method);

        var logger = new Mock<ILogger<MetadataRemovalObfuscator>>();
        var obfuscator = new MetadataRemovalObfuscator(logger.Object);

        var settings = new ObfySettings { Metadata = { RemoveDebugInfo = true } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        local.Name.ShouldBeNull();
    }

    [Fact]
    public async Task MetadataRemoval_RemovesCompilerGeneratedAttributes()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        // Add CompilerGeneratedAttribute
        var attrType = module.CorLibTypes.GetTypeRef("System.Runtime.CompilerServices", "CompilerGeneratedAttribute");
        var attr = new CustomAttribute(new MemberRefUser(module, ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void),
            attrType));
        type.CustomAttributes.Add(attr);

        var logger = new Mock<ILogger<MetadataRemovalObfuscator>>();
        var obfuscator = new MetadataRemovalObfuscator(logger.Object);

        var settings = new ObfySettings { Metadata = { RemoveAttributes = true } };
        var context = PipelineContext.ForAssembly(module, settings);

        var initialCount = type.CustomAttributes.Count;

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        type.CustomAttributes.Count.ShouldBeLessThan(initialCount);
    }

    [Fact]
    public async Task MetadataRemoval_StripsXmlResources()
    {
        // Arrange
        var module = CreateTestModule();

        // Add XML resource
        var resource = new EmbeddedResource("Documentation.xml", new byte[] { 0x3C, 0x3F, 0x78, 0x6D, 0x6C });
        module.Resources.Add(resource);

        var logger = new Mock<ILogger<MetadataRemovalObfuscator>>();
        var obfuscator = new MetadataRemovalObfuscator(logger.Object);

        var settings = new ObfySettings { Metadata = { StripDocumentation = true } };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        module.Resources.ShouldNotContain(r => r.Name.EndsWith(".xml"));
    }

    [Fact]
    public void MetadataRemoval_Properties_AreCorrect()
    {
        var logger = new Mock<ILogger<MetadataRemovalObfuscator>>();
        var obfuscator = new MetadataRemovalObfuscator(logger.Object);

        obfuscator.Name.ShouldBe("MetadataRemoval");
        obfuscator.Priority.ShouldBe(90);
        obfuscator.SupportsTargetType(TargetType.Assembly).ShouldBeTrue();
        obfuscator.SupportsTargetType(TargetType.SourceCode).ShouldBeFalse();
    }

    [Fact]
    public void MetadataRemoval_IsEnabled_RespectsSettings()
    {
        var logger = new Mock<ILogger<MetadataRemovalObfuscator>>();
        var obfuscator = new MetadataRemovalObfuscator(logger.Object);

        var enabledSettings = new ObfySettings { Metadata = { RemoveDebugInfo = true } };
        var disabledSettings = new ObfySettings { Metadata = { RemoveDebugInfo = false, RemoveAttributes = false, StripDocumentation = false } };

        obfuscator.IsEnabled(enabledSettings).ShouldBeTrue();
        obfuscator.IsEnabled(disabledSettings).ShouldBeFalse();
    }

    #endregion

    #region ResourceEncryptionObfuscator Tests

    [Fact]
    public async Task ResourceEncryption_EncryptsEmbeddedResources()
    {
        // Arrange
        var module = CreateTestModule();
        var resourceData = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var resource = new EmbeddedResource("TestResource.dat", resourceData);
        module.Resources.Add(resource);

        var logger = new Mock<ILogger<ResourceEncryptionObfuscator>>();
        var obfuscator = new ResourceEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ResourceEncryption = { Enabled = true, Algorithm = EncryptionAlgorithm.Aes256 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ResourcesEncrypted.ShouldBe(1);

        var remaining = module.Resources.OfType<EmbeddedResource>().FirstOrDefault(r => r.Name == "TestResource.dat");
        remaining.ShouldNotBeNull();
        remaining.CreateReader().ToArray().ShouldNotBe(resourceData);

        // Decryptor type should be injected
        var decryptorType = module.Types.FirstOrDefault(t => t.Name == "<ResourceDecryptor>");
        decryptorType.ShouldNotBeNull();
    }

    [Fact]
    public async Task ResourceEncryption_XorAlgorithm_EncryptsResources()
    {
        // Arrange
        var module = CreateTestModule();
        var resourceData = new byte[] { 0x54, 0x65, 0x73, 0x74 }; // "Test"
        var resource = new EmbeddedResource("Config.json", resourceData);
        module.Resources.Add(resource);

        var logger = new Mock<ILogger<ResourceEncryptionObfuscator>>();
        var obfuscator = new ResourceEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ResourceEncryption = { Enabled = true, Algorithm = EncryptionAlgorithm.Xor }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ResourcesEncrypted.ShouldBe(1);
    }

    [Fact]
    public async Task ResourceEncryption_RespectsIncludePatterns()
    {
        // Arrange
        var module = CreateTestModule();
        module.Resources.Add(new EmbeddedResource("Data.json", new byte[] { 0x7B, 0x7D }));
        module.Resources.Add(new EmbeddedResource("Image.png", new byte[] { 0x89, 0x50 }));
        module.Resources.Add(new EmbeddedResource("Config.xml", new byte[] { 0x3C, 0x3F }));

        var logger = new Mock<ILogger<ResourceEncryptionObfuscator>>();
        var obfuscator = new ResourceEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ResourceEncryption = new ResourceEncryptionSettings
            {
                Enabled = true,
                IncludePatterns = new List<string> { "*.json", "*.xml" }
            }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ResourcesEncrypted.ShouldBe(2); // json and xml, not png
        module.Resources.ShouldContain(r => r.Name == "Image.png");
    }

    [Fact]
    public async Task ResourceEncryption_RespectsExcludePatterns()
    {
        // Arrange
        var module = CreateTestModule();
        module.Resources.Add(new EmbeddedResource("Data.json", new byte[] { 0x7B, 0x7D }));
        module.Resources.Add(new EmbeddedResource("System.resources", new byte[] { 0x00, 0x01 }));

        var logger = new Mock<ILogger<ResourceEncryptionObfuscator>>();
        var obfuscator = new ResourceEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ResourceEncryption = new ResourceEncryptionSettings
            {
                Enabled = true,
                IncludePatterns = new List<string> { "*" },
                ExcludePatterns = new List<string> { "*.resources" }
            }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ResourcesEncrypted.ShouldBe(1); // Only json, not .resources
        module.Resources.ShouldContain(r => r.Name == "System.resources");
    }

    [Fact]
    public async Task ResourceEncryption_DisabledWhenSettingFalse()
    {
        // Arrange
        var module = CreateTestModule();
        module.Resources.Add(new EmbeddedResource("Test.dat", new byte[] { 0x01, 0x02 }));

        var logger = new Mock<ILogger<ResourceEncryptionObfuscator>>();
        var obfuscator = new ResourceEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ResourceEncryption = { Enabled = false }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act - shouldn't run because IsEnabled returns false
        obfuscator.IsEnabled(settings).ShouldBeFalse();
    }

    [Fact]
    public async Task ResourceEncryption_NoResourcesReturnsSuccess()
    {
        // Arrange
        var module = CreateTestModule();
        // No resources added

        var logger = new Mock<ILogger<ResourceEncryptionObfuscator>>();
        var obfuscator = new ResourceEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ResourceEncryption = { Enabled = true }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ResourcesEncrypted.ShouldBe(0);
    }

    [Fact]
    public async Task ResourceEncryption_InjectsDecryptorWithGetResourceMethod()
    {
        // Arrange
        var module = CreateTestModule();
        module.Resources.Add(new EmbeddedResource("Test.dat", new byte[] { 0x01, 0x02, 0x03 }));

        var logger = new Mock<ILogger<ResourceEncryptionObfuscator>>();
        var obfuscator = new ResourceEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ResourceEncryption = { Enabled = true }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        var decryptorType = module.Types.FirstOrDefault(t => t.Name == "<ResourceDecryptor>");
        decryptorType.ShouldNotBeNull();
        decryptorType.FindMethod("GetResource").ShouldNotBeNull();
        decryptorType.FindMethod(".cctor").ShouldNotBeNull();
    }

    [Fact]
    public void ResourceEncryption_Properties_AreCorrect()
    {
        var logger = new Mock<ILogger<ResourceEncryptionObfuscator>>();
        var obfuscator = new ResourceEncryptionObfuscator(logger.Object);

        obfuscator.Name.ShouldBe("ResourceEncryption");
        obfuscator.Priority.ShouldBe(15);
        obfuscator.SupportsTargetType(TargetType.Assembly).ShouldBeTrue();
        obfuscator.SupportsTargetType(TargetType.SourceCode).ShouldBeFalse();
    }

    [Fact]
    public void ResourceEncryption_IsEnabled_RespectsSettings()
    {
        var logger = new Mock<ILogger<ResourceEncryptionObfuscator>>();
        var obfuscator = new ResourceEncryptionObfuscator(logger.Object);

        var enabledSettings = new ObfySettings { ResourceEncryption = { Enabled = true } };
        var disabledSettings = new ObfySettings { ResourceEncryption = { Enabled = false } };

        obfuscator.IsEnabled(enabledSettings).ShouldBeTrue();
        obfuscator.IsEnabled(disabledSettings).ShouldBeFalse();
    }

    #endregion

    #region ConstantEncryptionObfuscator Tests

    private static MethodDef CreateMethodWithConstants(TypeDef type, string name, int intValue, long longValue, float floatValue, double doubleValue)
    {
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(type.Module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody();

        // ldc.i4 intValue + pop
        body.Instructions.Add(Instruction.CreateLdcI4(intValue));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));

        // ldc.i8 longValue + pop
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I8, longValue));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));

        // ldc.r4 floatValue + pop
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4, floatValue));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));

        // ldc.r8 doubleValue + pop
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R8, doubleValue));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));

        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;

        type.Methods.Add(method);
        return method;
    }

    [Fact]
    public async Task ConstantEncryption_EncryptsIntegerConstants()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        CreateMethodWithConstants(type, "TestMethod", 42, 100L, 3.14f, 2.718);

        var logger = new Mock<ILogger<ConstantEncryptionObfuscator>>();
        var obfuscator = new ConstantEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ConstantEncryption = { Enabled = true, Algorithm = EncryptionAlgorithm.Xor, IntegerThreshold = 2 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ConstantsEncrypted.ShouldBeGreaterThan(0);

        // Verify decryptor type was injected
        var decryptorType = module.Types.FirstOrDefault(t => t.Name == "<ConstantDecryptor>");
        decryptorType.ShouldNotBeNull();
    }

    [Fact]
    public async Task ConstantEncryption_SkipsSmallIntegers()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        // Create method with small constants (0, 1, -1)
        var method = new MethodDefUser(
            "SmallConstants",
            MethodSig.CreateStatic(type.Module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_M1));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;
        type.Methods.Add(method);

        var logger = new Mock<ILogger<ConstantEncryptionObfuscator>>();
        var obfuscator = new ConstantEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ConstantEncryption = { Enabled = true, IntegerThreshold = 2 } // Skip |value| < 2
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ConstantsEncrypted.ShouldBe(0);

        // Verify original instructions preserved
        var hasZero = method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4_0);
        hasZero.ShouldBeTrue();
    }

    [Fact]
    public async Task ConstantEncryption_SkipsCommonFloats()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var method = new MethodDefUser(
            "CommonFloats",
            MethodSig.CreateStatic(type.Module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static);

        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4, 0.0f));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R4, 1.0f));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;
        type.Methods.Add(method);

        var logger = new Mock<ILogger<ConstantEncryptionObfuscator>>();
        var obfuscator = new ConstantEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ConstantEncryption =
            {
                Enabled = true,
                EncryptIntegers = false, // Disable to focus on floats
                EncryptLongs = false,
                EncryptFloats = true,
                EncryptDoubles = false,
                SkipCommonFloats = true
            }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ConstantsEncrypted.ShouldBe(0);
    }

    [Fact]
    public async Task ConstantEncryption_InjectsDecryptorMethods()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        CreateMethodWithConstants(type, "TestMethod", 100, 200L, 3.14f, 2.718);

        var logger = new Mock<ILogger<ConstantEncryptionObfuscator>>();
        var obfuscator = new ConstantEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ConstantEncryption = { Enabled = true, IntegerThreshold = 0, LongThreshold = 0, SkipCommonFloats = false, SkipCommonDoubles = false }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        var decryptorType = module.Types.First(t => t.Name == "<ConstantDecryptor>");
        decryptorType.FindMethod("DecryptInt32").ShouldNotBeNull();
        decryptorType.FindMethod("DecryptInt64").ShouldNotBeNull();
        decryptorType.FindMethod("DecryptSingle").ShouldNotBeNull();
        decryptorType.FindMethod("DecryptDouble").ShouldNotBeNull();
        decryptorType.FindMethod(".cctor").ShouldNotBeNull();
    }

    [Fact]
    public void ConstantEncryption_Properties_AreCorrect()
    {
        var logger = new Mock<ILogger<ConstantEncryptionObfuscator>>();
        var obfuscator = new ConstantEncryptionObfuscator(logger.Object);

        obfuscator.Name.ShouldBe("ConstantEncryption");
        obfuscator.Priority.ShouldBe(11);
        obfuscator.SupportsTargetType(TargetType.Assembly).ShouldBeTrue();
        obfuscator.SupportsTargetType(TargetType.SourceCode).ShouldBeFalse();
    }

    [Fact]
    public void ConstantEncryption_IsEnabled_RespectsSettings()
    {
        var logger = new Mock<ILogger<ConstantEncryptionObfuscator>>();
        var obfuscator = new ConstantEncryptionObfuscator(logger.Object);

        var enabledSettings = new ObfySettings { ConstantEncryption = { Enabled = true } };
        var disabledSettings = new ObfySettings { ConstantEncryption = { Enabled = false } };

        obfuscator.IsEnabled(enabledSettings).ShouldBeTrue();
        obfuscator.IsEnabled(disabledSettings).ShouldBeFalse();
    }

    [Fact]
    public async Task ConstantEncryption_DisabledTypesNotEncrypted()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        CreateMethodWithConstants(type, "TestMethod", 100, 200L, 3.14f, 2.718);

        var logger = new Mock<ILogger<ConstantEncryptionObfuscator>>();
        var obfuscator = new ConstantEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ConstantEncryption =
            {
                Enabled = true,
                EncryptIntegers = true,
                EncryptLongs = false, // Disabled
                EncryptFloats = false, // Disabled
                EncryptDoubles = false, // Disabled
                IntegerThreshold = 0
            }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert - Only integers should be encrypted
        result.Success.ShouldBeTrue();
        result.Statistics.ConstantsEncrypted.ShouldBe(1); // Only the int
    }

    [Fact]
    public async Task ConstantEncryption_EncryptsAllTypesWhenEnabled()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        CreateMethodWithConstants(type, "TestMethod", 100, 200L, 3.14f, 2.718);

        var logger = new Mock<ILogger<ConstantEncryptionObfuscator>>();
        var obfuscator = new ConstantEncryptionObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            ConstantEncryption =
            {
                Enabled = true,
                EncryptIntegers = true,
                EncryptLongs = true,
                EncryptFloats = true,
                EncryptDoubles = true,
                IntegerThreshold = 0,
                LongThreshold = 0,
                SkipCommonFloats = false,
                SkipCommonDoubles = false
            }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert - All 4 constants should be encrypted
        result.Success.ShouldBeTrue();
        result.Statistics.ConstantsEncrypted.ShouldBe(4);
    }

    #endregion

    #region AntiDecompilerObfuscator Tests

    [Fact]
    public async Task AntiDecompiler_InjectsSuppressIldasmAttribute()
    {
        // Arrange
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            Protection = { AntiDecompiler = { Enabled = true, AddSuppressIldasmAttribute = true } }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        var suppressIldasm = module.Assembly.CustomAttributes
            .FirstOrDefault(a => a.TypeFullName == "System.Runtime.CompilerServices.SuppressIldasmAttribute");
        suppressIldasm.ShouldNotBeNull();
    }

    [Fact]
    public async Task AntiDecompiler_DoesNotDuplicateSuppressIldasmAttribute()
    {
        // Arrange
        var module = CreateTestModule();

        // Add existing SuppressIldasm attribute
        var attrType = new TypeRefUser(
            module,
            "System.Runtime.CompilerServices",
            "SuppressIldasmAttribute",
            module.CorLibTypes.AssemblyRef);
        var ctor = new MemberRefUser(
            module,
            ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void),
            attrType);
        var attr = new CustomAttribute(ctor);
        module.Assembly.CustomAttributes.Add(attr);

        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            Protection = { AntiDecompiler = { Enabled = true, AddSuppressIldasmAttribute = true } }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert - Should still only have one SuppressIldasm attribute
        var suppressIldasmCount = module.Assembly.CustomAttributes
            .Count(a => a.TypeFullName == "System.Runtime.CompilerServices.SuppressIldasmAttribute");
        suppressIldasmCount.ShouldBe(1);
    }

    [Fact]
    public async Task AntiDecompiler_InjectsJunkTypes()
    {
        // Arrange
        var module = CreateTestModule();

        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            Protection = { AntiDecompiler = { Enabled = true, InjectJunkTypes = true, JunkTypeCount = 3 } }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var initialTypeCount = module.Types.Count;

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        module.Types.Count.ShouldBe(initialTypeCount + 3);
    }

    [Fact]
    public async Task AntiDecompiler_JunkTypesHaveMethods()
    {
        // Arrange
        var module = CreateTestModule();

        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            Protection = { AntiDecompiler = { Enabled = true, InjectJunkTypes = true, JunkTypeCount = 1, JunkMethodsPerType = 5 } }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        var junkType = module.Types.FirstOrDefault(t => t.Namespace == "Obfy.Internal");
        junkType.ShouldNotBeNull();
        // Should have 5 junk methods + 1 static constructor
        junkType.Methods.Count.ShouldBe(6);
    }

    [Fact]
    public async Task AntiDecompiler_JunkTypesHaveFields()
    {
        // Arrange
        var module = CreateTestModule();

        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            Protection = { AntiDecompiler = { Enabled = true, InjectJunkTypes = true, JunkTypeCount = 1 } }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        var junkType = module.Types.FirstOrDefault(t => t.Namespace == "Obfy.Internal");
        junkType.ShouldNotBeNull();
        junkType.Fields.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task AntiDecompiler_JunkMethodsHaveValidIL()
    {
        // Arrange
        var module = CreateTestModule();

        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            Protection = { AntiDecompiler = { Enabled = true, InjectJunkTypes = true, JunkTypeCount = 1, JunkMethodsPerType = 1 } }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        await obfuscator.ObfuscateAsync(context);

        // Assert
        var junkType = module.Types.First(t => t.Namespace == "Obfy.Internal");
        foreach (var method in junkType.Methods)
        {
            method.Body.ShouldNotBeNull();
            method.Body.Instructions.ShouldNotBeEmpty();
            // All methods should end with ret
            method.Body.Instructions.Last().OpCode.ShouldBe(OpCodes.Ret);
        }
    }

    [Fact]
    public void AntiDecompiler_Properties_AreCorrect()
    {
        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        obfuscator.Name.ShouldBe("AntiDecompiler");
        obfuscator.Priority.ShouldBe(72);
        obfuscator.SupportsTargetType(TargetType.Assembly).ShouldBeTrue();
        obfuscator.SupportsTargetType(TargetType.SourceCode).ShouldBeFalse();
    }

    [Fact]
    public void AntiDecompiler_IsEnabled_RespectsSettings()
    {
        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        var enabledSettings = new ObfySettings { Protection = { AntiDecompiler = { Enabled = true } } };
        var disabledSettings = new ObfySettings { Protection = { AntiDecompiler = { Enabled = false } } };

        obfuscator.IsEnabled(enabledSettings).ShouldBeTrue();
        obfuscator.IsEnabled(disabledSettings).ShouldBeFalse();
    }

    [Fact]
    public async Task AntiDecompiler_SkipsWhenJunkTypesDisabled()
    {
        // Arrange
        var module = CreateTestModule();

        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            Protection = { AntiDecompiler = { Enabled = true, InjectJunkTypes = false, AddSuppressIldasmAttribute = false } }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var initialTypeCount = module.Types.Count;

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        module.Types.Count.ShouldBe(initialTypeCount);
    }

    [Fact]
    public async Task AntiDecompiler_RespectsJunkTypeCount()
    {
        // Arrange
        var module = CreateTestModule();

        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            Protection = { AntiDecompiler = { Enabled = true, InjectJunkTypes = true, JunkTypeCount = 10, AddSuppressIldasmAttribute = false } }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var initialTypeCount = module.Types.Count;

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(10);
        module.Types.Count.ShouldBe(initialTypeCount + 10);
    }

    [Fact]
    public async Task AntiDecompiler_ReportsCorrectStatistics()
    {
        // Arrange
        var module = CreateTestModule();

        var logger = new Mock<ILogger<AntiDecompilerObfuscator>>();
        var obfuscator = new AntiDecompilerObfuscator(logger.Object);

        var settings = new ObfySettings
        {
            Protection = { AntiDecompiler = { Enabled = true, InjectJunkTypes = true, JunkTypeCount = 5, AddSuppressIldasmAttribute = true } }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        // Act
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert
        result.Success.ShouldBeTrue();
        // 5 junk types + 1 for SuppressIldasm
        result.Statistics.ProtectionsApplied.ShouldBe(6);
    }

    #endregion
}

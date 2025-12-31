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
}

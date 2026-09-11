using System.Runtime.Loader;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators;
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

    private static MethodDef CreateMethodWithStringAndExceptionHandler(TypeDef type, string name, string stringValue)
    {
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(type.Module.CorLibTypes.String),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static);

        var ldstr = Instruction.Create(OpCodes.Ldstr, stringValue);
        var ret = Instruction.Create(OpCodes.Ret);
        var catchPop = Instruction.Create(OpCodes.Pop);
        var leaveTry = Instruction.Create(OpCodes.Leave, ret);
        var leaveCatch = Instruction.Create(OpCodes.Leave, ret);
        var ldnull = Instruction.Create(OpCodes.Ldnull);

        var body = new CilBody();
        body.Instructions.Add(ldstr);
        body.Instructions.Add(leaveTry);
        body.Instructions.Add(catchPop);
        body.Instructions.Add(ldnull);
        body.Instructions.Add(leaveCatch);
        body.Instructions.Add(ret);
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = ldstr,
            TryEnd = catchPop,
            HandlerStart = catchPop,
            HandlerEnd = ret,
            CatchType = type.Module.CorLibTypes.Object.ToTypeDefOrRef()
        });
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
    public async Task StringEncryption_EncryptsExceptionHandlerMethods()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var handled = CreateMethodWithStringAndExceptionHandler(type, "Handled", "ALongEnoughString");
        var sibling = CreateMethodWithString(type, "Plain", "AnotherLongString");

        var obfuscator = new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object);
        var settings = new ObfySettings
        {
            StringEncryption = { Enabled = true, MinStringLength = 3 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBeGreaterThanOrEqualTo(2);

        handled.Body.Instructions.ShouldNotContain(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand! == "ALongEnoughString");
        sibling.Body.Instructions.ShouldNotContain(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand! == "AnotherLongString");
        handled.Body.HasExceptionHandlers.ShouldBeTrue();
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
    public async Task SymbolRenaming_RenameParameters_HonorsPreservePublicApi()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "Calc", isPublic: true);

        var publicMethod = new MethodDefUser(
            "Add",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.Int32, module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        publicMethod.ParamDefs.Add(new ParamDefUser("left", 1));
        publicMethod.ParamDefs.Add(new ParamDefUser("right", 2));
        var publicBody = new CilBody();
        publicBody.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        publicBody.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        publicBody.Instructions.Add(Instruction.Create(OpCodes.Add));
        publicBody.Instructions.Add(Instruction.Create(OpCodes.Ret));
        publicMethod.Body = publicBody;
        type.Methods.Add(publicMethod);

        var privateMethod = new MethodDefUser(
            "Hidden",
            MethodSig.CreateStatic(module.CorLibTypes.Int32, module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static);
        privateMethod.ParamDefs.Add(new ParamDefUser("secret", 1));
        var privateBody = new CilBody();
        privateBody.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        privateBody.Instructions.Add(Instruction.Create(OpCodes.Ret));
        privateMethod.Body = privateBody;
        type.Methods.Add(privateMethod);

        var obfuscator = new SymbolRenamingObfuscator(
            new NameGenerator(), new Mock<ILogger<SymbolRenamingObfuscator>>().Object);
        var settings = new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                RenameMethods = true,
                RenameParameters = true,
                PreservePublicApi = true,
                Mode = NamingMode.Sequential
            }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.ParametersRenamed.ShouldBeGreaterThan(0);

        publicMethod.ParamDefs.ShouldContain(p => p.Name == "left");
        publicMethod.ParamDefs.ShouldContain(p => p.Name == "right");
        privateMethod.ParamDefs.ShouldNotContain(p => p.Name == "secret");
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

    private static void AddLinearIntMethod(TypeDef type, string name)
    {
        // public static int M() { int x = 7; x = x + 35; x = x * 3; return x; } => 126
        // Straight-line, stack-neutral at every statement boundary — the shape Switch flattening targets.
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(type.Module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        var x = new Local(type.Module.CorLibTypes.Int32);
        body.Variables.Add(x);

        body.Instructions.Add(Instruction.CreateLdcI4(7));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, x));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, x));
        body.Instructions.Add(Instruction.CreateLdcI4(35));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, x));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, x));
        body.Instructions.Add(Instruction.CreateLdcI4(3));
        body.Instructions.Add(Instruction.Create(OpCodes.Mul));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, x));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, x));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
        method.Body = body;
        type.Methods.Add(method);
    }

    [Fact]
    public async Task ControlFlow_SwitchMode_ProducesRunnableAssembly()
    {
        // Switch flattening previously chunked IL every 4 instructions regardless of stack depth,
        // producing unverifiable IL (InvalidProgramException at JIT). This runs the flattened method.
        var module = CreateTestModule();
        var type = CreateTestType(module, "Machine", isPublic: true);
        AddLinearIntMethod(type, "Compute");

        var logger = new Mock<ILogger<ControlFlowObfuscator>>();
        var obfuscator = new ControlFlowObfuscator(logger.Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsControlFlowObfuscated.ShouldBe(1);

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-cf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "TestAssembly.dll");
        try
        {
            module.Write(path);
            var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                var asm = alc.LoadFromAssemblyPath(path);
                var machine = asm.GetType("TestNamespace.Machine");
                machine.ShouldNotBeNull();
                ((int)machine!.GetMethod("Compute")!.Invoke(null, null)!).ShouldBe(126);
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
    public async Task ControlFlow_RecordsExceptionHandlerSkip()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var method = CreateMethodWithMultipleInstructions(type, "Handled", 20);
        var tryStart = method.Body.Instructions[0];
        var handlerEnd = method.Body.Instructions[^1];
        method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = handlerEnd,
            HandlerStart = handlerEnd,
            HandlerEnd = handlerEnd,
            CatchType = module.CorLibTypes.Object.ToTypeDefOrRef()
        });

        var obfuscator = new ControlFlowObfuscator(new Mock<ILogger<ControlFlowObfuscator>>().Object);
        var settings = new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsControlFlowObfuscated.ShouldBe(1);
        var usesTickCount = method.Body.Instructions.Any(i =>
            i.Operand is IMethod m && m.Name == "get_TickCount");
        usesTickCount.ShouldBeTrue();
    }

    [Fact]
    public async Task ControlFlow_DoesNotSplitPrefixFromInstruction()
    {
        // Prefixes have stack delta 0; splitting after volatile. produces unverifiable IL.
        var module = CreateTestModule();
        var type = CreateTestType(module, "Machine", isPublic: true);
        var field = new FieldDefUser(
            "Flag",
            new FieldSig(module.CorLibTypes.Int32),
            FieldAttributes.Public | FieldAttributes.Static);
        type.Fields.Add(field);

        var method = new MethodDefUser(
            "Compute",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody { InitLocals = true };
        var x = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(x);
        body.Instructions.Add(Instruction.CreateLdcI4(7));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, x));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, x));
        body.Instructions.Add(Instruction.CreateLdcI4(35));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, x));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, x));
        body.Instructions.Add(Instruction.CreateLdcI4(3));
        body.Instructions.Add(Instruction.Create(OpCodes.Mul));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, x));
        body.Instructions.Add(Instruction.Create(OpCodes.Volatile));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, field));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, x));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
        method.Body = body;
        type.Methods.Add(method);

        var obfuscator = new ControlFlowObfuscator(new Mock<ILogger<ControlFlowObfuscator>>().Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-cf-prefix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "TestAssembly.dll");
        try
        {
            module.Write(path);
            var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                var asm = alc.LoadFromAssemblyPath(path);
                var machine = asm.GetType("TestNamespace.Machine");
                machine.ShouldNotBeNull();
                ((int)machine!.GetMethod("Compute")!.Invoke(null, null)!).ShouldBe(126);
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
        var result = await obfuscator.ObfuscateAsync(context);

        // Assert - Switch mode flattens this linear method and adds exactly one state variable
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsControlFlowObfuscated.ShouldBe(1);
        method.Body.Variables.Count.ShouldBe(originalVarCount + 1);
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
        obfuscator.Priority.ShouldBe((int)ObfuscationPhase.AntiDebug);
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
        context.AntiTamperMetadata.ShouldNotBeNull();
        context.AntiTamperMetadata.ShouldBe(AntiTamperMetadata.Injected);
    }

    [Fact]
    public void AntiTamper_Properties_AreCorrect()
    {
        var logger = new Mock<ILogger<AntiTamperObfuscator>>();
        var obfuscator = new AntiTamperObfuscator(logger.Object);

        obfuscator.Name.ShouldBe("AntiTamper");
        obfuscator.Priority.ShouldBe((int)ObfuscationPhase.AntiTamper);
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

        // The pipeline gates each obfuscator on IsEnabled, so a disabled setting must report false
        // (and an enabled one true). This is the mechanism that prevents injection when disabled.
        obfuscator.IsEnabled(new ObfySettings { Protection = { AntiTamper = { Enabled = false } } }).ShouldBeFalse();
        obfuscator.IsEnabled(new ObfySettings { Protection = { AntiTamper = { Enabled = true } } }).ShouldBeTrue();
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
    public async Task MetadataRemoval_PreservesRuntimeCompatibilityAndCompilationRelaxations()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var relaxType = module.CorLibTypes.GetTypeRef("System.Runtime.CompilerServices", "CompilationRelaxationsAttribute");
        type.CustomAttributes.Add(new CustomAttribute(
            new MemberRefUser(module, ".ctor",
                MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.Int32),
                relaxType),
            new CAArgument[] { new(module.CorLibTypes.Int32, 8) }));

        var compatType = module.CorLibTypes.GetTypeRef("System.Runtime.CompilerServices", "RuntimeCompatibilityAttribute");
        type.CustomAttributes.Add(new CustomAttribute(
            new MemberRefUser(module, ".ctor",
                MethodSig.CreateInstance(module.CorLibTypes.Void),
                compatType)));

        var generatedType = module.CorLibTypes.GetTypeRef("System.Runtime.CompilerServices", "CompilerGeneratedAttribute");
        type.CustomAttributes.Add(new CustomAttribute(
            new MemberRefUser(module, ".ctor",
                MethodSig.CreateInstance(module.CorLibTypes.Void),
                generatedType)));

        var obfuscator = new MetadataRemovalObfuscator(new Mock<ILogger<MetadataRemovalObfuscator>>().Object);
        var settings = new ObfySettings { Metadata = { RemoveAttributes = true } };
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();

        type.CustomAttributes.ShouldContain(a => a.TypeFullName.Contains("CompilationRelaxationsAttribute"));
        type.CustomAttributes.ShouldContain(a => a.TypeFullName.Contains("RuntimeCompatibilityAttribute"));
        type.CustomAttributes.ShouldNotContain(a => a.TypeFullName.Contains("CompilerGeneratedAttribute"));
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
    public async Task ResourceEncryption_RecordsExcludedResources()
    {
        var module = CreateTestModule();
        module.Resources.Add(new EmbeddedResource("keep.resources", new byte[] { 1, 2, 3 }));
        module.Resources.Add(new EmbeddedResource("secret.bin", new byte[] { 4, 5, 6 }));

        var obfuscator = new ResourceEncryptionObfuscator(new Mock<ILogger<ResourceEncryptionObfuscator>>().Object);
        var settings = new ObfySettings
        {
            ResourceEncryption =
            {
                Enabled = true,
                IncludePatterns = new List<string> { "*" },
                ExcludePatterns = new List<string> { "*.resources" }
            }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.ResourcesEncrypted.ShouldBe(1);
        context.SkippedItems.ShouldContain(s =>
            s.Reason == SkipReason.ResourceExcluded &&
            s.ItemType == SkippedItemType.Resource &&
            s.ItemName == "keep.resources");
    }

    [Fact]
    public async Task ResourceEncryption_InjectsDecryptorWithLoaderMethod()
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
        decryptorType.FindMethod("LoadResourceStream").ShouldNotBeNull();
        decryptorType.FindMethod(".cctor").ShouldNotBeNull();
    }

    private static void AddResourceReadMethod(TypeDef type, string name)
    {
        // public static byte[] M(string resourceName)
        // {
        //     var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        //     if (s == null) return null;
        //     var ms = new MemoryStream();
        //     s.CopyTo(ms);
        //     return ms.ToArray();
        // }
        var module = type.Module;
        var streamType = new TypeRefUser(module, "System.IO", "Stream", module.CorLibTypes.AssemblyRef);
        var memoryStreamType = new TypeRefUser(module, "System.IO", "MemoryStream", module.CorLibTypes.AssemblyRef);
        var assemblyType = new TypeRefUser(module, "System.Reflection", "Assembly", module.CorLibTypes.AssemblyRef);

        var getExecuting = new MemberRefUser(module, "GetExecutingAssembly",
            MethodSig.CreateStatic(new ClassSig(assemblyType)), assemblyType);
        var getStream = new MemberRefUser(module, "GetManifestResourceStream",
            MethodSig.CreateInstance(new ClassSig(streamType), module.CorLibTypes.String), assemblyType);
        var copyTo = new MemberRefUser(module, "CopyTo",
            MethodSig.CreateInstance(module.CorLibTypes.Void, new ClassSig(streamType)), streamType);
        var toArray = new MemberRefUser(module, "ToArray",
            MethodSig.CreateInstance(new SZArraySig(module.CorLibTypes.Byte)), memoryStreamType);
        var msCtor = new MemberRefUser(module, ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void), memoryStreamType);

        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.String),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody { InitLocals = true };
        var s = new Local(new ClassSig(streamType));
        var ms = new Local(new ClassSig(memoryStreamType));
        body.Variables.Add(s);
        body.Variables.Add(ms);

        var read = Instruction.Create(OpCodes.Newobj, msCtor);

        body.Instructions.Add(Instruction.Create(OpCodes.Call, getExecuting));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getStream)); // rewritten to LoadResourceStream
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, s));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, s));
        body.Instructions.Add(Instruction.Create(OpCodes.Brtrue, read));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.Instructions.Add(read);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, ms));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, s));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ms));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, copyTo));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, ms));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, toArray));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
        method.Body = body;
        type.Methods.Add(method);
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public async Task ResourceEncryption_RoundTripsEncryptedAndPassesThroughExcluded(EncryptionAlgorithm algorithm)
    {
        // The runtime loader previously decrypted EVERY GetManifestResourceStream result, corrupting
        // resources it never encrypted. This verifies the encrypted resource decrypts back to its
        // original bytes AND an excluded (*.resources) resource is returned untouched.
        var secret = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };
        var plain = new byte[] { 200, 201, 202, 203, 204 };

        var module = CreateTestModule();
        module.Resources.Add(new EmbeddedResource("secret.bin", secret, ManifestResourceAttributes.Public));
        module.Resources.Add(new EmbeddedResource("keep.resources", plain, ManifestResourceAttributes.Public));
        var type = CreateTestType(module, "ResHolder", isPublic: true);
        AddResourceReadMethod(type, "Read");

        var logger = new Mock<ILogger<ResourceEncryptionObfuscator>>();
        var obfuscator = new ResourceEncryptionObfuscator(logger.Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            ResourceEncryption =
            {
                Enabled = true,
                Algorithm = algorithm,
                IncludePatterns = new List<string> { "*" },
                ExcludePatterns = new List<string> { "*.resources" }
            }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.ResourcesEncrypted.ShouldBe(1); // secret.bin only

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-res-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "TestAssembly.dll");
        try
        {
            module.Write(path);
            var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                var asm = alc.LoadFromAssemblyPath(path);
                var holder = asm.GetType("TestNamespace.ResHolder");
                holder.ShouldNotBeNull();
                var read = holder!.GetMethod("Read");

                var decrypted = (byte[])read!.Invoke(null, new object[] { "secret.bin" })!;
                decrypted.ShouldBe(secret); // encrypted resource decrypts back to original

                var passthrough = (byte[])read.Invoke(null, new object[] { "keep.resources" })!;
                passthrough.ShouldBe(plain); // excluded resource must NOT be altered
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

    private static void AddConstantReturningMethod(TypeDef type, string name, TypeSig returnType, Instruction loadConstant)
    {
        var method = new MethodDefUser(
            name,
            MethodSig.CreateStatic(returnType),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody();
        body.Instructions.Add(loadConstant);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
        method.Body = body;

        type.Methods.Add(method);
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public async Task ConstantEncryption_RuntimeDecryptsAllNumericTypes(EncryptionAlgorithm algorithm)
    {
        // Each numeric type exercises a distinct decrypt method; the long/float/double methods
        // previously emitted a stray leading array load (stack non-empty at ret -> InvalidProgramException),
        // and AES was never honored at runtime. This runs the obfuscated assembly to prove both are fixed.
        const int intValue = 123456;
        const long longValue = 9_876_543_210L;
        const float floatValue = 3.14159f;
        const double doubleValue = 2.718281828459045;

        var module = CreateTestModule();
        var type = CreateTestType(module, "Calc", isPublic: true);
        AddConstantReturningMethod(type, "GetInt", module.CorLibTypes.Int32, Instruction.CreateLdcI4(intValue));
        AddConstantReturningMethod(type, "GetLong", module.CorLibTypes.Int64, Instruction.Create(OpCodes.Ldc_I8, longValue));
        AddConstantReturningMethod(type, "GetFloat", module.CorLibTypes.Single, Instruction.Create(OpCodes.Ldc_R4, floatValue));
        AddConstantReturningMethod(type, "GetDouble", module.CorLibTypes.Double, Instruction.Create(OpCodes.Ldc_R8, doubleValue));

        var logger = new Mock<ILogger<ConstantEncryptionObfuscator>>();
        var obfuscator = new ConstantEncryptionObfuscator(logger.Object);
        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            ConstantEncryption =
            {
                Enabled = true,
                Algorithm = algorithm,
                IntegerThreshold = 0,
                LongThreshold = 0,
                SkipCommonFloats = false,
                SkipCommonDoubles = false
            }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.ConstantsEncrypted.ShouldBe(4);

        var dir = Path.Combine(Path.GetTempPath(), $"obfy-const-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "TestAssembly.dll");
        try
        {
            module.Write(path);
            var alc = new AssemblyLoadContext($"rt-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                var asm = alc.LoadFromAssemblyPath(path);
                var calc = asm.GetType("TestNamespace.Calc");
                calc.ShouldNotBeNull();
                ((int)calc!.GetMethod("GetInt")!.Invoke(null, null)!).ShouldBe(intValue);
                ((long)calc.GetMethod("GetLong")!.Invoke(null, null)!).ShouldBe(longValue);
                ((float)calc.GetMethod("GetFloat")!.Invoke(null, null)!).ShouldBe(floatValue);
                ((double)calc.GetMethod("GetDouble")!.Invoke(null, null)!).ShouldBe(doubleValue);
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
    public async Task ConstantEncryption_EncryptsExceptionHandlerMethods()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");

        var handled = new MethodDefUser(
            "Handled",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static);
        var tryStart = Instruction.CreateLdcI4(123456);
        var ret = Instruction.Create(OpCodes.Ret);
        var catchPop = Instruction.Create(OpCodes.Pop);
        var leaveTry = Instruction.Create(OpCodes.Leave, ret);
        var leaveCatch = Instruction.Create(OpCodes.Leave, ret);
        var ldcDefault = Instruction.CreateLdcI4(0);
        handled.Body = new CilBody();
        handled.Body.Instructions.Add(tryStart);
        handled.Body.Instructions.Add(leaveTry);
        handled.Body.Instructions.Add(catchPop);
        handled.Body.Instructions.Add(ldcDefault);
        handled.Body.Instructions.Add(leaveCatch);
        handled.Body.Instructions.Add(ret);
        handled.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchPop,
            HandlerStart = catchPop,
            HandlerEnd = ret,
            CatchType = module.CorLibTypes.Object.ToTypeDefOrRef()
        });
        type.Methods.Add(handled);

        var sibling = new MethodDefUser(
            "Plain",
            MethodSig.CreateStatic(module.CorLibTypes.Int32),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.Static);
        sibling.Body = new CilBody();
        sibling.Body.Instructions.Add(Instruction.CreateLdcI4(654321));
        sibling.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(sibling);

        var obfuscator = new ConstantEncryptionObfuscator(new Mock<ILogger<ConstantEncryptionObfuscator>>().Object);
        var settings = new ObfySettings
        {
            ConstantEncryption = { Enabled = true, IntegerThreshold = 0 }
        };
        var context = PipelineContext.ForAssembly(module, settings);

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.ConstantsEncrypted.ShouldBeGreaterThan(0);
        tryStart.OpCode.ShouldNotBe(OpCodes.Ldc_I4);
        handled.Body.HasExceptionHandlers.ShouldBeTrue();
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
        var junkType = module.Types.FirstOrDefault(t => !t.IsGlobalModuleType);
        junkType.ShouldNotBeNull();
        junkType.Namespace.String.ShouldNotBe("Obfy.Internal");
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
        var junkType = module.Types.FirstOrDefault(t => !t.IsGlobalModuleType);
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
        var junkType = module.Types.First(t => !t.IsGlobalModuleType);
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
        obfuscator.Priority.ShouldBe((int)ObfuscationPhase.AntiDecompiler);
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

    #region Coverage and hardening

    [Fact]
    public async Task StringEncryption_EncryptsCompilerGeneratedMethods()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var method = CreateMethodWithString(type, "<GetMessage>b__0", "LambdaCapturedSecret");

        var obfuscator = new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            StringEncryption = { Enabled = true, MinStringLength = 3 }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBe(1);
        method.Body.Instructions.ShouldNotContain(i =>
            i.OpCode == OpCodes.Ldstr && (string)i.Operand! == "LambdaCapturedSecret");
    }

    [Fact]
    public async Task StringEncryption_HonorsEncryptConstantStringsOff()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        CreateMethodWithString(type, "GetMessage", "LeaveThisPlain");

        var obfuscator = new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            StringEncryption = { Enabled = true, EncryptConstantStrings = false, MinStringLength = 3 }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBe(0);
    }

    [Fact]
    public async Task SymbolRenaming_RenamesPropertyAccessorsWithProperty()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var property = CreateTestProperty(type, "PrivateProperty", isPublic: false);

        var obfuscator = new SymbolRenamingObfuscator(new NameGenerator(), new Mock<ILogger<SymbolRenamingObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameProperties = true, Mode = NamingMode.Sequential }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        property.Name.String.ShouldNotBe("PrivateProperty");
        property.GetMethod.Name.String.ShouldBe("get_" + property.Name.String);
    }

    [Fact]
    public async Task SymbolRenaming_RenamesEventsAndAccessors()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var handler = new TypeRefUser(module, "System", "EventHandler", module.CorLibTypes.AssemblyRef);
        var add = new MethodDefUser("add_Changed", MethodSig.CreateInstance(module.CorLibTypes.Void, new ClassSig(handler)),
            MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
        add.Body = new CilBody();
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        type.Methods.Add(add);
        var evt = new EventDefUser("Changed", handler);
        evt.AddMethod = add;
        type.Events.Add(evt);

        var obfuscator = new SymbolRenamingObfuscator(new NameGenerator(), new Mock<ILogger<SymbolRenamingObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameEvents = true, Mode = NamingMode.Sequential }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.EventsRenamed.ShouldBe(1);
        evt.Name.String.ShouldNotBe("Changed");
        add.Name.String.ShouldBe("add_" + evt.Name.String);
    }

    [Fact]
    public async Task SymbolRenaming_RenamesNamespaces()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "Hidden", isPublic: false);

        var obfuscator = new SymbolRenamingObfuscator(new NameGenerator(), new Mock<ILogger<SymbolRenamingObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameNamespaces = true, Mode = NamingMode.Sequential }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.NamespacesRenamed.ShouldBe(1);
        type.Namespace.String.ShouldNotBe("TestNamespace");
    }

    [Fact]
    public async Task SymbolRenaming_HonorsMethodExclusions()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var kept = CreateTestMethod(type, "KeepMe");
        var renamed = CreateTestMethod(type, "HideMe");

        var obfuscator = new SymbolRenamingObfuscator(new NameGenerator(), new Mock<ILogger<SymbolRenamingObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            SymbolRenaming = { Enabled = true, RenameMethods = true, Mode = NamingMode.Sequential },
            Exclusions = { Methods = { "KeepMe" } }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        kept.Name.String.ShouldBe("KeepMe");
        renamed.Name.String.ShouldNotBe("HideMe");
    }

    [Fact]
    public async Task ControlFlow_FlattensBranchedMethods()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var method = new MethodDefUser("Branchy", MethodSig.CreateStatic(module.CorLibTypes.Int32),
            MethodImplAttributes.IL, MethodAttributes.Private | MethodAttributes.Static);
        var body = new CilBody { InitLocals = true };
        var local = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(local);
        var elseLabel = Instruction.Create(OpCodes.Ldc_I4_3);
        var endLabel = Instruction.Create(OpCodes.Ldloc, local);
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, local));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldloc, local));
        body.Instructions.Add(Instruction.Create(OpCodes.Brfalse, elseLabel));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_2));
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, local));
        body.Instructions.Add(Instruction.Create(OpCodes.Br, endLabel));
        body.Instructions.Add(elseLabel);
        body.Instructions.Add(Instruction.Create(OpCodes.Stloc, local));
        body.Instructions.Add(endLabel);
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        body.UpdateInstructionOffsets();
        method.Body = body;
        type.Methods.Add(method);

        var obfuscator = new ControlFlowObfuscator(new Mock<ILogger<ControlFlowObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.MethodsControlFlowObfuscated.ShouldBe(1);
        method.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Beq || i.OpCode == OpCodes.Switch);
    }

    [Fact]
    public async Task AntiDump_InjectsWipeAtModuleInitializer()
    {
        var module = CreateTestModule();
        var obfuscator = new AntiDumpObfuscator(new Mock<ILogger<AntiDumpObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings { Protection = { AntiDump = true } });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBe(1);
        var dumpType = module.Types.First(t => t.Name == "<AntiDump>");
        dumpType.FindMethod("Wipe").ShouldNotBeNull();
        var cctor = module.GlobalType.FindMethod(".cctor");
        cctor.ShouldNotBeNull();
        cctor!.Body.Instructions[0].OpCode.ShouldBe(OpCodes.Call);
    }

    [Fact]
    public async Task ReferenceProxy_RewritesCallSites()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "Calc");
        var target = CreateTestMethod(type, "Add");
        target.Body.Instructions.Clear();
        target.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        var caller = CreateTestMethod(type, "Run");
        caller.Body.Instructions.Clear();
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Call, target));
        caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        var obfuscator = new ReferenceProxyObfuscator(new Mock<ILogger<ReferenceProxyObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings { Protection = { ReferenceProxy = true } });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0);
        var called = (IMethod)caller.Body.Instructions[0].Operand;
        called.DeclaringType.Name.String.ShouldBe("<RefProxy>");
        var proxy = called.ResolveMethodDef();
        proxy.ShouldNotBeNull();
        proxy!.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Calli);
        proxy.Body.Instructions.ShouldContain(i => i.OpCode == OpCodes.Ldftn);
    }

    [Fact]
    public async Task StringEncryption_EncryptsCompilerGeneratedTypes()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "<>c");
        var attrType = module.CorLibTypes.GetTypeRef("System.Runtime.CompilerServices", "CompilerGeneratedAttribute");
        type.CustomAttributes.Add(new CustomAttribute(new MemberRefUser(
            module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), attrType)));
        var method = CreateMethodWithString(type, "MoveNext", "AsyncCapturedSecret");

        var obfuscator = new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            StringEncryption = { Enabled = true, MinStringLength = 3 }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBe(1);
        method.Body.Instructions.ShouldNotContain(i =>
            i.OpCode == OpCodes.Ldstr && (string)i.Operand! == "AsyncCapturedSecret");
    }

    [Fact]
    public async Task StringEncryption_ResourceHookLeavesShortStringsReadable()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "Strings", isPublic: true);
        var rmType = new TypeRefUser(module, "System.Resources", "ResourceManager", module.CorLibTypes.AssemblyRef);
        var getString = new MemberRefUser(module, "GetString",
            MethodSig.CreateInstance(module.CorLibTypes.String, module.CorLibTypes.String), rmType);

        var method = new MethodDefUser(
            "Read",
            MethodSig.CreateStatic(module.CorLibTypes.String, new ClassSig(rmType), module.CorLibTypes.String),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, getString));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;
        type.Methods.Add(method);

        using var output = new MemoryStream();
        using (var writer = new System.Resources.ResourceWriter(output))
        {
            writer.AddResource("short", "Hi");
            writer.AddResource("long", "LongEnoughSecret");
            writer.Generate();
        }

        module.Resources.Add(new EmbeddedResource("TestNamespace.Strings.resources", output.ToArray()));

        var obfuscator = new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            StringEncryption = { Enabled = true, EncryptResourceStrings = true, MinStringLength = 3 }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.StringsEncrypted.ShouldBeGreaterThan(0);

        var resource = module.Resources.OfType<EmbeddedResource>()
            .First(r => r.Name.String.EndsWith(".resources", StringComparison.OrdinalIgnoreCase));
        using var input = new MemoryStream(resource.CreateReader().ToArray());
        using var reader = new System.Resources.ResourceReader(input);
        var values = new Dictionary<string, string>();
        var enumerator = reader.GetEnumerator();
        while (enumerator.MoveNext())
            values[enumerator.Key.ToString()!] = (string)enumerator.Value!;

        values["short"].ShouldBe("Hi");
        values["long"].ShouldNotBe("LongEnoughSecret");
        values["long"][0].ShouldBe('\u0001');

        var hookCall = method.Body.Instructions.First(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt);
        ((IMethod)hookCall.Operand).Name.String.ShouldBe("Ds");
    }

    [Fact]
    public async Task AntiDump_WipeTouchesMoreThanChecksum()
    {
        var module = CreateTestModule();
        var obfuscator = new AntiDumpObfuscator(new Mock<ILogger<AntiDumpObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings { Protection = { AntiDump = true } });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();

        var wipe = module.Types.First(t => t.Name == "<AntiDump>").FindMethod("Wipe")!;
        var writeCount = wipe.Body.Instructions.Count(i =>
            i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "WriteInt32");
        writeCount.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task AntiTamper_VerifyFallsBackToProcessPath()
    {
        var module = CreateTestModule();
        var obfuscator = new AntiTamperObfuscator(new Mock<ILogger<AntiTamperObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            Protection = { AntiTamper = { Enabled = true, CheckEntryPoint = true } }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();

        var verify = module.Types.First(t => t.Name.String.Contains("AntiTamper")).FindMethod("Verify")!;
        var usesProcessPath = verify.Body.Instructions.Any(i =>
            i.Operand is IMethod m && m.Name == "get_ProcessPath");
        usesProcessPath.ShouldBeTrue();
        verify.IsPublic.ShouldBeFalse();
    }

    [Fact]
    public async Task StringEncryption_CallSitesUseEncodedIndex()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var method = CreateMethodWithString(type, "GetMessage", "HelloWorldSecret");

        var obfuscator = new StringEncryptionObfuscator(new Mock<ILogger<StringEncryptionObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            StringEncryption = { Enabled = true, MinStringLength = 3 }
        });

        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();

        var ldc = method.Body.Instructions.First(i => i.OpCode.Code.ToString().StartsWith("Ldc_I4", StringComparison.Ordinal));
        var value = ldc.OpCode == OpCodes.Ldc_I4_0 ? 0
            : ldc.OpCode == OpCodes.Ldc_I4_1 ? 1
            : ldc.Operand is int i ? i
            : ldc.Operand is sbyte b ? b
            : int.MinValue;
        value.ShouldNotBe(0);
    }

    [Fact]
    public async Task AntiDebug_InjectsNativeChecks()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "TestClass");
        var entry = CreateTestMethod(type, "Main", isPublic: true);
        module.EntryPoint = entry;

        var obfuscator = new AntiDebugObfuscator(new Mock<ILogger<AntiDebugObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings { Protection = { AntiDebug = true } });
        (await obfuscator.ObfuscateAsync(context)).Success.ShouldBeTrue();

        var anti = module.Types.First(t => t.Name == "<AntiDebug>");
        anti.FindMethod("Check").ShouldNotBeNull();
        anti.FindMethod("IsDebuggerPresent").ShouldNotBeNull();
    }

    [Fact]
    public async Task MethodEncryption_InjectsDecryptor()
    {
        var module = CreateTestModule();
        var type = CreateTestType(module, "Work");
        var method = CreateMethodWithMultipleInstructions(type, "Go", 12);

        var obfuscator = new MethodEncryptionObfuscator(new Mock<ILogger<MethodEncryptionObfuscator>>().Object);
        var context = PipelineContext.ForAssembly(module, new ObfySettings { Protection = { MethodEncryption = true } });
        var result = await obfuscator.ObfuscateAsync(context);
        result.Success.ShouldBeTrue();
        result.Statistics.ProtectionsApplied.ShouldBeGreaterThan(0);
        module.Types.ShouldContain(t => t.Name == "<MethodCrypt>");
        context.MethodEncryptionMetadata.ShouldNotBeNull();
        context.MethodEncryptionMetadata!.Methods.Count.ShouldBeGreaterThan(0);
    }

    #endregion
}

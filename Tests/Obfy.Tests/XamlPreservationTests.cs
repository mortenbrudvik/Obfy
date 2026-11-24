using System.Resources;
using System.Text;
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

public class XamlPreservationTests
{
    [Fact]
    public void XamlAssemblyInspector_CollectsConnectorsAndBamlTypeInfoRecords_WithoutDeserializingObjects()
    {
        var module = new ModuleDefUser("WpfSample.dll");

        var connectorType = AddType(module, "App.Views", "MainWindow");
        connectorType.Interfaces.Add(new InterfaceImplUser(
            new TypeRefUser(module, "System.Windows.Markup", "IComponentConnector")));

        var styleConnectorType = AddType(module, "App.Views", "RowTemplate");
        styleConnectorType.Interfaces.Add(new InterfaceImplUser(
            new TypeRefUser(module, "System.Windows.Markup", "IStyleConnector")));

        var bamlBytes = BuildBaml(
            ["App.Converters.DateConverter", "App.Controls.NumericBox"],
            includeSerializerInfoType: "App.Custom.SerializedBrush");

        var resourcesBytes = BuildResources(writer =>
        {
            writer.AddResource("views/mainwindow.baml", new MemoryStream(bamlBytes));
            // Custom user-type payload that would fail BinaryFormatter/deserialization if .Value were accessed.
            writer.AddResourceData("custom/unresolvable.bin", "NonExistent.Type, MissingAssembly", [1, 2, 3, 4]);
        });

        module.Resources.Add(new EmbeddedResource("WpfSample.g.resources", resourcesBytes, ManifestResourceAttributes.Public));

        var referenced = XamlAssemblyInspector.CollectReferencedTypes(module);

        referenced.ShouldContain("App.Views.MainWindow");
        referenced.ShouldContain("App.Views.RowTemplate");
        referenced.ShouldContain("App.Converters.DateConverter");
        referenced.ShouldContain("App.Controls.NumericBox");
        referenced.ShouldContain("App.Custom.SerializedBrush");

        var connectors = XamlAssemblyInspector.CollectConnectorTypes(module);
        connectors.ShouldBe(["App.Views.MainWindow", "App.Views.RowTemplate"], ignoreOrder: true);
    }

    [Fact]
    public void XamlAssemblyInspector_HandlesMalformedBamlAndCorruptResourcesGracefully()
    {
        var module = new ModuleDefUser("Broken.dll");
        module.Resources.Add(new EmbeddedResource("Broken.g.resources", [0xDE, 0xAD, 0xBE, 0xEF], ManifestResourceAttributes.Public));

        var validBeforeUnknown = BuildBamlWithTrailingGarbage("App.Views.RecoveredType");
        var badSig = Encoding.UTF8.GetBytes("NOT_MSBAML");
        var truncatedHeader = new byte[] { 0x0C, 0x00, 0x00 };

        var gResources = BuildResources(writer =>
        {
            writer.AddResource("views/recovered.baml", new MemoryStream(validBeforeUnknown));
            writer.AddResource("views/badsig.baml", new MemoryStream(badSig));
            writer.AddResource("views/short.baml", new MemoryStream(truncatedHeader));
        });
        module.Resources.Add(new EmbeddedResource("Mixed.g.resources", gResources, ManifestResourceAttributes.Public));

        var referenced = XamlAssemblyInspector.CollectReferencedTypes(module);

        referenced.ShouldContain("App.Views.RecoveredType");
    }

    [Fact]
    public async Task SymbolRenaming_PreserveXaml_KeepsConnectorAndFullNameExcludedTypesAndTheirNamespaces_AndDoesNotWarn()
    {
        var module = new ModuleDefUser("WpfApp.dll");
        var objRef = module.CorLibTypes.Object.TypeDefOrRef;

        var connectorWindow = AddType(module, "App.Views", "MainWindow", objRef);
        connectorWindow.Interfaces.Add(new InterfaceImplUser(
            new TypeRefUser(module, "System.Windows.Markup", "IComponentConnector")));
        var windowHandler = AddMethod(connectorWindow, "OnLoaded", module.CorLibTypes.Void);

        var excludedByFullName = AddType(module, "App.Controls", "CustomPanel", objRef);
        var panelMethod = AddMethod(excludedByFullName, "MeasureCustom", module.CorLibTypes.Void);

        var nestedExcluded = AddNestedType(excludedByFullName, "InnerLayout", objRef);

        // Same simple name in a different namespace must NOT be preserved by "App.Controls.CustomPanel".
        var sameShortNameDifferentNs = AddType(module, "App.Internal", "CustomPanel", objRef);
        var nonXamlPlain = AddType(module, "App.Models", "DashboardModel", objRef);

        // Pattern without dots ("*Views*") must only match Type.Name, NOT the "App.Views" namespace.
        var plainInViewsNs = AddType(module, "App.Views", "ViewHelper", objRef);

        var obfuscator = new SymbolRenamingObfuscator(
            new NameGenerator(),
            new Mock<ILogger<SymbolRenamingObfuscator>>().Object);

        var settings = new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                Mode = NamingMode.Sequential,
                RenameTypes = true,
                RenameMethods = true,
                RenameNamespaces = true,
                PreservePublicApi = false,
                PreserveXaml = true
            },
            Exclusions =
            {
                Types = ["App.Controls.CustomPanel", "*Views*"]
            }
        };

        var context = PipelineContext.ForAssembly(module, settings);
        var result = await obfuscator.ObfuscateAsync(context);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        context.Warnings.ShouldNotContain(SymbolRenamingObfuscator.NoXamlBindableTypes);

        // IComponentConnector type, member, and namespace stay intact.
        connectorWindow.Name.String.ShouldBe("MainWindow");
        connectorWindow.Namespace.String.ShouldBe("App.Views");
        windowHandler.Name.String.ShouldBe("OnLoaded");

        // Full-name excluded type, nested type, member, and namespace stay intact.
        excludedByFullName.Name.String.ShouldBe("CustomPanel");
        excludedByFullName.Namespace.String.ShouldBe("App.Controls");
        panelMethod.Name.String.ShouldBe("MeasureCustom");
        nestedExcluded.Name.String.ShouldBe("InnerLayout");

        // Unrelated types (even with same short name in another namespace or living in App.Views) are renamed.
        sameShortNameDifferentNs.Name.String.ShouldNotBe("CustomPanel");
        sameShortNameDifferentNs.Namespace.String.ShouldNotBe("App.Internal");
        nonXamlPlain.Name.String.ShouldNotBe("DashboardModel");
        nonXamlPlain.Namespace.String.ShouldNotBe("App.Models");
        plainInViewsNs.Name.String.ShouldNotBe("ViewHelper");
        plainInViewsNs.Namespace.String.ShouldNotBe("App.Views");
    }

    [Fact]
    public async Task SymbolRenaming_ClosedSet_PreservesBamlReferencedTypeAcrossModules()
    {
        var libModule = new ModuleDefUser("WpfLib.dll");
        var libControl = AddType(libModule, "WpfLib.Controls", "FancyPanel", libModule.CorLibTypes.Object.TypeDefOrRef);
        var libSecret = AddType(libModule, "WpfLib.Internal", "SecretHelper", libModule.CorLibTypes.Object.TypeDefOrRef);

        var appModule = new ModuleDefUser("WpfApp.dll");
        var appWindow = AddType(appModule, "WpfApp", "MainWindow", appModule.CorLibTypes.Object.TypeDefOrRef);
        appWindow.Interfaces.Add(new InterfaceImplUser(
            new TypeRefUser(appModule, "System.Windows.Markup", "IComponentConnector")));

        var bamlBytes = BuildBaml("WpfApp.MainWindow", "WpfLib.Controls.FancyPanel");
        var resourcesBytes = BuildResources(w => w.AddResource("mainwindow.baml", new MemoryStream(bamlBytes)));
        appModule.Resources.Add(new EmbeddedResource("WpfApp.g.resources", resourcesBytes, ManifestResourceAttributes.Public));

        var libSettings = new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                Mode = NamingMode.Sequential,
                RenameTypes = true,
                RenameNamespaces = true,
                PreservePublicApi = false,
                PreserveXaml = false
            }
        };
        var appSettings = new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                Mode = NamingMode.Sequential,
                RenameTypes = true,
                RenameNamespaces = true,
                PreservePublicApi = false,
                PreserveXaml = true
            }
        };

        var obfuscator = new SymbolRenamingObfuscator(
            new NameGenerator(),
            new Mock<ILogger<SymbolRenamingObfuscator>>().Object);
        var sharedContext = PipelineContext.ForAssembly(appModule, appSettings);

        obfuscator.RenameClosedSet(
            [(libModule, libSettings), (appModule, appSettings)],
            sharedContext);

        sharedContext.Warnings.ShouldNotContain(SymbolRenamingObfuscator.ClosedSetNoXamlBindableTypes);

        appWindow.Name.String.ShouldBe("MainWindow");
        appWindow.Namespace.String.ShouldBe("WpfApp");
        libControl.Name.String.ShouldBe("FancyPanel");
        libControl.Namespace.String.ShouldBe("WpfLib.Controls");
        libSecret.Name.String.ShouldNotBe("SecretHelper");
        libSecret.Namespace.String.ShouldNotBe("WpfLib.Internal");
    }

    [Fact]
    public async Task SymbolRenaming_PreserveXaml_KeepsEnclosingTypeNameAndNamespace_ForBamlReferencedNestedType()
    {
        var module = new ModuleDefUser("WpfNested.dll");
        var objRef = module.CorLibTypes.Object.TypeDefOrRef;

        var outer = AddType(module, "App", "Outer", objRef);
        var outerMethod = AddMethod(outer, "OuterMethod", module.CorLibTypes.Void);

        var inner = AddNestedType(outer, "Inner", objRef);
        var innerMethod = AddMethod(inner, "InnerMethod", module.CorLibTypes.Void);

        var unrelatedNested = AddNestedType(outer, "UnrelatedNested", objRef);
        var plainModel = AddType(module, "App.Models", "PlainModel", objRef);

        var bamlBytes = BuildBaml("App.Outer+Inner");
        var resourcesBytes = BuildResources(w => w.AddResource("mainwindow.baml", new MemoryStream(bamlBytes)));
        module.Resources.Add(new EmbeddedResource("WpfNested.g.resources", resourcesBytes, ManifestResourceAttributes.Public));

        var obfuscator = new SymbolRenamingObfuscator(
            new NameGenerator(),
            new Mock<ILogger<SymbolRenamingObfuscator>>().Object);

        var settings = new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                Mode = NamingMode.Sequential,
                RenameTypes = true,
                RenameMethods = true,
                RenameNamespaces = true,
                PreservePublicApi = false,
                PreserveXaml = true
            }
        };

        var context = PipelineContext.ForAssembly(module, settings);
        var result = await obfuscator.ObfuscateAsync(context);

        result.Success.ShouldBeTrue(result.ErrorMessage);
        context.Warnings.ShouldNotContain(SymbolRenamingObfuscator.NoXamlBindableTypes);

        outer.Name.String.ShouldBe("Outer");
        outer.Namespace.String.ShouldBe("App");
        inner.Name.String.ShouldBe("Inner");
        innerMethod.Name.String.ShouldBe("InnerMethod");

        outerMethod.Name.String.ShouldNotBe("OuterMethod");
        unrelatedNested.Name.String.ShouldNotBe("UnrelatedNested");
        plainModel.Name.String.ShouldNotBe("PlainModel");
        plainModel.Namespace.String.ShouldNotBe("App.Models");
    }

    [Fact]
    public async Task SymbolRenaming_FullNameExclusion_KeepsEnclosingTypeChainNameAndNamespace_ForNestedType()
    {
        var module = new ModuleDefUser("ExcludedNested.dll");
        var objRef = module.CorLibTypes.Object.TypeDefOrRef;

        var outer = AddType(module, "App", "Outer", objRef);
        var outerMethod = AddMethod(outer, "OuterMethod", module.CorLibTypes.Void);
        var mid = AddNestedType(outer, "Mid", objRef);
        var midMethod = AddMethod(mid, "MidMethod", module.CorLibTypes.Void);
        var inner = AddNestedType(mid, "Inner", objRef);
        var innerMethod = AddMethod(inner, "InnerMethod", module.CorLibTypes.Void);
        var plainModel = AddType(module, "App.Models", "PlainModel", objRef);

        var obfuscator = new SymbolRenamingObfuscator(
            new NameGenerator(),
            new Mock<ILogger<SymbolRenamingObfuscator>>().Object);

        var settings = new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                Mode = NamingMode.Sequential,
                RenameTypes = true,
                RenameMethods = true,
                RenameNamespaces = true,
                PreservePublicApi = false,
                PreserveXaml = false
            },
            Exclusions =
            {
                Types = ["App.Outer.Mid.Inner"]
            }
        };

        var context = PipelineContext.ForAssembly(module, settings);
        var result = await obfuscator.ObfuscateAsync(context);

        result.Success.ShouldBeTrue(result.ErrorMessage);

        outer.Name.String.ShouldBe("Outer");
        outer.Namespace.String.ShouldBe("App");
        mid.Name.String.ShouldBe("Mid");
        inner.Name.String.ShouldBe("Inner");
        innerMethod.Name.String.ShouldBe("InnerMethod");

        outerMethod.Name.String.ShouldNotBe("OuterMethod");
        midMethod.Name.String.ShouldNotBe("MidMethod");
        plainModel.Name.String.ShouldNotBe("PlainModel");
        plainModel.Namespace.String.ShouldNotBe("App.Models");
    }

    [Fact]
    public void SymbolRenaming_ClosedSet_KeepsEnclosingTypeNameAndNamespace_ForBamlReferencedNestedTypeInLibrary()
    {
        var libModule = new ModuleDefUser("WpfLib.dll");
        var libObj = libModule.CorLibTypes.Object.TypeDefOrRef;
        var outer = AddType(libModule, "WpfLib.Controls", "Outer", libObj);
        var outerMethod = AddMethod(outer, "OuterMethod", libModule.CorLibTypes.Void);
        var inner = AddNestedType(outer, "Inner", libObj);
        var innerMethod = AddMethod(inner, "InnerMethod", libModule.CorLibTypes.Void);
        var secret = AddType(libModule, "WpfLib.Internal", "SecretHelper", libObj);

        var appModule = new ModuleDefUser("WpfApp.dll");
        var appWindow = AddType(appModule, "WpfApp", "MainWindow", appModule.CorLibTypes.Object.TypeDefOrRef);
        appWindow.Interfaces.Add(new InterfaceImplUser(
            new TypeRefUser(appModule, "System.Windows.Markup", "IComponentConnector")));

        var bamlBytes = BuildBaml("WpfApp.MainWindow", "WpfLib.Controls.Outer+Inner");
        var resourcesBytes = BuildResources(w => w.AddResource("mainwindow.baml", new MemoryStream(bamlBytes)));
        appModule.Resources.Add(new EmbeddedResource("WpfApp.g.resources", resourcesBytes, ManifestResourceAttributes.Public));

        var libSettings = new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                Mode = NamingMode.Sequential,
                RenameTypes = true,
                RenameMethods = true,
                RenameNamespaces = true,
                PreservePublicApi = false,
                PreserveXaml = false
            }
        };
        var appSettings = new ObfySettings
        {
            SymbolRenaming =
            {
                Enabled = true,
                Mode = NamingMode.Sequential,
                RenameTypes = true,
                RenameMethods = true,
                RenameNamespaces = true,
                PreservePublicApi = false,
                PreserveXaml = true
            }
        };

        var obfuscator = new SymbolRenamingObfuscator(
            new NameGenerator(),
            new Mock<ILogger<SymbolRenamingObfuscator>>().Object);
        var sharedContext = PipelineContext.ForAssembly(appModule, appSettings);

        obfuscator.RenameClosedSet(
            [(libModule, libSettings), (appModule, appSettings)],
            sharedContext);

        sharedContext.Warnings.ShouldNotContain(SymbolRenamingObfuscator.ClosedSetNoXamlBindableTypes);
        outer.Name.String.ShouldBe("Outer");
        outer.Namespace.String.ShouldBe("WpfLib.Controls");
        inner.Name.String.ShouldBe("Inner");
        innerMethod.Name.String.ShouldBe("InnerMethod");
        outerMethod.Name.String.ShouldNotBe("OuterMethod");
        secret.Name.String.ShouldNotBe("SecretHelper");
        secret.Namespace.String.ShouldNotBe("WpfLib.Internal");
    }

    private static TypeDef AddType(ModuleDef module, string ns, string name, ITypeDefOrRef? baseType = null)
    {
        var type = new TypeDefUser(ns, name, baseType ?? module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(type);
        return type;
    }

    private static TypeDef AddNestedType(TypeDef declaringType, string name, ITypeDefOrRef? baseType = null)
    {
        var nested = new TypeDefUser(string.Empty, name, baseType ?? declaringType.Module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.NestedPublic | TypeAttributes.Class
        };
        declaringType.NestedTypes.Add(nested);
        return nested;
    }

    private static MethodDef AddMethod(TypeDef type, string name, TypeSig returnType)
    {
        var method = new MethodDefUser(
            name,
            MethodSig.CreateInstance(returnType),
            MethodImplAttributes.IL,
            MethodAttributes.Private | MethodAttributes.HideBySig);
        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;
        type.Methods.Add(method);
        return method;
    }

    private static byte[] BuildResources(Action<ResourceWriter> populate)
    {
        using var ms = new MemoryStream();
        using (var writer = new ResourceWriter(ms))
        {
            populate(writer);
            writer.Generate();
        }

        return ms.ToArray();
    }

    private static byte[] BuildBaml(params string[] typeInfoNames) =>
        BuildBaml(typeInfoNames, includeSerializerInfoType: null);

    private static byte[] BuildBaml(string[] typeInfoNames, string? includeSerializerInfoType)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            WriteBamlHeaderAndStart(writer);

            short typeId = 1;
            foreach (var fullName in typeInfoNames)
            {
                var currentId = typeId++;
                WriteVariableRecord(writer, 0x1D, payload =>
                {
                    payload.Write(currentId);
                    payload.Write((short)0);
                    payload.Write(fullName);
                });
            }

            if (!string.IsNullOrEmpty(includeSerializerInfoType))
            {
                var currentId = typeId++;
                WriteVariableRecord(writer, 0x1E, payload =>
                {
                    payload.Write(currentId);
                    payload.Write((short)0);
                    payload.Write(includeSerializerInfoType);
                    payload.Write((short)137);
                });
            }

            // ElementStart (0x03) + ElementEnd (0x04) + DocumentEnd (0x02)
            writer.Write((byte)0x03);
            writer.Write((short)1);
            writer.Write((byte)0);
            writer.Write((byte)0x04);
            writer.Write((byte)0x02);
        }

        return ms.ToArray();
    }

    private static byte[] BuildBamlWithTrailingGarbage(string typeName)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            WriteBamlHeaderAndStart(writer);
            WriteVariableRecord(writer, 0x1D, payload =>
            {
                payload.Write((short)1);
                payload.Write((short)0);
                payload.Write(typeName);
            });
            writer.Write((byte)0x1A); // Unimplemented/unknown EndAttributes record
            writer.Write(new byte[] { 0xFF, 0xFF, 0xFF });
        }

        return ms.ToArray();
    }

    private static void WriteBamlHeaderAndStart(BinaryWriter writer)
    {
        var sigBytes = Encoding.Unicode.GetBytes("MSBAML");
        writer.Write(sigBytes.Length);
        writer.Write(sigBytes);
        for (var i = 0; i < 3; i++)
        {
            writer.Write((short)0);
            writer.Write((short)96);
        }

        // DocumentStart (0x01): bool + int32 + bool (6 bytes)
        writer.Write((byte)0x01);
        writer.Write(false);
        writer.Write(-1);
        writer.Write(false);

        // AssemblyInfo (0x1C)
        WriteVariableRecord(writer, 0x1C, payload =>
        {
            payload.Write((short)0);
            payload.Write("WpfSample, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
        });
    }

    private static void WriteVariableRecord(BinaryWriter writer, byte recordType, Action<BinaryWriter> writePayload)
    {
        using var payloadStream = new MemoryStream();
        using (var payloadWriter = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
            writePayload(payloadWriter);

        var payload = payloadStream.ToArray();
        var initialSizeFieldLen = SizeOf7BitEncodedInt(payload.Length);
        var totalSize = SizeOf7BitEncodedInt(initialSizeFieldLen + payload.Length) + payload.Length;

        writer.Write(recordType);
        writer.Write7BitEncodedInt(totalSize);
        writer.Write(payload);
    }

    private static int SizeOf7BitEncodedInt(int value)
    {
        var count = 1;
        var v = (uint)value;
        while (v >= 0x80)
        {
            v >>= 7;
            count++;
        }

        return count;
    }
}

using dnlib.DotNet;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class ObfuscatorHelpersTests
{
    [Theory]
    [InlineData("System.SerializableAttribute", "SerializableAttribute")]
    [InlineData("System.SerializableAttribute", "Serializable")]
    [InlineData("System.SerializableAttribute", "System.SerializableAttribute")]
    [InlineData("System.Runtime.Serialization.DataContractAttribute", "DataContractAttribute")]
    public void MatchesAttribute_AcceptsShortAndFullNames(string fullName, string pattern)
    {
        ObfuscatorHelpers.MatchesAttribute(fullName, pattern).ShouldBeTrue();
    }

    [Fact]
    public void MatchesAttribute_DoesNotMatchUnrelated()
    {
        ObfuscatorHelpers.MatchesAttribute("System.ObsoleteAttribute", "SerializableAttribute")
            .ShouldBeFalse();
    }

    [Fact]
    public void IsPinnedAttributeType_MatchesWatermarkInObfyRuntimeOnly()
    {
        var runtime = new TypeDefUser(
            ObfuscatorHelpers.PinnedAttributeNames.WatermarkNamespace,
            ObfuscatorHelpers.PinnedAttributeNames.Watermark,
            new TypeRefUser(new ModuleDefUser("t"), "System", "Attribute"));
        var otherNs = new TypeDefUser(
            "MyApp",
            ObfuscatorHelpers.PinnedAttributeNames.Watermark,
            new TypeRefUser(new ModuleDefUser("t"), "System", "Attribute"));

        ObfuscatorHelpers.IsPinnedAttributeType(runtime).ShouldBeTrue();
        ObfuscatorHelpers.IsPinnedAttributeType(otherNs).ShouldBeFalse();
    }

    [Fact]
    public void IsExcluded_DoesNotSkipObfyCoreModelsNamespace()
    {
        var module = new ModuleDefUser("t");
        var type = new TypeDefUser("Obfy.Core.Models", "UserType", module.CorLibTypes.Object.TypeDefOrRef);
        ObfuscatorHelpers.IsExcluded(type, new Obfy.Core.Models.ExclusionRules()).ShouldBeFalse();
        ObfuscatorHelpers.IsRuntimeOrExcluded(type, new Obfy.Core.Models.ExclusionRules()).ShouldBeFalse();
    }

    [Fact]
    public void LooksLikeXamlBindable_ViewModelSuffix_IsTrue()
    {
        ObfuscatorHelpers.LooksLikeXamlBindable(NewType("MainViewModel")).ShouldBeTrue();
        ObfuscatorHelpers.LooksLikeXamlBindable(NewType("mainviewmodel")).ShouldBeTrue();
    }

    [Fact]
    public void LooksLikeXamlBindable_ViewSuffix_IsTrueIncludingOverview()
    {
        ObfuscatorHelpers.LooksLikeXamlBindable(NewType("DetailsView")).ShouldBeTrue();
        ObfuscatorHelpers.LooksLikeXamlBindable(NewType("BOARDVIEW")).ShouldBeTrue();
        ObfuscatorHelpers.LooksLikeXamlBindable(NewType("Overview")).ShouldBeTrue();
        ObfuscatorHelpers.LooksLikeXamlBindable(NewType("Preview")).ShouldBeTrue();
    }

    [Fact]
    public void LooksLikeXamlBindable_INotifyPropertyChanged_IsTrue()
    {
        var module = new ModuleDefUser("t");
        var type = new TypeDefUser("App", "BoardState", module.CorLibTypes.Object.TypeDefOrRef);
        type.Interfaces.Add(new InterfaceImplUser(
            new TypeRefUser(module, "System.ComponentModel", "INotifyPropertyChanged")));
        ObfuscatorHelpers.LooksLikeXamlBindable(type).ShouldBeTrue();
    }

    [Fact]
    public void LooksLikeXamlBindable_DependencyPropertyField_IsTrue()
    {
        var module = new ModuleDefUser("t");
        var type = new TypeDefUser("App", "TitleBox", module.CorLibTypes.Object.TypeDefOrRef);
        var dp = new TypeRefUser(module, "System.Windows", "DependencyProperty");
        type.Fields.Add(new FieldDefUser("TitleProperty", new FieldSig(new ClassSig(dp))));
        ObfuscatorHelpers.LooksLikeXamlBindable(type).ShouldBeTrue();
    }

    [Fact]
    public void LooksLikeXamlBindable_ResolvableDependencyObjectBase_IsTrue()
    {
        var module = new ModuleDefUser("t");
        var depObj = new TypeDefUser("System.Windows", "DependencyObject", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(depObj);
        var type = new TypeDefUser("App", "Shell", depObj);
        ObfuscatorHelpers.LooksLikeXamlBindable(type).ShouldBeTrue();
    }

    [Fact]
    public void LooksLikeXamlBindable_PlainPublicDto_IsFalse()
    {
        ObfuscatorHelpers.LooksLikeXamlBindable(NewType("CustomerDto")).ShouldBeFalse();
        ObfuscatorHelpers.LooksLikeXamlBindable(NewType("MainWindow")).ShouldBeFalse();
    }

    private static TypeDef NewType(string name)
    {
        var module = new ModuleDefUser("t");
        return new TypeDefUser("App", name, module.CorLibTypes.Object.TypeDefOrRef);
    }

    [Fact]
    public void IsPinnedAttributeType_MatchesDecoysInGlobalNamespaceOnly()
    {
        var confused = new TypeDefUser(
            "",
            ObfuscatorHelpers.PinnedAttributeNames.ConfusedBy,
            new TypeRefUser(new ModuleDefUser("t"), "System", "Attribute"));
        var namespaced = new TypeDefUser(
            "Other",
            ObfuscatorHelpers.PinnedAttributeNames.ConfusedBy,
            new TypeRefUser(new ModuleDefUser("t"), "System", "Attribute"));
        var dotfuscator = new TypeDefUser(
            "",
            ObfuscatorHelpers.PinnedAttributeNames.Dotfuscator,
            new TypeRefUser(new ModuleDefUser("t"), "System", "Attribute"));

        ObfuscatorHelpers.IsPinnedAttributeType(confused).ShouldBeTrue();
        ObfuscatorHelpers.IsPinnedAttributeType(dotfuscator).ShouldBeTrue();
        ObfuscatorHelpers.IsPinnedAttributeType(namespaced).ShouldBeFalse();
    }
}

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

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
}

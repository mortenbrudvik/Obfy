using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class WildcardMatcherTests
{
    [Theory]
    [InlineData("FooXBar", "Foo?Bar", true)]
    [InlineData("Keep.Resources", "*.resources", true)]
    [InlineData("keep.RESOURCES", "*.resources", true)]
    [InlineData("System.Foo", "System.*", true)]
    [InlineData("Other.Foo", "System.*", false)]
    [InlineData("FooBar", "Foo?Bar", false)]
    public void IsMatch_SupportsGlobAndIsCaseInsensitive(string value, string pattern, bool expected)
    {
        WildcardMatcher.IsMatch(value, pattern).ShouldBe(expected);
    }

    [Fact]
    public void IsMatch_NullOrEmptyValueNeverMatches()
    {
        WildcardMatcher.IsMatch(null, "*").ShouldBeFalse();
        WildcardMatcher.IsMatch("", "*").ShouldBeFalse();
        WildcardMatcher.IsMatch("", "").ShouldBeFalse();
    }

    [Fact]
    public void IsMatch_NullPatternThrows()
    {
        Should.Throw<ArgumentNullException>(() => WildcardMatcher.IsMatch("x", null!));
    }
}

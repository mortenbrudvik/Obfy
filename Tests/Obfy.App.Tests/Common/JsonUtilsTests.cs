using Obfy.Common;
using Shouldly;

namespace Obfy.App.Tests.Common;

public class JsonUtilsTests
{
    private enum Mode
    {
        Fast,
        Slow
    }

    private sealed class Sample
    {
        public string FileName { get; set; } = "";
    }

    private sealed class EnumHolder
    {
        public Mode Mode { get; set; }
    }

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var json = JsonUtils.Serialize(new Sample { FileName = "x" });

        json.ShouldContain("\"fileName\"");
    }

    [Fact]
    public void Deserialize_IsCaseInsensitive()
    {
        var result = JsonUtils.Deserialize<Sample>("{\"FileName\":\"x\"}");

        result.ShouldNotBeNull();
        result.FileName.ShouldBe("x");
    }

    [Fact]
    public void Serialize_EnumAsString()
    {
        var json = JsonUtils.Serialize(new EnumHolder { Mode = Mode.Slow });

        json.ShouldContain("\"Slow\"");
        json.ShouldNotContain(":1");
    }
}

using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Tests;

public class SymbolMapKeyTests
{
    [Fact]
    public void ParseAll_NestsMembersUnderTheLongestTypeName()
    {
        var keys = new[]
        {
            "Type:Ns.MyType",
            "Method:Ns.MyType.Foo(System.Void ())",
            "Method:Ns.MyType.IFace.M(System.Void (System.Int32))",
            "Field:Ns.MyType._value",
            "Property:Ns.MyType.Name",
            "Event:Ns.MyType.Changed",
            "Namespace:Ns",
            "Parameter:index",
            "Field:Settings.Core.SettingsServiceBase`1._filePath",
            "Type:Settings.Core.SettingsServiceBase`1"
        };

        var parsed = SymbolMapKey.ParseAll(keys).ToDictionary(entry => entry.Key);

        parsed["Method:Ns.MyType.Foo(System.Void ())"].TypeFullName.ShouldBe("Ns.MyType");
        parsed["Method:Ns.MyType.Foo(System.Void ())"].DisplayName.ShouldBe("Foo(System.Void ())");
        parsed["Method:Ns.MyType.IFace.M(System.Void (System.Int32))"].DisplayName
            .ShouldBe("IFace.M(System.Void (System.Int32))");
        parsed["Field:Ns.MyType._value"].DisplayName.ShouldBe("_value");
        parsed["Property:Ns.MyType.Name"].Kind.ShouldBe(SymbolMapKey.PropertyKind);
        parsed["Event:Ns.MyType.Changed"].Kind.ShouldBe(SymbolMapKey.EventKind);
        parsed["Namespace:Ns"].TypeFullName.ShouldBeNull();
        parsed["Namespace:Ns"].DisplayName.ShouldBe("Ns");
        parsed["Parameter:index"].Kind.ShouldBe(SymbolMapKey.ParameterKind);
        parsed["Field:Settings.Core.SettingsServiceBase`1._filePath"].TypeFullName
            .ShouldBe("Settings.Core.SettingsServiceBase`1");
        parsed["Field:Settings.Core.SettingsServiceBase`1._filePath"].DisplayName.ShouldBe("_filePath");
    }

    [Fact]
    public void Parse_WithoutTypeEntry_SplitsOnTheLastDotBeforeTheSignature()
    {
        var entry = SymbolMapKey.Parse(
            "Method:Ns.Only.Bar(System.Void ())",
            Array.Empty<string>());

        entry.TypeFullName.ShouldBe("Ns.Only");
        entry.DisplayName.ShouldBe("Bar(System.Void ())");
    }

    [Fact]
    public void KindOf_ReadsThePrefix()
    {
        SymbolMapKey.KindOf("Method:Ns.T.Foo()").ShouldBe("Method");
        SymbolMapKey.KindOf("NoColon").ShouldBe(string.Empty);
    }
}

using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Shouldly;

namespace Obfy.Tests.Virtualization;

/// <summary>
/// Release-optimized C# emits the fused compare-and-branch opcodes (<c>ble.un</c>, <c>bge.un</c>,
/// plain <c>ble</c>/<c>bge</c>) that Debug IL avoids. Their NaN behavior is the contract: ordered
/// branches are not taken on NaN, the <c>.un</c> forms are.
/// </summary>
public class VmFloatBranchTests
{
    [Theory]
    [InlineData("double")]
    [InlineData("float")]
    public void BleUn_TakenWhenUnordered(string t)
    {
        // if (v > max) return max; return v;  =>  ble.un over the clamp. NaN must skip the clamp.
        var source = $$"""
            public static class Lib {
                public static {{t}} Clamp({{t}} v, {{t}} max) { if (v > max) return max; return v; }
            }
            """;
        AssertUsesOpcode(source, "Clamp", Code.Ble_Un, Code.Ble_Un_S);
        var results = Run(source, "Clamp",
            Args(t, double.NaN, 3), Args(t, 5, 3), Args(t, 1, 3), Args(t, 3, double.NaN), Args(t, 3, 3));
        IsNaN(results[0]).ShouldBeTrue("NaN > 3 is false, so the original returns v (NaN)");
        results[1].ShouldBe(Cast(t, 3));
        results[2].ShouldBe(Cast(t, 1));
        results[3].ShouldBe(Cast(t, 3));
        results[4].ShouldBe(Cast(t, 3));
    }

    [Theory]
    [InlineData("double")]
    [InlineData("float")]
    public void BgeUn_TakenWhenUnordered(string t)
    {
        // if (v < min) return min; return v;  =>  bge.un over the raise. NaN must skip it.
        var source = $$"""
            public static class Lib {
                public static {{t}} Raise({{t}} v, {{t}} min) { if (v < min) return min; return v; }
            }
            """;
        AssertUsesOpcode(source, "Raise", Code.Bge_Un, Code.Bge_Un_S);
        var results = Run(source, "Raise",
            Args(t, double.NaN, 3), Args(t, 1, 3), Args(t, 5, 3), Args(t, 3, double.NaN), Args(t, 3, 3));
        IsNaN(results[0]).ShouldBeTrue("NaN < 3 is false, so the original returns v (NaN)");
        results[1].ShouldBe(Cast(t, 3));
        results[2].ShouldBe(Cast(t, 5));
        results[3].ShouldBe(Cast(t, 3));
        results[4].ShouldBe(Cast(t, 3));
    }

    [Theory]
    [InlineData("double")]
    [InlineData("float")]
    public void Ble_NotTakenWhenUnordered(string t)
    {
        // if (!(a <= b)) return 1; return 0;  =>  plain ble over "return 1". NaN is not <=, so 1.
        var source = $$"""
            public static class Lib {
                public static int NotLe({{t}} a, {{t}} b) { if (!(a <= b)) return 1; return 0; }
            }
            """;
        AssertUsesOpcode(source, "NotLe", Code.Ble, Code.Ble_S);
        var results = Run(source, "NotLe",
            Args(t, double.NaN, 1), Args(t, 1, double.NaN), Args(t, 1, 2), Args(t, 2, 1), Args(t, 1, 1));
        results.ShouldBe(new object?[] { 1, 1, 0, 1, 0 });
    }

    [Theory]
    [InlineData("double")]
    [InlineData("float")]
    public void Bge_NotTakenWhenUnordered(string t)
    {
        // if (!(a >= b)) return 1; return 0;  =>  plain bge over "return 1". NaN is not >=, so 1.
        var source = $$"""
            public static class Lib {
                public static int NotGe({{t}} a, {{t}} b) { if (!(a >= b)) return 1; return 0; }
            }
            """;
        AssertUsesOpcode(source, "NotGe", Code.Bge, Code.Bge_S);
        var results = Run(source, "NotGe",
            Args(t, double.NaN, 1), Args(t, 1, double.NaN), Args(t, 2, 1), Args(t, 1, 2), Args(t, 1, 1));
        results.ShouldBe(new object?[] { 1, 1, 0, 1, 0 });
    }

    [Fact]
    public void FusedBranches_StillCorrectForIntegers()
    {
        const string source = """
            public static class Lib {
                public static int NotLe(int a, int b) { if (!(a <= b)) return 1; return 0; }
                public static int NotGe(int a, int b) { if (!(a >= b)) return 1; return 0; }
                public static int NotLeUn(uint a, uint b) { if (!(a <= b)) return 1; return 0; }
                public static int NotGeUn(uint a, uint b) { if (!(a >= b)) return 1; return 0; }
            }
            """;
        Run(source, "NotLe", new object?[] { 1, 2 }, new object?[] { 2, 1 }, new object?[] { 2, 2 })
            .ShouldBe(new object?[] { 0, 1, 0 });
        Run(source, "NotGe", new object?[] { 1, 2 }, new object?[] { 2, 1 }, new object?[] { 2, 2 })
            .ShouldBe(new object?[] { 1, 0, 0 });
        Run(source, "NotLeUn", new object?[] { 1u, 2u }, new object?[] { uint.MaxValue, 1u }, new object?[] { 2u, 2u })
            .ShouldBe(new object?[] { 0, 1, 0 });
        Run(source, "NotGeUn", new object?[] { 1u, 2u }, new object?[] { uint.MaxValue, 1u }, new object?[] { 2u, 2u })
            .ShouldBe(new object?[] { 1, 0, 0 });
    }

    private static object?[] Run(string source, string method, params object?[][] argSets)
    {
        var vm = VmFixHarness.RunMany(source, OptimizationLevel.Release, "Lib", method, argSets);
        var native = VmFixHarness.RunNative(source, OptimizationLevel.Release, "Lib", method, argSets);
        for (var i = 0; i < vm.Length; i++)
        {
            // NaN != NaN, so compare through the bit pattern of the result.
            Describe(vm[i]).ShouldBe(Describe(native[i]), $"argument set {i} must match the CLR");
        }

        return vm;
    }

    private static string Describe(object? v) => v switch
    {
        double d => "d:" + BitConverter.DoubleToInt64Bits(double.IsNaN(d) ? double.NaN : d),
        float f => "f:" + BitConverter.SingleToInt32Bits(float.IsNaN(f) ? float.NaN : f),
        _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "null"
    };

    private static void AssertUsesOpcode(string source, string method, params Code[] codes)
    {
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        VmFixHarness.ContainsCode(VmFixHarness.Find(module, "Lib", method), codes)
            .ShouldBeTrue($"the Release IL of {method} should use {string.Join("/", codes)}");
    }

    private static object?[] Args(string t, double a, double b) => new[] { Cast(t, a), Cast(t, b) };

    private static object Cast(string t, double v)
    {
        // A conditional expression would unify both arms to double.
        if (t == "double")
            return v;
        return (float)v;
    }

    private static bool IsNaN(object? v) => v switch
    {
        double d => double.IsNaN(d),
        float f => float.IsNaN(f),
        _ => false
    };
}

using System.Reflection;
using Microsoft.CodeAnalysis;
using Shouldly;

namespace Obfy.Tests.Virtualization;

/// <summary>
/// Places where the VM interpreter must match the CLR: each probe runs once on the original
/// (the oracle) and once virtualized, and the outcomes (value or exception type) must agree.
/// </summary>
public class VmRuntimeSemanticsTests
{
    [Fact]
    public void UnboxAny_EnumAndUnderlyingType_AreInterchangeable()
    {
        const string source = """
            public enum Color { Red = 1, Blue = 2 }
            public enum Wide : long { Big = 5000000000 }
            public static class Lib {
                public static int AsInt(object o) => (int)o;
                public static object AsColor(object o) => (Color)o;
                public static long AsLong(object o) => (long)o;
            }
            """;
        string[] Probe(Assembly a)
        {
            var lib = VmExecuteHarness.GetType(a, "Lib");
            var color = VmExecuteHarness.GetType(a, "Color");
            var wide = VmExecuteHarness.GetType(a, "Wide");
            return new[]
            {
                Outcome(() => lib.GetMethod("AsInt")!.Invoke(null, new[] { Enum.ToObject(color, 2) })),
                Outcome(() => lib.GetMethod("AsInt")!.Invoke(null, new object[] { 5 })),
                Outcome(() => lib.GetMethod("AsInt")!.Invoke(null, new object[] { 5L })),
                Outcome(() => lib.GetMethod("AsInt")!.Invoke(null, new object[] { "s" })),
                Outcome(() => lib.GetMethod("AsColor")!.Invoke(null, new object[] { 2 })),
                Outcome(() => lib.GetMethod("AsColor")!.Invoke(null, new[] { Enum.ToObject(color, 1) })),
                Outcome(() => lib.GetMethod("AsColor")!.Invoke(null, new object[] { 2u })),
                Outcome(() => lib.GetMethod("AsColor")!.Invoke(null, new object?[] { null })),
                Outcome(() => lib.GetMethod("AsLong")!.Invoke(null, new[] { Enum.ToObject(wide, 5000000000) })),
            };
        }

        var (native, vm) = CompareOutcomes(source, new[] { "AsInt", "AsColor", "AsLong" }, Probe);
        vm.ShouldBe(native);
        native[0].ShouldBe("Int32:2", "the oracle itself allows (int)(object)enumValue");
        native[4].ShouldContain("Color");
    }

    [Fact]
    public void ThrowNull_ThrowsNullReferenceException()
    {
        const string source = "public static class Lib { public static int Boom() { throw null; } }";
        string[] Probe(Assembly a) => new[]
        {
            Outcome(() => VmExecuteHarness.GetType(a, "Lib").GetMethod("Boom")!.Invoke(null, null))
        };

        var (native, vm) = CompareOutcomes(source, new[] { "Boom" }, Probe);
        native[0].ShouldBe("NullReferenceException");
        vm.ShouldBe(native);
    }

    [Fact]
    public void RunawayRecursion_IsStoppedByTheDepthLimit()
    {
        const string source = """
            public static class Lib {
                public static int Down(int n) { if (n == 0) return 0; return Down(n - 1) + 1; }
            }
            """;
        using var module = VmFixHarness.Compile(source, OptimizationLevel.Release);
        var outcomes = (string[])VmFixHarness.VirtualizeAndRun(module, "Lib", new[] { "Down" }, a =>
        {
            var vmType = VmExecuteHarness.GetType(a, "Obfy.Runtime.Vm");
            var limit = vmType.GetField("MaxCallDepth", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException("Vm.MaxCallDepth was not found.");
            ((int)limit.GetValue(null)!).ShouldBeGreaterThan(1000, "the default must leave real recursion alone");
            limit.SetValue(null, 50);
            var down = VmExecuteHarness.GetType(a, "Lib").GetMethod("Down")!;
            return new[]
            {
                Outcome(() => down.Invoke(null, new object[] { 40 })),
                Outcome(() => down.Invoke(null, new object[] { 100000 }))
            };
        })!;

        outcomes[0].ShouldBe("Int32:40");
        outcomes[1].ShouldBe("InsufficientExecutionStackException");
    }

    private static (string[] Native, string[] Vm) CompareOutcomes(
        string source, string[] methods, Func<Assembly, string[]> probe)
    {
        using var nativeModule = VmFixHarness.Compile(source, OptimizationLevel.Release);
        var native = (string[])VmExecuteHarness.Invoke(nativeModule, a => probe(a))!;
        using var vmModule = VmFixHarness.Compile(source, OptimizationLevel.Release);
        var vm = (string[])VmFixHarness.VirtualizeAndRun(vmModule, "Lib", methods, a => probe(a))!;
        return (native, vm);
    }

    private static string Outcome(Func<object?> action)
    {
        try
        {
            var result = action();
            return result is null ? "null" : result.GetType().Name + ":" + result;
        }
        catch (TargetInvocationException ex)
        {
            return ex.InnerException!.GetType().Name;
        }
    }
}

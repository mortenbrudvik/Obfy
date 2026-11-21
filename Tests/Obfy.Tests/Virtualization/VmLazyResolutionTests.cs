using System.Reflection;
using Shouldly;

namespace Obfy.Tests.Virtualization;

/// <summary>
/// The VM resolves the members its bytecode names. One that cannot be resolved at run time (an
/// optional dependency that is not installed, an API a older runtime lacks) must fail only the
/// methods that use it, exactly as the original code would, not every virtualized method.
/// </summary>
public class VmLazyResolutionTests
{
    private const string Dependency = "namespace Dep { public static class Opt { public static int Get() => 41; } }";

    private const string Main = """
        public static class Lib {
            public static int Safe(int a, int b) => a + b;
            public static int UseOpt() => Dep.Opt.Get() + 1;
        }
        """;

    [Fact]
    public void UnresolvableDependency_OnlyFailsTheMethodThatUsesIt()
    {
        string[] Probe(Assembly a)
        {
            var lib = VmExecuteHarness.GetType(a, "Lib");
            return new[]
            {
                Outcome(() => lib.GetMethod("Safe")!.Invoke(null, new object[] { 2, 3 })),
                Outcome(() => lib.GetMethod("UseOpt")!.Invoke(null, null)),
            };
        }

        using var nativeModule = VmFixHarness.CompileWithMissingDependency(Dependency, Main);
        var native = (string[])VmExecuteHarness.Invoke(nativeModule, Probe)!;
        using var vmModule = VmFixHarness.CompileWithMissingDependency(Dependency, Main);
        var vm = (string[])VmFixHarness.VirtualizeAndRun(vmModule, "Lib", new[] { "Safe", "UseOpt" }, Probe)!;

        native[0].ShouldBe("Int32:5");
        native[1].ShouldBe("FileNotFoundException", "the oracle: a missing assembly fails only the caller");
        vm.ShouldBe(native);
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

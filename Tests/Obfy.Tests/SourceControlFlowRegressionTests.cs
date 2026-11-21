using Autofac;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Source;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Shouldly;
using static Obfy.Tests.SourceRvTestSupport;

namespace Obfy.Tests;

/// <summary>
/// Regression tests for the Roslyn control-flow obfuscator: randomness must never change what the
/// obfuscated program does or make the obfuscator itself hang.
/// </summary>
public class SourceControlFlowRegressionTests
{
    private static async Task<(PipelineContext Context, ObfuscationResult Result)> RunAsync(
        string source, ControlFlowMode mode, int intensity = 100, Func<CSharpCompilation, CSharpCompilation>? shape = null)
    {
        var compilation = CreateCompilation(source);
        if (shape != null)
            compilation = shape(compilation);

        var settings = new ObfySettings { ControlFlow = { Enabled = true, Mode = mode, Intensity = intensity } };
        var context = PipelineContext.ForSourceCode(compilation, settings);
        var result = await new SourceControlFlowObfuscator(NullLogger<SourceControlFlowObfuscator>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        return (context, result);
    }

    [Fact]
    public async Task Switch_MethodWithThousandStatements_FinishesAndStaysCorrect()
    {
        // 899 distinct states used to be the ceiling, so >= 900 statements looped forever.
        var body = string.Join("\n", Enumerable.Repeat("        Total += 1;", 1000));
        var source = $$"""
            public class Test
            {
                public int Total;
                public int Big()
                {
            {{body}}
                    return Total;
                }
            }
            """;

        var run = Task.Run(() => RunAsync(source, ControlFlowMode.Switch));
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20)));
        finished.ShouldBeSameAs(run, "the obfuscator did not finish (state numbers ran out)");

        var (context, result) = await run;
        result.Statistics.MethodsControlFlowObfuscated.ShouldBe(1);
        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));
        Invoke(context.Compilation!, "Test", "Big").ShouldBe(1000);
    }

    [Theory]
    [InlineData(ControlFlowMode.Switch)]
    [InlineData(ControlFlowMode.Combined)]
    public async Task MethodWithOutParameters_StillCompilesAndRuns(ControlFlowMode mode)
    {
        const string source = """
            public class Test
            {
                public int Total;
                public void Split(int v, out int a, out int b)
                {
                    a = v;
                    b = v * 2;
                    Total += 1;
                }
                public string Run(int v)
                {
                    int a;
                    int b;
                    Split(v, out a, out b);
                    return a + ":" + b;
                }
            }
            """;

        var (context, _) = await RunAsync(source, mode);

        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));
        Invoke(context.Compilation!, "Test", "Run", 3).ShouldBe("3:6");
    }

    [Fact]
    public async Task OpaquePredicate_ReadsTheClockOncePerPredicate()
    {
        // Two reads can straddle a tick: n * (n + 1) is then odd and the else branch throws at run time.
        const string source = """
            public class Test
            {
                public int Calculate(int x)
                {
                    x = x + 1;
                    x = x * 2;
                    x = x - 3;
                    x = x + 4;
                    return x;
                }
            }
            """;

        var (context, result) = await RunAsync(source, ControlFlowMode.OpaquePredicate);
        result.Statistics.MethodsControlFlowObfuscated.ShouldBe(1);

        var text = SourceOf(context);
        var predicates = text.Split("InvalidOperationException").Length - 1;
        predicates.ShouldBeGreaterThan(0);
        (text.Split("TickCount").Length - 1).ShouldBe(predicates, "each predicate must read TickCount exactly once");
        text.ShouldContain("global::System.Environment");
        text.ShouldContain("global::System.InvalidOperationException");
    }

    [Theory]
    [InlineData(ControlFlowMode.OpaquePredicate)]
    [InlineData(ControlFlowMode.Combined)]
    public async Task OpaquePredicate_DoesNotOverflowInACheckedProject(ControlFlowMode mode)
    {
        // TickCount * (TickCount + 1) overflows int for any machine up more than ~46 seconds.
        const string source = """
            public class Test
            {
                public int Calculate(int x)
                {
                    x = x + 1;
                    x = x * 2;
                    x = x - 3;
                    x = x + 4;
                    return x;
                }
            }
            """;

        var (context, _) = await RunAsync(
            source,
            mode,
            shape: c => c.WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: true)));

        context.Compilation!.Options.CheckOverflow.ShouldBeTrue();
        for (var i = 0; i < 20; i++)
            Invoke(context.Compilation!, "Test", "Calculate", 5).ShouldBe(((5 + 1) * 2 - 3) + 4);
    }

    private static async Task<string> WriteThroughServiceAsync(string source, ControlFlowMode mode)
    {
        var dir = NewTempDirectory();
        try
        {
            var input = Path.Combine(dir, "calc.cs");
            var output = Path.Combine(dir, "calc.obfuscated.cs");
            File.WriteAllText(input, source);
            var settings = OnlySettings(controlFlow: true);
            settings.ControlFlow.Mode = mode;

            await using var container = BuildContainer();
            var result = await container.Resolve<IObfuscationService>().ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            return File.ReadAllText(output);
        }
        finally
        {
            DeleteQuietly(dir);
        }
    }

    [Theory]
    [InlineData(ControlFlowMode.Switch)]
    [InlineData(ControlFlowMode.OpaquePredicate)]
    [InlineData(ControlFlowMode.Combined)]
    public async Task WrittenOutput_IsValidSourceAndStillRuns(ControlFlowMode mode)
    {
        // The nodes Obfy builds have no whitespace of their own; written to a file they used to print
        // as "int__state_1=5;" and "thrownew".
        const string source = """
            public class Calc
            {
                public int Run(int a, int b)
                {
                    a = a + 1;
                    b = b + 2;
                    a = a * b;
                    return a;
                }
            }
            """;

        var text = await WriteThroughServiceAsync(source, mode);

        var compilation = CreateCompilation(text);
        GetErrors(compilation).ShouldBeEmpty(ErrorText(GetErrors(compilation)));
        Invoke(compilation, "Calc", "Run", 2, 3).ShouldBe(15);
    }

    [Theory]
    [InlineData(ControlFlowMode.Switch)]
    [InlineData(ControlFlowMode.Combined)]
    public async Task WrittenOutput_KeepsCommentsAndConditionalCode(ControlFlowMode mode)
    {
        const string source = """
            public class Calc
            {
                public int Run(int a)
                {
                    a = a + 1; // first step
                    // before the conditional step
            #if DEBUG
                    a = a + 100;
            #endif
                    a = a * 2;
                    return a;
                }
            }
            """;

        var text = await WriteThroughServiceAsync(source, mode);

        text.ShouldContain("// first step");
        text.ShouldContain("// before the conditional step");
        text.ShouldContain("#if DEBUG");
        text.ShouldContain("#endif");

        var plain = CreateCompilation(text);
        GetErrors(plain).ShouldBeEmpty(ErrorText(GetErrors(plain)));
        Invoke(plain, "Calc", "Run", 5).ShouldBe(12);

        var debugTree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(preprocessorSymbols: ["DEBUG"]));
        var debug = plain.RemoveAllSyntaxTrees().AddSyntaxTrees(debugTree);
        GetErrors(debug).ShouldBeEmpty(ErrorText(GetErrors(debug)));
        Invoke(debug, "Calc", "Run", 5).ShouldBe(212);
    }

    [Fact]
    public async Task IntensityZero_NeverTransformsAMethod()
    {
        // A ~1% chance per method (Next(100) > 0 was false for 0) flattened methods at intensity 0.
        var methods = string.Join("\n", Enumerable.Range(0, 3000).Select(i => $$"""
                public int M{{i}}(int x)
                {
                    x = x + 1;
                    x = x * 2;
                    x = x - 3;
                    return x;
                }
            """));
        var source = $$"""
            public class Test
            {
            {{methods}}
            }
            """;

        foreach (var mode in new[] { ControlFlowMode.Switch, ControlFlowMode.OpaquePredicate, ControlFlowMode.Combined })
        {
            var (_, result) = await RunAsync(source, mode, intensity: 0);
            result.Statistics.MethodsControlFlowObfuscated.ShouldBe(0, $"mode {mode}");
        }
    }

    [Fact]
    public async Task IntensityHundred_TransformsEveryEligibleMethod()
    {
        var methods = string.Join("\n", Enumerable.Range(0, 300).Select(i => $$"""
                public int M{{i}}(int x)
                {
                    x = x + 1;
                    x = x * 2;
                    x = x - 3;
                    return x;
                }
            """));
        var source = $$"""
            public class Test
            {
            {{methods}}
            }
            """;

        var (_, result) = await RunAsync(source, ControlFlowMode.Switch, intensity: 100);

        result.Statistics.MethodsControlFlowObfuscated.ShouldBe(300);
    }

    [Fact]
    public async Task GeneratedCode_UsesGlobalQualifiedFrameworkNames_SoUserNamespacesCannotCaptureThem()
    {
        // A user namespace called System (or a type called System) used to break "System.Environment".
        const string source = """
            namespace MyApp.System
            {
                public class Holder { }
            }
            namespace MyApp
            {
                public class Test
                {
                    public int Calculate(int x)
                    {
                        x = x + 1;
                        x = x * 2;
                        x = x - 3;
                        x = x + 4;
                        return x;
                    }
                }
            }
            """;

        foreach (var mode in new[] { ControlFlowMode.OpaquePredicate, ControlFlowMode.Switch, ControlFlowMode.Combined })
        {
            var (context, _) = await RunAsync(source, mode);
            GetErrors(context.Compilation!).ShouldBeEmpty($"mode {mode}: " + ErrorText(GetErrors(context.Compilation!)));
        }
    }
}

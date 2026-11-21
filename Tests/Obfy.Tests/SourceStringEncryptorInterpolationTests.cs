using Shouldly;
using static Obfy.Tests.SourceRvTestSupport;

namespace Obfy.Tests;

/// <summary>
/// Regression tests: encrypting the text parts of an interpolated string must not change how the
/// holes are evaluated or formatted (numeric addition, DateTime arithmetic, char-to-int, format
/// specifiers, alignment, FormattableString targets).
/// </summary>
public class SourceStringEncryptorInterpolationTests
{
    private const string Prefix = """
        using System;
        using System.Globalization;
        public class Test
        {
        """;

    private static async Task<(Microsoft.CodeAnalysis.CSharp.CSharpCompilation Compilation, string Source)> RunAsync(string members)
    {
        var context = await EncryptStringsAsync(Prefix + "\n" + members + "\n}");
        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));
        return (context.Compilation!, SourceOf(context));
    }

    [Fact]
    public async Task AdjacentIntHoles_AreNotAddedNumerically()
    {
        var (compilation, source) = await RunAsync("""public string Run(int a, int b) => $"{a}{b} items";""");

        source.ShouldNotContain(" items");
        Invoke(compilation, "Test", "Run", 1, 2).ShouldBe("12 items");
    }

    [Fact]
    public async Task AdjacentCharHoles_AreNotConvertedToInt()
    {
        var (compilation, _) = await RunAsync("""public string Run(char a, char b) => $"{a}{b} chars";""");

        Invoke(compilation, "Test", "Run", 'x', 'y').ShouldBe("xy chars");
    }

    [Fact]
    public async Task AdjacentTimeSpanHoles_AreNotAdded()
    {
        var (compilation, _) = await RunAsync("""public string Run(TimeSpan a, TimeSpan b) => $"{a}{b} span";""");

        Invoke(compilation, "Test", "Run", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2))
            .ShouldBe("00:01:0000:02:00 span");
    }

    [Fact]
    public async Task DateTimeHoleThenTimeSpanHole_StaysText()
    {
        // DateTime + TimeSpan is valid C# arithmetic and used to compile silently.
        var (compilation, _) = await RunAsync("""public string Run(DateTime d, TimeSpan t) => $"{d:yyyy}{t} elapsed";""");

        Invoke(compilation, "Test", "Run", new DateTime(2024, 5, 6), TimeSpan.FromMinutes(1))
            .ShouldBe("202400:01:00 elapsed");
    }

    [Fact]
    public async Task FormatSpecifierAndAlignment_ArePreserved()
    {
        var (compilation, source) = await RunAsync(
            """public string Run(int n, int m) => $"{n:X4} total {m,5}|{m,-5}| done";""");

        source.ShouldNotContain(" total ");
        source.ShouldNotContain(" done");
        Invoke(compilation, "Test", "Run", 255, 7).ShouldBe("00FF total     7|7    | done");
    }

    [Fact]
    public async Task HoleFirstAndLast_KeepsOrder()
    {
        var (compilation, _) = await RunAsync(
            """public string Run(int a, int b) => $"{a}{b}";public string Run2(int a, string s) => $"{a} and {s}";""");

        Invoke(compilation, "Test", "Run", 3, 4).ShouldBe("34");
        Invoke(compilation, "Test", "Run2", 3, null).ShouldBe("3 and ");
    }

    [Fact]
    public async Task EscapedBraces_AreRoundTripped()
    {
        var (compilation, source) = await RunAsync(
            """public string Run(int a) => $"{{literal braces}} then {a}{a} end";""");

        source.ShouldNotContain("literal braces");
        Invoke(compilation, "Test", "Run", 5).ShouldBe("{literal braces} then 55 end");
    }

    [Fact]
    public async Task VerbatimInterpolatedString_KeepsSemantics()
    {
        var (compilation, _) = await RunAsync(
            "public string Run(int a, int b) => $@\"line\\one \"\"quoted\"\" {a}{b} tail\";");

        Invoke(compilation, "Test", "Run", 1, 2).ShouldBe("line\\one \"quoted\" 12 tail");
    }

    [Fact]
    public async Task StringLiteralInsideHole_IsEncryptedToo()
    {
        var (compilation, source) = await RunAsync(
            """
            static string Wrap(string s) => "[" + s + "]";
            public string Run(int a) => $"Value {Wrap("inner secret")} {a} end";
            """);

        source.ShouldNotContain("inner secret");
        Invoke(compilation, "Test", "Run", 9).ShouldBe("Value [inner secret] 9 end");
    }

    [Fact]
    public async Task LiteralAsTheWholeHoleOrAnOperand_StaysInsideItsHole()
    {
        // A top-level "global::" in a hole would be cut at the first ':' (the format clause).
        var (compilation, source) = await RunAsync(
            """public string Run(int a) => $"A {"hole literal"} B {"left operand" + a,3} C {a:D2}";""");

        source.ShouldNotContain("hole literal");
        source.ShouldNotContain("left operand");
        Invoke(compilation, "Test", "Run", 7).ShouldBe("A hole literal B left operand7 C 07");
    }

    [Fact]
    public async Task FormattableStringTarget_IsLeftAlone()
    {
        // Rewriting would change the format string seen by FormattableString consumers
        // (for example parameterized SQL builders), so the string must stay as written.
        var (compilation, source) = await RunAsync(
            """
            public string Run(int a)
            {
                FormattableString f = $"Value is {a} units";
                return f.Format;
            }
            """);

        source.ShouldContain("Value is");
        Invoke(compilation, "Test", "Run", 4).ShouldBe("Value is {0} units");
    }

    [Fact]
    public async Task RawInterpolatedString_StillCompilesAndKeepsSemantics()
    {
        var (compilation, _) = await RunAsync(
            "public string Run(int a, int b) => $\"\"\"raw text {a}{b} end\"\"\";");

        Invoke(compilation, "Test", "Run", 1, 2).ShouldBe("raw text 12 end");
    }

    [Fact]
    public async Task NestedInterpolation_KeepsSemantics()
    {
        var (compilation, _) = await RunAsync(
            """public string Run(int a, int b) => $"outer {a}{$"inner {b}{a} text"} end";""");

        Invoke(compilation, "Test", "Run", 1, 2).ShouldBe("outer 1inner 21 text end");
    }
}

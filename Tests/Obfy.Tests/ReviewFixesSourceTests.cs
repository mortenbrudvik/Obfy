using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Source;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Shouldly;
using static Obfy.Tests.SourceRvTestSupport;

namespace Obfy.Tests;

/// <summary>
/// Regression tests for Wave 1D fixes in the Roslyn source obfuscators:
/// interpolated string brace/escape decoding, file-scoped namespace `#endif` balancing,
/// control-flow pattern designations/tick collisions/unreachable code, and symbol renaming rules.
/// </summary>
public class ReviewFixesSourceTests
{
    [Fact]
    public async Task SourceStringEncryptor_PreservesDoubledBraces_AndEscapedBraces_InInterpolatedStrings()
    {
        const string source = """
            public class Test
            {
                public string DoubledFour(int x) => $"{{{{four}}}} {x}";
                public string UnicodeEscaped(int x) => $"a\u007b\u007bb {x}";
                public string HexEscaped(int x) => $"a\x7b\x007bb {x}";
                public string HexWithFollowingHexDigit(int x) => $"a\x7b0 {x}";
                public string VerbatimMixed(int x) => $@"a{{b""c\u007b {x}";
            }
            """;

        var context = PipelineContext.ForSourceCode(CreateCompilation(source), StringSettings(minLength: 3));
        var result = await new SourceStringEncryptor(NullLogger<SourceStringEncryptor>.Instance).ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));
        result.Statistics.StringsEncrypted.ShouldBeGreaterThanOrEqualTo(5);
        SourceOf(context).ShouldContain("Decrypt");

        var compilation = context.Compilation!;
        Invoke(compilation, "Test", "DoubledFour", 1).ShouldBe("{{four}} 1");
        Invoke(compilation, "Test", "UnicodeEscaped", 2).ShouldBe("a{{b 2");
        Invoke(compilation, "Test", "HexEscaped", 3).ShouldBe("a{{b 3");
        Invoke(compilation, "Test", "HexWithFollowingHexDigit", 4).ShouldBe("a\u07b0 4");
        Invoke(compilation, "Test", "VerbatimMixed", 5).ShouldBe("a{b\"c\\u007b 5");
    }

    [Theory]
    [InlineData("""
        namespace MyApp;

        #if FEATURE
        public class Test
        {
            public string Run() => "secret value";
        }
        #endif
        """)]
    [InlineData("""
        #if FEATURE
        namespace MyApp;

        public class Test
        {
            public string Run() => "secret value";
        }
        #endif
        """)]
    [InlineData("""
        #region Header
        namespace MyApp;
        #endregion

        public class Test
        {
            public string Run() => "secret value";
        }
        #if FEATURE
        // trailing comment
        #endif
        """)]
    public async Task SourceStringEncryptor_FileScopedNamespaceWithTrailingEndif_StaysBalanced(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols("FEATURE");
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Test.cs");
        var compilation = CreateCompilation([]).AddSyntaxTrees(tree);
        var context = PipelineContext.ForSourceCode(compilation, StringSettings(minLength: 3));

        var result = await new SourceStringEncryptor(NullLogger<SourceStringEncryptor>.Instance).ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        // Compiles both with FEATURE defined and with FEATURE undefined (proving #if/#endif and braces are balanced).
        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));

        var outputSource = SourceOf(context);
        var disabledTree = CSharpSyntaxTree.ParseText(outputSource, CSharpParseOptions.Default, path: "Test.cs");
        var disabledCompilation = CreateCompilation([]).AddSyntaxTrees(disabledTree);
        disabledCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty("disabled #if branch must still leave balanced braces and #endif");
    }

    [Fact]
    public async Task SourceStringEncryptor_FileScopedNamespaceWithOuterIfClosedInsideNamespace_PreservesTrailingRegionPlacement()
    {
        const string source = """
            #if FEATURE
            namespace MyApp;

            public class First
            {
                public string Run() => "secret value";
            }
            #endif

            public class Second
            {
                public string Other() => "another secret";
            }
            #region Trailing
            // trailing comment
            #endregion
            """;

        var parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols("FEATURE");
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Test.cs");
        var compilation = CreateCompilation([]).AddSyntaxTrees(tree);
        var context = PipelineContext.ForSourceCode(compilation, StringSettings(minLength: 3));

        var result = await new SourceStringEncryptor(NullLogger<SourceStringEncryptor>.Instance).ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));

        // Because #if FEATURE closed before Second, #region Trailing belongs inside namespace MyApp before its closing brace.
        var outputSource = SourceOf(context);
        var endRegionIndex = outputSource.IndexOf("#endregion", StringComparison.Ordinal);
        var helperNsIndex = outputSource.IndexOf("namespace Obfy.Runtime", StringComparison.Ordinal);
        var closeNamespaceIndex = outputSource.LastIndexOf("}", helperNsIndex, StringComparison.Ordinal);
        endRegionIndex.ShouldBeGreaterThan(0);
        closeNamespaceIndex.ShouldBeGreaterThan(endRegionIndex);
    }

    [Theory]
    [InlineData(ControlFlowMode.OpaquePredicate)]
    [InlineData(ControlFlowMode.Switch)]
    [InlineData(ControlFlowMode.Combined)]
    public async Task SourceControlFlow_PatternVariablesAndNestedLabels_CompileAndRun(ControlFlowMode mode)
    {
        const string source = """
            public class Test
            {
                public int PatternVar(object? value)
                {
                    int result = 0;
                    if (value is not string text) return -1;
                    result += text.Length;
                    return result + 10;
                }

                public int NestedLabel(bool skip)
                {
                    int total = 1;
                    if (skip) goto done;
                    if (total > 0)
                    {
                        step:
                        total += 4;
                    }
                    total += 2;
                    done:
                    return total;
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var settings = new ObfySettings { ControlFlow = { Enabled = true, Mode = mode, Intensity = 100 } };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        var result = await new SourceControlFlowObfuscator(NullLogger<SourceControlFlowObfuscator>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));
        Invoke(context.Compilation!, "Test", "PatternVar", "hello").ShouldBe(15);
        Invoke(context.Compilation!, "Test", "PatternVar", 123).ShouldBe(-1);
        Invoke(context.Compilation!, "Test", "NestedLabel", false).ShouldBe(7);
        Invoke(context.Compilation!, "Test", "NestedLabel", true).ShouldBe(1);
    }

    [Fact]
    public async Task SourceControlFlow_UniqueTickVariables_PreventCS0136OnCollisionAndReobfuscation()
    {
        const string source = """
            public class Test
            {
                public int Compute(int __obfyTick, int __obfyTick_1)
                {
                    int acc = __obfyTick + __obfyTick_1;
                    acc += 1;
                    acc += 2;
                    return acc;
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var settings = new ObfySettings
        {
            ControlFlow = { Enabled = true, Mode = ControlFlowMode.OpaquePredicate, Intensity = 100 }
        };
        var obfuscator = new SourceControlFlowObfuscator(NullLogger<SourceControlFlowObfuscator>.Instance);

        var first = PipelineContext.ForSourceCode(compilation, settings);
        (await obfuscator.ObfuscateAsync(first)).Success.ShouldBeTrue();
        GetErrors(first.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(first.Compilation!)));

        // Re-obfuscating the already-obfuscated source must not collide with earlier __obfyTick_N locals.
        var second = PipelineContext.ForSourceCode(first.Compilation!, settings);
        (await obfuscator.ObfuscateAsync(second)).Success.ShouldBeTrue();
        GetErrors(second.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(second.Compilation!)));
        Invoke(second.Compilation!, "Test", "Compute", 10, 20).ShouldBe(33);
    }

    [Fact]
    public async Task SourceControlFlow_SwitchDispatcher_TerminalReturnAndThrow_ProduceNoUnreachableCodeWarning()
    {
        const string source = """
            using System;
            public class Test
            {
                public int Value;

                public int ReturnAtEnd(int x)
                {
                    Value += x;
                    Value *= 2;
                    return Value;
                }

                public void ThrowAtEnd(int x)
                {
                    Value += x;
                    Value *= 2;
                    throw new InvalidOperationException("boom");
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var settings = new ObfySettings { ControlFlow = { Enabled = true, Mode = ControlFlowMode.Switch, Intensity = 100 } };
        var context = PipelineContext.ForSourceCode(compilation, settings);

        var result = await new SourceControlFlowObfuscator(NullLogger<SourceControlFlowObfuscator>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        var roundTripped = RoundTrip(context.Compilation!);
        var diagnostics = roundTripped.GetDiagnostics();
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        diagnostics.Where(d => d.Id == "CS0162").ShouldBeEmpty("unreachable __state = -1; break; must not be emitted");
        Invoke(roundTripped, "Test", "ReturnAtEnd", 5).ShouldBe(10);
    }

    [Fact]
    public async Task SourceSymbolRenamer_RecordPositionalInterfaceExcludedAndXamlProperties_ArePreserved()
    {
        const string source = """
            using System.Reflection;

            public interface INamed
            {
                string Title { get; }
            }

            record Card(string Title, string InternalNote) : INamed;
            record TokenHolder([property: Obfuscation(Exclude = true)] string SecretToken, string Alias);
            record ProfileViewModel(string DisplayName);

            public static class Use
            {
                public static string Run()
                {
                    INamed named = new Card("Hello", "note");
                    var holder = new TokenHolder("tok", "al");
                    var vm = new ProfileViewModel("Ada");
                    return $"{named.Title}|{((Card)named).InternalNote}|{holder.SecretToken}|{vm.DisplayName}";
                }
            }
            """;

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            SymbolRenaming =
            {
                Enabled = true,
                RenameTypes = true,
                RenameMethods = true,
                RenameProperties = true,
                RenameParameters = true,
                PreservePublicApi = false,
                PreserveXaml = true,
                Mode = NamingMode.Sequential
            }
        };
        var context = PipelineContext.ForSourceCode(CreateCompilation(source), settings);

        var result = await new SourceSymbolRenamer(new NameGenerator(), NullLogger<SourceSymbolRenamer>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));
        var output = SourceOf(context);
        output.ShouldContain("Title");
        output.ShouldContain("SecretToken");
        output.ShouldContain("DisplayName");
        output.ShouldNotContain("InternalNote");
        InvokeRenamed(context, "Use", "Run").ShouldBe("Hello|note|tok|Ada");
    }

    [Fact]
    public async Task SourceSymbolRenamer_SkipsDuckTypedCompilerPatternMethodsAndProperties()
    {
        const string source = """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            class CustomSeq : IEnumerable
            {
                private readonly List<int> _items = new();
                public void Add(int item) => _items.Add(item);
                public CustomEnum GetEnumerator() => new(_items);
                IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
                public CustomSeq Slice(int start, int length)
                {
                    var s = new CustomSeq();
                    for (var i = 0; i < length; i++) s.Add(_items[start + i]);
                    return s;
                }
                public CustomSeq Where(Func<int, bool> predicate)
                {
                    var s = new CustomSeq();
                    foreach (var x in _items) if (predicate(x)) s.Add(x);
                    return s;
                }
                public IEnumerable<T> Select<T>(Func<int, T> selector)
                {
                    foreach (var x in _items) yield return selector(x);
                }
                public int Count => _items.Count;
            }

            class CustomEnum
            {
                private readonly List<int> _items;
                private int _index = -1;
                public CustomEnum(List<int> items) => _items = items;
                public int Current => _items[_index];
                public bool MoveNext() => ++_index < _items.Count;
                public void Dispose() { }
            }

            class Pair
            {
                public int A;
                public int B;
                public void Deconstruct(out int a, out int b) { a = A; b = B; }
            }

            class ReadyAwaiter : INotifyCompletion
            {
                public bool IsCompleted => true;
                public int GetResult() => 99;
                public void OnCompleted(Action continuation) => continuation();
            }

            class ReadyAwaitable
            {
                public ReadyAwaiter GetAwaiter() => new();
            }

            public static class Use
            {
                public static string Run()
                {
                    var seq = new CustomSeq { 1, 2, 3, 4 };
                    var sum = 0;
                    foreach (var n in from x in seq where x > 1 select x)
                        sum += n;
                    var (a, b) = new Pair { A = 3, B = 4 };
                    var awaited = new ReadyAwaitable().GetAwaiter().GetResult();
                    return $"{sum}:{a + b}:{awaited}";
                }
            }
            """;

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            SymbolRenaming =
            {
                Enabled = true,
                RenameTypes = true,
                RenameMethods = true,
                RenameProperties = true,
                RenameFields = true,
                RenameParameters = true,
                PreservePublicApi = false,
                Mode = NamingMode.Sequential
            }
        };
        var context = PipelineContext.ForSourceCode(CreateCompilation(source), settings);

        var result = await new SourceSymbolRenamer(new NameGenerator(), NullLogger<SourceSymbolRenamer>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));
        var output = SourceOf(context);
        output.ShouldContain("GetEnumerator");
        output.ShouldContain("MoveNext");
        output.ShouldContain("Current");
        output.ShouldContain("GetAwaiter");
        output.ShouldContain("IsCompleted");
        output.ShouldContain("GetResult");
        output.ShouldContain("Deconstruct");
        output.ShouldContain("Slice");
        InvokeRenamed(context, "Use", "Run").ShouldBe("9:7:99");
    }

    [Fact]
    public async Task SourceSymbolRenamer_PartialMethodParametersUnified_AndContractParametersKept()
    {
        const string source = """
            public interface IService
            {
                int Compute(int secretFactor);
            }

            public abstract class BaseCalc
            {
                public abstract int Adjust(int deltaAmount);
            }

            public partial class Worker : BaseCalc, IService
            {
                public int Compute(int secretFactor) => secretFactor * 2;
                public override int Adjust(int deltaAmount) => deltaAmount + 10;

                private int _acc;
                partial void AddStep(int stepValue);
                public int Run()
                {
                    AddStep(stepValue: 5);
                    IService svc = this;
                    BaseCalc calc = this;
                    return _acc + svc.Compute(secretFactor: 3) + calc.Adjust(deltaAmount: 4);
                }
            }

            public partial class Worker
            {
                partial void AddStep(int stepValue) => _acc += stepValue;
            }
            """;

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            SymbolRenaming =
            {
                Enabled = true,
                RenameTypes = true,
                RenameMethods = true,
                RenameParameters = true,
                PreservePublicApi = false,
                Mode = NamingMode.Sequential
            }
        };
        var context = PipelineContext.ForSourceCode(CreateCompilation(source), settings);

        var result = await new SourceSymbolRenamer(new NameGenerator(), NullLogger<SourceSymbolRenamer>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));
        var output = SourceOf(context);
        output.ShouldContain("secretFactor");
        output.ShouldContain("deltaAmount");
        output.ShouldNotContain("stepValue");
        InvokeRenamed(context, "Worker", "Run").ShouldBe(25);
    }

    [Fact]
    public async Task SourceSymbolRenamer_PartialIndexerParameterExcludedOnImplementation_KeepsParameterName()
    {
        const string source = """
            using System.Reflection;

            public partial class IndexedBox
            {
                public partial int this[int keptIndex, int renamedIndex] { get; }
                public int Run() => this[keptIndex: 2, renamedIndex: 5];
            }

            public partial class IndexedBox
            {
                public partial int this[[Obfuscation(Exclude = true)] int keptIndex, int renamedIndex]
                    => keptIndex * 10 + renamedIndex;
            }
            """;

        var settings = new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            SymbolRenaming =
            {
                Enabled = true,
                RenameTypes = true,
                RenameProperties = true,
                RenameParameters = true,
                PreservePublicApi = false,
                Mode = NamingMode.Sequential
            }
        };
        var context = PipelineContext.ForSourceCode(CreateCompilation(source), settings);

        var result = await new SourceSymbolRenamer(new NameGenerator(), NullLogger<SourceSymbolRenamer>.Instance)
            .ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);

        GetErrors(context.Compilation!).ShouldBeEmpty(ErrorText(GetErrors(context.Compilation!)));
        var output = SourceOf(context);
        output.ShouldContain("keptIndex");
        output.ShouldNotContain("renamedIndex");
        InvokeRenamed(context, "IndexedBox", "Run").ShouldBe(25);
    }

    private static object? InvokeRenamed(PipelineContext context, string typeName, string methodName, params object?[] args)
    {
        var map = context.SymbolMap;
        var newType = map.TryGetValue($"NamedType:{typeName}", out var t) ? t : typeName;
        var prefix = $"Method:{typeName}.{methodName}(";
        var newMethod = map.FirstOrDefault(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Value ?? methodName;
        return Invoke(context.Compilation!, newType, newMethod, args);
    }
}

using System.Text;
using dnlib.DotNet;
using Obfy.Core.Models;
using Shouldly;
using static Obfy.Tests.RenamingTestSupport;

namespace Obfy.Tests;

/// <summary>
/// Renamed names must not stay readable in the output file. A member reference whose signature mentions a generic
/// parameter (a member of <c>Box&lt;T&gt;</c> used through <c>Box&lt;int&gt;</c>) is handed to the renamer as a fresh
/// copy of the metadata row, and a writer that preserves row ids also emits the ORIGINAL row next to it: unreferenced,
/// and still carrying the original name. The same happens for a MethodSpec whose instantiation is a generic
/// parameter (<c>ZqxHolder&lt;T&gt;.ZqxPassMethod&lt;U&gt;</c> calls <c>ZqxBox&lt;T&gt;.ZqxOpenMethod&lt;U&gt;</c>):
/// preserving MethodSpec ids pulls the unrenamed member reference back in. That method is referenced only that way.
/// Every case protects a program whose generic types have distinctive names
/// and demands that none of them (UTF-8 for the #Strings heap, UTF-16 for #US) is found anywhere in the output
/// file, and that the protected program still answers like the original.
/// </summary>
public class SymbolRenamingLeftoverNamesTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Runtime.CompilerServices;

        """;

    /// <summary>
    /// What the library defines: fields, methods, properties, events, an indexer, nested and derived generics, a
    /// generic struct and a generic method, each under a name that appears nowhere else.
    /// </summary>
    private const string Definitions = """
        public class ZqxBox<T>
        {
            public T ZqxItemField;
            public static int ZqxCountField;
            private T _zqxHiddenField;
            public ZqxBox(T item) { ZqxItemField = item; _zqxHiddenField = item; ZqxCountField++; }
            public T ZqxGetMethod() => ZqxItemField;
            public T ZqxPeekMethod() => _zqxHiddenField;
            public T ZqxValueProp { get; set; }
            public event Action<T> ZqxChangedEvent;
            public void ZqxRaiseMethod(T zqxRaiseArg) { ZqxChangedEvent?.Invoke(zqxRaiseArg); }
            public U ZqxConvertMethod<U>(Func<T, U> zqxConvertArg) => zqxConvertArg(ZqxItemField);
            public U ZqxOpenMethod<U>(T zqxOpenArg) => default!;
            [IndexerName("ZqxIndexer")]
            public T this[int zqxIndexArg] => ZqxItemField;

            public class ZqxInner<U>
            {
                public U ZqxInnerOtherField;
                public T ZqxInnerPickMethod(T zqxPickArg, U zqxPickOther) => zqxPickArg;
            }

            public ZqxInner<string> ZqxMakeInnerMethod() => new ZqxInner<string>();
        }

        public struct ZqxPair<T>
        {
            public T ZqxFirstField;
            public T ZqxPickMethod(bool zqxPickFlag) => ZqxFirstField;
        }

        public class ZqxBase<T>
        {
            public T ZqxBaseField;
            public T ZqxBaseGetMethod() => ZqxBaseField;
        }

        public class ZqxDerived : ZqxBase<int>
        {
            public int ZqxTwiceMethod() => ZqxBaseGetMethod() + ZqxBaseField;
        }

        public static class ZqxUtil
        {
            public static T ZqxEchoMethod<T>(T zqxEchoArg) => zqxEchoArg;
        }

        public class ZqxHolder<T>
        {
            // The only call to ZqxOpenMethod. Its type argument is this method's own type parameter, so the
            // MethodSpec is not a cached row and a writer that preserves MethodSpec ids emits the original name too.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public U ZqxPassMethod<U>(ZqxBox<T> zqxPassBox, T zqxPassArg) => zqxPassBox.ZqxOpenMethod<U>(zqxPassArg);
        }

        """;

    /// <summary>What uses them, through generic instantiations of every kind the C# compiler writes.</summary>
    private const string App = """
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        public static class Driver
        {
            public static string Run()
            {
                var a = new ZqxBox<int>(42);
                var b = new ZqxBox<string>("s");
                var seen = 0;
                a.ZqxChangedEvent += v => seen += v;
                a.ZqxRaiseMethod(5);
                a.ZqxValueProp = a.ZqxItemField + 1;
                var inner = new ZqxBox<int>.ZqxInner<string> { ZqxInnerOtherField = "o" };
                var pair = new ZqxPair<int> { ZqxFirstField = 3 };
                var derived = new ZqxDerived { ZqxBaseField = 4 };
                var nested = new ZqxBox<ZqxBox<int>>(new ZqxBox<int>(9));
                return a.ZqxGetMethod() + "/" + a.ZqxPeekMethod() + "/" + a.ZqxValueProp + "/" + b.ZqxGetMethod() + "/" + seen
                    + "/" + a.ZqxConvertMethod(x => x * 2) + "/" + a[0] + "/" + inner.ZqxInnerOtherField + inner.ZqxInnerPickMethod(1, "u")
                    + "/" + a.ZqxMakeInnerMethod().ZqxInnerOtherField + "/" + pair.ZqxPickMethod(true) + "/" + derived.ZqxTwiceMethod()
                    + "/" + nested.ZqxItemField.ZqxGetMethod() + nested.ZqxGetMethod().ZqxItemField
                    + "/" + ZqxUtil.ZqxEchoMethod(7) + ZqxUtil.ZqxEchoMethod("e") + "/" + ZqxBox<int>.ZqxCountField
                    + "/" + new ZqxHolder<int>().ZqxPassMethod<string>(new ZqxBox<int>(1), 1);
            }
        }
        """;

    /// <summary>
    /// Every original name the renamer replaces: the types, the members, their accessors and compiler-generated backing
    /// fields, and the parameters of ordinary methods. (The framework's own names, kept by design, are not in this
    /// list; neither are the parameters of property accessors and constructors, whose live rows keep their names
    /// because the parameter pass skips special-name methods, nothing to do with leftover rows.)
    /// </summary>
    private static readonly string[] OriginalNames =
    {
        "ZqxBox", "ZqxInner", "ZqxPair", "ZqxBase", "ZqxDerived", "ZqxUtil",
        "ZqxItemField", "ZqxCountField", "_zqxHiddenField", "ZqxGetMethod", "ZqxPeekMethod", "ZqxValueProp",
        "get_ZqxValueProp", "set_ZqxValueProp", "<ZqxValueProp>k__BackingField", "ZqxChangedEvent",
        "add_ZqxChangedEvent", "remove_ZqxChangedEvent", "ZqxRaiseMethod", "ZqxConvertMethod", "ZqxIndexer",
        "get_ZqxIndexer", "ZqxInnerOtherField", "ZqxInnerPickMethod", "ZqxMakeInnerMethod", "ZqxFirstField",
        "ZqxPickMethod", "ZqxBaseField", "ZqxBaseGetMethod", "ZqxTwiceMethod", "ZqxEchoMethod",
        "ZqxOpenMethod", "ZqxHolder", "ZqxPassMethod",
        "zqxRaiseArg", "zqxConvertArg", "zqxPickArg", "zqxPickOther", "zqxPickFlag", "zqxEchoArg",
        "zqxOpenArg", "zqxPassArg", "zqxPassBox"
    };

    public static TheoryData<string, string> Runs
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var shape in new[] { "single", "closed-set", "merge" })
                foreach (var configuration in new[] { "minimal", "standard", "aggressive" })
                    data.Add(shape, configuration);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Runs))]
    public async Task RenamedNames_AreNotReadableAnywhereInTheOutput(string shape, string configuration)
    {
        var dir = NewTempDir();
        try
        {
            var (inputs, driverAssembly) = BuildShape(shape, dir, Usings, Definitions, "", App);
            var expected = Run(driverAssembly);
            expected.StartsWith("!", StringComparison.Ordinal).ShouldBeFalse($"the unprotected program itself failed: {expected}");

            // The same names must be found in the input, or the search below proves nothing.
            foreach (var input in inputs)
                FindNames(input).ShouldNotBeEmpty($"{Path.GetFileName(input)} must hold the original names");

            var outDir = Path.Combine(dir, "out");
            var outputs = await ProtectAsync(shape, inputs, outDir, SettingsFor(configuration));

            var leaks = outputs
                .Select(path => (File: Path.GetFileName(path), Names: FindNames(path)))
                .Where(leak => leak.Names.Count > 0)
                .Select(leak => $"{leak.File}: {string.Join(", ", leak.Names)}")
                .ToList();
            leaks.ShouldBeEmpty($"({shape}, {configuration}) original names are still in the output bytes");

            var driverOutput = shape == "merge" ? outputs[0] : Path.Combine(outDir, Path.GetFileName(driverAssembly));
            Run(driverOutput).ShouldBe(expected, $"{shape}, {configuration}");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>
    /// A real application is the broadest sample there is: protect Obfy's own core assembly and look for the names of
    /// its private fields (long, underscore-prefixed, so nothing else spells them) in the output's bytes. Fields
    /// that the output still declares under their name are kept on purpose and are not looked for.
    /// </summary>
    [Fact]
    public async Task ProtectingObfyCoreItself_LeavesNoOriginalPrivateFieldNamesInTheBytes()
    {
        var core = typeof(ObfySettings).Assembly.Location;
        var dir = NewTempDir();
        try
        {
            var input = Path.Combine(dir, "Obfy.Core.dll");
            File.Copy(core, input);
            // The references the pipeline resolves while it plans the renames sit next to the input.
            foreach (var sibling in Directory.EnumerateFiles(Path.GetDirectoryName(core)!, "*.dll"))
            {
                var target = Path.Combine(dir, Path.GetFileName(sibling));
                if (!File.Exists(target))
                    File.Copy(sibling, target);
            }

            var output = Path.Combine(dir, "out", "Obfy.Core.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var result = await CreateService().ObfuscateAsync(input, output, new ObfySettings { Level = ObfuscationLevel.Standard });
            result.Success.ShouldBeTrue(result.ErrorMessage);

            HashSet<string> keptFields;
            using (var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output)))
            {
                keptFields = protectedModule.GetTypes().SelectMany(t => t.Fields).Select(f => f.Name.String).ToHashSet(StringComparer.Ordinal);
            }

            List<string> names;
            using (var original = ModuleDefMD.Load(File.ReadAllBytes(input)))
            {
                names = original.GetTypes().SelectMany(t => t.Fields)
                    .Where(f => f.IsPrivate && !f.IsLiteral && !f.IsSpecialName && !f.IsRuntimeSpecialName)
                    .Select(f => f.Name.String)
                    .Where(n => n.Length >= 8 && n[0] == '_' && !keptFields.Contains(n))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }

            names.Count.ShouldBeGreaterThan(30, "the sample must have private fields to look for");
            var bytes = File.ReadAllBytes(output);
            var leaked = names.Where(n => Contains(bytes, Encoding.UTF8.GetBytes(n))).OrderBy(n => n, StringComparer.Ordinal).ToList();
            leaked.ShouldBeEmpty($"{leaked.Count} of {names.Count} renamed private field names are still in the output");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>The original names found in the file as UTF-8 (#Strings) or UTF-16 (#US) text.</summary>
    private static List<string> FindNames(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var found = new List<string>();
        foreach (var name in OriginalNames)
        {
            if (Contains(bytes, Encoding.UTF8.GetBytes(name)))
                found.Add(name);
            else if (Contains(bytes, Encoding.Unicode.GetBytes(name)))
                found.Add(name + " (UTF-16)");
        }

        return found;
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}

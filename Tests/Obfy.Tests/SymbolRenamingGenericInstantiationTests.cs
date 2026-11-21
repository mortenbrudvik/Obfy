using System.Reflection;
using System.Runtime.Loader;
using Autofac;
using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.DependencyInjection;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Symbol renaming and generic instantiations of types the module defines. C# refers to a member of
/// <c>Box&lt;int&gt;</c> through a MemberRef whose parent is a TypeSpec (the instantiation), not through the
/// MethodDef or FieldDef itself, and the CLR binds such a reference by name and signature. Renaming the
/// definitions therefore has to rewrite those references too, or the program dies with
/// <c>MissingFieldException</c> / <c>MissingMethodException</c> the first time the code path runs.
/// Every case compiles a small library, runs its <c>Driver.Run()</c> unprotected and protected (symbol renaming
/// only, and the Standard preset) and demands the same answer, and then scans the output for any reference to a
/// definition of the module that names a member the definition no longer has.
/// </summary>
public class SymbolRenamingGenericInstantiationTests
{
    /// <summary>The smallest shape of the bug, available to every case.</summary>
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;

        internal class Box<T>
        {
            public T Item;
            public Box(T item) { Item = item; }
            public T Get() => Item;
        }

        """;

    /// <summary>
    /// <c>Driver</c> keeps its names (excluded from renaming only) so the test can call it; everything it uses is
    /// renamed, so every reference it holds to those types' members has to follow the rename.
    /// </summary>
    private const string DriverAttribute =
        "[System.Reflection.Obfuscation(Feature = \"renaming\", Exclude = true, ApplyToMembers = true)]";

    private sealed record Shape(string Source, string[] Types, string[] Members);

    private static readonly Dictionary<string, Shape> Cases = new()
    {
        ["GenericClassMembers"] = new(
            """
            internal class Crate<T>
            {
                public T Item;
                public static int Created;
                public static T Last;
                public Crate(T item) { Item = item; Created++; Last = item; }
                public T Get() => Item;
                public void Set(T value) { Item = value; Changed?.Invoke(value); }
                public static Crate<T> Make(T item) => new Crate<T>(item);
                public T Value { get => Item; set => Item = value; }
                public event Action<T> Changed;
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var a = new Crate<int>(42);
                    var b = new Crate<string>("s");
                    var seen = 0;
                    a.Changed += v => seen += v;
                    a.Set(5);
                    a.Value = a.Value + 1;
                    var m = Crate<int>.Make(7);
                    return $"{a.Item}/{a.Get()}/{a.Value}/{b.Item}{b.Get()}/{m.Get()}/{seen}/{Crate<int>.Created}/{Crate<string>.Created}/{Crate<int>.Last}";
                }
            }
            """,
            new[] { "Crate`1" },
            new[] { "Item", "Created", "Last", "Get", "Set", "Make", "Value", "Changed" }),

        ["IssueSample"] = new(
            DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var b = new Box<int>(42);
                    return (b.Item + b.Get()).ToString();
                }
            }
            """,
            new[] { "Box`1" },
            new[] { "Item", "Get" }),

        ["GenericMethods"] = new(
            """
            internal static class Util
            {
                public static T Echo<T>(T v) => v;
                public static U Map<T, U>(T v, Func<T, U> f) => f(v);
            }

            internal class Mapper<T>
            {
                private readonly T _seed;
                public Mapper(T seed) { _seed = seed; }
                public U Convert<U>(Func<T, U> f) => f(_seed);
                public static string Describe<U>(T a, U b) => a + "|" + b;
                public Mapper<U> Select<U>(Func<T, U> f) => new Mapper<U>(f(_seed));
                public T Seed => _seed;
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var m = new Mapper<int>(21);
                    return Util.Echo(3) + "/" + Util.Map("ab", s => s.Length) + "/" + m.Convert(x => x * 2)
                        + "/" + Mapper<int>.Describe(3, 2.5) + "/" + m.Select(x => x.ToString()).Convert(s => s + "!")
                        + "/" + m.Seed;
                }
            }
            """,
            new[] { "Util", "Mapper`1" },
            new[] { "Echo", "Map", "Convert", "Describe", "Select", "Seed", "_seed" }),

        ["NestedGenerics"] = new(
            """
            internal class Outer<T>
            {
                public T Value;
                public Outer(T v) { Value = v; }

                public class Inner<U>
                {
                    public U Other;
                    public static int Count;
                    public Inner(U o) { Other = o; }
                    public string Show(T t) => t + ":" + Other;
                }

                public class Plain
                {
                    public T Held;
                    public T Read() => Held;
                }

                public Inner<string> Make() => new Inner<string>("x");
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var o = new Outer<int>(1);
                    var i = new Outer<int>.Inner<string>("s");
                    Outer<int>.Inner<string>.Count++;
                    var p = new Outer<double>.Plain { Held = 2.5 };
                    var nested = new Box<Box<int>>(new Box<int>(9));
                    return i.Show(o.Value) + "/" + o.Make().Other + "/" + Outer<int>.Inner<string>.Count + "/" + p.Read() + "/"
                        + nested.Item.Item + nested.Get().Get() + "/" + o.Make().Show(3);
                }
            }
            """,
            new[] { "Outer`1", "Inner`1", "Plain" },
            new[] { "Held", "Read", "Other", "Show", "Make", "Count", "Value" }),

        ["GenericStruct"] = new(
            """
            internal struct Pair<T>
            {
                public T First;
                public T Second;
                public Pair(T a, T b) { First = a; Second = b; }
                public T Pick(bool f) => f ? First : Second;
                public Pair<T> Swap() => new Pair<T>(Second, First);
                public static Pair<T> Of(T a) => new Pair<T>(a, a);
                public T this[int i]
                {
                    get => i == 0 ? First : Second;
                    set { if (i == 0) First = value; else Second = value; }
                }
            }

            internal static class Pointers
            {
                public static unsafe int Sum()
                {
                    var p = new Pair<int>(3, 4);
                    Pair<int>* ptr = &p;
                    ptr->First += 1;
                    return ptr->First + ptr->Pick(false) + ptr->Swap().First;
                }
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var p = new Pair<int>(1, 2);
                    p.First += 10;
                    var q = p.Swap();
                    var r = Pair<string>.Of("z");
                    q[1] = 99;
                    var boxed = new Box<Pair<int>>(p);
                    return p.First + "/" + q.Second + "/" + q[0] + "/" + r.Pick(true) + r.Second + "/" + boxed.Item.First + boxed.Get().Pick(false) + "/" + Pointers.Sum();
                }
            }
            """,
            new[] { "Pair`1", "Pointers" },
            new[] { "First", "Second", "Pick", "Swap", "Of" }),

        ["GenericInterfaces"] = new(
            """
            internal interface IProvider<T>
            {
                T Provide();
                string Tag { get; }
            }

            internal class IntProvider : IProvider<int>
            {
                public int Provide() => 7;
                public string Tag => "int";
            }

            internal class StringProvider : IProvider<string>
            {
                string IProvider<string>.Provide() => "explicit";
                string IProvider<string>.Tag => "str";
            }

            internal class Forwarding<T> : IProvider<T>
            {
                private readonly T _v;
                public int Calls;
                public Forwarding(T v) { _v = v; }
                public T Provide() { Calls++; return _v; }
                public string Tag => "fwd";
                public T Peek() => _v;
            }

            internal static class Consume
            {
                public static string Use<T>(IProvider<T> p) => p.Provide() + p.Tag;
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    IProvider<int> a = new IntProvider();
                    IProvider<string> b = new StringProvider();
                    var c = new Forwarding<double>(1.5);
                    return a.Provide() + a.Tag + b.Provide() + b.Tag + Consume.Use(c) + Consume.Use<int>(a) + c.Calls + c.Peek();
                }
            }
            """,
            new[] { "IProvider`1", "IntProvider", "StringProvider", "Forwarding`1", "Consume" },
            new[] { "Calls", "Peek", "Use", "_v" }),

        ["GenericBase"] = new(
            """
            internal class Base<T>
            {
                public T Item;
                protected T Hold;
                public Base(T i) { Item = i; Hold = i; }
                public T Get() => Item;
                public static string Kind() => "base";
                public T Prop { get; set; }
                protected T Peek() => Hold;
            }

            internal class IntBox : Base<int>
            {
                public IntBox(int i) : base(i) { }
                public int Twice() => base.Get() + Item + Peek();
                public int ViaBase() { Prop = 5; return Prop + base.Get(); }
                public static string KindOf() => Kind();
            }

            internal class Derived<T> : Base<T>
            {
                public Derived(T t) : base(t) { }
                public T Both() { return Get(); }
                public T Field() => Item;
                public T ViaProp => Prop;
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var ib = new IntBox(4);
                    var d = new Derived<string>("d");
                    return ib.Twice() + "/" + ib.ViaBase() + "/" + IntBox.KindOf() + "/" + d.Both() + d.Field() + d.ViaProp + "/" + ib.Item + "/" + ib.Get() + Base<int>.Kind();
                }
            }
            """,
            new[] { "Base`1", "IntBox", "Derived`1" },
            new[] { "Item", "Hold", "Get", "Kind", "Prop", "Peek", "Twice", "ViaBase", "KindOf", "Both", "Field", "ViaProp" }),

        ["ClosuresAndDelegates"] = new(
            """
            internal delegate U Convert<T, U>(T value);
            internal delegate void Visitor<T>(Box<T> box);

            internal static class Lambdas
            {
                public static Func<T> Capture<T>(T v) { return () => v; }

                public static Func<int, T> CaptureLocal<T>(T v)
                {
                    var list = new List<T>();
                    return k => { list.Add(v); return list[0]; };
                }

                public static U Apply<T, U>(T v, Convert<T, U> c) => c(v);
                public static T Pick<T>(Box<T> b, Func<Box<T>, T> f) => f(b);
                public static int Cached<T>(T v) { Func<T, int> f = x => 5; return f(v); }
                public static void Visit<T>(T v, Visitor<T> vis) => vis(new Box<T>(v));
            }

            internal class Holder<T>
            {
                public T V;
                public Holder(T v) { V = v; }
                public Func<T> Getter() => () => V;
                public Func<int, T> Capturing() { var self = this; return i => self.V; }
                public Action<T> Setter() => x => V = x;
                public Convert<T, string> Cv => x => x + "!";
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var seen = "";
                    Lambdas.Visit(3, b => seen += b.Get());
                    var h = new Holder<string>("h");
                    h.Setter()("q");
                    return Lambdas.Capture(5)() + "/" + Lambdas.CaptureLocal("c")(0) + "/" + Lambdas.Apply(2, x => x * 3.5)
                        + "/" + Lambdas.Pick(new Box<int>(8), bx => bx.Item) + "/" + Lambdas.Cached("s") + seen + h.Getter()()
                        + h.Capturing()(0) + h.Cv("z");
                }
            }
            """,
            new[] { "Lambdas", "Holder`1", "Visitor`1" },
            new[] { "Capture", "CaptureLocal", "Apply", "Pick", "Cached", "Visit", "Getter", "Capturing", "Setter", "Cv", "V" }),

        ["Arrays"] = new(
            """
            internal class Cell
            {
                public int N;
                public Cell(int n) { N = n; }
                // The same names and signatures the runtime gives an array of Cell: the array's own
                // Get/Set members must never be mistaken for these.
                public Cell Get(int a, int b) => new Cell(N + a + b);
                public Cell Get(int a) => new Cell(N + a);
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var arr = new Box<int>[3];
                    arr[0] = new Box<int>(1);
                    arr[1] = new Box<int>(2);
                    arr[2] = arr[0];
                    var jag = new Box<string>[2][];
                    jag[0] = new[] { new Box<string>("a") };
                    var grid = new Box<int>[2, 2];
                    grid[1, 1] = new Box<int>(5);
                    var sum = 0;
                    foreach (var b in arr) sum += b.Item + b.Get();
                    sum += grid[1, 1].Get() + jag[0][0].Item.Length;
                    var copy = (Box<int>[])arr.Clone();
                    sum += copy.Length;
                    var cells = new Cell[2, 2];
                    cells[0, 1] = new Cell(10);
                    var szCells = new Cell[2];
                    szCells[1] = new Cell(20);
                    return sum + "/" + cells[0, 1].Get(1, 1).N + "/" + szCells[1].Get(1).N + "/" + cells[0, 1].N;
                }
            }
            """,
            new[] { "Cell" },
            new[] { "N" }),

        ["AttributesAndTypeof"] = new(
            """
            [AttributeUsage(AttributeTargets.All)]
            internal sealed class TypeArgAttribute : Attribute
            {
                public Type Arg;
                public TypeArgAttribute(Type t) { Arg = t; }
            }

            [AttributeUsage(AttributeTargets.All)]
            internal sealed class TagAttribute<T> : Attribute
            {
                public T Value;
                public TagAttribute(T v) { Value = v; }
            }

            [TypeArg(typeof(Box<int>))]
            [Tag<int>(3)]
            internal class Decorated
            {
                [TypeArg(typeof(Box<>))] public int F;
                [Tag<string>("x")] public void M() { }
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var arg = ((TypeArgAttribute)typeof(Decorated).GetCustomAttributes(typeof(TypeArgAttribute), false)[0]).Arg;
                    var tag = (TagAttribute<int>)typeof(Decorated).GetCustomAttributes(typeof(TagAttribute<int>), false)[0];
                    var open = ((TypeArgAttribute)typeof(Decorated).GetFields()[0].GetCustomAttributes(typeof(TypeArgAttribute), false)[0]).Arg;
                    var onMethod = (TagAttribute<string>)typeof(Decorated).GetMethods().Single(m => m.DeclaringType == typeof(Decorated) && m.GetParameters().Length == 0 && m.ReturnType == typeof(void))
                        .GetCustomAttributes(typeof(TagAttribute<string>), false)[0];
                    return arg.GetGenericArguments()[0].Name + "/" + tag.Value + "/" + open.IsGenericTypeDefinition + "/" + onMethod.Value
                        + "/" + typeof(Box<int>).GetGenericArguments().Length + "/" + nameof(Box<int>.Item) + "/" + typeof(Box<>).IsGenericTypeDefinition;
                }
            }
            """,
            new[] { "TypeArgAttribute", "TagAttribute`1", "Decorated" },
            new[] { "Arg", "Value" }),

        ["StaticsAndStaticConstructors"] = new(
            """
            internal class Registry<T>
            {
                public static List<T> Items;
                static Registry() { Items = new List<T>(); }
                public static int Count => Items.Count;
                public static void Add(T t) => Items.Add(t);
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    Registry<int>.Add(1);
                    Registry<string>.Add("a");
                    Registry<string>.Add("b");
                    return Registry<int>.Count + "/" + Registry<string>.Count + "/" + Registry<string>.Items[1];
                }
            }
            """,
            new[] { "Registry`1" },
            new[] { "Items", "Count", "Add" }),

        ["IndexersAndProperties"] = new(
            """
            internal class Table<T>
            {
                private readonly T[] _items = new T[4];
                public T this[int i] { get => _items[i]; set => _items[i] = value; }
                public T this[string key] { get => _items[key.Length]; set => _items[key.Length] = value; }
                public int Length => _items.Length;
                public T First { get; private set; }
                public void SetFirst(T t) { First = t; }
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var t = new Table<string>();
                    t[0] = "a";
                    t["kk"] = "b";
                    t.SetFirst("f");
                    var n = new Table<int>();
                    n[1] = 7;
                    return t[0] + t["kk"] + t[2] + "/" + t.Length + "/" + t.First + "/" + n[1] + n["x"];
                }
            }
            """,
            new[] { "Table`1" },
            new[] { "_items", "Length", "First", "SetFirst" }),

        ["OverloadsDistinguishedBySignature"] = new(
            """
            internal class Sink<T>
            {
                public string Put(T v) => "T";
                public string Put(int v) => "int";
                public string Put(T a, T b) => "TT";
                public string Put<U>(U u) => "U";
                public string Put(T[] many) => "T[]";
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var s = new Sink<int>();
                    var t = new Sink<string>();
                    return s.Put(5) + s.Put(5, 6) + s.Put("x") + s.Put(new[] { 1 }) + "/" + t.Put("a") + t.Put(5) + t.Put("a", "b") + t.Put(1.5) + t.Put(new[] { "a" });
                }
            }
            """,
            new[] { "Sink`1" },
            new[] { "Put" }),

        ["SameMemberNamesOnDifferentTypes"] = new(
            """
            internal class Alpha<T>
            {
                public T Item;
                public Alpha(T item) { Item = item; }
                public T Get() => Item;
                public string Name() => "alpha";
            }

            internal class Beta<T>
            {
                public T Item;
                public Beta(T item) { Item = item; }
                public T Get() => default(T);
                public string Name() => "beta";
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var a = new Alpha<int>(5);
                    var b = new Beta<int>(6);
                    var c = new Alpha<string>("a");
                    var d = new Beta<string>("b");
                    return a.Get() + "," + b.Get() + "," + c.Get() + "," + d.Get() + "," + a.Item + b.Item + c.Item + d.Item + a.Name() + b.Name() + c.Name() + d.Name();
                }
            }
            """,
            new[] { "Alpha`1", "Beta`1" },
            new[] { "Item", "Get", "Name" }),

        ["FrameworkGenerics"] = new(
            DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var list = new List<Box<int>>();
                    list.Add(new Box<int>(1));
                    list.Add(new Box<int>(2));
                    var dict = new Dictionary<string, Box<int>> { ["a"] = new Box<int>(3) };
                    var total = list.Sum(b => b.Item) + list.Count + dict["a"].Get() + dict.Count;
                    var q = list.Select(b => b.Get()).Where(x => x > 1).ToArray();
                    var nullable = new int?(4);
                    var tuple = (1, new Box<int>(2));
                    var kv = new KeyValuePair<int, Box<int>>(1, list[0]);
                    return total + "/" + q.Length + q[0] + "/" + nullable.Value + "/" + tuple.Item2.Item + "/" + kv.Value.Get() + kv.Key;
                }
            }
            """,
            new[] { "Box`1" },
            new[] { "Item", "Get" }),

        ["AsyncAndIterators"] = new(
            """
            internal class Worker<T>
            {
                private readonly T _v;
                public Worker(T v) { _v = v; }
                public async Task<T> GetAsync() { await Task.Yield(); return _v; }
                public IEnumerable<T> Repeat(int n) { for (var i = 0; i < n; i++) yield return _v; }
                public async Task<string> Join<U>(U u) { await Task.Delay(1); return _v + "-" + u; }
                public static IEnumerable<T> Once(T v) { yield return v; }
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var w = new Worker<int>(2);
                    var a = w.GetAsync().GetAwaiter().GetResult();
                    var r = string.Join(",", w.Repeat(3));
                    var j = w.Join("u").GetAwaiter().GetResult();
                    return a + "/" + r + "/" + j + "/" + string.Join("", Worker<string>.Once("o"));
                }
            }
            """,
            new[] { "Worker`1" },
            new[] { "GetAsync", "Repeat", "Join", "Once", "_v" }),

        ["AbstractAndOverriddenMembers"] = new(
            """
            internal abstract class Shape<T>
            {
                public abstract T Area();
                public virtual string Name() => "shape";
                public string Describe() => Name() + Area();
                public T Last;
            }

            internal class Square : Shape<int>
            {
                public override int Area() { Last = 4; return 4; }
                public override string Name() => "sq";
            }

            internal class Gen<T> : Shape<T>
            {
                private readonly T _v;
                public Gen(T v) { _v = v; }
                public override T Area() { Last = _v; return _v; }
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    Shape<int> s = new Square();
                    var g = new Gen<string>("g");
                    return s.Describe() + s.Area() + s.Name() + g.Describe() + g.Area() + s.Last + g.Last;
                }
            }
            """,
            new[] { "Shape`1", "Square", "Gen`1" },
            new[] { "Describe", "Last", "_v" }),

        ["VarArgs"] = new(
            """
            internal static class Va
            {
                // A vararg call site names its target through a MemberRef whose parent is the MethodDef itself.
                public static int Sum(int count, __arglist)
                {
                    var it = new ArgIterator(__arglist);
                    var total = 0;
                    for (var i = 0; i < count; i++)
                        total += __refvalue(it.GetNextArg(), int);
                    return total;
                }
            }

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run() => Va.Sum(3, __arglist(1, 2, 3)) + "/" + Va.Sum(0, __arglist());
            }
            """,
            new[] { "Va" },
            new[] { "Sum" }),

        ["GenericRecord"] = new(
            """
            internal record Rec<T>(T Value, int N);

            """ + DriverAttribute + """

            public static class Driver
            {
                public static string Run()
                {
                    var r = new Rec<int>(1, 2);
                    var r2 = r with { N = 3 };
                    var (v, n) = r;
                    return r2.N + "/" + r2.Value + "/" + (r == r2 ? 0 : 1) + "/" + (r.Equals(new Rec<int>(1, 2)) ? 1 : 0) + "/" + v + n + "/" + (r.GetHashCode() == new Rec<int>(1, 2).GetHashCode());
                }
            }
            """,
            new[] { "Rec`1" },
            new[] { "Value", "N" })
    };

    public static TheoryData<string> CaseNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Cases.Keys)
                data.Add(name);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task ProtectedLibrary_RunsLikeTheOriginal_AndLeavesNoStaleMemberReferences(string caseName)
    {
        var shape = Cases[caseName];
        var dir = NewTempDir();
        try
        {
            var input = Compile(Prelude + shape.Source, dir, "GenericShape");
            var expected = Run(input);
            expected.StartsWith("!", StringComparison.Ordinal).ShouldBeFalse($"the unprotected library itself failed: {expected}");

            foreach (var configuration in new[] { "rename-only", "standard" })
            {
                var output = Path.Combine(dir, $"GenericShape.{configuration}.dll");
                var result = await CreateService().ObfuscateAsync(input, output, SettingsFor(configuration));
                result.Success.ShouldBeTrue(result.ErrorMessage);

                var failures = new List<string>();

                using (var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output)))
                {
                    foreach (var type in shape.Types.Where(type => protectedModule.GetTypes().Any(t => t.Name.String == type)))
                        failures.Add($"{type} was not renamed");

                    var kept = KeptNames(protectedModule);
                    foreach (var member in shape.Members.Where(kept.Contains))
                        failures.Add($"{member} was not renamed");

                    foreach (var stale in StaleMemberReferenceScanner.Find(protectedModule))
                        failures.Add("stale reference: " + stale);
                }

                var actual = Run(output);
                if (actual != expected)
                    failures.Add($"the protected library answered '{actual}', the original '{expected}'");

                failures.ShouldBeEmpty($"{caseName}, {configuration}");
            }
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task FrameworkGenericMembers_KeepTheirNames()
    {
        var dir = NewTempDir();
        try
        {
            var input = Compile(Prelude + Cases["FrameworkGenerics"].Source, dir, "GenericShape");
            var output = Path.Combine(dir, "GenericShape.obf.dll");
            var result = await CreateService().ObfuscateAsync(input, output, SettingsFor("rename-only"));
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output));
            var framework = MemberRefNamesOnGenericInstantiations(protectedModule);
            foreach (var name in new[] { "Add", "get_Count", "get_Item", "set_Item", "Sum", "Select", "Where", "ToArray", "get_Key", "get_Value" })
                framework.ShouldContain(name, $"the framework member {name} must not have been touched");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task PreservePublicApi_KeepsPublicMembersOfAGenericTypeAndRenamesTheRest()
    {
        const string source = """
            public class PubBox<T>
            {
                public T Item;
                private T _hidden;
                public PubBox(T item) { Item = item; _hidden = item; }
                public T Get() => Item;
                public T Peek() => _hidden;
                internal T Internal() => _hidden;
                private T Secret() => _hidden;
                public T Reveal() => Secret();
            }

            [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
            public static class Driver
            {
                public static string Run()
                {
                    var b = new PubBox<int>(5);
                    var s = new PubBox<string>("s");
                    return b.Item + "/" + b.Get() + "/" + b.Peek() + "/" + b.Reveal() + "/" + s.Item + s.Get() + s.Reveal();
                }
            }
            """;
        var dir = NewTempDir();
        try
        {
            var input = Compile(source, dir, "GenericShape");
            var expected = Run(input);
            var output = Path.Combine(dir, "GenericShape.obf.dll");
            var settings = SettingsFor("rename-only");
            settings.SymbolRenaming.PreservePublicApi = true;
            var result = await CreateService().ObfuscateAsync(input, output, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using (var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output)))
            {
                var pub = protectedModule.Types.Single(t => t.Name.String.StartsWith("PubBox", StringComparison.Ordinal));
                pub.Fields.Select(f => f.Name.String).ShouldContain("Item", "public names are kept");
                pub.Methods.Select(m => m.Name.String).ShouldContain("Get");
                pub.Methods.Select(m => m.Name.String).ShouldNotContain("Secret", "private names are renamed");
                pub.Fields.Select(f => f.Name.String).ShouldNotContain("_hidden");
                StaleMemberReferenceScanner.Find(protectedModule).ShouldBeEmpty();
            }

            Run(output).ShouldBe(expected);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task ClosedSet_GenericTypeDefinedInOneAssemblyAndInstantiatedInAnother()
    {
        const string library = """
            using System;
            using System.Collections.Generic;

            public class SharedBox<T>
            {
                public T Item;
                public static int Created;
                public SharedBox(T item) { Item = item; Created++; }
                public T Get() => Item;
                public void Set(T value) { Item = value; Changed?.Invoke(value); }
                public static SharedBox<T> Make(T item) => new SharedBox<T>(item);
                public U Convert<U>(Func<T, U> f) => f(Item);
                public T Value { get; set; }
                public event Action<T> Changed;
                public class Nested<U> { public U Other; public T Pick(T t) => t; }
            }

            public struct SharedPair<T>
            {
                public T First;
                public T Second;
                public SharedPair(T a, T b) { First = a; Second = b; }
                public T Pick(bool f) => f ? First : Second;
            }

            public class SharedBase<T>
            {
                public T Item;
                public SharedBase(T i) { Item = i; }
                public T Get() => Item;
            }

            // Same names and signatures the runtime gives an array of Cell.
            public class Cell
            {
                public int N;
                public Cell(int n) { N = n; }
                public Cell Get(int a, int b) => new Cell(N + a + b);
            }
            """;
        const string app = """
            using System;
            using System.Collections.Generic;

            internal class LocalBox<T>
            {
                public T Item;
                public LocalBox(T item) { Item = item; }
                public T Get() => Item;
            }

            internal class FromLibrary : SharedBase<int>
            {
                public FromLibrary(int i) : base(i) { }
                public int Twice() => base.Get() + Item;
            }

            public static class Program { public static int Main() => 0; }

            [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
            public static class Driver
            {
                public static string Run()
                {
                    var a = new SharedBox<int>(42);
                    var b = new SharedBox<string>("s");
                    var seen = 0;
                    a.Changed += v => seen += v;
                    a.Set(5);
                    a.Value = a.Item + 1;
                    var m = SharedBox<int>.Make(7);
                    var inner = new SharedBox<int>.Nested<string> { Other = "o" };
                    var pair = new SharedPair<int>(1, 2);
                    pair.First += 10;
                    var local = new LocalBox<SharedBox<int>>(a);
                    var cells = new Cell[2, 2];
                    cells[0, 1] = new Cell(10);
                    var arr = new SharedBox<int>[2];
                    arr[1] = m;
                    return a.Item + "/" + a.Get() + "/" + a.Value + "/" + b.Item + b.Get() + "/" + m.Get() + "/" + seen
                        + "/" + SharedBox<int>.Created + "/" + a.Convert(x => x * 2) + "/" + inner.Other + inner.Pick(3)
                        + "/" + pair.First + pair.Pick(false) + "/" + local.Item.Get() + local.Get().Item + "/"
                        + new FromLibrary(4).Twice() + "/" + cells[0, 1].Get(1, 1).N + "/" + arr[1].Get();
                }
            }
            """;
        var dir = NewTempDir();
        try
        {
            var libPath = Compile(library, dir, "SharedLib");
            var appPath = Compile(app, dir, "GenericApp", OutputKind.ConsoleApplication, libPath);
            var outDir = Path.Combine(dir, "out");

            var expected = Run(appPath);
            expected.StartsWith("!", StringComparison.Ordinal).ShouldBeFalse($"the unprotected pair itself failed: {expected}");

            var result = await CreateService().ObfuscateClosedSetAsync(
                new[] { ClosedSetInput.From(appPath), ClosedSetInput.From(libPath) }, outDir, SettingsFor("rename-only"));
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using (var loadedApp = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(outDir, "GenericApp.dll"))))
            using (var loadedLib = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(outDir, "SharedLib.dll"))))
            {
                loadedLib.GetTypes().ShouldNotContain(t => t.Name.String.StartsWith("SharedBox", StringComparison.Ordinal), "the library must have been renamed");
                loadedApp.GetTypes().ShouldNotContain(t => t.Name.String.StartsWith("LocalBox", StringComparison.Ordinal), "the app's own generic type must have been renamed");
                StaleMemberReferenceScanner.Find(new ModuleDef[] { loadedApp, loadedLib }).ShouldBeEmpty();
            }

            Run(Path.Combine(outDir, "GenericApp.dll")).ShouldBe(expected);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>
    /// A real application is the broadest sample there is: protect Obfy's own core assembly (generic helper
    /// types, iterators, async, lambdas, records, collection expressions) and scan what comes out.
    /// </summary>
    [Fact]
    public async Task ProtectingObfyCoreItself_LeavesNoStaleMemberReferences()
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
            result.Statistics.TypesRenamed.ShouldBeGreaterThan(100);

            using var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output));
            StaleMemberReferenceScanner.Find(protectedModule).ShouldBeEmpty();
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static ObfySettings SettingsFor(string configuration)
    {
        if (configuration == "standard")
            return new ObfySettings { Level = ObfuscationLevel.Standard };

        return new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            StringEncryption = { Enabled = false },
            ControlFlow = { Enabled = false },
            Metadata = { RemoveDebugInfo = false, RemoveAttributes = false },
            SymbolRenaming = { Enabled = true, PreservePublicApi = false }
        };
    }

    /// <summary>Names of every field and method the module's types declare, except <c>Driver</c> (which keeps its own).</summary>
    private static HashSet<string> KeptNames(ModuleDef module)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in module.GetTypes().Where(t => t.Name.String != "Driver"))
        {
            foreach (var field in type.Fields)
                names.Add(field.Name.String);
            foreach (var method in type.Methods)
                names.Add(method.Name.String);
            foreach (var property in type.Properties)
                names.Add(property.Name.String);
            foreach (var evt in type.Events)
                names.Add(evt.Name.String);
        }

        return names;
    }

    /// <summary>Names of the MemberRefs whose parent is a generic instantiation of a type from outside the module.</summary>
    private static HashSet<string> MemberRefNamesOnGenericInstantiations(ModuleDefMD module)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (uint rid = 1; rid <= module.TablesStream.MemberRefTable.Rows; rid++)
        {
            var reference = module.ResolveMemberRef(rid);
            if (reference.Class is TypeSpec { TypeSig: GenericInstSig { GenericType.TypeDefOrRef: TypeRef } })
                names.Add(reference.Name.String);
            else if (reference.Class is TypeRef)
                names.Add(reference.Name.String);
        }

        return names;
    }

    private static IObfuscationService CreateService()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule<ObfuscationModule>();
        return builder.Build().Resolve<IObfuscationService>();
    }

    private static readonly Lazy<List<MetadataReference>> RuntimeReferences = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList());

    private static string Compile(
        string source,
        string dir,
        string assemblyName,
        OutputKind kind = OutputKind.DynamicallyLinkedLibrary,
        string? referencePath = null)
    {
        var references = new List<MetadataReference>(RuntimeReferences.Value);
        if (referencePath is not null)
            references.Add(MetadataReference.CreateFromFile(referencePath));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(kind, optimizationLevel: OptimizationLevel.Release, allowUnsafe: true));
        var path = Path.Combine(dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    /// <summary>
    /// Loads the assembly into its own collectible context, runs <c>Driver.Run()</c> and returns what it returned,
    /// or the failure prefixed with <c>!</c> (so a missing member shows up as the answer, not as a crash).
    /// </summary>
    private static string Run(string assemblyPath)
    {
        var context = new IsolatedLoadContext(assemblyPath);
        try
        {
            var method = context.LoadFromAssemblyPath(assemblyPath).GetType("Driver")!.GetMethod("Run")!;
            return (string)method.Invoke(null, null)!;
        }
        catch (TargetInvocationException ex)
        {
            return "!" + ex.InnerException!.GetType().Name + ": " + ex.InnerException.Message;
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>Resolves siblings of the assembly from its own folder, so each copy sees its own library.</summary>
    private sealed class IsolatedLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;

        public IsolatedLoadContext(string assemblyPath)
            : base("generic-rename-" + Guid.NewGuid().ToString("N"), isCollectible: true) =>
            _directory = Path.GetDirectoryName(assemblyPath)!;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var candidate = Path.Combine(_directory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obfy-generic-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* ignore */ }
    }
}

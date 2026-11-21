using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Obfy.Core.Models.Solution;
using Shouldly;
using static Obfy.Tests.RenamingTestSupport;

namespace Obfy.Tests;

/// <summary>
/// A member reference names the type it is looked up on, and the CLR finds a method there or on a base type (comparing
/// signatures with the base type's generic arguments substituted). C#, VB and F# always name the declaring type, so no
/// compiler writes a reference whose parent is a DERIVED type, but the metadata allows it and the renamer used to look
/// for the member on the named type only: such a reference kept its old name, the run printed "MemberRef(s) ...
/// were not rewritten" and the output died with <c>MissingMethodException</c>. The input here is hand-built: Roslyn
/// compiles the program, then dnlib re-parents the member references of <c>Driver.Run</c> from the declaring type to a
/// derived one (and writes the signature the way the derived type sees it).
/// </summary>
public class SymbolRenamingDerivedParentTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;

        """;

    private const string Definitions = """
        public class ZBox<T>
        {
            public T Item;
            public static int Count;
            public ZBox(T item) { Item = item; Count++; }
            public T Get() => Item;
            public string Show(T value) => "show:" + value;
            public U Map<U>(Func<T, U> f) => f(Item);
            public static string Make(T value) => "make:" + value;
        }

        public class IntBox : ZBox<int>
        {
            public IntBox(int item) : base(item) { }
        }

        public class Mid<T> : ZBox<List<T>>
        {
            public Mid(List<T> items) : base(items) { }
        }

        public class Leaf : Mid<string>
        {
            public Leaf() : base(new List<string> { "a", "b" }) { }
        }

        public class Plain
        {
            public int Value = 3;
            public int Twice() => Value * 2;
            public static int Shared() => 7;
        }

        public class PlainDerived : Plain { }

        """;

    private const string App = """
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        public static class Driver
        {
            public static string Run()
            {
                var ib = new IntBox(5);
                var leaf = new Leaf();
                var plain = new PlainDerived();
                return ib.Get() + "/" + ib.Item + "/" + ib.Show(3) + "/" + ib.Map(x => x + 1) + "/" + ZBox<int>.Make(2) + "/" + ZBox<int>.Count
                    + "/" + leaf.Get().Count + leaf.Item[0] + leaf.Show(new List<string> { "z" }).Length + "/" + leaf.Map(l => l.Count)
                    + "/" + plain.Twice() + plain.Value + "/" + Plain.Shared();
            }
        }
        """;

    /// <summary>
    /// <c>derived-class</c>: the references name <c>IntBox</c> (a class deriving from <c>ZBox&lt;int&gt;</c>), <c>Leaf</c> (two
    /// levels down, through the generic <c>Mid&lt;string&gt;</c>) and <c>PlainDerived</c>; <c>derived-generic</c>: the
    /// references to <c>ZBox&lt;List&lt;string&gt;&gt;</c> name the generic <c>Mid&lt;string&gt;</c> instead.
    /// </summary>
    public static TheoryData<string, string> Runs
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var shape in new[] { "single", "closed-set" })
                foreach (var variant in new[] { "derived-class", "derived-generic" })
                    data.Add(shape, variant);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Runs))]
    public async Task MethodReferencesThroughADerivedType_AreRewrittenLikeTheOthers(string shape, string variant)
    {
        var dir = NewTempDir();
        try
        {
            var (inputs, driverAssembly) = BuildShape(shape, dir, Usings, Definitions, "", App);
            // The compiled program runs from a copy: a loaded assembly keeps its file locked, and the input is rewritten below.
            var pristine = Path.Combine(dir, "pristine");
            Directory.CreateDirectory(pristine);
            foreach (var input in inputs)
                File.Copy(input, Path.Combine(pristine, Path.GetFileName(input)));
            var expected = Run(Path.Combine(pristine, Path.GetFileName(driverAssembly)));
            expected.StartsWith("!", StringComparison.Ordinal).ShouldBeFalse($"the compiled program itself failed: {expected}");

            ReparentToDerivedTypes(driverAssembly, shape, variant, fields: false);
            Run(driverAssembly).ShouldBe(expected, "the hand-built input must run like the compiled one, or it is not a shape the CLR accepts");

            var outDir = Path.Combine(dir, "out");
            var failures = await ProtectAndCheck(shape, inputs, outDir);
            var actual = Run(Path.Combine(outDir, Path.GetFileName(driverAssembly)));
            if (actual != expected)
                failures.Add($"the protected program answered '{actual}', the original '{expected}'");

            failures.ShouldBeEmpty($"{shape}, {variant}");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>
    /// CoreCLR itself binds a field reference only on the type it names (a reference to <c>Item</c> through
    /// <c>IntBox</c> is a <c>MissingFieldException</c> even before protection), but other runtimes (Mono, Unity) look
    /// the field up the base chain, so the renamer treats a field reference like a method reference. This case cannot
    /// be run: it checks the output's metadata.
    /// </summary>
    [Theory]
    [MemberData(nameof(Runs))]
    public async Task FieldReferencesThroughADerivedType_AreRewrittenToo(string shape, string variant)
    {
        var dir = NewTempDir();
        try
        {
            var (inputs, driverAssembly) = BuildShape(shape, dir, Usings, Definitions, "", App);
            ReparentToDerivedTypes(driverAssembly, shape, variant, fields: true);

            (await ProtectAndCheck(shape, inputs, Path.Combine(dir, "out"))).ShouldBeEmpty($"{shape}, {variant}");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>
    /// Protects the inputs and returns what is wrong with the output: a "not rewritten" warning of the run, a type that
    /// kept its name, a reference that names a member its definition no longer has.
    /// </summary>
    private static async Task<List<string>> ProtectAndCheck(string shape, List<string> inputs, string outDir)
    {
        var failures = new List<string>();
        var settings = SettingsFor("rename-only");
        var warnings = new List<string>();
        List<string> outputs;
        if (shape == "closed-set")
        {
            var result = await CreateService().ObfuscateClosedSetAsync(
                inputs.Select(i => ClosedSetInput.From(i)).ToList(), outDir, settings);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            warnings.AddRange(result.ModuleResults.SelectMany(r => r.Warnings));
            outputs = inputs.Select(i => Path.Combine(outDir, Path.GetFileName(i))).ToList();
        }
        else
        {
            Directory.CreateDirectory(outDir);
            var output = Path.Combine(outDir, Path.GetFileName(inputs[0]));
            var single = await CreateService().ObfuscateAsync(inputs[0], output, settings);
            single.Success.ShouldBeTrue(single.ErrorMessage);
            warnings.AddRange(single.Warnings);
            outputs = new List<string> { output };
        }

        failures.AddRange(warnings.Where(w => w.Contains("not rewritten")).Select(w => "warning: " + w));

        var loaded = outputs.Select(o => ModuleDefMD.Load(File.ReadAllBytes(o))).ToList();
        try
        {
            if (loaded.SelectMany(m => m.GetTypes()).Any(t => t.Name.String == "ZBox`1"))
                failures.Add("the generic type was not renamed");
            failures.AddRange(StaleMemberReferenceScanner.Find(loaded.Cast<ModuleDef>().ToList()).Select(f => "stale reference: " + f));
        }
        finally
        {
            loaded.ForEach(m => m.Dispose());
        }

        return failures;
    }

    /// <summary>
    /// Rewrites the method references (or, with <paramref name="fields"/>, the field references) of <c>Driver.Run</c> so
    /// that they name a derived type instead of the declaring one, with the signature the derived type sees.
    /// </summary>
    private static void ReparentToDerivedTypes(string path, string shape, string variant, bool fields)
    {
        byte[] bytes;
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(path)))
        {
            var library = shape == "single" ? null : module.GetAssemblyRefs().Single(a => a.Name == "ShapeLib");

            ITypeDefOrRef Derived(string name)
            {
                if (library is null)
                    return module.Types.Single(t => t.Name == name);
                var existing = module.GetTypeRefs().FirstOrDefault(t => t.Name == name);
                return existing ?? module.UpdateRowId(new TypeRefUser(module, UTF8String.Empty, name, library));
            }

            var run = module.Types.Single(t => t.Name == "Driver").Methods.Single(m => m.Name == "Run");
            foreach (var instruction in run.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case MemberRef memberRef when memberRef.IsFieldRef == fields:
                        ReparentMemberRef(module, memberRef, Derived, variant);
                        break;
                    case MethodSpec { Method: MemberRef viaSpec } when !fields:
                        ReparentMemberRef(module, viaSpec, Derived, variant);
                        break;
                    case MethodDef { DeclaringType.Name.String: "Plain" } direct when !fields:
                        instruction.Operand = module.UpdateRowId(new MemberRefUser(module, direct.Name, direct.MethodSig, Derived("PlainDerived")));
                        break;
                    case FieldDef { DeclaringType.Name.String: "Plain" } directField when fields:
                        instruction.Operand = module.UpdateRowId(new MemberRefUser(module, directField.Name, directField.FieldSig, Derived("PlainDerived")));
                        break;
                }
            }

            using var stream = new MemoryStream();
            module.Write(stream);
            bytes = stream.ToArray();
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void ReparentMemberRef(ModuleDef module, MemberRef memberRef, Func<string, ITypeDefOrRef> derived, string variant)
    {
        if (memberRef.Class is TypeRef { Name.String: "Plain" })
        {
            memberRef.Class = derived("PlainDerived");
            return;
        }

        if (memberRef.Class is not TypeSpec { TypeSig: GenericInstSig instantiation }
            || instantiation.GenericType.TypeDefOrRef.Name != "ZBox`1")
            return;

        var argument = instantiation.GenericArguments[0];
        if (argument.ElementType == ElementType.I4)
        {
            // ZBox<int> is the base of IntBox: the derived type sees the same signature, instantiated.
            Substitute(memberRef, instantiation.GenericArguments);
            memberRef.Class = derived("IntBox");
        }
        else if (variant == "derived-class")
        {
            // ZBox<List<string>> is the base of Mid<string>, the base of Leaf.
            Substitute(memberRef, instantiation.GenericArguments);
            memberRef.Class = derived("Leaf");
        }
        else
        {
            // Through the generic Mid<string>: the signature is written in terms of Mid's own parameter, !0, so the
            // base's ZBox<List<string>> reads ZBox<List<!0>>.
            var list = (GenericInstSig)argument;
            var listOfMidParameter = new GenericInstSig(list.GenericType, new TypeSig[] { new GenericVar(0) });
            Substitute(memberRef, new TypeSig[] { listOfMidParameter });
            memberRef.Class = module.UpdateRowId(new TypeSpecUser(
                new GenericInstSig(new ClassSig(derived("Mid`1")), module.CorLibTypes.String)));
        }
    }

    /// <summary>Writes the member's signature as seen through an instantiation of its declaring generic type.</summary>
    private static void Substitute(MemberRef memberRef, IList<TypeSig> arguments) =>
        memberRef.Signature = memberRef.IsFieldRef
            ? TypeSigSubstitution.Substitute(memberRef.FieldSig, arguments)
            : TypeSigSubstitution.Substitute(memberRef.MethodSig, arguments);

    /// <summary>
    /// Several members of one generic base can share a signature once the base is instantiated (<c>Pick(T)</c> and
    /// <c>Pick(int)</c> on <c>Base&lt;int&gt;</c>). The runtime, faced with a reference that names the derived type,
    /// prefers a virtual match over a non-virtual one and otherwise takes the last match in metadata order. Hiding,
    /// an explicit interface method under its own name, and an overload that does not collapse must keep the member
    /// the reference actually names. The call is hand-built: no compiler emits a derived parent.
    /// </summary>
    [Theory]
    [InlineData("nonvirtual-generic-first", "Pick", false, "2")]
    [InlineData("nonvirtual-int-first", "Pick", false, "1")]
    [InlineData("plain-then-virtual", "Pick", false, "1")]
    [InlineData("virtual-then-plain", "Pick", false, "1")]
    [InlineData("two-virtuals-generic-first", "Pick", false, "2")]
    [InlineData("two-virtuals-int-first", "Pick", false, "1")]
    [InlineData("hiding", "Value", false, "2")]
    [InlineData("explicit-interface", "M", false, "1")]
    [InlineData("distinct-overload", "Pick", false, "1")]
    [InlineData("distinct-through-generic-base", "Pick", false, "1")]
    [InlineData("collapsed-through-generic-base", "Pick", false, "2")]
    [InlineData("hiding-field", "Item", true, "2")]
    public async Task CollapsedSignaturesOnABase_AreBoundLikeTheRuntime(string name, string member, bool isField, string expected)
    {
        var dir = NewTempDir();
        try
        {
            var path = Compile(Usings + BindingSource(name) + BindingDriver, dir, "ShapeApp");
            InstallDerivedCall(path, member, isField);
            Run(path).ShouldBe(expected, $"{name}: the hand-built call is not what this runtime binds");

            var outDir = Path.Combine(dir, "out");
            Directory.CreateDirectory(outDir);
            var output = Path.Combine(outDir, Path.GetFileName(path));
            var result = await CreateService().ObfuscateAsync(path, output, SettingsFor("rename-only"));
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.Warnings.Any(w => w.Contains("not rewritten", StringComparison.Ordinal))
                .ShouldBeFalse(string.Join(" | ", result.Warnings));

            Run(output).ShouldBe(expected, name);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private const string BindingDriver = """

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        public static class Driver
        {
            public static string Run() => "unset";
        }
        """;

    private static string BindingSource(string name) => name switch
    {
        "nonvirtual-generic-first" => GenericBase("""
            public int Pick(T value) => 1;
            public int Pick(int value) => 2;
            """),
        "nonvirtual-int-first" => GenericBase("""
            public int Pick(int value) => 2;
            public int Pick(T value) => 1;
            """),
        "plain-then-virtual" => GenericBase("""
            public int Pick(int value) => 2;
            public virtual int Pick(T value) => 1;
            """),
        "virtual-then-plain" => GenericBase("""
            public virtual int Pick(T value) => 1;
            public int Pick(int value) => 2;
            """),
        "two-virtuals-generic-first" => GenericBase("""
            public virtual int Pick(T value) => 1;
            public virtual int Pick(int value) => 2;
            """),
        "two-virtuals-int-first" => GenericBase("""
            public virtual int Pick(int value) => 2;
            public virtual int Pick(T value) => 1;
            """),
        "distinct-overload" => GenericBase("""
            public int Pick(T value) => 1;
            public int Pick(string value) => 2;
            """),
        "distinct-through-generic-base" => GenericBase("""
            public int Pick(T value) => 1;
            public int Pick(string value) => 2;
            """, derivedBase: "Mid<int>", after: "public class Mid<U> : Base<U> { }"),
        "collapsed-through-generic-base" => GenericBase("""
            public int Pick(T value) => 1;
            public int Pick(int value) => 2;
            """, derivedBase: "Mid<int>", after: "public class Mid<U> : Base<U> { }"),
        "hiding" => """
            public class Base
            {
                public int Value() => 1;
            }

            public class Derived : Base
            {
                public new int Value() => 2;
                public Derived() { }
            }
            """,
        "explicit-interface" => """
            public interface IFace
            {
                int Other();
            }

            public class Base
            {
                public int M() => 1;
            }

            public class Derived : Base, IFace
            {
                int IFace.Other() => 9;
                public Derived() { }
            }
            """,
        "hiding-field" => """
            public class Base<T>
            {
                public T Item;
            }

            public class Derived : Base<int>
            {
                public new int Item = 2;
                public Derived() { }
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    private static string GenericBase(string methods, string derivedBase = "Base<int>", string after = "") => $$"""
        public class Base<T>
        {
            {{methods}}
        }

        {{after}}
        public class Derived : {{derivedBase}}
        {
            public Derived() { }
        }
        """;

    /// <summary>
    /// Replaces <c>Driver.Run</c> with <c>new Derived().Member(...).ToString()</c>. The member reference names
    /// <c>Derived</c> and, for <c>Pick</c>, carries the instantiated signature <c>int32 (int32)</c>.
    /// </summary>
    private static void InstallDerivedCall(string path, string member, bool isField)
    {
        byte[] bytes;
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(path)))
        {
            var derived = module.Types.Single(t => t.Name == "Derived");
            var ctor = derived.Methods.Single(m => m.IsConstructor && !m.IsStatic);
            var run = module.Types.Single(t => t.Name == "Driver").Methods.Single(m => m.Name == "Run");

            MemberRef target = isField
                ? new MemberRefUser(module, member, new FieldSig(module.CorLibTypes.Int32), derived)
                : new MemberRefUser(
                    module,
                    member,
                    member == "Pick"
                        ? MethodSig.CreateInstance(module.CorLibTypes.Int32, module.CorLibTypes.Int32)
                        : MethodSig.CreateInstance(module.CorLibTypes.Int32),
                    derived);
            module.UpdateRowId(target);

            var toString = module.UpdateRowId(new MemberRefUser(
                module,
                "ToString",
                MethodSig.CreateInstance(module.CorLibTypes.String),
                module.CorLibTypes.Object.TypeDefOrRef));

            var body = new CilBody { MaxStack = 3 };
            body.Instructions.Add(Instruction.Create(OpCodes.Newobj, ctor));
            if (isField)
            {
                body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, target));
            }
            else
            {
                if (member == "Pick")
                    body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
                body.Instructions.Add(Instruction.Create(OpCodes.Call, target));
            }

            body.Instructions.Add(Instruction.Create(OpCodes.Box, module.CorLibTypes.Int32.TypeDefOrRef));
            body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, toString));
            body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            run.Body = body;

            using var stream = new MemoryStream();
            module.Write(stream);
            bytes = stream.ToArray();
        }

        File.WriteAllBytes(path, bytes);
    }
}

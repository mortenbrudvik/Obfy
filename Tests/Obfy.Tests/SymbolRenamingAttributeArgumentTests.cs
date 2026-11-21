using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Shouldly;
using static Obfy.Tests.RenamingTestSupport;

namespace Obfy.Tests;

/// <summary>
/// Named arguments of custom attributes. <c>[Info(Note = "x")]</c> stores the member's NAME in the attribute blob, and
/// the CLR looks it up when the attribute is read: after the renamer renamed <c>Note</c> the read threw
/// <c>CustomAttributeFormatException: 'Note' property specified was not found</c>. Every case runs the protected
/// library, reads the attributes it carries through reflection (values only: the member names are what changes) and
/// demands what the unprotected library answered.
/// </summary>
public class SymbolRenamingAttributeArgumentTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Reflection;

        """;

    /// <summary>Assembly and module attributes must come before every type of the file.</summary>
    private const string AssemblyAttributes = """
        [assembly: Info("assembly", Note = "asm", Level = 11)]
        [module: Info("module", Note = "mod", Kind = K.B)]

        """;

    /// <summary>
    /// The attributes, public so that another assembly can apply them. A property, a field, an enum-typed property,
    /// <c>typeof</c> arguments (positional, named and array), an object-typed property holding an enum, an attribute
    /// inheriting its named members from in-module base attributes (one of them a generic instantiation), a generic
    /// attribute, and one member that keeps its name (<c>Keep</c>).
    /// </summary>
    private const string Definitions = """
        public enum K { A, B }

        public class Box<T> { public T Item; public Box(T item) { Item = item; } }

        public class Plain { }

        [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
        public class InfoAttribute : Attribute
        {
            public InfoAttribute(string label) { Label = label; }
            public InfoAttribute(string label, Type of) { Label = label; Of = of; }
            public string Label { get; }
            public Type Of { get; }
            public string Note { get; set; }
            public int Level;
            public K Kind { get; set; }
            public Type Type { get; set; }
            public string[] Tags { get; set; }
            public Type[] Types { get; set; }
            public K[] Kinds { get; set; }
            public object Obj { get; set; }
            [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
            public string Keep { get; set; }
        }

        [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
        public class BaseInfoAttribute : Attribute
        {
            public BaseInfoAttribute(string label) { Label = label; }
            public string Label { get; }
            public string BaseNote { get; set; }
            public int BaseLevel;
        }

        public class DerivedInfoAttribute : BaseInfoAttribute
        {
            public DerivedInfoAttribute(string label) : base(label) { }
            public string Extra { get; set; }
        }

        public class LeafInfoAttribute : DerivedInfoAttribute
        {
            public LeafInfoAttribute(string label) : base(label) { }
            public K LeafKind { get; set; }
        }

        [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
        public class GenInfoAttribute<T> : Attribute
        {
            public GenInfoAttribute(string label) { Label = label; }
            public string Label { get; }
            public string GenNote { get; set; }
            public int GenLevel;
            public T GenValue { get; set; }
        }

        public class IntInfoAttribute : GenInfoAttribute<int>
        {
            public IntInfoAttribute(string label) : base(label) { }
        }

        """;

    /// <summary>
    /// Every place an attribute can sit that reflection reaches: assembly, module, type, generic parameter, field,
    /// property, event, method, parameter and return value. <c>Driver</c> prints the values it reads.
    /// </summary>
    private const string Uses = """
        [Info("type", Note = "t", Level = 3, Kind = K.B, Type = typeof(Box<int>), Tags = new[] { "a", "b" }, Keep = "kept")]
        [Info("typeof", typeof(Box<int>))]
        [Info("typeof-plain", typeof(Plain), Types = new[] { typeof(Box<string>), typeof(Plain) }, Obj = K.B)]
        [Info("object", Obj = 5, Type = typeof(Plain))]
        [DerivedInfo("derived", BaseNote = "bn", BaseLevel = 4, Extra = "ex")]
        [LeafInfo("leaf", BaseNote = "lbn", Extra = "lex", LeafKind = K.B, BaseLevel = 9)]
        [GenInfo<int>("generic", GenNote = "gn", GenLevel = 6)]
        [GenInfo<Box<string>>("generic-box", GenNote = "gb")]
        [IntInfo("int-info", GenNote = "in", GenLevel = 8)]
        [Info("enum-array", Kinds = new[] { K.A, K.B })]
        [GenInfo<K>("generic-enum", GenNote = "gk", GenValue = K.B)]
        internal class Target
        {
            [Info("field", Note = "f", Level = 1)]
            public int F;

            [Info("property", Note = "p", Kind = K.B)]
            public int P { get; set; }

            [Info("event", Note = "e", Tags = new[] { "x" })]
            public event Action E;

            [Info("method", Note = "m", Level = 2, Kind = K.A)]
            [return: Info("return", Note = "r", Obj = 5, Type = typeof(Box<int>))]
            public string M([Info("param", Note = "q", Level = 7)] int x, [DerivedInfo("param-derived", Extra = "pe")] string y)
            {
                E?.Invoke();
                return x + y;
            }

            [GenInfo<string>("generic-method", GenNote = "gm")]
            public static T G<[Info("method-gp", Note = "mg")] T>(T value) => value;
        }

        internal class GenTarget<[Info("class-gp", Note = "cg", Level = 12)] T>
        {
            public T Value;
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        public static class Driver
        {
            public static string Run()
            {
                var lines = new List<string>();
                void Add(string where, ICustomAttributeProvider provider)
                {
                    foreach (var attribute in provider.GetCustomAttributes(false))
                    {
                        var text = Format(attribute);
                        if (text != null)
                            lines.Add(where + ":" + text);
                    }
                }

                const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly;
                Add("assembly", typeof(Driver).Assembly);
                Add("module", typeof(Driver).Module);
                Add("type", typeof(Target));
                Add("class-gp", typeof(GenTarget<>).GetGenericArguments()[0]);
                foreach (var field in typeof(Target).GetFields(All)) Add("field", field);
                foreach (var property in typeof(Target).GetProperties(All)) Add("property", property);
                foreach (var evt in typeof(Target).GetEvents(All)) Add("event", evt);
                foreach (var method in typeof(Target).GetMethods(All))
                {
                    Add("method", method);
                    Add("return", method.ReturnParameter);
                    foreach (var parameter in method.GetParameters()) Add("param", parameter);
                    if (method.IsGenericMethodDefinition)
                        Add("method-gp", method.GetGenericArguments()[0]);
                }

                lines.Sort(StringComparer.Ordinal);
                return string.Join("\n", lines);
            }

            private static string Types(Type[] types) =>
                types == null ? "-" : string.Join(",", types.Select(t =>
                    t == typeof(Plain) ? "Plain" : t == typeof(Box<string>) ? "BoxS" : t == typeof(Box<int>) ? "BoxI" : "?"));

            private static string Format(object attribute)
            {
                switch (attribute)
                {
                    case LeafInfoAttribute leaf:
                        return $"Leaf[{leaf.Label}|{leaf.BaseNote}|{leaf.BaseLevel}|{leaf.Extra}|{leaf.LeafKind}]";
                    case DerivedInfoAttribute derived:
                        return $"Derived[{derived.Label}|{derived.BaseNote}|{derived.BaseLevel}|{derived.Extra}]";
                    case InfoAttribute info:
                        return $"Info[{info.Label}|{info.Note}|{info.Level}|{info.Kind}|{info.Type == typeof(Box<int>)}"
                            + $"|{info.Type == typeof(Plain)}|{info.Of == typeof(Box<int>)}|{info.Of == typeof(Plain)}"
                            + $"|{(info.Tags == null ? "-" : string.Join(",", info.Tags))}|{Types(info.Types)}|{info.Obj}|{info.Keep}"
                            + $"|{(info.Kinds == null ? "-" : string.Join(",", info.Kinds))}]";
                    case GenInfoAttribute<K> genericEnum:
                        return $"Gen<K>[{genericEnum.Label}|{genericEnum.GenNote}|{genericEnum.GenValue}]";
                    case IntInfoAttribute intInfo:
                        return $"Int[{intInfo.Label}|{intInfo.GenNote}|{intInfo.GenLevel}]";
                    case GenInfoAttribute<int> generic:
                        return $"Gen<int>[{generic.Label}|{generic.GenNote}|{generic.GenLevel}]";
                    case GenInfoAttribute<string> generic:
                        return $"Gen<string>[{generic.Label}|{generic.GenNote}|{generic.GenLevel}]";
                    case GenInfoAttribute<Box<string>> generic:
                        return $"Gen<Box<string>>[{generic.Label}|{generic.GenNote}|{generic.GenLevel}]";
                    default:
                        return null;
                }
            }
        }
        """;

    /// <summary>
    /// <c>typeof(X)</c> in an attribute is stored as the type's NAME (a string), so a renamed type has to be renamed
    /// there too. Positional (alone, so that nothing else can fail first), named, array-valued and nested generic.
    /// </summary>
    private const string TypeofUses = """
        [Info("positional-generic", typeof(Box<int>))]
        [Info("positional-nested-generic", typeof(Box<Box<string>>))]
        [Info("positional-plain", typeof(Plain))]
        [Info("named", Type = typeof(Box<int>))]
        [Info("named-array", Types = new[] { typeof(Box<string>), typeof(Plain), typeof(Box<Plain>) })]
        internal class TypeofTarget { }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        public static class Driver
        {
            public static string Run()
            {
                var lines = new List<string>();
                foreach (var attribute in typeof(TypeofTarget).GetCustomAttributes(false).OfType<InfoAttribute>())
                {
                    lines.Add(attribute.Label
                        + "|" + (attribute.Of == typeof(Box<int>)) + (attribute.Of == typeof(Box<Box<string>>)) + (attribute.Of == typeof(Plain))
                        + "|" + (attribute.Type == typeof(Box<int>))
                        + "|" + (attribute.Types == null ? "-" : string.Join(",", attribute.Types.Select(t =>
                            t == typeof(Box<string>) ? "BoxS" : t == typeof(Plain) ? "Plain" : t == typeof(Box<Plain>) ? "BoxP" : "?"))));
                }

                lines.Sort(StringComparer.Ordinal);
                return string.Join("\n", lines);
            }
        }
        """;

    private static readonly string[] RenamedMembers =
        { "Note", "Level", "Kind", "Tags", "Types", "Kinds", "Obj", "BaseNote", "BaseLevel", "Extra", "LeafKind", "GenNote", "GenLevel", "GenValue" };

    public static TheoryData<string, string> Runs
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var shape in new[] { "single", "closed-set", "merge" })
                foreach (var configuration in new[] { "rename-only", "minimal", "standard", "aggressive" })
                    data.Add(shape, configuration);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Runs))]
    public async Task NamedArgumentsOfRenamedAttributeMembers_ReadLikeTheOriginal(string shape, string configuration)
    {
        var dir = NewTempDir();
        try
        {
            var (inputs, driverAssembly) = Build(shape, dir);
            var expected = Run(driverAssembly);
            expected.StartsWith("!", StringComparison.Ordinal).ShouldBeFalse($"the unprotected program itself failed: {expected}");
            expected.Split('\n').Length.ShouldBe(23, "the sample must carry attributes on every kind of target");

            var outDir = Path.Combine(dir, "out");
            var outputs = await ProtectAsync(shape, inputs, outDir, SettingsFor(configuration));

            var loaded = outputs.Select(o => ModuleDefMD.Load(File.ReadAllBytes(o))).ToList();
            try
            {
                var kept = loaded.SelectMany(m => m.GetTypes().Where(t => t.Name.String != "Driver"))
                    .SelectMany(t => t.Properties.Select(p => p.Name.String).Concat(t.Fields.Select(f => f.Name.String)))
                    .ToHashSet(StringComparer.Ordinal);
                RenamedMembers.Where(kept.Contains).ShouldBeEmpty("the attribute members must really have been renamed");
                kept.ShouldContain("Keep", "an excluded member keeps its name");

                StaleMemberReferenceScanner.Find(loaded.Cast<ModuleDef>().ToList()).ShouldBeEmpty();
            }
            finally
            {
                loaded.ForEach(m => m.Dispose());
            }

            var driverOutput = shape == "merge" ? outputs[0] : Path.Combine(outDir, Path.GetFileName(driverAssembly));
            Run(driverOutput).ShouldBe(expected, $"{shape}, {configuration}");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Theory]
    [InlineData("single", "rename-only")]
    [InlineData("single", "standard")]
    [InlineData("closed-set", "rename-only")]
    [InlineData("closed-set", "standard")]
    [InlineData("merge", "rename-only")]
    [InlineData("merge", "standard")]
    public async Task TypeofArguments_StillNameTheRenamedTypes(string shape, string configuration)
    {
        var dir = NewTempDir();
        try
        {
            var (inputs, driverAssembly) = Build(shape, dir, TypeofUses, assemblyAttributes: "");
            var expected = Run(driverAssembly);
            expected.StartsWith("!", StringComparison.Ordinal).ShouldBeFalse($"the unprotected program itself failed: {expected}");
            expected.ShouldContain("positional-nested-generic|FalseTrueFalse|", Case.Sensitive, "the sample must really compare against the types");

            var outDir = Path.Combine(dir, "out");
            var outputs = await ProtectAsync(shape, inputs, outDir, SettingsFor(configuration));

            foreach (var path in outputs)
            {
                using var output = ModuleDefMD.Load(File.ReadAllBytes(path));
                output.GetTypes().Select(t => t.Name.String).ShouldNotContain("Box`1", "the generic type must really have been renamed");
                output.GetTypes().Select(t => t.Name.String).ShouldNotContain("Plain");
            }

            var driverOutput = shape == "merge" ? outputs[0] : Path.Combine(outDir, Path.GetFileName(driverAssembly));
            Run(driverOutput).ShouldBe(expected, $"{shape}, {configuration}");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>
    /// Compilers put attributes on types, members, parameters and generic parameters; the metadata format allows
    /// them on other rows too. This builds such a module (the owners are attached with dnlib to what Roslyn
    /// compiled), protects it and reads every row of the output's CustomAttribute table, whatever its parent is.
    /// </summary>
    private const string OwnerSource = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Runtime.InteropServices;

        [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(System.Diagnostics.Stopwatch))]

        public class InfoAttribute : Attribute
        {
            public InfoAttribute(string label) { }
            public string Note { get; set; }
            public int Level;
        }

        public interface IFoo { }
        public class C : IFoo { }

        public class G<T> where T : IFoo
        {
            public void M<U>() where U : IFoo { }
        }

        public static class Calls
        {
            [DllImport("kernel32")] static extern int GetTickCount();
            public static int Run() => GetTickCount() + string.Concat("a", "b").Length + Enumerable.Empty<int>().Count() + new List<int>().Count;
        }
        """;

    [Fact]
    public async Task NamedArgumentsOnEveryRowKindThatCanCarryAttributes_FollowTheRename()
    {
        var dir = NewTempDir();
        try
        {
            var compiled = Compile(OwnerSource, dir, "OwnerShape");
            var input = Path.Combine(dir, "OwnerShape.in.dll");
            var attached = AttachAttributesToEveryKindOfOwner(compiled, input);

            var persisted = OwnerKindsCarryingInfo(input);
            persisted.ShouldBe(attached.OrderBy(k => k, StringComparer.Ordinal).ToList(), "the writer must keep an attribute on every kind of row this test attaches one to");

            var output = Path.Combine(dir, "OwnerShape.dll");
            var result = await CreateService().ObfuscateAsync(input, output, SettingsFor("rename-only"));
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output));
            var info = protectedModule.GetTypes().Single(t => t.BaseType?.FullName == "System.Attribute");
            info.Name.String.ShouldNotBe("InfoAttribute");
            info.Properties.Select(p => p.Name.String).ShouldNotContain("Note");
            info.Fields.Select(f => f.Name.String).ShouldNotContain("Level");

            OwnerKindsCarryingInfo(output).ShouldBe(attached.OrderBy(k => k, StringComparer.Ordinal).ToList());
            StaleMemberReferenceScanner.Find(protectedModule).ShouldBeEmpty();
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static List<string> AttachAttributesToEveryKindOfOwner(string compiledPath, string outputPath)
    {
        using var module = ModuleDefMD.Load(File.ReadAllBytes(compiledPath));
        var info = module.Types.Single(t => t.Name == "InfoAttribute");
        var constructor = info.FindInstanceConstructors().Single();
        var str = module.CorLibTypes.String;
        var int32 = module.CorLibTypes.Int32;
        var kinds = new List<string>();

        void Attach(string kind, IHasCustomAttribute owner)
        {
            owner.CustomAttributes.Add(new CustomAttribute(
                constructor,
                new[] { new CAArgument(str, kind) },
                new[]
                {
                    new CANamedArgument(false, str, "Note", new CAArgument(str, kind)),
                    new CANamedArgument(true, int32, "Level", new CAArgument(int32, 5))
                }));
            kinds.Add(kind);
        }

        var generic = module.Types.Single(t => t.Name == "G`1");
        var genericMethod = generic.Methods.Single(m => m.Name == "M");
        var calls = module.Types.Single(t => t.Name == "Calls");

        Attach("assembly", module.Assembly);
        Attach("module", module);
        Attach("interfaceImpl", module.Types.Single(t => t.Name == "C").Interfaces[0]);
        Attach("typeGenericParam", generic.GenericParameters[0]);
        Attach("typeGenericConstraint", generic.GenericParameters[0].GenericParamConstraints[0]);
        Attach("methodGenericParam", genericMethod.GenericParameters[0]);
        Attach("methodGenericConstraint", genericMethod.GenericParameters[0].GenericParamConstraints[0]);
        Attach("exportedType", module.ExportedTypes[0]);
        Attach("assemblyRef", module.GetAssemblyRefs().First());
        Attach("moduleRef", module.GetModuleRefs().Single(r => r.Name == "kernel32"));
        Attach("typeRef", module.GetTypeRefs().First(t => t.Name == "List`1"));

        var run = calls.Methods.Single(m => m.Name == "Run");
        Attach("memberRef", run.Body.Instructions.Select(i => i.Operand).OfType<MemberRef>().First(m => m.Name == "Concat"));
        Attach("methodSpec", run.Body.Instructions.Select(i => i.Operand).OfType<MethodSpec>().First());
        Attach("typeSpec", (TypeSpec)run.Body.Instructions.Select(i => i.Operand).OfType<MemberRef>()
            .First(m => m.Class is TypeSpec { TypeSig: GenericInstSig { GenericType.TypeDefOrRef.Name.String: "List`1" } }).Class);

        var resource = new EmbeddedResource("res", new byte[] { 1, 2, 3 });
        module.Resources.Add(resource);
        Attach("resource", resource);

        var securityAttribute = new SecurityAttribute(info);
        securityAttribute.NamedArguments.Add(new CANamedArgument(false, str, "Note", new CAArgument(str, "security")));
        var security = new DeclSecurityUser(SecurityAction.Demand, new List<SecurityAttribute> { securityAttribute });
        generic.DeclSecurities.Add(security);
        Attach("declSecurity", security);

        module.Write(outputPath);
        return kinds;
    }

    /// <summary>The kinds of row that carry an <c>InfoAttribute</c> in the file, read from the CustomAttribute table.</summary>
    private static List<string> OwnerKindsCarryingInfo(string path)
    {
        using var module = ModuleDefMD.Load(File.ReadAllBytes(path));
        var kinds = new List<string>();
        for (uint rid = 1; rid <= module.TablesStream.CustomAttributeTable.Rows; rid++)
        {
            module.TablesStream.TryReadCustomAttributeRow(rid, out var row).ShouldBeTrue();
            var owner = module.ResolveHasCustomAttribute(row.Parent);
            foreach (var attribute in owner.CustomAttributes.Where(a => a.NamedArguments.Count == 2 && a.ConstructorArguments.Count == 1
                && a.ConstructorArguments[0].Value is UTF8String))
            {
                var label = ((UTF8String)attribute.ConstructorArguments[0].Value).String;
                if (OwnerKind(owner) == label && !kinds.Contains(label))
                    kinds.Add(label);
            }
        }

        var declSecurity = Enumerable.Range(1, (int)module.TablesStream.DeclSecurityTable.Rows)
            .SelectMany(rid => module.ResolveDeclSecurity((uint)rid).SecurityAttributes)
            .Any(a => a.NamedArguments.Count == 1);
        if (declSecurity && !kinds.Contains("declSecurity"))
            kinds.Add("declSecurity");
        return kinds.OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    private static string? OwnerKind(IHasCustomAttribute owner) =>
        owner switch
        {
            AssemblyDef => "assembly",
            ModuleDef => "module",
            InterfaceImpl => "interfaceImpl",
            GenericParam { Owner: TypeDef } => "typeGenericParam",
            GenericParam { Owner: MethodDef } => "methodGenericParam",
            GenericParamConstraint constraint => constraint.Owner.Owner is TypeDef ? "typeGenericConstraint" : "methodGenericConstraint",
            ExportedType => "exportedType",
            AssemblyRef => "assemblyRef",
            ModuleRef => "moduleRef",
            TypeRef => "typeRef",
            MemberRef => "memberRef",
            MethodSpec => "methodSpec",
            TypeSpec => "typeSpec",
            ManifestResource or Resource => "resource",
            DeclSecurity => "declSecurity",
            _ => null
        };

    [Fact]
    public async Task PreservedAttributeMembers_KeepTheirNamesInTheNamedArguments()
    {
        var dir = NewTempDir();
        try
        {
            var (inputs, driverAssembly) = Build("single", dir);
            var expected = Run(driverAssembly);
            var outDir = Path.Combine(dir, "out");
            var outputs = await ProtectAsync("single", inputs, outDir, SettingsFor("preserve-public"));

            using (var output = ModuleDefMD.Load(File.ReadAllBytes(outputs[0])))
            {
                var info = output.GetTypes().Single(t => t.Name.String == "InfoAttribute");
                info.Properties.Select(p => p.Name.String).ShouldContain("Note", "public names are kept");
                info.Properties.Select(p => p.Name.String).ShouldContain("Kind");
            }

            Run(outputs[0]).ShouldBe(expected);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static (List<string> Inputs, string DriverAssembly) Build(
        string shape, string dir, string uses = Uses, string assemblyAttributes = AssemblyAttributes) =>
        BuildShape(shape, dir, Usings, Definitions, assemblyAttributes, uses);
}

using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Source;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Shouldly;
using static Obfy.Tests.SourceRvTestSupport;

namespace Obfy.Tests;

/// <summary>
/// Regression tests for the Roslyn symbol renamer: events, anonymous/tuple member names, partial
/// methods, scope rules (inclusions, exclusions, XAML) and the symbol map.
/// </summary>
public class SourceSymbolRenamerRegressionTests
{
    private static ObfySettings Settings(Action<ObfySettings>? configure = null)
    {
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
                RenameEvents = true,
                PreservePublicApi = false,
                Mode = NamingMode.Sequential
            }
        };
        configure?.Invoke(settings);
        return settings;
    }

    private static async Task<PipelineContext> RenameAsync(
        string source, Action<ObfySettings>? configure = null)
    {
        return await RenameFilesAsync([("input.cs", source)], configure);
    }

    private static async Task<PipelineContext> RenameFilesAsync(
        (string Path, string Source)[] files, Action<ObfySettings>? configure = null)
    {
        var context = PipelineContext.ForSourceCode(CreateCompilation(files), Settings(configure));
        var renamer = new SourceSymbolRenamer(new NameGenerator(), NullLogger<SourceSymbolRenamer>.Instance);
        var result = await renamer.ObfuscateAsync(context);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        return context;
    }

    /// <summary>
    /// Invokes a method by its ORIGINAL type and method name; the symbol map says what they were renamed to.
    /// </summary>
    private static object? InvokeRenamed(PipelineContext context, string typeName, string methodName, params object?[] args)
    {
        var map = context.SymbolMap;
        var newType = map.TryGetValue($"NamedType:{typeName}", out var t) ? t : typeName;
        var prefix = $"Method:{typeName}.{methodName}(";
        var newMethod = map.FirstOrDefault(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Value ?? methodName;
        return SourceRvTestSupport.Invoke(context.Compilation!, newType, newMethod, args);
    }

    private static void ShouldCompile(PipelineContext context)
    {
        var errors = GetErrors(context.Compilation!);
        errors.ShouldBeEmpty(ErrorText(errors));
    }

    // ---- (a) events -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Event_ImplementingAnInterfaceEvent_IsNotRenamed()
    {
        var context = await RenameAsync("""
            using System.ComponentModel;
            public class Model : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;
                private string _name = "";
                public string Name
                {
                    get => _name;
                    set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Name")); }
                }
                public string Run()
                {
                    var model = new Model();
                    var count = 0;
                    model.PropertyChanged += (s, e) => count++;
                    model.Name = "x";
                    model.Name = "y";
                    return count.ToString();
                }
            }
            """);

        ShouldCompile(context);
        SourceOf(context).ShouldContain("PropertyChanged");
        InvokeRenamed(context, "Model", "Run").ShouldBe("2");
    }

    [Fact]
    public async Task InterfaceEventAndItsImplementation_KeepTheSameName()
    {
        var context = await RenameAsync("""
            using System;
            public interface IThing { event EventHandler Changed; }
            public class Thing : IThing
            {
                public event EventHandler? Changed;
                public int Fired;
                public void Fire() { Changed?.Invoke(this, EventArgs.Empty); }
                public int Run()
                {
                    IThing thing = new Thing();
                    var hits = 0;
                    thing.Changed += (s, e) => hits++;
                    ((Thing)thing).Fire();
                    return hits;
                }
            }
            """);

        ShouldCompile(context);
        InvokeRenamed(context, "Thing", "Run").ShouldBe(1);
    }

    [Fact]
    public async Task OverrideAndAbstractEvents_AreNotRenamed()
    {
        var context = await RenameAsync("""
            using System;
            public abstract class Base { public abstract event EventHandler? Raised; }
            public class Derived : Base
            {
                public override event EventHandler? Raised;
                public int Run() { var n = 0; Raised += (s, e) => n++; Raised?.Invoke(this, EventArgs.Empty); return n; }
            }
            """);

        ShouldCompile(context);
        InvokeRenamed(context, "Derived", "Run").ShouldBe(1);
    }

    [Fact]
    public async Task PlainPrivateEvent_IsStillRenamed()
    {
        var context = await RenameAsync("""
            using System;
            public class Source
            {
                public event EventHandler? Ticked;
                public int Run() { var n = 0; Ticked += (s, e) => n++; Ticked?.Invoke(this, EventArgs.Empty); return n; }
            }
            """);

        ShouldCompile(context);
        SourceOf(context).ShouldNotContain("Ticked");
        InvokeRenamed(context, "Source", "Run").ShouldBe(1);
    }

    // ---- (b) anonymous types and tuples --------------------------------------------------------------------------

    private const string PersonSource = """
        public class Person
        {
            public string Name { get; set; } = "";
            public int Age { get; set; }
        }
        public class Use
        {
            public string Anonymous()
            {
                var p = new Person { Name = "Ada", Age = 36 };
                var a = new { p.Name, p.Age };
                return a.Name + a.Age;
            }
            public string Tuple()
            {
                var p = new Person { Name = "Ada", Age = 36 };
                var t = (p.Name, p.Age);
                return t.Name + ":" + t.Age;
            }
            public string Json()
            {
                var p = new Person { Name = "Ada", Age = 36 };
                return System.Text.Json.JsonSerializer.Serialize(new { p.Name, p.Age });
            }
            public string Duplicate()
            {
                var p = new Person { Name = "Ada", Age = 36 };
                var q = new Person { Name = "Bob", Age = 40 };
                var t = (p.Name, q.Name);
                return t.Item1 + t.Item2;
            }
        }
        """;

    [Fact]
    public async Task AnonymousType_MembersInferredFromRenamedProperties_KeepTheirNames()
    {
        var context = await RenameAsync(PersonSource);

        ShouldCompile(context);
        InvokeRenamed(context, "Use", "Anonymous").ShouldBe("Ada36");
    }

    [Fact]
    public async Task AnonymousType_SerializedMemberNames_AreUnchanged()
    {
        var context = await RenameAsync(PersonSource);

        ShouldCompile(context);
        InvokeRenamed(context, "Use", "Json").ShouldBe("{\"Name\":\"Ada\",\"Age\":36}");
    }

    [Fact]
    public async Task Tuple_ElementNamesInferredFromRenamedProperties_KeepTheirNames()
    {
        var context = await RenameAsync(PersonSource);

        ShouldCompile(context);
        InvokeRenamed(context, "Use", "Tuple").ShouldBe("Ada:36");
        // Two elements that infer the same name get no names at all; adding them would not compile.
        InvokeRenamed(context, "Use", "Duplicate").ShouldBe("AdaBob");
    }

    // ---- (c) partial methods -------------------------------------------------------------------------------------

    [Fact]
    public async Task PartialMethod_DefinitionAndImplementation_GetTheSameName()
    {
        var context = await RenameAsync("""
            public partial class Gadget
            {
                public int Total;
                partial void OnChanged(int value);
                public void Set(int value) { OnChanged(value); }
                public int Run() { Set(5); Set(6); return Total; }
            }
            public partial class Gadget
            {
                partial void OnChanged(int value) { Total += value; }
            }
            """);

        ShouldCompile(context);
        InvokeRenamed(context, "Gadget", "Run").ShouldBe(11);
    }

    [Fact]
    public async Task PartialMethod_AcrossFiles_GetTheSameName()
    {
        var context = await RenameFilesAsync(
        [
            ("A.cs", """
                public partial class Gadget
                {
                    public int Total;
                    partial void OnChanged(int value);
                    public void Set(int value) { OnChanged(value); }
                    public int Run() { Set(5); Set(6); return Total; }
                }
                """),
            ("B.cs", """
                public partial class Gadget
                {
                    partial void OnChanged(int value) { Total += value; }
                }
                """)
        ]);

        ShouldCompile(context);
        InvokeRenamed(context, "Gadget", "Run").ShouldBe(11);
    }

    // ---- (d) scope: inclusions, exclusions, XAML ------------------------------------------------------------------

    private const string TwoTypes = """
        namespace Keep.Me
        {
            public class Stay
            {
                public int Field;
                public int StayMethod(int arg) { return arg + Field; }
            }
        }
        namespace Change.Me
        {
            public class Go
            {
                public int Field;
                public int GoMethod(int arg) { return arg + Field; }
            }
        }
        """;

    [Fact]
    public async Task Inclusions_Types_OnlyMatchingTypesAreRenamed()
    {
        var context = await RenameAsync(TwoTypes, s => s.Inclusions.Types.Add("Go"));

        ShouldCompile(context);
        var text = SourceOf(context);
        text.ShouldNotContain("GoMethod");
        text.ShouldContain("class Stay");
        text.ShouldContain("StayMethod");
        text.ShouldContain("int arg");
    }

    [Fact]
    public async Task Inclusions_Namespaces_OnlyMatchingNamespacesAreRenamed()
    {
        var context = await RenameAsync(TwoTypes, s => s.Inclusions.Namespaces.Add("Change.*"));

        ShouldCompile(context);
        var text = SourceOf(context);
        text.ShouldNotContain("GoMethod");
        text.ShouldContain("class Stay");
        text.ShouldContain("StayMethod");
    }

    [Fact]
    public async Task Inclusions_Methods_OnlyMatchingMethodsAreRenamed()
    {
        var context = await RenameAsync(TwoTypes, s => s.Inclusions.Methods.Add("GoMethod"));

        ShouldCompile(context);
        var text = SourceOf(context);
        text.ShouldNotContain("GoMethod");
        text.ShouldContain("StayMethod");
        text.ShouldContain("class Go");
        text.ShouldContain("class Stay");
    }

    [Fact]
    public async Task Inclusions_MatchingNothing_AddsAWarning()
    {
        var context = await RenameAsync(TwoTypes, s => s.Inclusions.Types.Add("DoesNotExist"));

        var text = SourceOf(context);
        text.ShouldContain("class Go");
        context.Warnings.ShouldContain(w => w.Contains("Inclusions matched 0"));
    }

    [Fact]
    public async Task Exclusions_Types_ProtectTheTypeAndItsMembers()
    {
        var context = await RenameAsync(TwoTypes, s => s.Exclusions.Types.Add("Stay"));

        ShouldCompile(context);
        var text = SourceOf(context);
        text.ShouldContain("class Stay");
        text.ShouldContain("StayMethod");
        text.ShouldContain("int arg");
        text.ShouldNotContain("GoMethod");
    }

    [Fact]
    public async Task Exclusions_Namespaces_ProtectEverythingInThem()
    {
        var context = await RenameAsync(TwoTypes, s => s.Exclusions.Namespaces.Add("Keep.*"));

        ShouldCompile(context);
        var text = SourceOf(context);
        text.ShouldContain("class Stay");
        text.ShouldContain("StayMethod");
        text.ShouldNotContain("GoMethod");
    }

    [Fact]
    public async Task Exclusions_Attributes_DefaultListProtectsSerializableTypesAndDataMembers()
    {
        var context = await RenameAsync("""
            using System;
            using System.Runtime.Serialization;
            [Serializable]
            public class Saved
            {
                public int Counter;
                public int Bump() { return ++Counter; }
            }
            [DataContract]
            public class Contract
            {
                [DataMember] public string Label { get; set; } = "";
                public string Other { get; set; } = "";
            }
            public class Plain
            {
                [DataMember] public string Kept { get; set; } = "";
                public string Renamed { get; set; } = "";
            }
            """);

        ShouldCompile(context);
        var text = SourceOf(context);
        text.ShouldContain("class Saved");
        text.ShouldContain("Counter");
        text.ShouldContain("Bump");
        text.ShouldContain("class Contract");
        text.ShouldContain("Label");
        text.ShouldContain("Other");
        text.ShouldContain("Kept");
        text.ShouldNotContain("Renamed");
    }

    [Fact]
    public async Task PreserveXaml_KeepsPublicPropertiesOfViewModelsOnly()
    {
        var context = await RenameAsync("""
            public class MainViewModel
            {
                public string Title { get; set; } = "";
                private int _secret;
                public int Bump() { return ++_secret; }
            }
            public class Helper
            {
                public string Label { get; set; } = "";
            }
            """, s => s.SymbolRenaming.PreserveXaml = true);

        ShouldCompile(context);
        var text = SourceOf(context);
        text.ShouldContain("Title");
        text.ShouldNotContain("_secret");
        text.ShouldNotContain("Bump");
        text.ShouldNotContain("Label");
    }

    [Fact]
    public async Task PreserveXaml_Off_RenamesViewModelProperties()
    {
        var context = await RenameAsync("""
            public class MainViewModel
            {
                public string Title { get; set; } = "";
            }
            """);

        ShouldCompile(context);
        SourceOf(context).ShouldNotContain("Title");
    }

    // ---- (e) new compile errors ----------------------------------------------------------------------------------

    [Fact]
    public void NewErrors_AreReportedAsAWarning()
    {
        var before = CreateCompilation("public class A { public int M() { return 1; } }");
        var after = CreateCompilation("public class A { public int M() { return missing; } }");
        var warnings = new List<string>();

        SourceOutputCheck.WarnOnNewErrors("Test technique", before, after, warnings);

        warnings.Count.ShouldBe(1);
        warnings[0].ShouldContain("Test technique");
        warnings[0].ShouldContain("CS0103");
    }

    [Fact]
    public void ErrorsThatWereAlreadyInTheInput_AreNotReported()
    {
        var broken = CreateCompilation("public class A { public int M() { return missing; } }");
        var warnings = new List<string>();

        SourceOutputCheck.WarnOnNewErrors("Test technique", broken, broken, warnings);

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CleanRenames_ProduceNoCompileWarning()
    {
        var context = await RenameAsync(PersonSource);

        context.Warnings.ShouldNotContain(w => w.Contains("compile error"));
    }

    [Fact]
    public async Task GeneratedNames_NeverCollideWithKeptIdentifiers()
    {
        // The first sequential name is "a"; the public method keeps its parameters a and b, so a renamed
        // Helper called "a" would be captured by the parameter at its call sites.
        var context = await RenameAsync("""
            public class Calc
            {
                private int Helper(int x) { return x + 1; }
                public int Run(int a, int b) { return Helper(a) + Helper(b); }
            }
            """, s => s.SymbolRenaming.PreservePublicApi = true);

        ShouldCompile(context);
        context.Warnings.ShouldNotContain(w => w.Contains("compile error"));
        InvokeRenamed(context, "Calc", "Run", 1, 2).ShouldBe(5);
    }

    // ---- (f) symbol map ------------------------------------------------------------------------------------------

    [Fact]
    public async Task SymbolMap_LocalsAndParametersWithTheSameNameStayDistinct()
    {
        var context = await RenameAsync("""
            public class Calc
            {
                public int A(int x)
                {
                    int i = x + 1;
                    int j = i * 2;
                    return j;
                }
                public int B(int x)
                {
                    int i = x + 2;
                    { int t = 1; i += t; }
                    { int t = 2; i += t; }
                    return i;
                }
            }
            """);

        ShouldCompile(context);
        var map = context.SymbolMap;
        map.Keys.Count(k => k.StartsWith("Parameter:", StringComparison.Ordinal)).ShouldBe(2);
        // i and j in A, i and two t in B.
        map.Keys.Count(k => k.StartsWith("Local:", StringComparison.Ordinal)).ShouldBe(5);
        map.Values.Distinct().Count().ShouldBe(map.Count, "every renamed symbol has its own new name");
    }

    [Fact]
    public async Task SymbolMap_HasOneEntryPerRenamedSymbol()
    {
        var context = await RenameAsync("""
            public class Calc
            {
                public int Field;
                public int Prop { get; set; }
                public int Method(int arg) { return arg + Field + Prop; }
            }
            """);

        ShouldCompile(context);
        context.SymbolMap.Count.ShouldBe(5); // type, field, property, method, parameter
    }
}

using dnlib.DotNet;
using Shouldly;
using static Obfy.Tests.RenamingTestSupport;

namespace Obfy.Tests;

/// <summary>
/// The scanner is the safety net of the renaming tests, so it gets tested too. It used to treat a reference whose
/// parent it could not resolve as "not in the set": a TypeRef whose name the renamer forgot to rewrite in a closed set was
/// skipped instead of reported, and a test that relied on the scanner stayed green.
/// </summary>
public class StaleMemberReferenceScannerTests
{
    private const string Usings = "using System;\n";

    private const string Library = "public class ZBox<T> { public T Item; public ZBox(T item) { Item = item; } public T Get() => Item; }\n";

    private const string App = """
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        public static class Driver
        {
            public static string Run() { var b = new ZBox<int>(4); return b.Get() + "/" + b.Item; }
        }
        """;

    [Fact]
    public void AClosedSetWhoseReferencesAllResolve_HasNoFindings()
    {
        var dir = NewTempDir();
        try
        {
            var (inputs, _) = BuildShape("closed-set", dir, Usings, Library, "", App);
            using var app = ModuleDefMD.Load(File.ReadAllBytes(inputs[0]));
            using var lib = ModuleDefMD.Load(File.ReadAllBytes(inputs[1]));

            StaleMemberReferenceScanner.Find(new ModuleDef[] { app, lib }).ShouldBeEmpty();
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void ATypeRefToAnAssemblyOfTheSetThatNamesNoTypeOfIt_IsReported()
    {
        var dir = NewTempDir();
        try
        {
            var (inputs, _) = BuildShape("closed-set", dir, Usings, Library, "", App);
            using var app = ModuleDefMD.Load(File.ReadAllBytes(inputs[0]));
            using var lib = ModuleDefMD.Load(File.ReadAllBytes(inputs[1]));

            // What a renamer that renamed the library's type and forgot the reference would leave behind.
            app.GetTypeRefs().Single(t => t.Name == "ZBox`1").Name = "Renamed`1";

            var findings = StaleMemberReferenceScanner.Find(new ModuleDef[] { app, lib });
            findings.ShouldContain(f => f.Contains("type reference Renamed`1 names no type of its assembly"), string.Join("\n", findings));
            findings.ShouldContain(f => f.StartsWith("method 'Get' on Renamed`1", StringComparison.Ordinal), string.Join("\n", findings));
            findings.ShouldContain(f => f.StartsWith("field 'Item' on Renamed`1", StringComparison.Ordinal), string.Join("\n", findings));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void ATypeRefToAnAssemblyOutsideTheSet_IsNotAFinding()
    {
        var dir = NewTempDir();
        try
        {
            var (inputs, _) = BuildShape("closed-set", dir, Usings, Library, "", App);
            using var app = ModuleDefMD.Load(File.ReadAllBytes(inputs[0]));
            using var lib = ModuleDefMD.Load(File.ReadAllBytes(inputs[1]));

            // The set is the application alone: the library (and the framework) are somebody else's types.
            StaleMemberReferenceScanner.Find(new ModuleDef[] { app }).ShouldBeEmpty();
            app.GetTypeRefs().Single(t => t.Name == "ZBox`1").Name = "Renamed`1";
            StaleMemberReferenceScanner.Find(new ModuleDef[] { app }).ShouldBeEmpty();
        }
        finally
        {
            TryDelete(dir);
        }
    }
}

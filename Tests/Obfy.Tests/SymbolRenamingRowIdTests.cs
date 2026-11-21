using dnlib.DotNet;
using Microsoft.CodeAnalysis;
using Shouldly;
using static Obfy.Tests.RenamingTestSupport;

namespace Obfy.Tests;

/// <summary>
/// The output keeps the row ids of the tables whose tokens reflection can see, so code that resolves a metadata token
/// it holds as a number (<c>Module.ResolveMethod(0x06000002)</c>) still reaches the same member after the members were
/// renamed. (MemberRef and MethodSpec ids are not kept: see <c>AssemblyProcessor.OutputMetadataFlags</c>.
/// Those tokens only appear in IL, where the writer writes the id itself.)
/// </summary>
public class SymbolRenamingRowIdTests
{
    private const string Source = """
        using System;
        using System.Reflection;

        internal class Target
        {
            public int Level = 5;
            public int Compute(int x) => x * 3 + Level;
            public static string Name() => "target";
            public string Text { get; set; } = "text";
        }

        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
        public static class Driver
        {
            public static string Run()
            {
                var module = typeof(Driver).Module;
                var type = module.ResolveType(0x%TYPE%);
                var compute = (MethodInfo)module.ResolveMethod(0x%COMPUTE%);
                var name = (MethodInfo)module.ResolveMethod(0x%NAME%);
                var level = module.ResolveField(0x%LEVEL%);
                var getText = (MethodInfo)module.ResolveMethod(0x%GETTEXT%);
                var target = Activator.CreateInstance(type);
                return (type == typeof(Target)) + "/" + compute.Invoke(target, new object[] { 4 }) + "/" + name.Invoke(null, null)
                    + "/" + level.GetValue(target) + "/" + getText.Invoke(target, null);
            }
        }
        """;

    [Theory]
    [InlineData("rename-only")]
    [InlineData("minimal")]
    [InlineData("standard")]
    [InlineData("aggressive")]
    public async Task HardCodedTokensOfTypeMethodAndFieldRows_StillReachTheSameMembers(string configuration)
    {
        var dir = NewTempDir();
        try
        {
            // The tokens depend only on the layout of the metadata, not on the literals written into Run, so compile
            // once to learn them and again with them in place.
            var probe = Compile(Source.Replace("%TYPE%", "02000001").Replace("%COMPUTE%", "06000001").Replace("%NAME%", "06000001")
                .Replace("%LEVEL%", "04000001").Replace("%GETTEXT%", "06000001"), dir, "TokenProbe");
            string Token(IMDTokenProvider member) => member.MDToken.Raw.ToString("X8");
            string source;
            using (var module = ModuleDefMD.Load(File.ReadAllBytes(probe)))
            {
                var target = module.GetTypes().Single(t => t.Name == "Target");
                source = Source.Replace("%TYPE%", Token(target))
                    .Replace("%COMPUTE%", Token(target.Methods.Single(m => m.Name == "Compute")))
                    .Replace("%NAME%", Token(target.Methods.Single(m => m.Name == "Name")))
                    .Replace("%LEVEL%", Token(target.Fields.Single(f => f.Name == "Level")))
                    .Replace("%GETTEXT%", Token(target.Methods.Single(m => m.Name == "get_Text")));
            }

            var input = Compile(source, dir, "TokenShape");
            var expected = Run(input);
            expected.ShouldBe("True/17/target/5/text");

            var output = Path.Combine(dir, "out", "TokenShape.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var result = await CreateService().ObfuscateAsync(input, output, SettingsFor(configuration));
            result.Success.ShouldBeTrue(result.ErrorMessage);

            using (var protectedModule = ModuleDefMD.Load(File.ReadAllBytes(output)))
            {
                var target = protectedModule.GetTypes().Single(t => t.Methods.Any(m => m.Name == "get_Text" || m.Name.String.StartsWith("get_", StringComparison.Ordinal))
                    && t.Name != "Driver" && t.Fields.Count == 2 && t.Properties.Count == 1);
                target.Name.String.ShouldNotBe("Target", "the type must really have been renamed");
            }

            Run(output).ShouldBe(expected, configuration);
        }
        finally
        {
            TryDelete(dir);
        }
    }
}

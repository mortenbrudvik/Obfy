using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Obfy.VisualStudio.Commands;
using Shouldly;

namespace Obfy.VisualStudio.Tests;

/// <summary>
/// Community.VisualStudio.Toolkit's <c>BaseCommand&lt;T&gt;.InitializeAsync</c> builds the command's <c>CommandID</c> from
/// the <c>[Command]</c> attribute, and from the package GUID when the attribute has no GUID. The .vsct defines every
/// button under the command-set GUID, so a command without that GUID in its attribute is registered for an ID that no
/// menu item has and never runs. These tests read the .vsct and the command sources, since the commands themselves
/// need the Visual Studio SDK.
/// </summary>
public class CommandTableTests
{
    private static readonly string ProjectDir = Path.Combine(FindRepoRoot(), "Src", "Obfy.VisualStudio");

    private static readonly Regex CommandAttribute = new(
        @"^\s*\[Command\((?<args>[^\]]*)\)\]\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void EveryCommandClass_IsRegisteredUnderACommandSetAndIdThatTheVsctDefines()
    {
        var table = ReadVsct();
        var commands = ReadCommandClasses();

        commands.ShouldNotBeEmpty();
        var problems = new List<string>();
        foreach (var command in commands)
        {
            if (command.AttributeArguments is null)
            {
                problems.Add($"{command.Class} has no [Command(...)] attribute");
                continue;
            }

            var match = Regex.Match(command.AttributeArguments, @"^PackageGuids\.(?<guid>\w+)\s*,\s*PackageIds\.(?<id>\w+)$");
            if (!match.Success)
            {
                problems.Add(
                    $"{command.Class}: [Command({command.AttributeArguments})] must name the command-set GUID and the id " +
                    "([Command(PackageGuids.CommandSetString, PackageIds.<id>)]); the one-argument form registers the " +
                    "command under the package GUID, which names no button in the .vsct");
                continue;
            }

            var guid = Guid.Parse((string)Constant(typeof(PackageGuids), match.Groups["guid"].Value));
            var id = (int)Constant(typeof(PackageIds), match.Groups["id"].Value);

            if (!table.Buttons.Contains((guid, id)))
                problems.Add($"{command.Class} is registered as ({guid}, 0x{id:X4}), which is not a <Button> in ObfyVsixCommandTable.vsct");
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryVsctButton_HasACommandClass()
    {
        var table = ReadVsct();
        var registered = ReadCommandClasses()
            .Select(command => Regex.Match(command.AttributeArguments ?? string.Empty, @"^PackageGuids\.(?<guid>\w+)\s*,\s*PackageIds\.(?<id>\w+)$"))
            .Where(match => match.Success)
            .Select(match => (
                Guid.Parse((string)Constant(typeof(PackageGuids), match.Groups["guid"].Value)),
                (int)Constant(typeof(PackageIds), match.Groups["id"].Value)))
            .ToList();

        foreach (var button in table.Buttons)
            registered.ShouldContain(button, $"the .vsct button {button.Guid} 0x{button.Id:X4} has no command class");

        registered.Distinct().Count().ShouldBe(registered.Count, "two command classes share an id");
    }

    [Fact]
    public void EveryCommandClass_IsInitializedByThePackage()
    {
        var package = File.ReadAllText(Path.Combine(ProjectDir, "ObfyPackage.cs"));

        foreach (var command in ReadCommandClasses())
        {
            package.ShouldContain(
                $"await {command.Class}.InitializeAsync(this);",
                customMessage: $"ObfyPackage.InitializeAsync never initializes {command.Class}");
        }
    }

    [Fact]
    public void TheVsctPackageGuid_IsThePackagesGuid()
    {
        ReadVsct().PackageGuid.ShouldBe(Guid.Parse(PackageGuids.PackageString));
    }

    [Fact]
    public void TheCommandSetGuid_IsNotThePackageGuid()
    {
        // The two are different symbols in the .vsct; confusing them is the defect these tests guard against.
        PackageGuids.CommandSetString.ShouldNotBe(PackageGuids.PackageString);
        ReadVsct().CommandSetGuid.ShouldBe(Guid.Parse(PackageGuids.CommandSetString));
    }

    private static object Constant(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
        field.ShouldNotBeNull($"{type.Name}.{name} does not exist");
        return field.GetRawConstantValue()!;
    }

    private static IReadOnlyList<(string Class, string? AttributeArguments)> ReadCommandClasses()
    {
        var found = new List<(string, string?)>();
        foreach (var file in Directory.GetFiles(Path.Combine(ProjectDir, "Commands"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (Match declaration in Regex.Matches(text, @"class\s+(?<name>\w+)\s*:\s*BaseCommand<\k<name>>"))
            {
                // The [Command(...)] attribute is the last one before the class declaration.
                var before = text.Substring(0, declaration.Index);
                var attributes = CommandAttribute.Matches(before);
                found.Add((
                    declaration.Groups["name"].Value,
                    attributes.Count == 0 ? null : attributes[attributes.Count - 1].Groups["args"].Value.Trim()));
            }
        }

        return found;
    }

    private static (Guid PackageGuid, Guid CommandSetGuid, IReadOnlyList<(Guid Guid, int Id)> Buttons) ReadVsct()
    {
        var document = XDocument.Load(Path.Combine(ProjectDir, "ObfyVsixCommandTable.vsct"));
        var elements = document.Descendants().ToList();

        var guids = elements
            .Where(e => e.Name.LocalName == "GuidSymbol")
            .ToDictionary(e => (string)e.Attribute("name")!, e => Guid.Parse((string)e.Attribute("value")!));

        var ids = elements
            .Where(e => e.Name.LocalName == "IDSymbol")
            .ToDictionary(
                e => (string)e.Attribute("name")!,
                e => int.Parse(((string)e.Attribute("value")!).Replace("0x", string.Empty), NumberStyles.HexNumber));

        var buttons = elements
            .Where(e => e.Name.LocalName == "Button")
            .Select(e => (guids[(string)e.Attribute("guid")!], ids[(string)e.Attribute("id")!]))
            .ToList();

        return (guids["guidObfyPackage"], guids["guidObfyPackageCmdSet"], buttons);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Obfy.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate Obfy.sln from {AppContext.BaseDirectory}");
    }
}

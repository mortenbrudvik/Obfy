using System.IO;
using System.Text.Json;
using Obfy.App.Models;
using Obfy.App.Sessions;
using Shouldly;

namespace Obfy.Interop.Tests;

/// <summary>
/// Pins the page's copy (<c>Src/WebApp/src/content/techniques.json</c> and <c>guide.json</c>) to the app layer.
/// The app is the source of truth: a technique or option the session exposes must have words, and words must not
/// describe a technique or option the session does not have, so an option can never ship without its explanation.
/// </summary>
public class TechniqueContentContractTests
{
    private static readonly string ContentDir = Path.Combine(FindRepoRoot(), "Src", "WebApp", "src", "content");

    private static readonly JsonElement Content = Load("techniques.json");
    private static readonly JsonElement Guide = Load("guide.json");
    private static readonly SettingsSnapshot Snapshot = new ProtectionSession().GetSnapshot();

    // The four techniques that hide data or code behind a key shipped in the output. CLAUDE.md's security note
    // ("obfuscation, not confidentiality") must be set apart on each of them.
    private static readonly string[] KeyedTechniques = ["stringEncryption", "constantEncryption", "resourceEncryption", "methodEncryption"];

    [Fact]
    public void TechniqueIds_AreExactlyTheIdsTheSessionExposes()
    {
        var content = ContentTechniques().Select(t => t.Name).ToHashSet();
        var app = Snapshot.Techniques.Select(t => t.Id).ToHashSet();

        content.Except(app).ShouldBeEmpty("techniques.json describes a technique the app does not have");
        app.Except(content).ShouldBeEmpty("the app exposes a technique with no content in techniques.json");
    }

    [Fact]
    public void ScopeIsNotATechnique_ItDescribesTheInclusionAndExclusionLists()
    {
        // "scope" is deliberately absent from the technique ids above: the session does not list the exclusion and
        // inclusion lists as a technique. They arrive as SettingsSnapshot.Exclusions / Inclusions, so they are checked
        // against those instead.
        Content.GetProperty("techniques").TryGetProperty("scope", out _).ShouldBeFalse("scope is a sibling of techniques, not one of them");

        var content = Content.GetProperty("scope").GetProperty("lists").EnumerateArray()
            .Select(l => $"{l.GetProperty("set").GetString()}.{l.GetProperty("key").GetString()}")
            .ToList();
        var app = new[] { nameof(SettingsSnapshot.Exclusions), nameof(SettingsSnapshot.Inclusions) }
            .SelectMany(set => typeof(NameLists).GetProperties().Select(p => $"{Camel(set)}.{Camel(p.Name)}"))
            .ToList();

        content.ShouldBe(app, ignoreOrder: true);
    }

    [Fact]
    public void OptionIds_OfEveryTechnique_AreExactlyTheOptionsTheSessionExposes()
    {
        foreach (var technique in Snapshot.Techniques)
        {
            var appOptions = technique.Options.Keys.ToHashSet();
            var contentOptions = OptionIds(Content.GetProperty("techniques").GetProperty(technique.Id));

            contentOptions.Except(appOptions).ShouldBeEmpty($"{technique.Id}: techniques.json describes an option the app does not have");
            appOptions.Except(contentOptions).ShouldBeEmpty($"{technique.Id}: the app exposes an option with no content");
            contentOptions.Count.ShouldBe(contentOptions.Distinct().Count(), $"{technique.Id}: an option is described twice");
        }
    }

    [Fact]
    public void EveryOption_HasALabel_AndEveryGroupMemberToo()
    {
        foreach (var (id, technique) in ContentTechniques())
        {
            foreach (var option in technique.GetProperty("options").EnumerateArray())
            {
                Text(option, "label").ShouldNotBeNullOrWhiteSpace($"{id}.{option.GetProperty("key")} needs a label");
                if (option.TryGetProperty("members", out var members))
                {
                    members.GetArrayLength().ShouldBeGreaterThan(0, $"{id}.{option.GetProperty("key")} is a group with no members");
                    foreach (var member in members.EnumerateArray())
                        Text(member, "label").ShouldNotBeNullOrWhiteSpace($"{id}.{member.GetProperty("key")} needs a label");
                }
            }
        }
    }

    [Fact]
    public void EveryTechnique_HasItsWords_AndANameInAKnownGroup()
    {
        var groups = Content.GetProperty("groups").EnumerateArray().Select(g => g.GetString()).ToList();
        foreach (var (id, technique) in ContentTechniques())
        {
            foreach (var field in new[] { "name", "summary", "what", "group", "guideTopic" })
                Text(technique, field).ShouldNotBeNullOrWhiteSpace($"{id} needs {field}");
            groups.ShouldContain(Text(technique, "group"), $"{id} is in a group that is not listed");
            technique.GetProperty("good").GetArrayLength().ShouldBeGreaterThan(0, $"{id} needs a good-to-know list");
        }
    }

    [Fact]
    public void KeyedTechniques_SetApartTheNotConfidentialityNote()
    {
        foreach (var id in KeyedTechniques)
        {
            var technique = Content.GetProperty("techniques").GetProperty(id);
            technique.TryGetProperty("securityNote", out var note).ShouldBeTrue($"{id} needs the security note");
            Text(note, "title")!.ShouldContain("not confidentiality", Case.Insensitive, $"{id}");
            Text(note, "text").ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Dropdowns_HaveWordsForExactlyTheValuesTheSessionOffers()
    {
        var offered = new Dictionary<string, IReadOnlyList<string>>
        {
            ["algorithms"] = Snapshot.Choices.Algorithms,
            ["controlFlowModes"] = Snapshot.Choices.ControlFlowModes,
            ["namingModes"] = Snapshot.Choices.NamingModes,
            ["packingRids"] = Snapshot.Choices.PackingRids,
        };

        var sets = Content.GetProperty("choiceSets").EnumerateObject().ToList();
        sets.Select(s => Text(s.Value, "source")).ShouldBe(offered.Keys, ignoreOrder: true, "every choice list needs one set of words");
        foreach (var set in sets)
        {
            var values = set.Value.GetProperty("values").EnumerateObject().Select(v => v.Name);
            values.ShouldBe(offered[Text(set.Value, "source")!], ignoreOrder: true, $"choiceSets.{set.Name}");
        }

        // Every dropdown option names a set that exists.
        var setNames = sets.Select(s => s.Name).ToHashSet();
        foreach (var (id, technique) in ContentTechniques())
        {
            foreach (var option in technique.GetProperty("options").EnumerateArray().Where(o => Text(o, "type") == "select"))
                setNames.ShouldContain(Text(option, "choiceSet")!, $"{id}.{option.GetProperty("key")}");
        }
    }

    [Fact]
    public void RuntimeProfiles_HaveWordsForExactlyTheProfilesTheSessionOffers()
    {
        var content = Content.GetProperty("runtimeProfiles").EnumerateObject().Select(p => p.Name);

        content.ShouldBe(Snapshot.Choices.RuntimeProfiles, ignoreOrder: true);
    }

    [Fact]
    public void GuideTopics_LinkedFromTheContent_Exist()
    {
        var topics = Guide.GetProperty("topics").EnumerateArray().Select(t => Text(t, "id")).ToHashSet();
        var linked = ContentTechniques().Select(t => Text(t.Value, "guideTopic"))
            .Append(Text(Content.GetProperty("scope"), "guideTopic"));

        foreach (var topic in linked)
            topics.ShouldContain(topic, $"techniques.json links to a Guide topic that does not exist: {topic}");
    }

    [Fact]
    public void Guide_KeepsTheNotConfidentialityNoteOnce()
    {
        var notes = Guide.GetProperty("topics").EnumerateArray()
            .SelectMany(t => t.GetProperty("blocks").EnumerateArray())
            .Where(b => Text(b, "type") == "note")
            .ToList();

        notes.Count(n => Text(n, "title")!.Contains("secrecy", StringComparison.OrdinalIgnoreCase)
                         || Text(n, "title")!.Contains("encryption", StringComparison.OrdinalIgnoreCase)
                         || Text(n, "title")!.Contains("confidentiality", StringComparison.OrdinalIgnoreCase))
            .ShouldBe(1, "the not-confidentiality note should appear in one Guide topic only");
    }

    private static IEnumerable<(string Name, JsonElement Value)> ContentTechniques() =>
        Content.GetProperty("techniques").EnumerateObject().Select(p => (p.Name, p.Value));

    /// <summary>A group option (a row of checkboxes) stands for its members, which are the options the app exposes.</summary>
    private static HashSet<string> OptionIds(JsonElement technique)
    {
        var ids = new HashSet<string>();
        foreach (var option in technique.GetProperty("options").EnumerateArray())
        {
            if (Text(option, "type") == "group")
            {
                foreach (var member in option.GetProperty("members").EnumerateArray())
                    ids.Add(Text(member, "key")!);
            }
            else
            {
                ids.Add(Text(option, "key")!);
            }
        }

        return ids;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static JsonElement Load(string file)
    {
        var path = Path.Combine(ContentDir, file);
        File.Exists(path).ShouldBeTrue($"Expected {path}");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
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

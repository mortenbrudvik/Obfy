using Obfy.UI.Help;
using Shouldly;

namespace Obfy.UI.Tests.Help;

public class HelpCatalogTests
{
    private static readonly string[] ExpectedIds =
    [
        HelpCatalog.GettingStartedId,
        HelpCatalog.FilesOutputId,
        HelpCatalog.SettingsLevelsId,
        HelpCatalog.TechniquesId,
        HelpCatalog.ShortcutsId,
        HelpCatalog.ResultsId
    ];

    private static readonly string[] ExpectedTitles =
    [
        "Getting started",
        "Files & output",
        "Settings & levels",
        "Techniques",
        "Shortcuts",
        "Results"
    ];

    private static readonly string[] TechniqueHeadings =
    [
        "String Encryption",
        "Constant Encryption",
        "Control Flow",
        "Symbol Renaming",
        "Protection",
        "Metadata",
        "Watermark",
        "Managed launcher",
        "Strong-Name Signing",
        "Resource Encryption",
        "Assembly Merge",
        "Exclusions"
    ];

    [Fact]
    public void Create_ReturnsSixTopicsInLockedOrder()
    {
        var topics = HelpCatalog.Create();

        topics.Select(t => t.Id).ShouldBe(ExpectedIds);
        topics.Select(t => t.Title).ShouldBe(ExpectedTitles);
    }

    [Fact]
    public void Create_ReturnsANewListEachCall()
    {
        ReferenceEquals(HelpCatalog.Create(), HelpCatalog.Create()).ShouldBeFalse();
    }

    [Fact]
    public void EveryTopic_HasNonEmptyBlocksAndText()
    {
        foreach (var topic in HelpCatalog.Create())
        {
            topic.Blocks.Count.ShouldBeGreaterThanOrEqualTo(1);
            foreach (var block in topic.Blocks)
            {
                switch (block)
                {
                    case HelpParagraph p:
                        p.Text.ShouldNotBeNullOrWhiteSpace();
                        break;
                    case HelpShortcut s:
                        s.Keys.ShouldNotBeNullOrWhiteSpace();
                        s.Action.ShouldNotBeNullOrWhiteSpace();
                        break;
                    case HelpNamedNote n:
                        n.Heading.ShouldNotBeNullOrWhiteSpace();
                        n.Text.ShouldNotBeNullOrWhiteSpace();
                        break;
                    default:
                        throw new Exception($"Unexpected block type {block.GetType().Name} in {topic.Id}");
                }
            }
        }
    }

    [Fact]
    public void GettingStarted_ContainsConfidentialitySentence()
    {
        var text = JoinParagraphs(Topic(HelpCatalog.GettingStartedId));
        text.ShouldContain(HelpCatalog.ConfidentialitySentence);
    }

    [Fact]
    public void FilesOutput_MentionsSymbolMapAndRemoveFile_NotSymbolMapPath()
    {
        var text = JoinParagraphs(Topic(HelpCatalog.FilesOutputId));
        text.ShouldContain("Write symbol map after obfuscation");
        text.ShouldContain("Output Directory");
        text.ShouldContain("symbolmap.json");
        text.ShouldContain("Remove file");
        text.ShouldNotContain("SymbolMapPath");
        text.ShouldNotContain("Files.SymbolMapPath");
    }

    [Fact]
    public void Shortcuts_ContainsLockedKeys()
    {
        var shortcuts = Topic(HelpCatalog.ShortcutsId).Blocks.OfType<HelpShortcut>().ToList();
        shortcuts.Select(s => s.Keys).ShouldContain("F1");
        shortcuts.Select(s => s.Keys).ShouldContain("Ctrl+Enter");
        shortcuts.Select(s => s.Keys).ShouldContain("Esc");
        shortcuts.Select(s => s.Keys).ShouldNotContain("Escape");
    }

    [Fact]
    public void Techniques_HasTwelveExpanderHeadings()
    {
        var headings = Topic(HelpCatalog.TechniquesId).Blocks.OfType<HelpNamedNote>()
            .Select(n => n.Heading)
            .ToList();
        headings.ShouldBe(TechniqueHeadings);
    }

    [Fact]
    public void Techniques_ManagedLauncher_SaysVirtualizeMethods()
    {
        var note = Topic(HelpCatalog.TechniquesId).Blocks.OfType<HelpNamedNote>()
            .Single(n => n.Heading == "Managed launcher");
        note.Text.ShouldContain("Virtualize methods");
        note.Text.ShouldNotContain("Virtualize simple methods");
    }

    private static HelpTopic Topic(string id) =>
        HelpCatalog.Create().Single(t => t.Id == id);

    private static string JoinParagraphs(HelpTopic topic) =>
        string.Join(" ", topic.Blocks.OfType<HelpParagraph>().Select(p => p.Text));
}

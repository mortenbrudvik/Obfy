using System.Text.Json.Nodes;
using Obfy.App.Services;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.App.Tests.Services;

/// <summary>Regression tests for reading and writing a configuration file.</summary>
public sealed class ConfigFileReviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obfy-cfgfile-" + Guid.NewGuid().ToString("N"));

    public ConfigFileReviewTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    // ---- 6a: saving ----

    [Fact]
    public async Task Save_OverTheFileItCameFrom_KeepsTheSchemaLineAndUnknownFields()
    {
        var path = PathOf("obfy.json");
        await File.WriteAllTextAsync(path, """
            {
              "$schema": "https://example.invalid/obfy.schema.json",
              "level": "custom",
              "futureOption": { "keep": 1 },
              "controlFlow": { "enabled": false, "futureNested": "x" }
            }
            """);
        var settings = await ConfigFile.LoadAsync(path);
        settings.ControlFlow.Enabled = true;

        await ConfigFile.SaveAsync(settings, path, preserveExisting: true);

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        saved["$schema"]!.GetValue<string>().ShouldBe("https://example.invalid/obfy.schema.json");
        saved["futureOption"]!["keep"]!.GetValue<int>().ShouldBe(1);
        saved["controlFlow"]!["futureNested"]!.GetValue<string>().ShouldBe("x");
        saved["controlFlow"]!["enabled"]!.GetValue<bool>().ShouldBeTrue();
        saved.First().Key.ShouldBe("$schema");
        (await ConfigFile.LoadAsync(path)).ControlFlow.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Save_DropsAValueTheSettingsNoLongerHold()
    {
        var path = PathOf("obfy.json");
        await File.WriteAllTextAsync(path, """{ "level": "custom", "signing": { "enabled": true, "keyFile": "old.snk" } }""");
        var settings = await ConfigFile.LoadAsync(path);
        settings.Signing.KeyFile = null; // written as absent, not as null

        await ConfigFile.SaveAsync(settings, path, preserveExisting: true);

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        saved["signing"]!.AsObject().ContainsKey("keyFile").ShouldBeFalse();
    }

    [Fact]
    public async Task Save_ToANewFile_WritesJustTheSettings()
    {
        var path = PathOf("sub" + Path.DirectorySeparatorChar + "new.json");

        await ConfigFile.SaveAsync(ObfySettings.ForLevel(ObfuscationLevel.Minimal), path, preserveExisting: true);

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        saved.ContainsKey("$schema").ShouldBeFalse();
        (await ConfigFile.LoadAsync(path)).Level.ShouldBe(ObfuscationLevel.Minimal);
    }

    [Fact]
    public async Task Save_ToADifferentExistingFile_DoesNotMergeItsContent()
    {
        var path = PathOf("other.json");
        await File.WriteAllTextAsync(path, """{ "name": "somebody else's file", "$schema": "x" }""");

        await ConfigFile.SaveAsync(ObfySettings.ForLevel(ObfuscationLevel.Minimal), path);

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        saved.ContainsKey("name").ShouldBeFalse();
        saved.ContainsKey("$schema").ShouldBeFalse();
    }

    [Fact]
    public async Task Save_ReplacesTheFileAtomically_AndLeavesNoTempFileBehind()
    {
        var path = PathOf("obfy.json");
        await File.WriteAllTextAsync(path, """{ "level": "minimal" }""");
        var original = await File.ReadAllTextAsync(path);

        // A reader that opened the file before the save still sees the whole old content: the save wrote a new file and swapped.
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            await ConfigFile.SaveAsync(ObfySettings.ForLevel(ObfuscationLevel.Aggressive), path);

            using var text = new StreamReader(reader);
            (await text.ReadToEndAsync()).ShouldBe(original);
        }

        (await ConfigFile.LoadAsync(path)).Level.ShouldBe(ObfuscationLevel.Aggressive);
        Directory.GetFiles(_dir).ShouldBe([path]);
    }

    [Fact]
    public async Task Save_ThatFails_LeavesTheOriginalAndNoTempFile()
    {
        var path = PathOf("obfy.json");
        await File.WriteAllTextAsync(path, """{ "level": "minimal" }""");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            await Should.ThrowAsync<ObfyApiException>(async () =>
                await ConfigFile.SaveAsync(ObfySettings.ForLevel(ObfuscationLevel.Aggressive), path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        (await File.ReadAllTextAsync(path)).ShouldBe("""{ "level": "minimal" }""");
        Directory.GetFiles(_dir).ShouldBe([path]);
    }

    // ---- loading: only a configuration is adopted ----

    [Theory]
    [InlineData("""{ "name": "my-package", "version": "1.0.0", "scripts": {} }""")]
    [InlineData("[1, 2, 3]")]
    [InlineData("42")]
    [InlineData("null")]
    public async Task Load_ARecognisedConfigurationOnly(string json)
    {
        var path = PathOf("package.json");
        await File.WriteAllTextAsync(path, json);

        var ex = await Should.ThrowAsync<ObfyApiException>(async () => await ConfigFile.LoadAsync(path));

        ex.Code.ShouldBe("settings.configInvalid");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "$schema": "https://example.invalid/s.json" }""")]
    [InlineData("""{ "Level": "Minimal" }""")]
    [InlineData("""{ "packing": { "rid": "portable" } }""")]
    public async Task Load_AcceptsAConfiguration_EvenAnEmptyOne(string json)
    {
        var path = PathOf("obfy.json");
        await File.WriteAllTextAsync(path, json);

        (await ConfigFile.LoadAsync(path)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Load_ALevelOnlyFile_HoldsThePresetsFlags()
    {
        var path = PathOf("obfy.json");
        await File.WriteAllTextAsync(path, """{ "level": "aggressive" }""");

        var settings = await ConfigFile.LoadAsync(path);

        settings.Level.ShouldBe(ObfuscationLevel.Aggressive);
        settings.ControlFlow.Enabled.ShouldBeTrue();
        settings.Protection.AntiDebug.ShouldBeTrue();
    }
}

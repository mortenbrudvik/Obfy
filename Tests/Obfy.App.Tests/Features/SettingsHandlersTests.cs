using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Tests.Common;
using Obfy.App;
using Obfy.App.Features.Settings;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.Common;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.App.Tests.Features;

public sealed class SettingsHandlersTests : IDisposable
{
    private sealed class FakeDialogs : IFileDialogs
    {
        public string[] Files { get; set; } = [];
        public string? Save { get; set; }
        public string? Key { get; set; }
        public string? SaveDefaultName { get; private set; }

        public Task<string[]> PickFilesAsync(string title, string filter, bool multi) => Task.FromResult(Files);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveAsync(string title, string filter, string? defaultName)
        {
            SaveDefaultName = defaultName;
            return Task.FromResult(Save);
        }

        public Task<string?> PickKeyAsync() => Task.FromResult(Key);
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obfy-settings-" + Guid.NewGuid().ToString("N"));
    private readonly ProtectionSession _session = new();
    private readonly InputSession _input = new(new FakeSettings(), null, new FakeShell(), null, NullLogger<InputSession>.Instance, TimeSpan.Zero);
    private readonly FakeDialogs _dialogs = new();

    public SettingsHandlersTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Dictionary<string, JsonElement> JsonPatch(string json) =>
        JsonUtils.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public async Task Get_ReturnsTheSnapshot()
    {
        var snapshot = await new GetSettingsRequestHandler(_session).Handle(new GetSettingsRequest(), default);

        snapshot.Level.ShouldBe("Standard");
        snapshot.Techniques.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Update_AppliesAPatchFromJson_AndReturnsTheNewSnapshot()
    {
        var patch = JsonPatch("""
            { "controlFlow.enabled": true, "controlFlow.intensity": 70, "controlFlow.mode": "Combined",
              "exclusions.types": ["A", "B"], "watermark.id": "x" }
            """);

        var snapshot = await new UpdateSettingsRequestHandler(_session).Handle(new UpdateSettingsRequest(patch), default);

        snapshot.Level.ShouldBe("Custom");
        var settings = _session.ToObfySettings();
        settings.ControlFlow.Enabled.ShouldBeTrue();
        settings.ControlFlow.Intensity.ShouldBe(70);
        settings.ControlFlow.Mode.ShouldBe(ControlFlowMode.Combined);
        settings.Exclusions.Types.ShouldBe(new[] { "A", "B" });
        settings.Watermark.Id.ShouldBe("x");
    }

    [Fact]
    public async Task Update_RejectsAWrongTypedValue()
    {
        var patch = JsonPatch("""{ "controlFlow.enabled": "yes" }""");

        var ex = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new UpdateSettingsRequestHandler(_session).Handle(new UpdateSettingsRequest(patch), default));

        ex.Code.ShouldBe("settings.invalidValue");
    }

    [Fact]
    public async Task ApplyLevel_ParsesTheNameCaseInsensitively()
    {
        var snapshot = await new ApplyLevelRequestHandler(_session).Handle(new ApplyLevelRequest("aggressive"), default);

        snapshot.Level.ShouldBe("Aggressive");
        _session.Level.ShouldBe(ObfuscationLevel.Aggressive);
    }

    [Fact]
    public async Task ApplyLevel_RejectsAnUnknownLevel()
    {
        var ex = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new ApplyLevelRequestHandler(_session).Handle(new ApplyLevelRequest("Extreme"), default));

        ex.Code.ShouldBe("settings.invalidValue");
    }

    [Fact]
    public async Task OpenConfig_LoadsThePickedFile_AndRemembersItsPath()
    {
        var path = Path.Combine(_dir, "in.json");
        var source = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
        source.Watermark.Enabled = true;
        source.Watermark.Id = "w";
        await ConfigFile.SaveAsync(source, path);
        _dialogs.Files = [path];

        var snapshot = await new OpenConfigRequestHandler(_session, _dialogs, IdleRun.For(_input, _session)).Handle(new OpenConfigRequest(), default);

        snapshot.Level.ShouldBe("Aggressive");
        snapshot.ConfigPath.ShouldBe(path);
        snapshot.Dirty.ShouldBeFalse();
        _session.ToObfySettings().Watermark.Id.ShouldBe("w");
    }

    [Fact]
    public async Task OpenConfig_PresetLevelWithSwitchesThatDiffer_LoadsAsCustom_AndLeavesTheFileAlone()
    {
        // The run would re-apply the file's preset over its switches, so the app shows and runs them as Custom. The file
        // on disk keeps saying what it said, and loading does not make the configuration "unsaved".
        var path = Path.Combine(_dir, "differs.json");
        var source = ObfySettings.ForLevel(ObfuscationLevel.Standard);
        source.ControlFlow.Enabled = true;
        await ConfigFile.SaveAsync(source, path);
        var before = await File.ReadAllTextAsync(path);
        _dialogs.Files = [path];

        var snapshot = await new OpenConfigRequestHandler(_session, _dialogs, IdleRun.For(_input, _session)).Handle(new OpenConfigRequest(), default);

        snapshot.Level.ShouldBe("Custom");
        snapshot.Techniques.Single(t => t.Id == "controlFlow").Enabled.ShouldBeTrue();
        snapshot.ConfigPath.ShouldBe(path);
        snapshot.Dirty.ShouldBeFalse();
        _session.ToObfySettings().Level.ShouldBe(ObfuscationLevel.Custom);
        (await File.ReadAllTextAsync(path)).ShouldBe(before);
    }

    [Fact]
    public async Task LoadConfig_PresetLevelWithSwitchesThatDiffer_LoadsAsCustom_ButExactPresetKeepsItsLevel()
    {
        var differs = Path.Combine(_dir, "differs.json");
        var exact = Path.Combine(_dir, "exact.json");
        var changed = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
        changed.Protection.AntiDump = false;
        await ConfigFile.SaveAsync(changed, differs);
        await ConfigFile.SaveAsync(ObfySettings.ForLevel(ObfuscationLevel.Aggressive), exact);
        var handler = new LoadConfigRequestHandler(_session, IdleRun.For(_input, _session));

        (await handler.Handle(new LoadConfigRequest(differs), default)).Level.ShouldBe("Custom");
        (await handler.Handle(new LoadConfigRequest(exact), default)).Level.ShouldBe("Aggressive");
    }

    [Fact]
    public async Task OpenConfig_Cancelled_ChangesNothing()
    {
        _dialogs.Files = [];

        var snapshot = await new OpenConfigRequestHandler(_session, _dialogs, IdleRun.For(_input, _session)).Handle(new OpenConfigRequest(), default);

        snapshot.ConfigPath.ShouldBeNull();
        snapshot.Level.ShouldBe("Standard");
    }

    [Fact]
    public async Task LoadConfig_ReadsEnumsWrittenAsNamesOrNumbers()
    {
        var named = Path.Combine(_dir, "named.json");
        // Complete presets (a preset level over other switches would load as Custom); only the spelling of the enums varies.
        var namedJson = System.Text.Json.Nodes.JsonNode.Parse(JsonUtils.Serialize(ObfySettings.ForLevel(ObfuscationLevel.Minimal)))!.AsObject();
        namedJson["level"] = "Minimal";
        namedJson["runtimeProfile"] = "BlazorWasm";
        await File.WriteAllTextAsync(named, namedJson.ToJsonString());
        var numbered = Path.Combine(_dir, "numbered.json");
        var numberedJson = System.Text.Json.Nodes.JsonNode.Parse(JsonUtils.Serialize(ObfySettings.ForLevel(ObfuscationLevel.Aggressive)))!.AsObject();
        numberedJson["level"] = (int)ObfuscationLevel.Aggressive; // a number, not a name
        await File.WriteAllTextAsync(numbered, numberedJson.ToJsonString());
        var handler = new LoadConfigRequestHandler(_session, IdleRun.For(_input, _session));

        (await handler.Handle(new LoadConfigRequest(named), default)).RuntimeProfile.ShouldBe("BlazorWasm");
        _session.Level.ShouldBe(ObfuscationLevel.Minimal);

        await handler.Handle(new LoadConfigRequest(numbered), default);
        _session.Level.ShouldBe(ObfuscationLevel.Aggressive);
    }

    [Fact]
    public async Task LoadConfig_MissingOrInvalidFile_ThrowsAnApiError()
    {
        var handler = new LoadConfigRequestHandler(_session, IdleRun.For(_input, _session));
        var bad = Path.Combine(_dir, "bad.json");
        await File.WriteAllTextAsync(bad, "{ not json");

        (await Should.ThrowAsync<ObfyApiException>(async () => await handler.Handle(new LoadConfigRequest(Path.Combine(_dir, "none.json")), default)))
            .Code.ShouldBe("settings.configNotFound");
        (await Should.ThrowAsync<ObfyApiException>(async () => await handler.Handle(new LoadConfigRequest(bad), default)))
            .Code.ShouldBe("settings.configInvalid");
    }

    [Fact]
    public async Task SaveConfig_AsksForAPathFirst_ThenSavesOverIt()
    {
        var path = Path.Combine(_dir, "out", "obfy.json");
        _dialogs.Save = path;
        _session.UpdateSetting("controlFlow.enabled", true);
        var handler = new SaveConfigRequestHandler(_session, _dialogs);

        var first = await handler.Handle(new SaveConfigRequest(), default);

        first.ConfigPath.ShouldBe(path);
        first.Dirty.ShouldBeFalse();
        File.Exists(path).ShouldBeTrue();

        // Second save goes to the same file without asking again.
        _dialogs.Save = null;
        _session.UpdateSetting("controlFlow.intensity", 90);
        _session.GetSnapshot().Dirty.ShouldBeTrue();
        var second = await handler.Handle(new SaveConfigRequest(), default);

        second.Dirty.ShouldBeFalse();
        (await ConfigFile.LoadAsync(path)).ControlFlow.Intensity.ShouldBe(90);
    }

    [Fact]
    public async Task SaveConfig_Cancelled_SavesNothing()
    {
        _dialogs.Save = null;
        _session.UpdateSetting("controlFlow.enabled", true);

        var snapshot = await new SaveConfigRequestHandler(_session, _dialogs).Handle(new SaveConfigRequest(), default);

        snapshot.ConfigPath.ShouldBeNull();
        snapshot.Dirty.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveConfig_RoundTripsThroughLoad()
    {
        var path = Path.Combine(_dir, "rt.json");
        _dialogs.Save = path;
        _session.ApplyLevel(ObfuscationLevel.Aggressive);
        _session.UpdateSetting("exclusions.namespaces", new[] { "Keep" });
        await new SaveConfigRequestHandler(_session, _dialogs).Handle(new SaveConfigRequest(), default);

        var other = new ProtectionSession();
        await new LoadConfigRequestHandler(other, IdleRun.For(_input, other)).Handle(new LoadConfigRequest(path), default);

        other.Level.ShouldBe(ObfuscationLevel.Aggressive);
        other.ToObfySettings().Exclusions.Namespaces.ShouldBe(new[] { "Keep" });
        (await File.ReadAllTextAsync(path)).ShouldContain("\"level\": \"Aggressive\"");
    }

    [Fact]
    public async Task PickSigningKey_SetsTheKeyFile_AndCancelChangesNothing()
    {
        var handler = new PickSigningKeyRequestHandler(_session, _dialogs);

        _dialogs.Key = null;
        await handler.Handle(new PickSigningKeyRequest(), default);
        _session.ToObfySettings().Signing.KeyFile.ShouldBeNull();

        _dialogs.Key = @"C:\keys\lib.snk";
        var snapshot = await handler.Handle(new PickSigningKeyRequest(), default);

        _session.ToObfySettings().Signing.KeyFile.ShouldBe(@"C:\keys\lib.snk");
        snapshot.Techniques.Single(t => t.Id == "signing").Options["keyFile"].ShouldBe(@"C:\keys\lib.snk");
        snapshot.Level.ShouldBe("Standard");
    }
}

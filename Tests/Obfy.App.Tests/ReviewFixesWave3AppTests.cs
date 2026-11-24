using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Features.Settings;
using Obfy.App.Models;
using Obfy.App.Services;
using Obfy.App.Tests.Common;
using Obfy.Common;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.App.Tests;

public sealed class ReviewFixesWave3AppTests : IDisposable
{
    private readonly ReviewHarness _h = new();

    public void Dispose() => _h.Dispose();

    private InputIntake CreateIntake() =>
        new(_h.Input, _h.Protection, _h.Run, NullLogger<InputIntake>.Instance);

    private async Task<string> WriteConfigAsync(string relativePath, string json)
    {
        var path = Path.Combine(_h.Directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    [Fact]
    public async Task InputIntake_DirtySession_StagesPendingConfigDrop_WithoutOverwritingOrLeakingPath()
    {
        var originalPath = await WriteConfigAsync("current.json", """{ "level": "Standard" }""");
        _h.Protection.FromObfySettings(ObfySettings.ForLevel(ObfuscationLevel.Standard), originalPath);
        _h.Protection.UpdateSetting("controlFlow.enabled", true);

        var droppedPath = await WriteConfigAsync(
            Path.Combine("secret-drop-folder", "dropped.json"),
            """{ "level": "Aggressive", "unknownDropKey": true }""");

        await CreateIntake().AddPathsAsync([droppedPath], default);

        var snapshot = _h.Protection.GetSnapshot();
        snapshot.PendingConfigDrop.ShouldBeTrue();
        snapshot.Dirty.ShouldBeTrue();
        snapshot.Level.ShouldBe("Custom");
        snapshot.ConfigPath.ShouldBe(originalPath);
        _h.Protection.ToObfySettings().ControlFlow.Enabled.ShouldBeTrue();
        _h.Protection.ToObfySettings().ConstantEncryption.Enabled.ShouldBeFalse();

        // Privacy & premature-notice checks: snapshot never reveals the dropped path or unknown keys prior to confirmation.
        var serialized = JsonUtils.Serialize(snapshot);
        serialized.ShouldNotContain("secret-drop-folder");
        serialized.ShouldNotContain("dropped.json");
        serialized.ShouldNotContain("unknownDropKey");
        _h.Input.GetSnapshot().Notices.ShouldBeEmpty();

        _h.Events.Published
            .Where(e => e.Name == AppEvents.SettingsChanged)
            .Select(e => (SettingsSnapshot)e.Data!)
            .Last()
            .PendingConfigDrop.ShouldBeTrue();
    }

    [Fact]
    public async Task ConfirmConfigDrop_AppliesStagedConfig_AndReplacesOrClearsUnknownKeysNotice()
    {
        _h.Protection.UpdateSetting("controlFlow.enabled", true);
        _h.Input.AddNotice(NoticeLevel.Warning, "Unrelated", "Keep me.");
        _h.Input.AddNotice(NoticeLevel.Warning, "Unknown configuration keys", $"{ConfigFile.UnknownKeysNoticePrefix} staleKey.");

        var droppedWithUnknown = await WriteConfigAsync(
            Path.Combine("private-dir", "new.json"),
            """{ "level": "Minimal", "bogusKeyOne": 1 }""");
        var intake = CreateIntake();
        await intake.AddPathsAsync([droppedWithUnknown], default);
        _h.Protection.GetSnapshot().PendingConfigDrop.ShouldBeTrue();

        var confirmHandler = new ConfirmConfigDropRequestHandler(_h.Protection, _h.Run, _h.Input);
        var confirmed = await confirmHandler.Handle(new ConfirmConfigDropRequest(), default);

        confirmed.PendingConfigDrop.ShouldBeFalse();
        confirmed.Dirty.ShouldBeFalse();
        confirmed.Level.ShouldBe("Minimal");
        confirmed.ConfigPath.ShouldBe(droppedWithUnknown);
        _h.Protection.ToObfySettings().ControlFlow.Enabled.ShouldBeFalse();

        var noticesAfterFirst = _h.Input.GetSnapshot().Notices;
        noticesAfterFirst.Count.ShouldBe(2);
        noticesAfterFirst.ShouldContain(n => n.Title == "Unrelated");
        var unknownNotice = noticesAfterFirst.Single(n => n.Title == "Unknown configuration keys");
        unknownNotice.Message.ShouldContain("bogusKeyOne");
        unknownNotice.Message.ShouldNotContain("staleKey");
        unknownNotice.Message.ShouldNotContain("private-dir");

        // Confirming a subsequent clean drop clears the unknown-keys notice while keeping unrelated notices.
        _h.Protection.UpdateSetting("controlFlow.enabled", true);
        var droppedClean = await WriteConfigAsync("clean.json", """{ "level": "Aggressive" }""");
        await intake.AddPathsAsync([droppedClean], default);

        var confirmedClean = await confirmHandler.Handle(new ConfirmConfigDropRequest(), default);
        confirmedClean.PendingConfigDrop.ShouldBeFalse();
        confirmedClean.Level.ShouldBe("Aggressive");
        var remaining = _h.Input.GetSnapshot().Notices.ShouldHaveSingleItem();
        remaining.Title.ShouldBe("Unrelated");
    }

    [Fact]
    public async Task CancelConfigDrop_ClearsPendingDrop_AndPreservesDirtySettings()
    {
        var originalPath = await WriteConfigAsync("orig.json", """{ "level": "Standard" }""");
        _h.Protection.FromObfySettings(ObfySettings.ForLevel(ObfuscationLevel.Standard), originalPath);
        _h.Protection.UpdateSetting("controlFlow.enabled", true);
        _h.Input.AddNotice(NoticeLevel.Info, "Existing", "Stay intact.");

        var droppedPath = await WriteConfigAsync("dropped.json", """{ "level": "Aggressive", "unknownFlag": true }""");
        await CreateIntake().AddPathsAsync([droppedPath], default);
        _h.Protection.GetSnapshot().PendingConfigDrop.ShouldBeTrue();

        var cancelled = await new CancelConfigDropRequestHandler(_h.Protection)
            .Handle(new CancelConfigDropRequest(), default);

        cancelled.PendingConfigDrop.ShouldBeFalse();
        cancelled.Dirty.ShouldBeTrue();
        cancelled.Level.ShouldBe("Custom");
        cancelled.ConfigPath.ShouldBe(originalPath);
        _h.Protection.ToObfySettings().ControlFlow.Enabled.ShouldBeTrue();

        var notice = _h.Input.GetSnapshot().Notices.ShouldHaveSingleItem();
        notice.Title.ShouldBe("Existing");
    }

    [Fact]
    public async Task InputIntake_CleanSession_AppliesDroppedConfigDirectly()
    {
        var droppedPath = await WriteConfigAsync("clean-direct.json", """{ "level": "Minimal", "ignoredOption": 42 }""");

        await CreateIntake().AddPathsAsync([droppedPath], default);

        var snapshot = _h.Protection.GetSnapshot();
        snapshot.PendingConfigDrop.ShouldBeFalse();
        snapshot.Dirty.ShouldBeFalse();
        snapshot.Level.ShouldBe("Minimal");
        snapshot.ConfigPath.ShouldBe(droppedPath);

        var notice = _h.Input.GetSnapshot().Notices.ShouldHaveSingleItem();
        notice.Title.ShouldBe("Unknown configuration keys");
        notice.Message.ShouldContain("ignoredOption");
    }

    [Fact]
    public async Task SaveOrOpenConfig_ClearsPendingConfigDropAutomatically()
    {
        var intake = CreateIntake();
        var droppedPath = await WriteConfigAsync("dropped.json", """{ "level": "Minimal" }""");

        // Saving clears pending drop and writes the user's dirty settings (not the dropped config).
        _h.Protection.UpdateSetting("controlFlow.enabled", true);
        await intake.AddPathsAsync([droppedPath], default);
        _h.Protection.GetSnapshot().PendingConfigDrop.ShouldBeTrue();

        var savedPath = Path.Combine(_h.Directory, "saved.json");
        var saveDialogs = new FakeDialogs { SavePath = savedPath };
        var saved = await new SaveConfigRequestHandler(_h.Protection, saveDialogs)
            .Handle(new SaveConfigRequest(), default);

        saved.PendingConfigDrop.ShouldBeFalse();
        saved.Dirty.ShouldBeFalse();
        saved.ConfigPath.ShouldBe(savedPath);
        _h.Protection.ToObfySettings().ControlFlow.Enabled.ShouldBeTrue();
        (await ConfigFile.LoadAsync(savedPath)).ControlFlow.Enabled.ShouldBeTrue();

        // Opening another config clears pending drop and applies the opened config.
        _h.Protection.UpdateSetting("controlFlow.enabled", false);
        await intake.AddPathsAsync([droppedPath], default);
        _h.Protection.GetSnapshot().PendingConfigDrop.ShouldBeTrue();

        var openedPath = await WriteConfigAsync("opened.json", """{ "level": "Aggressive" }""");
        var openDialogs = new FakeDialogs { Files = [openedPath] };
        var opened = await new OpenConfigRequestHandler(_h.Protection, openDialogs, _h.Run, _h.Input)
            .Handle(new OpenConfigRequest(), default);

        opened.PendingConfigDrop.ShouldBeFalse();
        opened.Dirty.ShouldBeFalse();
        opened.Level.ShouldBe("Aggressive");
        opened.ConfigPath.ShouldBe(openedPath);
    }

    [Fact]
    public async Task ConfirmConfigDrop_RefusesWhileRunIsInProgress_WithoutClearingPendingDrop()
    {
        _h.AddFile("app.dll");
        _h.Protection.UpdateSetting("controlFlow.enabled", true);
        var droppedPath = await WriteConfigAsync("dropped.json", """{ "level": "Minimal" }""");
        await CreateIntake().AddPathsAsync([droppedPath], default);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Engine.Obfuscate = async (_, _, _, _) =>
        {
            started.TrySetResult();
            await finish.Task;
            return ObfuscationResult.Successful(new ObfuscationStatistics());
        };

        var runTask = _h.Run.Start();
        await started.Task.WaitAsync(ReviewHarness.Guard);

        var ex = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new ConfirmConfigDropRequestHandler(_h.Protection, _h.Run, _h.Input)
                .Handle(new ConfirmConfigDropRequest(), default));
        ex.Code.ShouldBe("run.blocked");
        _h.Protection.GetSnapshot().PendingConfigDrop.ShouldBeTrue();
        _h.Protection.GetSnapshot().Dirty.ShouldBeTrue();

        finish.SetResult(ObfuscationResult.Successful(new ObfuscationStatistics()));
        await runTask.WaitAsync(ReviewHarness.Guard);
    }

    [Fact]
    public void ProtectionSession_ApplyOrStageConfigDrop_AppliesWhenCleanAndStagesWhenDirty_WithoutDiskProbe()
    {
        var probeCount = 0;
        var session = new Obfy.App.Sessions.ProtectionSession(
            events: null,
            fileExists: _ =>
            {
                Interlocked.Increment(ref probeCount);
                return true;
            });

        session.UpdateSetting("signing.enabled", true);
        session.UpdateSetting("signing.keyFile", "missing.snk");

        // Dirty: stages without probing the signing key file on disk.
        var staged = session.ApplyOrStageConfigDrop(
            ObfySettings.ForLevel(ObfuscationLevel.Minimal),
            "dropped-dirty.json",
            ["extraKey"]);
        staged.ShouldBeFalse();
        probeCount.ShouldBe(0);
        session.GetSnapshot().PendingConfigDrop.ShouldBeTrue();

        // MarkSaved clears dirty and pending drop; next ApplyOrStageConfigDrop applies immediately without probing.
        probeCount = 0;
        session.MarkSaved("saved.json");
        probeCount.ShouldBe(0);

        var applied = session.ApplyOrStageConfigDrop(
            ObfySettings.ForLevel(ObfuscationLevel.Aggressive),
            "dropped-clean.json",
            []);
        applied.ShouldBeTrue();
        probeCount.ShouldBe(0);

        var snapshot = session.GetSnapshot();
        snapshot.PendingConfigDrop.ShouldBeFalse();
        snapshot.Dirty.ShouldBeFalse();
        snapshot.Level.ShouldBe("Aggressive");
        snapshot.ConfigPath.ShouldBe("dropped-clean.json");
    }

    [Fact]
    public async Task FailedConfigDrop_DeduplicatesErrorNotices_AndValidLoadClearsStaleErrorAcrossAllPaths()
    {
        var intake = CreateIntake();
        _h.Input.AddNotice(NoticeLevel.Info, "Unrelated", "Keep me.");

        var broken1 = await WriteConfigAsync("broken-one.json", "{ not json");
        var broken2 = await WriteConfigAsync("broken-two.json", "{ still not json");
        var validClean = await WriteConfigAsync("valid-clean.json", """{ "level": "Minimal" }""");
        var validOpened = await WriteConfigAsync("valid-opened.json", """{ "level": "Standard" }""");
        var validConfirmed = await WriteConfigAsync("valid-confirmed.json", """{ "level": "Aggressive" }""");

        // Repeated failed drops deduplicate the error notice rather than stacking duplicates.
        await intake.AddPathsAsync([broken1], default);
        await intake.AddPathsAsync([broken2], default);

        var afterTwoFailures = _h.Input.GetSnapshot().Notices;
        afterTwoFailures.Count.ShouldBe(2);
        afterTwoFailures.ShouldContain(n => n.Title == "Unrelated");
        var errorNotice = afterTwoFailures.Single(n => n.Title == ConfigFile.ConfigLoadErrorNoticePrefix);
        errorNotice.Level.ShouldBe(NoticeLevel.Error);
        errorNotice.Message.ShouldContain("broken-two.json");
        errorNotice.Message.ShouldNotContain("broken-one.json");

        // 1. Valid drop clears the stale error notice.
        await intake.AddPathsAsync([validClean], default);
        _h.Input.GetSnapshot().Notices.ShouldHaveSingleItem().Title.ShouldBe("Unrelated");

        // 2. OpenConfig clears a stale error notice.
        await intake.AddPathsAsync([broken1], default);
        _h.Input.GetSnapshot().Notices.ShouldContain(n => n.Title == ConfigFile.ConfigLoadErrorNoticePrefix);
        var openDialogs = new FakeDialogs { Files = [validOpened] };
        await new OpenConfigRequestHandler(_h.Protection, openDialogs, _h.Run, _h.Input)
            .Handle(new OpenConfigRequest(), default);
        _h.Input.GetSnapshot().Notices.ShouldHaveSingleItem().Title.ShouldBe("Unrelated");

        // 3. ConfirmConfigDrop clears a stale error notice.
        _h.Protection.UpdateSetting("controlFlow.enabled", true);
        await intake.AddPathsAsync([broken2], default);
        _h.Input.GetSnapshot().Notices.ShouldContain(n => n.Title == ConfigFile.ConfigLoadErrorNoticePrefix);
        await intake.AddPathsAsync([validConfirmed], default);
        _h.Protection.GetSnapshot().PendingConfigDrop.ShouldBeTrue();
        await new ConfirmConfigDropRequestHandler(_h.Protection, _h.Run, _h.Input)
            .Handle(new ConfirmConfigDropRequest(), default);
        _h.Input.GetSnapshot().Notices.ShouldHaveSingleItem().Title.ShouldBe("Unrelated");
    }
}

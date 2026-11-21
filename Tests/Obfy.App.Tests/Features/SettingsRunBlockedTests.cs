using System.Text.Json;
using Obfy.App.Features.Settings;
using Obfy.App.Services;
using Obfy.App.Tests.Common;
using Obfy.Common;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.App.Tests.Features;

/// <summary>
/// The page locks the protection settings during a run, but a settings call from anywhere else must be refused too, or the
/// shown values differ from what the run captured. Same <c>run.blocked</c> as the file and configuration calls.
/// </summary>
public sealed class SettingsRunBlockedTests : IDisposable
{
    private sealed class KeyDialogs : IFileDialogs
    {
        public int KeyPicks;

        public Task<string[]> PickFilesAsync(string title, string filter, bool multi) => Task.FromResult<string[]>([]);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveAsync(string title, string filter, string? defaultName) => Task.FromResult<string?>(null);

        public Task<string?> PickKeyAsync()
        {
            KeyPicks++;
            return Task.FromResult<string?>(@"C:\keys\my.snk");
        }
    }

    private readonly ReviewHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task StartRunThatWaitsAsync(TaskCompletionSource finish)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Engine.Obfuscate = async (_, _, _, _) =>
        {
            started.TrySetResult();
            await finish.Task;
            return ObfuscationResult.Successful(new ObfuscationStatistics());
        };
        _h.AddFile();
        _ = _h.Run.Start();
        await started.Task.WaitAsync(ReviewHarness.Guard);
    }

    [Fact]
    public async Task Update_IsRefusedWhileARunGoes_AndChangesNothing()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await StartRunThatWaitsAsync(finish);
        var before = _h.Protection.GetSnapshot().Revision;
        var patch = JsonUtils.Deserialize<Dictionary<string, JsonElement>>("""{ "controlFlow.enabled": true }""")!;

        var ex = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new UpdateSettingsRequestHandler(_h.Protection, _h.Run).Handle(new UpdateSettingsRequest(patch), default));

        ex.Code.ShouldBe("run.blocked");
        _h.Protection.GetSnapshot().Revision.ShouldBe(before);
        _h.Protection.ToObfySettings().ControlFlow.Enabled.ShouldBeFalse();
        finish.SetResult();
        await _h.Run.Completion.WaitAsync(ReviewHarness.Guard);
    }

    [Fact]
    public async Task ApplyLevel_IsRefusedWhileARunGoes_AndChangesNothing()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await StartRunThatWaitsAsync(finish);

        var ex = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new ApplyLevelRequestHandler(_h.Protection, _h.Run).Handle(new ApplyLevelRequest("Aggressive"), default));

        ex.Code.ShouldBe("run.blocked");
        _h.Protection.Level.ShouldBe(ObfuscationLevel.Standard);
        finish.SetResult();
        await _h.Run.Completion.WaitAsync(ReviewHarness.Guard);
    }

    [Fact]
    public async Task PickSigningKey_IsRefusedWhileARunGoes_BeforeTheDialogOpens()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await StartRunThatWaitsAsync(finish);
        var dialogs = new KeyDialogs();

        var ex = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new PickSigningKeyRequestHandler(_h.Protection, dialogs, _h.Run).Handle(new PickSigningKeyRequest(), default));

        ex.Code.ShouldBe("run.blocked");
        dialogs.KeyPicks.ShouldBe(0);
        _h.Protection.ToObfySettings().Signing.KeyFile.ShouldBeNull();
        finish.SetResult();
        await _h.Run.Completion.WaitAsync(ReviewHarness.Guard);
    }

    [Fact]
    public async Task SettingsCalls_WorkWhenNoRunGoes()
    {
        var patch = JsonUtils.Deserialize<Dictionary<string, JsonElement>>("""{ "controlFlow.enabled": true }""")!;

        var updated = await new UpdateSettingsRequestHandler(_h.Protection, _h.Run).Handle(new UpdateSettingsRequest(patch), default);
        var leveled = await new ApplyLevelRequestHandler(_h.Protection, _h.Run).Handle(new ApplyLevelRequest("Aggressive"), default);

        updated.Level.ShouldBe("Custom");
        leveled.Level.ShouldBe("Aggressive");
    }
}

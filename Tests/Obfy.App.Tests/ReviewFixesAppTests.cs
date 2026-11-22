using Obfy.App;
using Obfy.App.Features.Files;
using Obfy.App.Features.Output;
using Obfy.App.Models;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.App.Tests;

public sealed class ReviewFixesAppTests : IDisposable
{
    private readonly ReviewHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Start_AssignsCompletionImmediatelyWhileRunning()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Engine.Obfuscate = (_, _, _, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };
        _h.AddFile("a.dll");

        var runTask = _h.Run.Start();
        var completion = _h.Run.Completion;

        _h.Run.IsRunning.ShouldBeTrue();
        completion.IsCompleted.ShouldBeFalse();

        await started.Task.WaitAsync(ReviewHarness.Guard);
        finish.SetResult(ObfuscationResult.Successful(new ObfuscationStatistics()));

        await completion.WaitAsync(ReviewHarness.Guard);
        await runTask.WaitAsync(ReviewHarness.Guard);
        _h.Run.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task WhileIdle_RunsAtomicallyWhenIdle_AndThrowsWhileRunning()
    {
        var first = _h.AddFile("a.dll");
        var second = _h.AddFile("b.dll");
        var rows = _h.Input.GetSnapshot().Files;
        var firstId = rows.Single(r => r.Name == Path.GetFileName(first)).Id;

        var count = _h.Run.WhileIdle(() =>
        {
            _h.Input.Remove(firstId);
            return _h.Input.GetSnapshot().Files.Count;
        });
        count.ShouldBe(1);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ObfuscationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Engine.Obfuscate = (_, _, _, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };

        var runTask = _h.Run.Start();
        await started.Task.WaitAsync(ReviewHarness.Guard);

        var secondId = _h.Input.GetSnapshot().Files.Single(r => r.Name == Path.GetFileName(second)).Id;
        var ex = await Should.ThrowAsync<ObfyApiException>(async () =>
            await new RemoveFileRequestHandler(_h.Input, _h.Run).Handle(new RemoveFileRequest(secondId), default));
        ex.Code.ShouldBe("run.blocked");
        _h.Input.GetSnapshot().Files.ShouldHaveSingleItem();

        finish.SetResult(ObfuscationResult.Successful(new ObfuscationStatistics()));
        await runTask.WaitAsync(ReviewHarness.Guard);
    }

    [Fact]
    public async Task Finish_PublishesTerminalRunCompletedStatus()
    {
        _h.AddFile("a.dll");
        _h.Engine.Obfuscate = (_, _, _, _) => Task.FromResult(ObfuscationResult.Successful(new ObfuscationStatistics()));

        await _h.Run.Start().WaitAsync(ReviewHarness.Guard);

        var completed = _h.Events.Published
            .Where(e => e.Name == AppEvents.RunCompleted)
            .Select(e => (RunSnapshot)e.Data!)
            .ToList();
        completed.ShouldHaveSingleItem();
        completed[0].Status.ShouldBe(RunStatus.Done);
    }

    [Fact]
    public void InputSession_MultiFileSourcePlanAndSnapshot_ComputeExpectedPaths()
    {
        var sub = Path.Combine(_h.Directory, "src", "nested");
        Directory.CreateDirectory(sub);
        var a = Path.Combine(_h.Directory, "src", "A.cs");
        var b = Path.Combine(sub, "B.cs");
        File.WriteAllText(a, "class A {}");
        File.WriteAllText(b, "class B {}");
        var outDir = Path.Combine(_h.Directory, "out");

        _h.Input.AddPaths([a, b]);
        _h.Input.SetOutputFolder(outDir);

        var plan = _h.Input.PlanOutput(merge: false);
        plan.Outputs.Select(o => o.OutputPath).ShouldBe([
            Path.Combine(outDir, "A.cs"),
            Path.Combine(outDir, "nested", "B.cs")
        ]);
        _h.Input.GetSnapshot().OutputHint.ShouldBe($"Writes each file to {outDir}.");
    }

    [Fact]
    public void WhileIdle_PublishesDeferredDependencyChange_EvenWhenDelegateThrowsAfterMutating()
    {
        _h.AddFile("a.dll");
        var beforeCount = _h.Events.Published.Count(e => e.Name == AppEvents.RunChanged);

        Should.Throw<InvalidOperationException>(() =>
            _h.Run.WhileIdle<int>(() =>
            {
                _h.Input.SetWriteSymbolMap(true);
                throw new InvalidOperationException("boom after mutation");
            }));

        var runChanged = _h.Events.Published
            .Where(e => e.Name == AppEvents.RunChanged)
            .Select(e => (RunSnapshot)e.Data!)
            .ToList();
        runChanged.Count.ShouldBeGreaterThan(beforeCount);

        // No stale deferred flag leaks into a subsequent no-op WhileIdle call.
        var afterErrorCount = runChanged.Count;
        _h.Run.WhileIdle(() => { });
        _h.Events.Published.Count(e => e.Name == AppEvents.RunChanged).ShouldBe(afterErrorCount);
    }

    [Fact]
    public async Task SetSymbolMap_WithInvalidPath_IsAtomic_AndLeavesEnabledUnchanged()
    {
        var handler = new SetSymbolMapRequestHandler(_h.Input, _h.Run);
        var initial = _h.Input.GetSnapshot();
        initial.WriteSymbolMap.ShouldBeFalse();

        var ex = await Should.ThrowAsync<ObfyApiException>(async () =>
            await handler.Handle(new SetSymbolMapRequest(Enabled: true, Path: "bad\0path.json"), default));

        ex.Code.ShouldBe("output.invalidPath");
        var after = _h.Input.GetSnapshot();
        after.WriteSymbolMap.ShouldBeFalse();
        after.SymbolMapPath.ShouldBe(initial.SymbolMapPath);
        after.Revision.ShouldBe(initial.Revision);
    }
}

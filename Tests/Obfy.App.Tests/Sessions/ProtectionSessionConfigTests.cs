using Obfy.App.Sessions;
using Obfy.App.Services;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.App.Tests.Sessions;

/// <summary>Regression tests for what happens when a configuration loads, and for validation outside the lock.</summary>
public sealed class ProtectionSessionConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obfy-protcfg-" + Guid.NewGuid().ToString("N"));

    public ProtectionSessionConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<ObfySettings> LoadJsonAsync(string json)
    {
        var path = Path.Combine(_dir, "obfy.json");
        await File.WriteAllTextAsync(path, json);
        return await ConfigFile.LoadAsync(path);
    }

    // ---- 6b: a null section ----

    [Theory]
    [InlineData("""{ "watermark": null }""")]
    [InlineData("""{ "signing": null }""")]
    [InlineData("""{ "protection": null }""")]
    [InlineData("""{ "protection": { "antiTamper": null, "antiDecompiler": null } }""")]
    [InlineData("""{ "stringEncryption": null, "controlFlow": null, "symbolRenaming": null, "metadata": null }""")]
    [InlineData("""{ "packing": null, "incremental": null, "virtualization": null, "assemblyMerge": null }""")]
    [InlineData("""{ "resourceEncryption": null, "constantEncryption": null, "dependencyEmbedding": null }""")]
    [InlineData("""{ "exclusions": null, "inclusions": null }""")]
    public async Task ALoadedConfigWithANullSection_StillGivesASnapshotAndAValidationResult(string json)
    {
        var settings = await LoadJsonAsync(json);
        var session = new ProtectionSession();

        session.FromObfySettings(settings, Path.Combine(_dir, "obfy.json"));

        Should.NotThrow(() => session.GetSnapshot());
        Should.NotThrow(() => session.ValidationErrors());
        Should.NotThrow(() => session.ToObfySettings().Validate());
        session.GetSnapshot().Techniques.ShouldNotBeEmpty();
    }

    [Fact]
    public void ANullSectionBecomesTheDefault_NotAnEmptyOrBrokenOne()
    {
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Custom);
        typeof(ObfySettings).GetProperty(nameof(ObfySettings.Watermark))!.SetValue(settings, null);
        var session = new ProtectionSession();

        session.FromObfySettings(settings);

        var loaded = session.ToObfySettings();
        loaded.Watermark.ShouldNotBeNull();
        loaded.Watermark.Enabled.ShouldBeFalse();
    }

    // ---- 6c: a preset label that matches what the file holds ----

    [Fact]
    public void APresetFileWhoseTechniqueFlagsDifferFromThePreset_BecomesCustom_SoTheRunHonoursThem()
    {
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Standard);
        settings.ControlFlow.Enabled = true; // Standard turns control flow off, and the engine would turn it off again
        var session = new ProtectionSession();

        session.FromObfySettings(settings, @"C:\cfg\obfy.json");

        session.Level.ShouldBe(ObfuscationLevel.Custom);
        session.ToObfySettings().ControlFlow.Enabled.ShouldBeTrue();
        session.GetSnapshot().Dirty.ShouldBeFalse();
    }

    [Theory]
    [InlineData(ObfuscationLevel.Minimal)]
    [InlineData(ObfuscationLevel.Standard)]
    [InlineData(ObfuscationLevel.Aggressive)]
    public void APresetFileWhoseFlagsMatchThePreset_KeepsItsLabel(ObfuscationLevel level)
    {
        var session = new ProtectionSession();

        session.FromObfySettings(ObfySettings.ForLevel(level), @"C:\cfg\obfy.json");

        session.Level.ShouldBe(level);
    }

    [Fact]
    public void WhatTheProtectionScreenShows_IsWhatTheEngineWillRun()
    {
        var settings = ObfySettings.ForLevel(ObfuscationLevel.Aggressive);
        settings.Protection.AntiDump = false;
        settings.ConstantEncryption.Algorithm = EncryptionAlgorithm.Aes256;
        var session = new ProtectionSession();

        session.FromObfySettings(settings);

        var shown = session.ToObfySettings();
        var engine = shown.Clone();
        if (engine.Level != ObfuscationLevel.Custom)
            engine.ApplyLevel(); // what ObfuscationService does before it runs
        engine.Protection.AntiDump.ShouldBe(shown.Protection.AntiDump);
        engine.ConstantEncryption.Algorithm.ShouldBe(shown.ConstantEncryption.Algorithm);
    }

    [Fact]
    public async Task ALevelOnlyFile_RunsThePreset_AndShowsIt()
    {
        var settings = await LoadJsonAsync("""{ "level": "aggressive", "watermark": { "enabled": true, "id": "x" } }""");
        var session = new ProtectionSession();

        session.FromObfySettings(settings, Path.Combine(_dir, "obfy.json"));

        session.Level.ShouldBe(ObfuscationLevel.Aggressive);
        var shown = session.ToObfySettings();
        shown.ControlFlow.Enabled.ShouldBeTrue();
        shown.Protection.AntiTamper.Enabled.ShouldBeTrue();
        shown.Watermark.Id.ShouldBe("x");
    }

    [Fact]
    public async Task AFileThatNamesTechniqueFlags_IsJudgedByThem()
    {
        var settings = await LoadJsonAsync("""{ "level": "standard", "controlFlow": { "enabled": true } }""");
        var session = new ProtectionSession();

        session.FromObfySettings(settings, Path.Combine(_dir, "obfy.json"));

        session.Level.ShouldBe(ObfuscationLevel.Custom);
        session.ToObfySettings().ControlFlow.Enabled.ShouldBeTrue();
    }

    // ---- 6d: the key file is probed outside the lock ----

    [Fact]
    public void TheKeyFileIsProbedOutsideTheSessionLock()
    {
        ProtectionSession? session = null;
        var probes = 0;
        var otherThreadGotTheLock = true;
        session = new ProtectionSession(fileExists: _ =>
        {
            Interlocked.Increment(ref probes);
            // Level takes the session's lock: from another thread it can only return if this probe holds none.
            otherThreadGotTheLock &= Task.Run(() => session!.Level).Wait(TimeSpan.FromSeconds(2));
            return true;
        });
        session.Update(new Dictionary<string, object?> { ["signing.enabled"] = true, ["signing.keyFile"] = "key.snk" });
        probes = 0;

        session.ValidationErrors();
        session.GetSnapshot();

        probes.ShouldBe(2);
        otherThreadGotTheLock.ShouldBeTrue();
    }

    [Fact]
    public void AMissingKeyFile_StillBlocks_WithItsMessageFirst()
    {
        var session = new ProtectionSession(fileExists: _ => false);
        session.Update(new Dictionary<string, object?> { ["signing.enabled"] = true, ["signing.keyFile"] = "key.snk" });

        var errors = session.ValidationErrors();

        errors.ShouldContain(e => e.Field == "signing.keyFile" && e.Message.Contains("not found"));
        session.GetSnapshot().Errors.Select(e => e.Message).ShouldBe(errors.Select(e => e.Message));
    }
}

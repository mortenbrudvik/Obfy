using System.CommandLine;
using Obfy.Console.Wizard;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Console.Tests;

public class ReviewFixesConsoleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyReviewConsole_{Guid.NewGuid():N}");

    public ReviewFixesConsoleTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task BuildSettingsAsync_NullJsonSections_FillsDefaultsWithoutThrowing()
    {
        var path = Path.Combine(_root, "nulls.json");
        await File.WriteAllTextAsync(path, """
            {
              "signing": null,
              "watermark": null,
              "exclusions": null,
              "inclusions": null,
              "protection": {
                "antiTamper": null,
                "antiDecompiler": null
              },
              "packing": {
                "rid": null
              }
            }
            """);

        var settings = await Program.BuildSettingsAsync(
            new FileInfo(path),
            "standard",
            stringEncrypt: false,
            controlFlow: false,
            rename: false,
            antiDebug: false,
            stripMetadata: false,
            encryptResources: false,
            preservePublic: false);

        settings.Signing.ShouldNotBeNull();
        settings.Watermark.ShouldNotBeNull();
        settings.Exclusions.ShouldNotBeNull();
        settings.Exclusions.Namespaces.ShouldNotBeNull();
        settings.Inclusions.ShouldNotBeNull();
        settings.Protection.AntiTamper.ShouldNotBeNull();
        settings.Protection.AntiDecompiler.ShouldNotBeNull();
        settings.Packing.Rid.ShouldBe("win-x64");
    }

    [Fact]
    public void ConfigKeyValidator_DuplicateKeys_DoesNotThrow()
    {
        var json = """
            {
              "level": "Standard",
              "level": "Minimal",
              "protection": {
                "antiDebug": true,
                "antiDebug": false
              }
            }
            """;

        var unknown = ConfigKeyValidator.FindUnknownKeys(json);

        unknown.ShouldBeEmpty();
    }

    [Fact]
    public void Wizard_ApplyUseCaseDefaults_DoesNotOverrideExplicitFalsePreservePublicApi()
    {
        var context = new WizardContext
        {
            UseCase = "Class Library / NuGet Package",
            IsPublicApi = false,
            PreservePublicApiChoice = false,
            Settings = ObfySettings.ForLevel(ObfuscationLevel.Standard)
        };
        context.Settings.SymbolRenaming.PreservePublicApi = false;

        ConfigurationWizard.ApplyUseCaseDefaults(context);

        context.Settings.SymbolRenaming.PreservePublicApi.ShouldBeFalse();
    }

    [Fact]
    public void ConfigGenerate_ErrorWithDefaultInvocationConfiguration_WritesToConsoleErrorOnly()
    {
        var previousError = System.Console.Error;
        using var stderr = new StringWriter();
        using var capture = new AnsiConsoleCapture();
        System.Console.SetError(stderr);
        try
        {
            var exitCode = Program.CreateRootCommand()
                .Parse($"config generate -o \"{Path.Combine(_root, "bad.json")}\" --level banana")
                .Invoke(new InvocationConfiguration());

            exitCode.ShouldBe(1);
            stderr.ToString().ShouldContain("Unknown obfuscation level 'banana'");
            capture.Text.ShouldNotContain("Unknown obfuscation level 'banana'");
        }
        finally
        {
            System.Console.SetError(previousError);
        }
    }
}

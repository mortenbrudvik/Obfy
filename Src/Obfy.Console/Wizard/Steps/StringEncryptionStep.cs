using Obfy.Core.Models;
using Spectre.Console;

namespace Obfy.Console.Wizard.Steps;

/// <summary>
/// Step to configure string encryption.
/// </summary>
public class StringEncryptionStep : WizardStep
{
    public override string Title => "String Encryption";

    public override Task ExecuteAsync(WizardContext context)
    {
        // Skip in quick mode
        if (context.IsQuickMode)
        {
            return Task.CompletedTask;
        }

        WriteHeader(Title);

        WriteHint("String encryption replaces hardcoded strings with encrypted versions.");
        WriteHint("They are decrypted at runtime, hiding sensitive data from decompilers.");
        AnsiConsole.WriteLine();

        var enabled = AnsiConsole.Confirm(
            "Enable string encryption?",
            defaultValue: context.Settings.StringEncryption.Enabled);

        context.Settings.StringEncryption.Enabled = enabled;

        if (enabled)
        {
            AnsiConsole.WriteLine();

            // Algorithm selection
            var algorithm = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Encryption algorithm:")
                    .HighlightStyle(Style.Parse("cyan"))
                    .AddChoices([
                        "AES-256 - Strong encryption (recommended)",
                        "XOR     - Faster, simpler encryption"
                    ]));

            context.Settings.StringEncryption.Algorithm = algorithm.StartsWith("AES")
                ? EncryptionAlgorithm.Aes256
                : EncryptionAlgorithm.Xor;

            // Minimum string length
            AnsiConsole.WriteLine();
            var minLength = AnsiConsole.Prompt(
                new TextPrompt<int>("Minimum string length to encrypt:")
                    .DefaultValue(context.Settings.StringEncryption.MinStringLength)
                    .Validate(value => value switch
                    {
                        < 1 => ValidationResult.Error("[red]Must be at least 1[/]"),
                        > 100 => ValidationResult.Error("[red]Must be 100 or less[/]"),
                        _ => ValidationResult.Success()
                    }));

            context.Settings.StringEncryption.MinStringLength = minLength;
        }

        return Task.CompletedTask;
    }
}

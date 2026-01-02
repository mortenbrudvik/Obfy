using Spectre.Console;

namespace Obfy.Console.Wizard.Steps;

/// <summary>
/// Step to configure runtime protection features.
/// </summary>
public class ProtectionStep : WizardStep
{
    public override string Title => "Runtime Protection";

    public override Task ExecuteAsync(WizardContext context)
    {
        // Skip in quick mode
        if (context.IsQuickMode)
        {
            return Task.CompletedTask;
        }

        WriteHeader(Title);

        WriteHint("Runtime protection adds checks that detect debugging and tampering.");
        AnsiConsole.WriteLine();

        // Anti-Debug
        var antiDebug = AnsiConsole.Confirm(
            "Enable anti-debugging protection?",
            defaultValue: context.Settings.Protection.AntiDebug);
        context.Settings.Protection.AntiDebug = antiDebug;

        if (antiDebug)
        {
            WriteInfo("Assembly will detect and respond to debugger attachment.");
        }

        // Anti-Tamper
        AnsiConsole.WriteLine();
        var antiTamper = AnsiConsole.Confirm(
            "Enable anti-tamper detection?",
            defaultValue: context.Settings.Protection.AntiTamper.Enabled);
        context.Settings.Protection.AntiTamper.Enabled = antiTamper;

        if (antiTamper)
        {
            WriteInfo("Assembly integrity will be verified at runtime.");
        }

        // Anti-Decompiler
        AnsiConsole.WriteLine();
        var antiDecompiler = AnsiConsole.Confirm(
            "Enable anti-decompiler protection?",
            defaultValue: context.Settings.Protection.AntiDecompiler.Enabled);
        context.Settings.Protection.AntiDecompiler.Enabled = antiDecompiler;

        if (antiDecompiler)
        {
            WriteInfo("Junk code will be added to confuse decompilers.");
        }

        // Resource Encryption
        AnsiConsole.WriteLine();
        var resourceEncryption = AnsiConsole.Confirm(
            "Enable resource encryption?",
            defaultValue: context.Settings.ResourceEncryption.Enabled);
        context.Settings.ResourceEncryption.Enabled = resourceEncryption;

        if (resourceEncryption)
        {
            WriteInfo("Embedded resources will be encrypted.");
        }

        // Constant Encryption
        AnsiConsole.WriteLine();
        var constantEncryption = AnsiConsole.Confirm(
            "Enable constant (numeric) encryption?",
            defaultValue: context.Settings.ConstantEncryption.Enabled);
        context.Settings.ConstantEncryption.Enabled = constantEncryption;

        if (constantEncryption)
        {
            WriteInfo("Numeric literals will be encrypted.");
        }

        return Task.CompletedTask;
    }
}

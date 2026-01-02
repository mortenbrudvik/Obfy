using Spectre.Console;

namespace Obfy.Console.Wizard.Steps;

/// <summary>
/// Step to select the application type.
/// </summary>
public class UseCaseStep : WizardStep
{
    public override string Title => "Application Type";

    public override Task ExecuteAsync(WizardContext context)
    {
        WriteHeader(Title);

        WriteHint("This helps us suggest optimal settings for your project.");
        AnsiConsole.WriteLine();

        var useCase = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What type of application are you protecting?")
                .HighlightStyle(Style.Parse("cyan"))
                .AddChoices([
                    "Desktop Application",
                    "Console Application",
                    "Class Library / NuGet Package",
                    "Web Application (ASP.NET)",
                    "Game (Unity)",
                    "Other"
                ]));

        context.UseCase = useCase;

        // For libraries, ask about public API
        if (useCase is "Class Library / NuGet Package" or "Other")
        {
            AnsiConsole.WriteLine();
            context.IsPublicApi = AnsiConsole.Confirm(
                "Does this project expose a public API that external code will call?",
                defaultValue: useCase == "Class Library / NuGet Package");

            if (context.IsPublicApi)
            {
                WriteInfo("Public API names will be preserved.");
            }
        }

        return Task.CompletedTask;
    }
}

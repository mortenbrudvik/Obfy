using System.CommandLine;

namespace Obfy.Console.Tests;

internal static class CommandLineTestHelpers
{
    public static int Invoke(RootCommand command, string args, out string output)
    {
        var writer = new StringWriter();
        var config = new InvocationConfiguration
        {
            Output = writer,
            Error = writer
        };
        var exitCode = command.Parse(args).Invoke(config);
        output = writer.ToString();
        return exitCode;
    }

    public static async Task<int> InvokeAsync(RootCommand command, string args)
    {
        var writer = new StringWriter();
        var config = new InvocationConfiguration
        {
            Output = writer,
            Error = writer
        };
        return await command.Parse(args).InvokeAsync(config);
    }
}

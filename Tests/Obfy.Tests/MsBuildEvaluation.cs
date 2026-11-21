using System.Diagnostics;
using Shouldly;

namespace Obfy.Tests;

/// <summary>Runs <c>dotnet msbuild</c> to evaluate a project property, or a target, without building.</summary>
internal static class MsBuildEvaluation
{
    public static (int ExitCode, string Output) Run(string workingDirectory, string project, params string[] arguments)
    {
        File.Exists(project).ShouldBeTrue($"Expected project at {project}");

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "msbuild", project, "-nologo", "-nodeReuse:false" }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(180_000).ShouldBeTrue("dotnet msbuild timed out");
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    /// <summary>The value MSBuild evaluates for <paramref name="property"/> (empty when it is not set).</summary>
    public static string Property(string workingDirectory, string project, string property, params string[] arguments)
    {
        var (exitCode, output) = Run(workingDirectory, project, ["-getProperty:" + property, .. arguments]);
        exitCode.ShouldBe(0, output);
        return output.Trim();
    }
}

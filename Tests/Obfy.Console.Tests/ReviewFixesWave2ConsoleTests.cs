using System.CommandLine;
using Obfy.Console;
using Shouldly;

namespace Obfy.Console.Tests;

public class ReviewFixesWave2ConsoleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ObfyWave2Console_{Guid.NewGuid():N}");

    public ReviewFixesWave2ConsoleTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task CreateRootCommand_ConcurrentCalls_AreIsolatedAndThreadSafe()
    {
        const int workers = 16;
        using var gate = new ManualResetEventSlim(false);
        var levels = new[] { "minimal", "standard", "aggressive", "custom" };

        var tasks = Enumerable.Range(0, workers).Select(index => Task.Run(async () =>
        {
            gate.Wait();

            var level = levels[index % levels.Length];
            var dll = Path.Combine(_root, $"app-{index}.dll");
            var configOut = Path.Combine(_root, $"cfg-{index}.json");
            var watermark = $"mark-{index}";

            RootCommand cmd = Program.CreateRootCommand();
            var parse = cmd.Parse($"\"{dll}\" --level {level} --watermark-id {watermark} {(index % 2 == 0 ? "--merge" : "")}");

            parse.Errors.ShouldBeEmpty();
            parse.ArgumentValue<FileInfo[]>("input").ShouldHaveSingleItem().FullName.ShouldBe(dll);
            parse.OptionValue<string>("--level").ShouldBe(level);
            parse.OptionValue<string?>("--watermark-id").ShouldBe(watermark);
            parse.OptionValue<bool>("--merge").ShouldBe(index % 2 == 0);

            var genCmd = Program.CreateRootCommand();
            var exitCode = await CommandLineTestHelpers.InvokeAsync(
                genCmd, $"config generate -o \"{configOut}\" -l {(index % 2 == 0 ? "minimal" : "aggressive")}");
            exitCode.ShouldBe(0);
            File.Exists(configOut).ShouldBeTrue();
        })).ToArray();

        gate.Set();
        await Task.WhenAll(tasks);
    }
}

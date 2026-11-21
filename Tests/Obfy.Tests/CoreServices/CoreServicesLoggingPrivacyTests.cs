using Autofac;
using Microsoft.CodeAnalysis;
using Moq;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Obfuscators;
using Obfy.Core.Pipeline;
using Obfy.Core.Services;
using Obfy.Core.Services.Reporting;
using Obfy.Core.Services.Solution;
using Shouldly;

namespace Obfy.Tests.CoreServices;

/// <summary>
/// CLAUDE.md: the log file keeps Debug and above, so nothing the user typed or picked may be logged.
/// The orchestration services log counts, stages and exception types. Paths and exception text stay in
/// the user-facing result (ErrorMessage / warnings) only.
/// </summary>
public class CoreServicesLoggingPrivacyTests : IDisposable
{
    private const string DirSentinel = "SENTINELDIR7f3a";
    private const string ExceptionSentinel = "EXCEPTIONTEXT9c1d";

    private readonly string _root = CoreServicesTestSupport.NewTempDirectory("obfy-privacy");
    private readonly string _secretDir;

    public CoreServicesLoggingPrivacyTests()
    {
        _secretDir = Path.Combine(_root, DirSentinel);
        Directory.CreateDirectory(_secretDir);
    }

    public void Dispose() => CoreServicesTestSupport.DeleteQuietly(_root);

    private static ObfySettings Strings() => new()
    {
        Level = ObfuscationLevel.Custom,
        StringEncryption = { Enabled = true },
        SymbolRenaming = { Enabled = true },
        ControlFlow = { Enabled = false }
    };

    [Fact]
    public async Task ObfuscateAsync_Success_LogsNoInputOrOutputPath()
    {
        var sink = new LogSink();
        using var container = CoreServicesTestSupport.BuildContainer(sink);
        var service = container.Resolve<IObfuscationService>();
        var input = CoreServicesTestSupport.CompileLibrary(
            _secretDir, "Lib", """public class C { public static string S() => "hello world"; }""");
        var output = Path.Combine(_secretDir, "Lib.obfuscated.dll");

        var result = await service.ObfuscateAsync(input, output, Strings());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        sink.OwnedCount().ShouldBeGreaterThan(0, "the service should still log its stages");
        sink.ShouldHaveLoggedNoneOf(DirSentinel, _root, "Lib.obfuscated", "Lib.dll");
    }

    [Fact]
    public async Task ObfuscateAsync_LoadFailure_LogsOnlyTheExceptionType_AndResultKeepsTheMessage()
    {
        var sink = new LogSink();
        var input = Path.Combine(_secretDir, "in.dll");
        await File.WriteAllBytesAsync(input, [1, 2, 3]);

        var processor = new Mock<IAssemblyProcessor>();
        processor
            .Setup(p => p.LoadAsync(It.IsAny<string>(), It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"{ExceptionSentinel} {input}"));
        var service = NewService(sink, processor.Object);

        var result = await service.ObfuscateAsync(input, null, Strings());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain(ExceptionSentinel, Case.Sensitive);
        sink.OwnedCount().ShouldBeGreaterThan(0);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, ExceptionSentinel, _root, "in.dll");
        sink.OwnedTexts().ShouldContain(t => t.Contains(nameof(InvalidOperationException)));
    }

    [Fact]
    public async Task ObfuscateAsync_SaveFailure_LogsNoPathOrExceptionText()
    {
        var sink = new LogSink();
        using var container = CoreServicesTestSupport.BuildContainer();
        var input = CoreServicesTestSupport.CompileLibrary(
            _secretDir, "Lib", """public class C { public static int N() => 1; }""");
        var output = Path.Combine(_secretDir, "Lib.out.dll");

        var real = container.Resolve<IAssemblyProcessor>();
        var processor = new Mock<IAssemblyProcessor>();
        processor
            .Setup(p => p.LoadAsync(It.IsAny<string>(), It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .Returns((string path, ObfySettings s, CancellationToken ct) => real.LoadAsync(path, s, ct));
        processor
            .Setup(p => p.SaveAsync(It.IsAny<PipelineContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException($"{ExceptionSentinel} {output}"));
        var service = NewService(sink, processor.Object, container);

        var result = await service.ObfuscateAsync(input, output, Strings());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain(ExceptionSentinel, Case.Sensitive);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, ExceptionSentinel, _root, "Lib.out", "Lib.dll");
    }

    [Fact]
    public async Task ObfuscateAsync_MissingSigningKey_ResultNamesTheKeyFile_ButTheLogDoesNot()
    {
        var sink = new LogSink();
        using var container = CoreServicesTestSupport.BuildContainer(sink);
        var service = container.Resolve<IObfuscationService>();
        var input = CoreServicesTestSupport.CompileLibrary(
            _secretDir, "Lib", """public class C { public static int N() => 1; }""");
        var keyFile = Path.Combine(_secretDir, "missing-key.snk");
        var settings = Strings();
        settings.Signing.Enabled = true;
        settings.Signing.KeyFile = keyFile;

        var result = await service.ObfuscateAsync(input, Path.Combine(_secretDir, "Lib.out.dll"), settings);

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain(keyFile);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, "missing-key", _root);
    }

    [Fact]
    public async Task ObfuscateSourcesAsync_LoadFailure_LogsNoPathOrExceptionText()
    {
        var sink = new LogSink();
        var first = Path.Combine(_secretDir, "A.cs");
        var second = Path.Combine(_secretDir, "B.cs");
        var source = new Mock<ISourceProcessor>();
        source
            .Setup(p => p.LoadFilesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<ObfySettings>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"{ExceptionSentinel} {first}"));
        var service = NewService(sink, new Mock<IAssemblyProcessor>().Object, sourceProcessor: source.Object);

        var result = await service.ObfuscateSourcesAsync([first, second], Path.Combine(_secretDir, "out"), Strings());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain(ExceptionSentinel, Case.Sensitive);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, ExceptionSentinel, _root);
    }

    [Fact]
    public async Task WriteSymbolMapAsync_DoesNotLogThePath()
    {
        var sink = new LogSink();
        var service = NewService(sink, new Mock<IAssemblyProcessor>().Object);

        await service.WriteSymbolMapAsync(new Dictionary<string, string> { ["a"] = "b" }, Path.Combine(_secretDir, "map.json"));

        sink.OwnedCount().ShouldBeGreaterThan(0);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, "map.json", _root);
    }

    [Fact]
    public async Task MergeAndObfuscateAsync_MergeFailure_LogsNoPathOrMergerMessage()
    {
        var sink = new LogSink();
        var first = Path.Combine(_secretDir, "A.dll");
        var second = Path.Combine(_secretDir, "B.dll");
        var merger = new Mock<IAssemblyMerger>();
        merger
            .Setup(m => m.MergeAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<AssemblyMergeSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssemblyMergeResult { Success = false, ErrorMessage = $"{ExceptionSentinel} {first}" });
        var service = NewService(sink, new Mock<IAssemblyProcessor>().Object, merger: merger.Object);

        var result = await service.MergeAndObfuscateAsync(
            [first, second], Path.Combine(_secretDir, "Merged.dll"), Strings());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain(ExceptionSentinel, Case.Sensitive);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, ExceptionSentinel, _root, "Merged.dll");
    }

    [Fact]
    public async Task AssemblyMerger_RealMerge_LogsNoPaths()
    {
        var sink = new LogSink();
        using var container = CoreServicesTestSupport.BuildContainer(sink);
        var merger = container.Resolve<IAssemblyMerger>();
        var libA = CoreServicesTestSupport.CompileLibrary(_secretDir, "LibA", "public class A { }");
        var libB = CoreServicesTestSupport.CompileLibrary(_secretDir, "LibB", "public class B { }");
        var output = Path.Combine(_secretDir, "Merged.dll");

        var result = await merger.MergeAsync([libA, libB], output, new AssemblyMergeSettings());

        result.Success.ShouldBeTrue(result.ErrorMessage);
        sink.OwnedCount().ShouldBeGreaterThan(0);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, _root, "LibA.dll", "LibB.dll", "Merged.dll");
    }

    [Fact]
    public async Task AssemblyMerger_FailedMerge_LogsNoPaths_AndResultKeepsTheMessage()
    {
        var sink = new LogSink();
        using var container = CoreServicesTestSupport.BuildContainer(sink);
        var merger = container.Resolve<IAssemblyMerger>();
        var junkA = Path.Combine(_secretDir, "JunkA.dll");
        var junkB = Path.Combine(_secretDir, "JunkB.dll");
        await File.WriteAllBytesAsync(junkA, [1, 2, 3]);
        await File.WriteAllBytesAsync(junkB, [4, 5, 6]);

        var result = await merger.MergeAsync([junkA, junkB], Path.Combine(_secretDir, "Merged.dll"), new AssemblyMergeSettings());

        result.Success.ShouldBeFalse();
        sink.OwnedCount().ShouldBeGreaterThan(0);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, _root, "JunkA", "JunkB", "Merged.dll");
    }

    [Fact]
    public async Task ReportGenerators_DoNotLogTheReportPath()
    {
        var sink = new LogSink();
        var html = new HtmlReportGenerator(new CapturingLogger<HtmlReportGenerator>(sink));
        var json = new JsonReportGenerator(new CapturingLogger<JsonReportGenerator>(sink));
        var report = new ObfuscationReport();

        await html.GenerateAsync(report, Path.Combine(_secretDir, "report.html"));
        await json.GenerateAsync(report, Path.Combine(_secretDir, "report.json"));

        File.Exists(Path.Combine(_secretDir, "report.html")).ShouldBeTrue();
        sink.OwnedCount().ShouldBe(2);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, "report.html", "report.json", _root);
    }

    [Fact]
    public async Task Pipeline_ObfuscatorFailureAndException_LogNoMessageText_ButResultsKeepIt()
    {
        var sink = new LogSink();
        var failing = new Mock<IObfuscator>();
        failing.SetupGet(o => o.Name).Returns("Failing");
        failing.SetupGet(o => o.Priority).Returns(1);
        failing.Setup(o => o.SupportsTargetType(It.IsAny<TargetType>())).Returns(true);
        failing.Setup(o => o.IsEnabled(It.IsAny<ObfySettings>())).Returns(true);
        failing
            .Setup(o => o.ObfuscateAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ObfuscationResult.Failed($"{ExceptionSentinel} {_secretDir}"));
        var throwing = new Mock<IObfuscator>();
        throwing.SetupGet(o => o.Name).Returns("Throwing");
        throwing.SetupGet(o => o.Priority).Returns(2);
        throwing.Setup(o => o.SupportsTargetType(It.IsAny<TargetType>())).Returns(true);
        throwing.Setup(o => o.IsEnabled(It.IsAny<ObfySettings>())).Returns(true);
        throwing
            .Setup(o => o.ObfuscateAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"{ExceptionSentinel} {_secretDir}"));

        var logger = new CapturingLogger<ObfuscationPipeline>(sink);
        var failResult = await new ObfuscationPipeline([failing.Object], logger).ExecuteAsync(NewSourceContext());
        var throwResult = await new ObfuscationPipeline([throwing.Object], logger).ExecuteAsync(NewSourceContext());

        failResult.Success.ShouldBeFalse();
        failResult.ErrorMessage.ShouldNotBeNull().ShouldContain(ExceptionSentinel, Case.Sensitive);
        throwResult.Success.ShouldBeFalse();
        throwResult.ErrorMessage.ShouldNotBeNull().ShouldContain(ExceptionSentinel, Case.Sensitive);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, ExceptionSentinel, _root);
    }

    [Fact]
    public async Task ClosedSet_LoadFailureAndSuccess_LogNoPaths_AndResultKeepsThePath()
    {
        var sink = new LogSink();
        using var container = CoreServicesTestSupport.BuildContainer(sink);
        var service = container.Resolve<IObfuscationService>();
        var good = CoreServicesTestSupport.CompileLibrary(_secretDir, "Good", "public class G { }");
        var bad = Path.Combine(_secretDir, "Bad.dll");
        await File.WriteAllBytesAsync(bad, [1, 2, 3, 4]);

        var failed = await service.ObfuscateClosedSetAsync(
            [Input(good), Input(bad)], Path.Combine(_secretDir, "out-bad"), Strings());
        var good2 = CoreServicesTestSupport.CompileLibrary(_secretDir, "Good2", "public class G2 { }");
        var ok = await service.ObfuscateClosedSetAsync(
            [Input(good), Input(good2)], Path.Combine(_secretDir, "out-ok"), Strings());

        failed.Success.ShouldBeFalse();
        failed.LoadFailures.ShouldContain(f => f.Path == bad);
        ok.Success.ShouldBeTrue(ok.ErrorMessage);
        sink.OwnedCount().ShouldBeGreaterThan(0);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, _root, "Bad.dll", "Good.dll", "Good2.dll");
    }

    [Fact]
    public async Task ClosedSet_SaveFailure_LogsNoPathOrExceptionText()
    {
        var sink = new LogSink();
        using var container = CoreServicesTestSupport.BuildContainer();
        var a = CoreServicesTestSupport.CompileLibrary(_secretDir, "A", "public class A { }");
        var b = CoreServicesTestSupport.CompileLibrary(_secretDir, "B", "public class B { }");
        var processor = new Mock<IAssemblyProcessor>();
        processor
            .Setup(p => p.SaveAsync(It.IsAny<PipelineContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException($"{ExceptionSentinel} {_secretDir}"));
        var closed = new ClosedSetProcessor(
            new CapturingLogger<ClosedSetProcessor>(sink),
            container.Resolve<IObfuscationPipeline>(),
            processor.Object,
            container.Resolve<Obfy.Core.Obfuscators.Assembly.SymbolRenamingObfuscator>());

        var result = await closed.ExecuteAsync([Input(a), Input(b)], Path.Combine(_secretDir, "out"), Strings());

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain(ExceptionSentinel, Case.Sensitive);
        sink.ShouldHaveLoggedNoneOf(DirSentinel, ExceptionSentinel, _root, "A.dll", "B.dll");
    }

    [Fact]
    public async Task ClosedSet_CommitFailure_LogsNoPathOrExceptionText()
    {
        var sink = new LogSink();
        using var container = CoreServicesTestSupport.BuildContainer(sink);
        var service = container.Resolve<IObfuscationService>();
        var a = CoreServicesTestSupport.CompileLibrary(_secretDir, "A", "public class A { }");
        var b = CoreServicesTestSupport.CompileLibrary(_secretDir, "B", "public class B { }");
        var outDir = Path.Combine(_secretDir, "out");
        Directory.CreateDirectory(Path.Combine(outDir, "B.dll")); // a directory where the file must go

        var result = await service.ObfuscateClosedSetAsync([Input(a), Input(b)], outDir, Strings());

        result.Success.ShouldBeFalse();
        sink.OwnedTexts().ShouldContain(t => t.Contains("UnauthorizedAccessException") || t.Contains("IOException"));
        sink.ShouldHaveLoggedNoneOf(DirSentinel, _root, "B.dll", "A.dll");
    }

    private static ClosedSetInput Input(string path) =>
        new() { AssemblyPath = path, Hints = new ProjectSettingsHints() };

    private static PipelineContext NewSourceContext() => new() { TargetType = TargetType.SourceCode, Settings = new ObfySettings() };

    private static ObfuscationService NewService(
        LogSink sink,
        IAssemblyProcessor assemblyProcessor,
        IContainer? container = null,
        ISourceProcessor? sourceProcessor = null,
        IAssemblyMerger? merger = null)
    {
        return new ObfuscationService(
            assemblyProcessor,
            sourceProcessor ?? new Mock<ISourceProcessor>().Object,
            container?.Resolve<IObfuscationPipeline>() ?? new Mock<IObfuscationPipeline>().Object,
            merger ?? new Mock<IAssemblyMerger>().Object,
            new CapturingLogger<ObfuscationService>(sink),
            new Mock<IClosedSetProcessor>().Object);
    }
}

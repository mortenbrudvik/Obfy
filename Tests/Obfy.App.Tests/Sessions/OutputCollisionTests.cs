using Microsoft.Extensions.Logging.Abstractions;
using Obfy.App.Sessions;
using Obfy.App.Tests.Common;
using Obfy.Core.Models;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.App.Tests.Sessions;

/// <summary>
/// The guard that a run never writes over a file the user listed (data loss: the output folder was the folder of an
/// input, and the protected copy replaced the original). These tests look at the session level, with the real planning
/// code: which outputs the plan holds for each mode and how paths are told apart. <c>RunCoordinatorTests</c> covers the run
/// bar's reason and the refusal to start.
/// </summary>
public sealed class OutputCollisionTests : IDisposable
{
    private readonly FakeSettings _settings = new();
    private readonly string _temp;

    public OutputCollisionTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), $"OutputCollisionTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_temp))
                Directory.Delete(_temp, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private InputSession Create() =>
        new(_settings, null, new FakeShell(), null, NullLogger<InputSession>.Instance, TimeSpan.Zero);

    /// <summary>Makes the file (and its folders) and returns its path.</summary>
    private string Make(string relative)
    {
        var path = Path.Combine(_temp, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    private string Folder(string relative = "")
    {
        var path = Path.Combine(_temp, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    private InputSession With(params string[] files)
    {
        var session = Create();
        session.AddPaths(files);
        return session;
    }

    private static OutputCollision? Collision(InputSession session, bool merge = false) => session.CaptureRun(merge).Collision;

    private static string SameFolderMessage(string name, string wayOut) =>
        $"The output folder is the folder of {name}; the run would replace your original. {wayOut}";

    // ---- Each ----

    [Fact]
    public void Each_FolderIsTheInputFolder_Collides_AndTheMessageSaysHowToGetOut()
    {
        var session = With(Make("App.dll"));
        session.SetOutputFolder(_temp);

        var collision = Collision(session).ShouldNotBeNull();

        collision.InputPath.ShouldBe(Path.Combine(_temp, "App.dll"));
        collision.OutputPath.ShouldBe(Path.Combine(_temp, "App.dll"));
        collision.Message.ShouldBe(SameFolderMessage(
            "App.dll", "Pick another folder, or clear it to write App.obfuscated.dll beside it."));
    }

    [Fact]
    public void Each_ASourceFile_IsProtectedToo()
    {
        var session = With(Make("Program.cs"));
        session.SetOutputFolder(_temp);

        Collision(session).ShouldNotBeNull().Message.ShouldBe(SameFolderMessage(
            "Program.cs", "Pick another folder, or clear it to write Program.obfuscated.cs beside it."));
    }

    [Fact]
    public void Each_NoFolder_DoesNotCollide()
    {
        Collision(With(Make("App.dll"))).ShouldBeNull();
    }

    [Fact]
    public void Each_AnotherFolder_DoesNotCollide()
    {
        var session = With(Make("App.dll"));

        session.SetOutputFolder(Path.Combine(_temp, "out"));
        Collision(session).ShouldBeNull();

        // A folder inside the input's folder, and the folder above it, are not the input's folder.
        session.SetOutputFolder(Folder("sub"));
        Collision(session).ShouldBeNull();
        session.SetOutputFolder(Path.GetDirectoryName(_temp)!);
        Collision(session).ShouldBeNull();
    }

    [Fact]
    public void NothingListed_DoesNotCollide()
    {
        var session = Create();
        session.SetOutputFolder(_temp);

        Collision(session).ShouldBeNull();
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("upper")]
    [InlineData("lower")]
    [InlineData("trailingSeparator")]
    [InlineData("trailingAltSeparator")]
    [InlineData("dotDot")]
    [InlineData("dot")]
    [InlineData("doubleSeparators")]
    public void Each_ADifferentSpellingOfTheInputFolder_StillCollides(string spelling)
    {
        var session = With(Make("App.dll"));
        Folder("sub");
        var typed = spelling switch
        {
            "upper" => _temp.ToUpperInvariant(),
            "lower" => _temp.ToLowerInvariant(),
            "trailingSeparator" => _temp + Path.DirectorySeparatorChar,
            "trailingAltSeparator" => _temp + Path.AltDirectorySeparatorChar,
            "dotDot" => Path.Combine(_temp, "sub", ".."),
            "dot" => Path.Combine(_temp, "."),
            "doubleSeparators" => _temp + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar + "sub" + Path.DirectorySeparatorChar + "..",
            _ => _temp
        };

        session.SetOutputFolder(typed);

        Collision(session).ShouldNotBeNull();
    }

    [Fact]
    public void Each_TheInputAddedWithADotDotSegment_StillCollides()
    {
        Make("App.dll");
        Folder("sub");
        var session = With(Path.Combine(_temp, "sub", "..", "App.dll"));
        session.SetOutputFolder(_temp);

        Collision(session).ShouldNotBeNull().InputPath.ShouldBe(Path.Combine(_temp, "App.dll"));
    }

    [Fact]
    public void Each_AnExcludedRow_IsStillAFileTheRunMustNotReplace()
    {
        var included = Make(Path.Combine("a", "Lib.dll"));
        var excluded = Make(Path.Combine("b", "Lib.dll"));
        var session = With(included, excluded);
        var row = session.GetSnapshot().Files.Single(f => f.Path == excluded);
        session.SetIncluded(row.Id, included: false);

        session.SetOutputFolder(Path.GetDirectoryName(excluded)!);

        Collision(session).ShouldNotBeNull().InputPath.ShouldBe(excluded);
        session.SetOutputFolder(Path.GetDirectoryName(included)!);
        Collision(session).ShouldNotBeNull().InputPath.ShouldBe(included);
        session.SetOutputFolder(Folder("elsewhere"));
        Collision(session).ShouldBeNull();
    }

    // ---- Closed set ----

    [Fact]
    public void ClosedSet_FolderIsTheFolderOfAnInput_Collides_AndNamesWhereTheDefaultGoes()
    {
        var session = With(Make("a.dll"), Make("b.dll"));
        session.SetOutputFolder(_temp);

        var collision = Collision(session).ShouldNotBeNull();

        collision.InputPath.ShouldBe(Path.Combine(_temp, "a.dll"));
        collision.Message.ShouldBe(SameFolderMessage(
            "a.dll", $"Pick another folder, or clear it to write a.dll to {Path.Combine(_temp, "obfy-out")}."));
    }

    [Fact]
    public void ClosedSet_OnlyTheOtherInputsFolder_Collides_AndNamesThatInput()
    {
        var session = With(Make(Path.Combine("a", "one.dll")), Make(Path.Combine("b", "two.dll")));
        session.SetOutputFolder(Path.Combine(_temp, "b"));

        Collision(session).ShouldNotBeNull().InputPath.ShouldBe(Path.Combine(_temp, "b", "two.dll"));
    }

    [Fact]
    public void ClosedSet_NoFolder_AndAnotherFolder_DoNotCollide()
    {
        var session = With(Make("a.dll"), Make("b.dll"));
        Collision(session).ShouldBeNull();

        session.SetOutputFolder(Path.Combine(_temp, "out"));
        Collision(session).ShouldBeNull();
    }

    [Fact]
    public void ClosedSet_ASharedNameLandsInAFolderOfItsOwn_AndThatCanReplaceAnInput()
    {
        // Two App.dll: the engine writes {tfm}\App.dll under the output folder. With the output folder at bin, that is
        // exactly where the originals are.
        var session = With(Make(Path.Combine("bin", "net8.0", "App.dll")), Make(Path.Combine("bin", "net6.0", "App.dll")));
        var bin = Path.Combine(_temp, "bin");
        session.SetOutputFolder(bin);

        var plan = session.PlanOutput(merge: false);
        plan.Outputs.Select(o => o.OutputPath).ShouldBe(
            [Path.Combine(bin, "net8.0", "App.dll"), Path.Combine(bin, "net6.0", "App.dll")]);

        var collision = Collision(session).ShouldNotBeNull();
        collision.InputPath.ShouldBe(Path.Combine(bin, "net8.0", "App.dll"));
        collision.Message.ShouldStartWith(
            "The output folder holds App.dll, one of your inputs; the run would replace your original. Pick another folder");
    }

    // ---- Merge ----

    [Fact]
    public void Merge_FolderIsTheInputFolder_OnlyCollidesWhenTheMergedNameIsListed()
    {
        // The merged file is a.obfuscated.dll: writing it beside a.dll and b.dll replaces neither.
        var session = With(Make("a.dll"), Make("b.dll"));
        session.SetOutputFolder(_temp);
        Collision(session, merge: true).ShouldBeNull();

        // Listed as well, it is the one thing the merge would replace.
        session.AddPaths([Make("a.obfuscated.dll")]);
        var collision = Collision(session, merge: true).ShouldNotBeNull();

        collision.InputPath.ShouldBe(Path.Combine(_temp, "a.obfuscated.dll"));
        collision.OutputPath.ShouldBe(session.PlanOutput(merge: true).MergePath);
        // Clearing the folder writes beside the first input, which is the same folder: no way out but another folder.
        collision.Message.ShouldBe(SameFolderMessage("a.obfuscated.dll", "Pick another folder."));
    }

    [Fact]
    public void Merge_TheMergedNameIsListedInAnotherFolder_ClearingTheFolderIsAWayOut()
    {
        var session = With(Make(Path.Combine("a", "a.dll")), Make(Path.Combine("b", "a.obfuscated.dll")));
        session.SetOutputFolder(Path.Combine(_temp, "b"));

        var collision = Collision(session, merge: true).ShouldNotBeNull();

        collision.Message.ShouldBe(SameFolderMessage(
            "a.obfuscated.dll", $"Pick another folder, or clear it to write a.obfuscated.dll to {Path.Combine(_temp, "a")}."));
    }

    [Fact]
    public void Merge_NoFolder_CollidesOnlyWhenTheDefaultNameIsListed()
    {
        var session = With(Make("a.dll"), Make("b.dll"));
        Collision(session, merge: true).ShouldBeNull();

        session.AddPaths([Make("a.obfuscated.dll")]);
        var collision = Collision(session, merge: true).ShouldNotBeNull();

        collision.Message.ShouldBe(
            "The run would write a.obfuscated.dll over your input of the same name. Pick an output folder to write elsewhere.");
    }

    // ---- C# sources ----

    [Fact]
    public void Sources_TwoFilesIntoTheirOwnFolder_Collide()
    {
        var session = With(Make("a.cs"), Make("b.cs"));
        session.SetOutputFolder(_temp);

        var collision = Collision(session).ShouldNotBeNull();

        collision.Message.ShouldBe(SameFolderMessage(
            "a.cs", $"Pick another folder, or clear it to write a.cs to {Path.Combine(_temp, "obfy-src")}."));
    }

    [Fact]
    public void Sources_TwoFilesWithNoFolderOrAnotherFolder_DoNotCollide()
    {
        var session = With(Make("a.cs"), Make("b.cs"));
        Collision(session).ShouldBeNull();

        session.SetOutputFolder(Path.Combine(_temp, "out"));
        Collision(session).ShouldBeNull();
    }

    [Fact]
    public void Sources_AFileInASubfolder_IsReplacedWhenTheOutputFolderIsTheFolderTheyShare()
    {
        // The engine keeps each file's path relative to the folder the files share, so sub\b.cs is written to sub\b.cs.
        var first = Make("a.cs");
        var second = Make(Path.Combine("sub", "b.cs"));
        var session = With(second, first);
        session.SetOutputFolder(_temp);

        session.PlanOutput(merge: false).Outputs.Select(o => o.OutputPath).ShouldBe([second, first]);
        Collision(session).ShouldNotBeNull().InputPath.ShouldBe(second);
    }

    [Fact]
    public async Task Sources_ThePlannedPaths_AreTheFilesTheEngineWrites()
    {
        // The plan borrows the engine's own layout rule; this pins that it still names what the engine really writes.
        var inputs = new[]
        {
            Make(Path.Combine("src", "a.cs")),
            Make(Path.Combine("src", "deep", "b.cs")),
            Make(Path.Combine("src", "deep", "er", "c.cs"))
        };
        foreach (var input in inputs)
            File.WriteAllText(input, $"class C{Path.GetFileNameWithoutExtension(input)} {{}}");
        var session = With(inputs);
        var output = Path.Combine(_temp, "out");
        session.SetOutputFolder(output);
        var planned = session.PlanOutput(merge: false).Outputs.Select(o => o.OutputPath).ToList();

        var processor = new SourceProcessor(NullLogger<SourceProcessor>.Instance);
        var context = await processor.LoadFilesAsync(inputs, new ObfySettings());
        await processor.SaveAsync(context, output);

        planned.Count.ShouldBe(3);
        Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ShouldBe(planned.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sources_AMixedList_PlansTheSourcesAsOneCompilationAndTheAssemblyAlone()
    {
        var session = With(Make("a.cs"), Make("b.cs"), Make("App.dll"));
        session.SetOutputFolder(Path.Combine(_temp, "out"));

        session.PlanOutput(merge: false).Outputs.Select(o => o.OutputPath).ShouldBe(
        [
            Path.Combine(_temp, "out", "a.cs"),
            Path.Combine(_temp, "out", "b.cs"),
            Path.Combine(_temp, "out", "App.dll")
        ]);
    }

    // ---- The symbol map (B-44: a map typed onto a listed assembly replaced it with JSON after the run) ----

    private static string MapOverInput(string name) =>
        $"The symbol map would replace {name}, one of your inputs. Choose another location for the symbol map.";

    private static string MapOverOutput(string name) =>
        $"The symbol map would replace {name}, which the run itself writes. Choose another location for the symbol map.";

    private InputSession WithSymbolMap(string mapPath, params string[] files)
    {
        var session = With(files);
        session.SetWriteSymbolMap(true);
        session.SetSymbolMapPath(mapPath);
        return session;
    }

    [Fact]
    public void SymbolMap_PathIsAnIncludedInput_Collides_AndTheMessageSaysHowToGetOut()
    {
        var input = Make("App.dll");
        var session = WithSymbolMap(input, input);

        var collision = Collision(session).ShouldNotBeNull();

        collision.InputPath.ShouldBe(input);
        collision.OutputPath.ShouldBe(input);
        collision.Message.ShouldBe(MapOverInput("App.dll"));
    }

    [Fact]
    public void SymbolMap_PathIsASkippedListedRow_Collides()
    {
        var included = Make("App.dll");
        var skipped = Make("Other.dll");
        var session = With(included, skipped);
        session.SetIncluded(session.GetSnapshot().Files.Single(f => f.Path == skipped).Id, included: false);
        session.SetWriteSymbolMap(true);
        session.SetSymbolMapPath(skipped);

        var collision = Collision(session).ShouldNotBeNull();

        collision.InputPath.ShouldBe(skipped);
        collision.Message.ShouldBe(MapOverInput("Other.dll"));
    }

    [Theory]
    [InlineData("upper")]
    [InlineData("lower")]
    [InlineData("dotDot")]
    [InlineData("dot")]
    [InlineData("doubleSeparators")]
    public void SymbolMap_ADifferentSpellingOfTheInput_StillCollides(string spelling)
    {
        var input = Make("App.dll");
        Folder("sub");
        var typed = spelling switch
        {
            "upper" => input.ToUpperInvariant(),
            "lower" => input.ToLowerInvariant(),
            "dotDot" => Path.Combine(_temp, "sub", "..", "App.dll"),
            "dot" => Path.Combine(_temp, ".", "App.dll"),
            _ => _temp + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar + "App.dll"
        };

        var session = WithSymbolMap(typed, input);

        Collision(session).ShouldNotBeNull().Message.ShouldBe(MapOverInput("App.dll"));
    }

    [Fact]
    public void SymbolMap_ARelativePathOntoAListedFileInTheOutputFolder_Collides()
    {
        // The protected copy goes to out\App.dll; the skipped row out\Lib.dll is a different file the map would replace.
        var included = Make(Path.Combine("a", "App.dll"));
        var skipped = Make(Path.Combine("out", "Lib.dll"));
        var session = With(included, skipped);
        session.SetIncluded(session.GetSnapshot().Files.Single(f => f.Path == skipped).Id, included: false);
        session.SetOutputFolder(Path.Combine(_temp, "out"));
        session.SetWriteSymbolMap(true);
        session.SetSymbolMapPath("Lib.dll");

        session.ResolveSymbolMapPath().ShouldBe(skipped);
        Collision(session).ShouldNotBeNull().Message.ShouldBe(MapOverInput("Lib.dll"));
    }

    [Fact]
    public void SymbolMap_ARelativePathWithDotDotOntoAnIncludedInput_Collides()
    {
        var input = Make(Path.Combine("a", "App.dll"));
        var session = With(input);
        session.SetOutputFolder(Path.Combine(_temp, "out"));
        session.SetWriteSymbolMap(true);
        session.SetSymbolMapPath(Path.Combine("..", "a", "App.dll"));

        Collision(session).ShouldNotBeNull().InputPath.ShouldBe(input);
    }

    [Fact]
    public void SymbolMap_ARelativePathWithNoFolder_ResolvesBesideTheFirstInput()
    {
        var first = Make(Path.Combine("a", "App.dll"));
        var other = Make(Path.Combine("a", "Lib.dll"));
        var session = With(first);
        session.SetWriteSymbolMap(true);
        session.SetSymbolMapPath("Lib.dll");
        Collision(session).ShouldBeNull();

        session.AddPaths([other]);
        session.SetIncluded(session.GetSnapshot().Files.Single(f => f.Path == other).Id, included: false);

        Collision(session).ShouldNotBeNull().InputPath.ShouldBe(other);
    }

    [Fact]
    public void SymbolMap_PathIsAPlannedOutput_Collides()
    {
        var input = Make("App.dll");
        var session = WithSymbolMap(Path.Combine(_temp, "out", "App.dll"), input);
        session.SetOutputFolder(Path.Combine(_temp, "out"));

        var collision = Collision(session).ShouldNotBeNull();

        collision.InputPath.ShouldBe(input);
        collision.OutputPath.ShouldBe(Path.Combine(_temp, "out", "App.dll"));
        collision.Message.ShouldBe(MapOverOutput("App.dll"));
    }

    [Fact]
    public void SymbolMap_PathIsTheMergedAssembly_Collides()
    {
        var session = WithSymbolMap(Path.Combine(_temp, "a.obfuscated.dll"), Make("a.dll"), Make("b.dll"));

        Collision(session, merge: true).ShouldNotBeNull().Message.ShouldBe(MapOverOutput("a.obfuscated.dll"));
        // Not a merge run: a.obfuscated.dll is not something the run writes.
        Collision(session, merge: false).ShouldBeNull();
    }

    [Fact]
    public void SymbolMap_PathIsAClosedSetOutput_Collides()
    {
        var session = WithSymbolMap(Path.Combine(_temp, "out", "b.dll"), Make("a.dll"), Make("b.dll"));
        session.SetOutputFolder(Path.Combine(_temp, "out"));

        Collision(session).ShouldNotBeNull().Message.ShouldBe(MapOverOutput("b.dll"));
    }

    [Fact]
    public void SymbolMap_AnOutputThatReplacesAnInputIsReportedFirst()
    {
        // Both are wrong; the guard that was there first speaks first, so its message and tests stay what they were.
        var input = Make("App.dll");
        var session = WithSymbolMap(input, input);
        session.SetOutputFolder(_temp);

        Collision(session).ShouldNotBeNull().Message.ShouldStartWith("The output folder is the folder of App.dll;");
    }

    [Fact]
    public void SymbolMap_DefaultOrAnotherFile_DoesNotCollide()
    {
        var input = Make("App.dll");
        var session = With(input);
        session.SetWriteSymbolMap(true);
        Collision(session).ShouldBeNull();

        session.SetSymbolMapPath(Path.Combine(_temp, "maps", "app.map.json"));
        Collision(session).ShouldBeNull();

        // Beside the input, under a name no input has and no run writes.
        session.SetSymbolMapPath(Path.Combine(_temp, "App.dll.map"));
        Collision(session).ShouldBeNull();
    }

    [Fact]
    public void SymbolMap_OptionOff_AMapPathOnAListedFileDoesNotCollide()
    {
        var input = Make("App.dll");
        var session = WithSymbolMap(input, input);
        Collision(session).ShouldNotBeNull();

        session.SetWriteSymbolMap(false);

        session.CaptureRun(merge: false).SymbolMapPath.ShouldBeNull();
        Collision(session).ShouldBeNull();
    }

    [JunctionFact]
    public void SymbolMap_PathReachedThroughAJunction_IsTheListedFileInTheRealFolder()
    {
        var input = Make(Path.Combine("real", "App.dll"));
        var link = Path.Combine(_temp, "link");
        Junctions.TryCreate(link, Path.GetDirectoryName(input)!, out var error).ShouldBeTrue(error);

        var session = WithSymbolMap(Path.Combine(link, "App.dll"), input);

        Collision(session).ShouldNotBeNull().Message.ShouldBe(MapOverInput("App.dll"));
    }

    // ---- Links ----

    [JunctionFact]
    public void AJunctionToTheInputFolder_IsTheInputFolder()
    {
        var input = Make(Path.Combine("real", "App.dll"));
        var link = Path.Combine(_temp, "link");
        Junctions.TryCreate(link, Path.GetDirectoryName(input)!, out var error).ShouldBeTrue(error);
        var session = With(input);

        session.SetOutputFolder(link);

        var collision = Collision(session).ShouldNotBeNull();
        collision.Message.ShouldBe(SameFolderMessage(
            "App.dll", "Pick another folder, or clear it to write App.obfuscated.dll beside it."));
    }

    [JunctionFact]
    public void AnInputReachedThroughAJunction_IsTheFileInTheRealFolder()
    {
        var real = Path.GetDirectoryName(Make(Path.Combine("real", "App.dll")))!;
        var link = Path.Combine(_temp, "link");
        Junctions.TryCreate(link, real, out var error).ShouldBeTrue(error);
        var session = With(Path.Combine(link, "App.dll"));

        session.SetOutputFolder(real);

        Collision(session).ShouldNotBeNull();
    }

    [JunctionFact]
    public void AJunctionAboveTheInputFolder_IsFollowedToo()
    {
        var input = Make(Path.Combine("real", "deep", "App.dll"));
        var link = Path.Combine(_temp, "link");
        Junctions.TryCreate(link, Path.Combine(_temp, "real"), out var error).ShouldBeTrue(error);
        var session = With(input);

        // link\deep is a folder inside the junction's target, so only the folder above it is a link.
        session.SetOutputFolder(Path.Combine(link, "deep"));

        Collision(session).ShouldNotBeNull();
    }

    [JunctionFact]
    public void AJunctionToAnotherFolder_DoesNotCollide()
    {
        var input = Make(Path.Combine("real", "App.dll"));
        var other = Folder("other");
        var link = Path.Combine(_temp, "link");
        Junctions.TryCreate(link, other, out var error).ShouldBeTrue(error);
        var session = With(input);

        session.SetOutputFolder(link);

        Collision(session).ShouldBeNull();
    }

    [SymbolicLinkFact]
    public void AnInputThatIsASymbolicLink_IsTheFileItPointsTo()
    {
        // The row is links\Lib.dll, a link to real\Lib.dll; an output folder of real writes real\Lib.dll.
        var real = Make(Path.Combine("real", "Lib.dll"));
        var link = Path.Combine(_temp, "links", "Lib.dll");
        SymbolicLinks.TryCreate(link, real, out var error).ShouldBeTrue(error);
        var session = With(link);

        session.SetOutputFolder(Path.GetDirectoryName(real)!);

        Collision(session).ShouldNotBeNull().Message.ShouldStartWith("The output folder holds Lib.dll, one of your inputs;");
    }

    [SymbolicLinkFact]
    public void AnExistingOutputThatIsASymbolicLinkToTheInput_Collides()
    {
        // Writing out\App.dll would write through the link into the original.
        var input = Make(Path.Combine("real", "App.dll"));
        SymbolicLinks.TryCreate(Path.Combine(_temp, "out", "App.dll"), input, out var error).ShouldBeTrue(error);
        var session = With(input);

        session.SetOutputFolder(Path.Combine(_temp, "out"));

        Collision(session).ShouldNotBeNull().InputPath.ShouldBe(input);
    }

    [JunctionFact]
    public void AJunctionBetweenTheOutputFolderAndTheInput_CollidesThroughAClosedSet()
    {
        var one = Make(Path.Combine("real", "one.dll"));
        var two = Make(Path.Combine("real", "two.dll"));
        var link = Path.Combine(_temp, "link");
        Junctions.TryCreate(link, Path.Combine(_temp, "real"), out var error).ShouldBeTrue(error);
        var session = With(one, two);

        session.SetOutputFolder(link);

        Collision(session).ShouldNotBeNull();
    }
}

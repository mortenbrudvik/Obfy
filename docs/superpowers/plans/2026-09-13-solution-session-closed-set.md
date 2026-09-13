# Solution Session + Closed-Set Pipeline Implementation Plan

> **As-built (review follow-up):** Implemented. Skip taxonomy is `ProjectSkipReason` (not `SkipReason`). Load failures live on `ClosedSetResult.LoadFailures`. UI session identity is `AssemblyFile.FromSession`. CLI closed-set is session-only (`obfy App.dll Lib.dll` stays per-file). Checkboxes below were the original task list, not remaining work.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Accept a `.sln` / `.slnx` / `.csproj` in the CLI and WPF app, analyze the ship set on disk, skip tests and missing outputs, and obfuscate included assemblies as one closed application (in-solution public APIs renamed together).

**Architecture:** A Core `SolutionAnalyzer` builds a `ProtectionSession` from project XML and `bin` layout (no MSBuild host). A `ClosedSetProcessor` loads included modules on one dnlib context, runs symbol renaming once across the set (updating sibling `TypeRef`/`MemberRef`), then runs the rest of the pipeline per module and commits output only if every included module succeeds.

**Tech Stack:** C# / .NET 10, dnlib, Autofac, System.CommandLine, WPF, xUnit, Shouldly.

**Spec:** `docs/superpowers/specs/2026-09-13-solution-session-closed-set-design.md`

## Global Constraints

- No MSBuild evaluation, restore, or `dotnet build` inside Obfy.
- Do not read `Directory.Build.props` or expand arbitrary `$(Property)`.
- Skip test-like projects: `IsTestProject`, test SDK/package, or project file name (no extension) that equals `Test`, ends with `Tests` / `.Tests` / `.Test` / `.Testing`, or contains `.Tests.`. Never use a bare `*Test` suffix (`Contest.csproj` is not a test).
- Prefer `bin/Release` over `bin/Debug` when both exist.
- Closed-set public API: exe+lib in the included set → lib public names **are** renamed; libraries-only session → preserve public API; skipped/missing libs are outside the set.
- Pipeline failure for an included module commits **no** output.
- Single-file `obfy App.dll` path is unchanged.
- v1 surfaces: CLI + WPF only (not VS/Rider, not MSBuild `.targets` injection).
- User `--level` / UI level selects techniques; analyzer hints never turn on Aggressive extras.
- If `-c` JSON sets `runtimeProfile` to a non-`Default` value, do not overwrite it.
- `--preserve-public` forces library-mode on all included libraries.

## File map

**Create**

- `Src/Obfy.Core/Models/Solution/SkipReason.cs`
- `Src/Obfy.Core/Models/Solution/ProjectSettingsHints.cs`
- `Src/Obfy.Core/Models/Solution/ProjectProtectionEntry.cs`
- `Src/Obfy.Core/Models/Solution/ProtectionSession.cs`
- `Src/Obfy.Core/Models/Solution/ClosedSetInput.cs`
- `Src/Obfy.Core/Models/Solution/ClosedSetResult.cs`
- `Src/Obfy.Core/Services/Solution/TestProjectClassifier.cs`
- `Src/Obfy.Core/Services/Solution/SolutionFileParser.cs`
- `Src/Obfy.Core/Services/Solution/ProjectFileReader.cs`
- `Src/Obfy.Core/Services/Solution/AssemblyOutputLocator.cs`
- `Src/Obfy.Core/Services/Solution/SolutionHintApplier.cs`
- `Src/Obfy.Core/Services/Solution/ISolutionAnalyzer.cs`
- `Src/Obfy.Core/Services/Solution/SolutionAnalyzer.cs`
- `Src/Obfy.Core/Services/Solution/IClosedSetProcessor.cs`
- `Src/Obfy.Core/Services/Solution/ClosedSetProcessor.cs`
- `Src/Obfy.Core/Utilities/PlatformExclusions.cs`
- `Tests/Obfy.Tests/Solution/TestProjectClassifierTests.cs`
- `Tests/Obfy.Tests/Solution/SolutionFileParserTests.cs`
- `Tests/Obfy.Tests/Solution/ProjectFileReaderTests.cs`
- `Tests/Obfy.Tests/Solution/AssemblyOutputLocatorTests.cs`
- `Tests/Obfy.Tests/Solution/SolutionAnalyzerTests.cs`
- `Tests/Obfy.Tests/Solution/ClosedSetProcessorTests.cs`
- `Tests/Obfy.Console.Tests/SolutionInputTests.cs`

**Modify**

- `Src/Obfy.Core/Obfuscators/Assembly/SymbolRenamingObfuscator.cs` — add `RenameClosedSet`
- `Src/Obfy.Core/Services/IObfuscationService.cs` / `ObfuscationService.cs` — `ObfuscateClosedSetAsync`
- `Src/Obfy.Core/DependencyInjection/ObfuscationModule.cs` — register analyzer + processor; `SymbolRenamingObfuscator` as self
- `Src/Obfy.Console/Program.cs` — accept solution/project inputs, print plan, exit 0/1/2
- `Src/Obfy.UI/Models/AssemblyFile.cs` — skip status, hints, skip reason
- `Src/Obfy.UI/ViewModels/FilesViewModel.cs` — expand solutions on drop/add
- `Src/Obfy.UI/ViewModels/MainViewModel.cs` — closed-set run
- `Src/Obfy.UI/Converters/FileStatusToIconConverter.cs`
- `Src/Obfy.UI/Services/FileDialogService.cs` — filters
- `Src/Obfy.UI/Views/Controls/FilesPanel.xaml` — copy + skip reason
- `Tests/Obfy.UI.Tests/ViewModels/FilesViewModelTests.cs`
- `Tests/Obfy.UI.Tests/Converters/ConverterTests.cs`
- `docs/CLI.md`, `README.md` (CLI examples), `Claude.md` CLI usage line

---

### Task 1: Session models and test-project classifier

**Files:**
- Create: `Src/Obfy.Core/Models/Solution/SkipReason.cs`
- Create: `Src/Obfy.Core/Models/Solution/ProjectSettingsHints.cs`
- Create: `Src/Obfy.Core/Models/Solution/ProjectProtectionEntry.cs`
- Create: `Src/Obfy.Core/Models/Solution/ProtectionSession.cs`
- Create: `Src/Obfy.Core/Services/Solution/TestProjectClassifier.cs`
- Test: `Tests/Obfy.Tests/Solution/TestProjectClassifierTests.cs`

**Interfaces:**
- Consumes: `Obfy.Core.Models.RuntimeProfile`
- Produces:
  - `enum SkipReason { None, SkipTest, SkipMissing, SkipMissingProject, SkipLoadFailed, SkipUnsupported }`
  - `class ProjectSettingsHints { bool PreserveXaml; bool PreservePublicApi; RuntimeProfile RuntimeProfile; bool AddUnityExcludes; bool AddAspNetMvcExcludes; }` (init-only, defaults: all false, `RuntimeProfile.Default`)
  - `class ProjectProtectionEntry { required string ProjectPath; required string ProjectName; string? OutputPath; SkipReason SkipReason; string? SkipMessage; ProjectSettingsHints Hints; bool IsIncluded { get; } }`
  - `IsIncluded` is `SkipReason == None && OutputPath is not null`
  - `class ProtectionSession { required string SourcePath; required IReadOnlyList<ProjectProtectionEntry> Entries; IEnumerable<ProjectProtectionEntry> Included { get; } }`
  - `static class TestProjectClassifier { static bool IsTestProjectName(string projectFileNameWithoutExtension); }`

- [ ] **Step 1: Write the failing tests**

```csharp
using Obfy.Core.Services.Solution;
using Shouldly;

namespace Obfy.Tests.Solution;

public class TestProjectClassifierTests
{
    [Theory]
    [InlineData("Obfy.Tests", true)]
    [InlineData("Obfy.Console.Tests", true)]
    [InlineData("MyApp.Test", true)]
    [InlineData("MyApp.Testing", true)]
    [InlineData("Test", true)]
    [InlineData("Contest", false)]
    [InlineData("MyApp", false)]
    [InlineData("Latest", false)]
    public void IsTestProjectName_MatchesSpec(string name, bool expected)
        => TestProjectClassifier.IsTestProjectName(name).ShouldBe(expected);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~TestProjectClassifierTests`

Expected: FAIL (type not found)

- [ ] **Step 3: Write models + classifier**

`IsTestProjectName`: ordinal-ignore-case; true if name equals `Test`, ends with `Tests` / `.Tests` / `.Test` / `.Testing`, or contains `.Tests.`.

`ProjectProtectionEntry.IsIncluded` as specified. `ProtectionSession.Included` filters `Entries` with `IsIncluded`.

- [ ] **Step 4: Run tests**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~TestProjectClassifierTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Models/Solution Src/Obfy.Core/Services/Solution/TestProjectClassifier.cs Tests/Obfy.Tests/Solution/TestProjectClassifierTests.cs
git commit -m "feat(core): add protection session models and test-project name classifier"
```

---

### Task 2: Solution file parser

**Files:**
- Create: `Src/Obfy.Core/Services/Solution/SolutionFileParser.cs`
- Test: `Tests/Obfy.Tests/Solution/SolutionFileParserTests.cs`

**Interfaces:**
- Consumes: none
- Produces:
  - `readonly record struct SolutionProjectRef(string Name, string RelativePath);`
  - `static class SolutionFileParser { static IReadOnlyList<SolutionProjectRef> Parse(string solutionPath); }`
  - `.sln`: lines matching `Project("{...}") = "Name", "path", "{...}"` (quoted paths; unescape `""`). Resolve nothing — return relative path as in the file.
  - `.slnx`: XML `//Project[@Path or @path]` (case-insensitive attribute). Name = file name without extension.
  - Unknown extension: throw `ArgumentException`.
  - Missing file: throw `FileNotFoundException`.

- [ ] **Step 1: Write the failing tests**

Create temp `.sln` text:

```
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\\App\\App.csproj", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}"
EndProject
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App.Tests", "tests\\App.Tests\\App.Tests.csproj", "{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}"
EndProject
```

Assert three refs (including the solution folder — analyzer will skip by extension later) with relative paths `src\App\App.csproj` and `tests\App.Tests\App.Tests.csproj`.

Temp `.slnx`:

```xml
<Solution>
  <Project Path="src/App/App.csproj" />
  <Project Path="tests/App.Tests/App.Tests.csproj" />
</Solution>
```

Assert two refs.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~SolutionFileParserTests`

Expected: FAIL

- [ ] **Step 3: Implement parser**

Use `File.ReadAllLines` for `.sln`. Regex:

`^Project\("[^"]+"\)\s*=\s*"([^"]+)"\s*,\s*"([^"]+)"`

For `.slnx` use `XDocument.Load`. Ignore empty Path.

- [ ] **Step 4: Run tests** — expected PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Services/Solution/SolutionFileParser.cs Tests/Obfy.Tests/Solution/SolutionFileParserTests.cs
git commit -m "feat(core): parse .sln and .slnx project lists"
```

---

### Task 3: Project XML reader and output locator

**Files:**
- Create: `Src/Obfy.Core/Services/Solution/ProjectFileReader.cs`
- Create: `Src/Obfy.Core/Services/Solution/AssemblyOutputLocator.cs`
- Test: `Tests/Obfy.Tests/Solution/ProjectFileReaderTests.cs`
- Test: `Tests/Obfy.Tests/Solution/AssemblyOutputLocatorTests.cs`

**Interfaces:**
- Consumes: `TestProjectClassifier`
- Produces:
  - `sealed class ProjectFileInfo` with: `string Path`, `string AssemblyName`, `string OutputType` (`Library`/`Exe`/`WinExe`, default `Library` if missing), `IReadOnlyList<string> TargetFrameworks`, `bool UseWpf`, `bool UseWinForms`, `bool UseMaui`, `bool PublishAot`, `bool IsBlazorWasm`, `bool IsAspNetWeb`, `bool IsTest`, `bool ReferencesUnity`, `IReadOnlyList<string> ProjectReferences` (raw `Include` values)
  - `static class ProjectFileReader { static ProjectFileInfo Read(string projectPath); }`
  - `static class AssemblyOutputLocator { static IReadOnlyList<string> FindAll(string projectDirectory, string assemblyName, IReadOnlyList<string> targetFrameworks); }`

Search order per TFM: `bin/Release/{tfm}/`, `bin/Debug/{tfm}/`. Also search `bin/Release/` and `bin/Debug/` with no TFM segment (old-style). File names `{assemblyName}.dll` then `{assemblyName}.exe`. Do not return duplicates.

**ProjectFileReader XML (no MSBuild):**

- Root `Sdk` attribute contains `BlazorWebAssembly` → `IsBlazorWasm`
- Root `Sdk` contains `Web` (and not Blazor WASM) → `IsAspNetWeb`
- PropertyGroup: `OutputType`, `AssemblyName` (fallback `Path.GetFileNameWithoutExtension`), `TargetFramework`, `TargetFrameworks` (split `;`), `UseWPF`/`UseWinForms`/`UseMaui` true if value is `true` ignore-case, `PublishAot`/`IsAotCompatible`, `IsTestProject`
- Any `PackageReference` Include containing `xunit`, `nunit`, `MSTest` (ignore-case) or `Microsoft.NET.Test.Sdk` or `MSTest.Sdk` → `IsTest`
- `IsTest` also if `TestProjectClassifier.IsTestProjectName`
- `ProjectReference` Include values as-is
- `ReferencesUnity` if any `Reference`/`PackageReference`/`HintPath` value contains `UnityEngine`

- [ ] **Step 1: Write failing tests**

`ProjectFileReaderTests`: write a temp SDK-style WPF csproj with `UseWPF`, `net8.0-windows`, `OutputType=WinExe`; assert flags. Write a test csproj with `Microsoft.NET.Test.Sdk` and name `App`; assert `IsTest`. Write `Contest.csproj` without test packages; assert not test.

`AssemblyOutputLocatorTests`: create `bin/Debug/net8.0/App.dll` and `bin/Release/net8.0/App.dll`; `FindAll` returns only the Release path. Create only `bin/Release/App.exe` (no TFM); returned. Empty tree returns empty list.

- [ ] **Step 2: Run tests** — expected FAIL

- [ ] **Step 3: Implement reader + locator** using `XDocument`. Treat unprefixed and MSBuild default namespace. Property values: last PropertyGroup wins (scan in document order).

- [ ] **Step 4: Run tests** — expected PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Services/Solution/ProjectFileReader.cs Src/Obfy.Core/Services/Solution/AssemblyOutputLocator.cs Tests/Obfy.Tests/Solution/ProjectFileReaderTests.cs Tests/Obfy.Tests/Solution/AssemblyOutputLocatorTests.cs
git commit -m "feat(core): read csproj hints and locate bin outputs"
```

---

### Task 4: SolutionAnalyzer and hint applier

**Files:**
- Create: `Src/Obfy.Core/Utilities/PlatformExclusions.cs`
- Create: `Src/Obfy.Core/Services/Solution/SolutionHintApplier.cs`
- Create: `Src/Obfy.Core/Services/Solution/ISolutionAnalyzer.cs`
- Create: `Src/Obfy.Core/Services/Solution/SolutionAnalyzer.cs`
- Modify: `Src/Obfy.Core/DependencyInjection/ObfuscationModule.cs`
- Test: `Tests/Obfy.Tests/Solution/SolutionAnalyzerTests.cs`

**Interfaces:**
- Consumes: `SolutionFileParser`, `ProjectFileReader`, `AssemblyOutputLocator`, `TestProjectClassifier`, session models
- Produces:
  - `static class PlatformExclusions` with `UnityNamespaces` = `UnityEngine`, `UnityEngine.*`, `Unity`, `Unity.*` and `AspNetMvcAttributes` matching `ConfigurationWizard.ApplyUseCaseDefaults` (Route/ApiController/HttpGet/Post/Put/Delete)
  - `static class SolutionHintApplier { static void Apply(ObfySettings settings, ProjectSettingsHints hints, bool forcePreservePublic); }`
    - Sets `PreserveXaml`, `RuntimeProfile` only if `settings.RuntimeProfile == Default`, Unity namespace excludes, ASP.NET attribute excludes, `PreservePublicApi` from hints unless `forcePreservePublic` (then true)
  - `interface ISolutionAnalyzer { ProtectionSession Analyze(string path); }`
  - `class SolutionAnalyzer : ISolutionAnalyzer`

**Analyzer algorithm**

1. If path is `.sln`/`.slnx`, parse projects; else if `.csproj`/`.vbproj`/`.fsproj`, one synthetic ref. Else throw `ArgumentException`.
2. Resolve relative paths from solution/project directory.
3. Missing file → entry `SkipMissingProject`.
4. Extension not cs/vb/fs → `SkipUnsupported`.
5. `ProjectFileReader.Read`. If `IsTest` → `SkipTest` (message `"Test project"`).
6. `FindAll` outputs. None → `SkipMissing` (`"No built output in bin/Release or bin/Debug"`).
7. One entry per output path (multi-TFM = multiple entries sharing project path; `ProjectName` includes TFM if more than one output, e.g. `App (net8.0)`).
8. Build include graph from `ProjectReference` resolved against other entries’ `ProjectPath`.
9. `hasIncludedExe` = any included entry whose `OutputType` is `Exe` or `WinExe`.
10. Hints:
    - `UseWpf`/`UseWinForms`/`UseMaui` → `PreserveXaml`
    - `PublishAot` → `RuntimeProfile.NativeAot`
    - `IsBlazorWasm` or any TFM contains `-browser` → `BlazorWasm`
    - `ReferencesUnity` → `UnityIl2Cpp` + `AddUnityExcludes`
    - `IsAspNetWeb` && !IsBlazorWasm → `AddAspNetMvcExcludes`
    - `PreservePublicApi`: if `OutputType` is Library and (`!hasIncludedExe` **or** no included exe `ProjectReference`s this project) → true; if an included exe references it → false; WinExe/Exe → false

- [ ] **Step 1: Write failing tests** in `SolutionAnalyzerTests` using a temp directory:

**Fixture A — app + lib + tests**

- `App.sln` with App, Lib, App.Tests
- App.csproj: `OutputType=Exe`, `ProjectReference` to Lib, `TargetFramework=net8.0`
- Lib.csproj: Library net8.0
- App.Tests.csproj: `IsTestProject=true` + output dll present
- Create `App/bin/Release/net8.0/App.dll`, `Lib/bin/Release/net8.0/Lib.dll`, `App.Tests/bin/Release/net8.0/App.Tests.dll`

Assert: App and Lib included; Tests `SkipTest`; Lib `Hints.PreservePublicApi == false`; App `PreservePublicApi == false`.

**Fixture B — libraries only:** two class libs with outputs, no exe. Both `PreservePublicApi == true`.

**Fixture C — WPF + AOT:** `UseWPF=true`, `PublishAot=true` → `PreserveXaml` and `NativeAot`.

**Fixture D — missing bin:** project exists, no bin → `SkipMissing`.

**Fixture E — `Contest.csproj` library with output:** included, not SkipTest.

`SolutionHintApplierTests` (same file or adjacent): `RuntimeProfile` already `BlazorWasm` is not overwritten by a NativeAot hint.

- [ ] **Step 2: Run tests** — expected FAIL

- [ ] **Step 3: Implement analyzer + applier + DI**

```csharp
builder.RegisterType<SolutionAnalyzer>().As<ISolutionAnalyzer>().SingleInstance();
```

- [ ] **Step 4: Run tests** — expected PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Utilities/PlatformExclusions.cs Src/Obfy.Core/Services/Solution Src/Obfy.Core/DependencyInjection/ObfuscationModule.cs Tests/Obfy.Tests/Solution/SolutionAnalyzerTests.cs
git commit -m "feat(core): analyze solutions into a protection session with platform hints"
```

---

### Task 5: Closed-set symbol renaming

**Files:**
- Modify: `Src/Obfy.Core/Obfuscators/Assembly/SymbolRenamingObfuscator.cs`
- Modify: `Src/Obfy.Core/DependencyInjection/ObfuscationModule.cs` — `.AsSelf().As<IObfuscator>()`
- Test: `Tests/Obfy.Tests/Solution/ClosedSetProcessorTests.cs` (rename-only facts first; processor comes in Task 6 in the same file)

**Interfaces:**
- Consumes: existing `ShouldSkipType` / `CanRename*` on `SymbolRenamingObfuscator`
- Produces:

```csharp
public void RenameClosedSet(
    IReadOnlyList<(ModuleDef Module, ObfySettings Settings)> modules,
    PipelineContext sharedContext,
    CancellationToken cancellationToken = default);
```

Behavior:

1. One `_nameGenerator.Reset()`.
2. For each module, collect type/method/field/property/event/namespace renames using **that module’s** `Settings.SymbolRenaming` (so `PreservePublicApi` and `PreserveXaml` differ per module). Share namespace maps across modules: if namespace `Lib.Api` is renamed in module A, module B uses the same new name (dictionary keyed by original namespace string).
3. Apply def renames (same as current apply loops).
4. For every module, rewrite metadata that resolves into the set:
   - Each `TypeRef`: `ResolveTypeDef()`; if resolved and name/namespace differ, copy from def.
   - Each `MemberRef`: resolve method/field; if resolved, copy `Name`.
   - `ExportedType` names if present.
5. Merge stats into `sharedContext.Statistics` and names into `sharedContext.SymbolMap`.

Do not run other obfuscators in this task.

- [ ] **Step 1: Write failing tests** — helper to emit two assemblies with Roslyn (copy the pattern in `EndToEndObfuscationTests`):

Lib (`OutputKind.DynamicallyLinkedLibrary`):

```csharp
namespace Lib.Api;
public class Greeter
{
    public static string Hello() => "hi";
}
```

App (`OutputKind.ConsoleApplication`) with metadata reference to Lib:

```csharp
using Lib.Api;
public static class Program
{
    public static string Run() => Greeter.Hello();
}
```

Load both with **one** `ModuleDef.CreateModuleContext()`, `AssemblyResolver.AddToCache` for both modules.

Call `RenameClosedSet` with Lib settings `PreservePublicApi = false`, App same, `NamingMode.Sequential`, rename types+methods enabled.

Assert:

- Lib type formerly `Greeter` no longer named `Greeter`
- App `TypeRef` to that type uses the new name
- Invoke via `AssemblyLoadContext` loading both output bytes (write to temp files after rename using `Module.Write`) — `Program.Run()` still returns `"hi"`

Second fact: both modules `PreservePublicApi = true` → `Greeter` still `Greeter`.

- [ ] **Step 2: Run tests** — expected FAIL

- [ ] **Step 3: Implement `RenameClosedSet` + reference rewrite.** Keep single-module `ObfuscateAsync` unchanged (it already works). Register `AsSelf()`.

- [ ] **Step 4: Run tests** — expected PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Obfuscators/Assembly/SymbolRenamingObfuscator.cs Src/Obfy.Core/DependencyInjection/ObfuscationModule.cs Tests/Obfy.Tests/Solution/ClosedSetProcessorTests.cs
git commit -m "feat(core): rename symbols across a closed set of assemblies"
```

---

### Task 6: ClosedSetProcessor (load, pipeline, all-or-nothing write)

**Files:**
- Create: `Src/Obfy.Core/Models/Solution/ClosedSetInput.cs`
- Create: `Src/Obfy.Core/Models/Solution/ClosedSetResult.cs`
- Create: `Src/Obfy.Core/Services/Solution/IClosedSetProcessor.cs`
- Create: `Src/Obfy.Core/Services/Solution/ClosedSetProcessor.cs`
- Modify: `ObfuscationModule.cs` register processor
- Test: extend `Tests/Obfy.Tests/Solution/ClosedSetProcessorTests.cs`

**Interfaces:**
- Consumes: `IAssemblyProcessor` save path, `IObfuscationPipeline`, `SymbolRenamingObfuscator`, `SolutionHintApplier`, `RuntimeProfileGating`
- Produces:

```csharp
public sealed class ClosedSetInput
{
    public required string AssemblyPath { get; init; }
    public required ProjectSettingsHints Hints { get; init; }
}

public sealed class ClosedSetResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<ObfuscationResult> ModuleResults { get; init; } = [];
    public IReadOnlyList<string> LoadFailures { get; init; } = [];
    public Dictionary<string, string> SymbolMap { get; init; } = new();
}

public interface IClosedSetProcessor
{
    Task<ClosedSetResult> ExecuteAsync(
        IReadOnlyList<ClosedSetInput> inputs,
        string outputDirectory,
        ObfySettings baseSettings,
        bool forcePreservePublic = false,
        CancellationToken cancellationToken = default);
}
```

**Algorithm**

1. Create one `ModuleContext`. Read each input as bytes (`File.ReadAllBytesAsync`), `ModuleDefMD.Load(bytes, ctx)`, `AssemblyResolver.AddToCache`. On load exception, record `LoadFailures` with the path and omit from the set (do not fail the whole run unless nothing remains).
2. Recompute library-mode from **loaded** `AssemblyRef`s: if any remaining module has an entry point (`module.EntryPoint != null` or `OutputType` exe) and an `AssemblyRef` whose `Name` matches another remaining module’s assembly name, that referenced module’s effective `PreservePublicApi` is false unless `forcePreservePublic`. If no remaining module has an entry point, all libraries keep `PreservePublicApi` true.
3. Clone `baseSettings` per module, `SolutionHintApplier.Apply`, then apply the recomputed public-API flag.
4. `RuntimeProfileGating.Apply` per module context.
5. `RenameClosedSet` once on remaining modules.
6. For each module: `PipelineContext.ForAssembly`, `Settings` clone with `SymbolRenaming.Enabled = false` (already applied), `InputPath`/`OutputPath` assigned. Run `_pipeline.ExecuteAsync`. On failure: **delete any temp dir, write nothing to `outputDirectory`, return Success=false** with the obfuscator error.
7. Output path: `Path.Combine(outputDirectory, Path.GetFileName(input))`. If two inputs share the file name, use the parent folder name of the source (TFM segment when present: if path contains `netX.Y`, `Path.Combine(outputDirectory, tfm, fileName)`).
8. Write all modules to a new temp directory first (`IAssemblyProcessor.SaveAsync` or `module.Write` + existing post-processors — inject `IAssemblyProcessor` and set `context.OutputPath` to temp). If all writes succeed, move files into `outputDirectory` (create it). If any write fails, delete temp and return failure (output dir unchanged).
9. Cancel: delete temp, throw `OperationCanceledException`.

- [ ] **Step 1: Write failing tests**

- Happy path: App+Lib from Task 5, `ExecuteAsync` to an output dir, App `Run()` still `"hi"`, Lib public type renamed, both files exist in output.
- Failure: pass a corrupted second file (0-byte dll) after a valid Lib — load failure listed, Lib still processed if it remains a libraries-only set (preserve public). Then a separate test: two valid modules, mock pipeline that fails on the second — **output dir empty** (all-or-nothing). Easiest: implement by targeting `ClosedSetProcessor` with a real pipeline and an input that is a valid DLL but set `baseSettings` that validate-fail… Better: add an internal hook only if needed. Prefer: second assembly path is a text file renamed to `.dll` so load fails; first still succeeds. **All-or-nothing test:** use two valid assemblies and a custom `IObfuscationPipeline` test double registered via constructing `ClosedSetProcessor` directly:

```csharp
var failingPipeline = new Mock<IObfuscationPipeline>();
failingPipeline.Setup(p => p.ExecuteAsync(It.IsAny<PipelineContext>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(ObfuscationResult.Failed("boom"));
```

Assert `outputDir` has no dll/exe after `ExecuteAsync`.

- [ ] **Step 2: Run tests** — expected FAIL

- [ ] **Step 3: Implement processor + DI**

```csharp
builder.RegisterType<ClosedSetProcessor>().As<IClosedSetProcessor>().SingleInstance();
```

Constructor: `ILogger<ClosedSetProcessor>`, `IObfuscationPipeline`, `IAssemblyProcessor`, `SymbolRenamingObfuscator`.

- [ ] **Step 4: Run tests** — expected PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Models/Solution Src/Obfy.Core/Services/Solution/ClosedSetProcessor.cs Src/Obfy.Core/Services/Solution/IClosedSetProcessor.cs Src/Obfy.Core/DependencyInjection/ObfuscationModule.cs Tests/Obfy.Tests/Solution/ClosedSetProcessorTests.cs
git commit -m "feat(core): closed-set processor with all-or-nothing output"
```

---

### Task 7: ObfuscationService closed-set entry point

**Files:**
- Modify: `Src/Obfy.Core/Services/IObfuscationService.cs`
- Modify: `Src/Obfy.Core/Services/ObfuscationService.cs`
- Test: `Tests/Obfy.Tests/ServiceTests.cs` (or `Tests/Obfy.Tests/Solution/ObfuscationServiceClosedSetTests.cs`)

**Interfaces:**
- Consumes: `ISolutionAnalyzer`, `IClosedSetProcessor`
- Produces:

```csharp
Task<ClosedSetResult> ObfuscateClosedSetAsync(
    IReadOnlyList<ClosedSetInput> inputs,
    string outputDirectory,
    ObfySettings settings,
    bool forcePreservePublic = false,
    CancellationToken cancellationToken = default);
```

Implementation: delegate to `IClosedSetProcessor`. If `settings.Merge` would be used, **do not** change `MergeAndObfuscateAsync`; CLI/UI choose merge vs closed-set (Task 8/10). Single-file `ObfuscateAsync` unchanged. `ObfuscateBatchAsync` unchanged (callers that want closed-set use the new method).

- [ ] **Step 1: Write a failing test** that resolves `IObfuscationService` from a test Autofac container (`ObfuscationModule` + logging) and calls `ObfuscateClosedSetAsync` with two compiled assemblies; assert `Success` and two output files.

- [ ] **Step 2: Run test** — expected FAIL (method missing)

- [ ] **Step 3: Add method, inject `IClosedSetProcessor` into `ObfuscationService`** (optional parameter with null check throwing if closed-set called without it — or required ctor param; Autofac will supply it). Prefer required ctor param; update any `new ObfuscationService(` tests to pass a mock processor.

Search tests for `new ObfuscationService` and fix compiles.

- [ ] **Step 4: Run `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj`** — expected PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Services Tests/Obfy.Tests
git commit -m "feat(core): expose closed-set obfuscation on IObfuscationService"
```

---

### Task 8: CLI

**Files:**
- Modify: `Src/Obfy.Console/Program.cs`
- Test: `Tests/Obfy.Console.Tests/SolutionInputTests.cs`
- Modify: `docs/CLI.md`, `README.md`, `Claude.md` (one example line each)

**Interfaces:**
- Consumes: `ISolutionAnalyzer`, `IObfuscationService.ObfuscateClosedSetAsync`
- Produces: CLI behavior from the spec

**Program.cs changes**

- `InputArgument` description: `"Input files to obfuscate (DLL, EXE, .cs, .sln, .slnx, or project files)"`
- After resolving `FileInfo[] inputs`:
  1. Count solution files (`.sln`/`.slnx`). If count > 1: print error `"Only one solution file can be obfuscated per run."` return **1**.
  2. Collect project/solution paths vs assembly/source paths.
  3. If any solution/project:
     - `ISolutionAnalyzer.Analyze` each (one sln, or N projects as separate Analyze calls). Merge `Entries` into one list (synthetic `ProtectionSession` with `SourcePath` = first).
     - Append extra `.dll`/`.exe` as `ClosedSetInput` with default hints.
     - Ignore extra `.cs` with a warning (`"Source files are not part of a solution session and were skipped."`) — spec: session is assemblies only.
     - Print a table: Project, Status (Included / skip reason), Output, Library-mode, RuntimeProfile.
     - If `--dry-run`: return **0**.
     - If no included assemblies: print `"No built outputs found. Build the ship set (Release) and retry."` return **2**.
     - Require `--output` for sessions (multiple files). If `-o` missing, default to `{solutionDir}/obfy-out` and print that path.
     - `ObfuscateClosedSetAsync` with `forcePreservePublic: preservePublic flag`.
     - Success → 0; failure → 1.
  4. Else existing `RunStandardObfuscationAsync` / merge.

`--dry-run` today prints per-file “Would obfuscate” inside the standard loop — session path must run **before** that loop.

Extract `internal static bool IsSolutionOrProject(string path)` for tests.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Parse_SlnPath_IsAcceptedAsInput()
{
    var parseResult = Program.CreateRootCommand().Parse(@"C:\src\App.sln -o out");
    parseResult.Errors.ShouldBeEmpty();
    parseResult.GetRequiredValue(Program.InputArgument)[0].Name.ShouldBe("App.sln");
}

[Fact]
public void IsSolutionOrProject_RecognizesExtensions()
{
    Program.IsSolutionOrProject("a.sln").ShouldBeTrue();
    Program.IsSolutionOrProject("a.slnx").ShouldBeTrue();
    Program.IsSolutionOrProject("a.csproj").ShouldBeTrue();
    Program.IsSolutionOrProject("a.dll").ShouldBeFalse();
}
```

Add an invoke test with a temp sln of only a `Foo.Tests` project and `--dry-run`: exit **2** (all skipped). Use `CommandLineTestHelpers.Invoke` **only if** the root action is the real handler. `CreateRootCommand` currently sets a no-op then `Main` replaces it — **tests that need the real handler must call the same method `Main` uses**. Extract `internal static RootCommand CreateRootCommand()` already exists; **move the real `SetAction` into `CreateRootCommand`** (delete the no-op) so parse tests and invoke tests share one command. That is part of this task: one `SetAction` in `CreateRootCommand`. Fix any Console test that assumed no-op invoke returns 0 with fake files (`ErrorHandlingTests.Invoke_WithUnknownOption_TreatsAsFileArgument` expects 0 — it will now try to obfuscate `input.dll` and `--fake-option` as files and fail). **Update that test** to expect non-zero or parse-only. Read `ErrorHandlingTests` and `IntegrationParseTests` and adjust only what this change breaks.

- [ ] **Step 2: Run `dotnet test Tests/Obfy.Console.Tests/Obfy.Console.Tests.csproj --filter FullyQualifiedName~SolutionInputTests`** — expected FAIL

- [ ] **Step 3: Implement CLI + docs**

`docs/CLI.md` argument text and example:

```bash
obfy MyApp.sln -o out/
obfy MyApp.sln --dry-run
```

Document exit code 2.

- [ ] **Step 4: Run full Console tests** — expected PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Console/Program.cs Tests/Obfy.Console.Tests docs/CLI.md README.md Claude.md
git commit -m "feat(cli): obfuscate solutions and projects as a closed set"
```

---

### Task 9: UI file list — drop a solution

**Files:**
- Modify: `Src/Obfy.UI/Models/AssemblyFile.cs`
- Modify: `Src/Obfy.UI/ViewModels/FilesViewModel.cs`
- Modify: `Src/Obfy.UI/Converters/FileStatusToIconConverter.cs`
- Modify: `Src/Obfy.UI/Services/FileDialogService.cs`
- Modify: `Src/Obfy.UI/Views/Controls/FilesPanel.xaml`
- Test: `Tests/Obfy.UI.Tests/ViewModels/FilesViewModelTests.cs`
- Test: `Tests/Obfy.UI.Tests/Converters/ConverterTests.cs`

**Interfaces:**
- Consumes: `ISolutionAnalyzer` (inject into `FilesViewModel`; tests pass a stub)
- Produces: drop/add expands sessions into the list

**Model**

```csharp
public enum FileStatus { Pending, Processing, Success, Error, Skipped }

// AssemblyFile additions:
public string? SkipReason { get; set; }
public ProjectSettingsHints? Hints { get; set; }
public bool IsSkipped => Status == FileStatus.Skipped;
public bool IsIncluded => Status != FileStatus.Skipped;
```

`FromPath` unchanged. Factory `FromSessionEntry(ProjectProtectionEntry entry)` sets `FilePath` to output or project path, `Status` Skipped when not included, `SkipReason` from `SkipMessage`, `Hints` from entry, `FileSize` 0 if missing.

**FilesViewModel**

- `HasIncludedFiles => Files.Any(f => f.IsIncluded && (f.IsAssembly || f.IsSourceFile))`
- `CanObfuscate` in MainViewModel uses `HasIncludedFiles` instead of `HasFiles`
- `IsSupportedInputPath`: also `.sln`, `.slnx`, `.csproj`, `.vbproj`, `.fsproj`
- `AddFilesInternal`: if path is solution/project, call analyzer and add `FromSessionEntry` for each entry (dedupe by `FilePath`); else existing logic
- Constructor: add `ISolutionAnalyzer?` (null → skip expansion, keep dll-only; production Autofac must register and inject). Prefer non-null required; UI Autofac module registers Core `ObfuscationModule` already — confirm `Src/Obfy.UI` registers it. If not, add `ObfuscationModule` or register `SolutionAnalyzer` in the UI module.

**XAML:** empty state and overlay: `"Drag and drop assemblies or a solution"` / `".dll, .exe, .cs, .sln, .csproj"`. Under `FilePath`, bind `SkipReason` with visibility when `IsSkipped`. Icon converter: `Skipped => SymbolRegular.Warning24`.

**FileDialogService** first filter: `Assemblies, source, and solutions (*.dll;*.exe;*.cs;*.sln;*.slnx;*.csproj;*.vbproj;*.fsproj)|...`

- [ ] **Step 1: Write failing tests**

- `IsSupportedInputPath` true for a temp `.sln`
- `HandleFileDrop` with a temp sln (App + Tests fixture from Task 4, simplified) adds included dll + skipped test row
- `CanAcceptDrop` true for `.sln`
- `HasIncludedFiles` false when only skipped rows
- Converter maps `Skipped` to `Warning24`

- [ ] **Step 2: Run UI tests filter** — expected FAIL

- [ ] **Step 3: Implement model/VM/XAML/dialog/converter.** Wire `ISolutionAnalyzer` in the UI Autofac registration (`Src/Obfy.UI` host / module). Update `FilesViewModelTests` ctor call sites with `new SolutionAnalyzer()` or mock.

- [ ] **Step 4: Run `dotnet test Tests/Obfy.UI.Tests/Obfy.UI.Tests.csproj`** — expected PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.UI Tests/Obfy.UI.Tests
git commit -m "feat(ui): expand dropped solutions into included and skipped files"
```

---

### Task 10: UI obfuscate uses closed-set; docs wrap-up

**Files:**
- Modify: `Src/Obfy.UI/ViewModels/MainViewModel.cs`
- Test: `Tests/Obfy.UI.Tests/ViewModels/MainViewModelTests.cs` (add facts; create the file if missing — grep first. If MainViewModel is hard to construct, extract `internal static bool ShouldUseClosedSet(IReadOnlyList<AssemblyFile> files)` and test that plus a thin orchestrator.)

**Interfaces:**
- Consumes: `IObfuscationService.ObfuscateClosedSetAsync`
- Produces: Obfuscate button runs closed-set when two or more **included assemblies** are in the list, or when any file has `Hints` (came from a session). One included assembly → existing `ObfuscateAsync`. Merge checkbox still uses `MergeAndObfuscateAsync` and wins over closed-set.

**MainViewModel.ObfuscateAsync**

```
var included = files.Where(f => f.IsIncluded).ToList();
var skipped = files.Where(f => f.IsSkipped).ToList();
foreach (var s in skipped) { /* leave Skipped; log reason */ }

if (ShouldMerge(...)) { existing merge on included assemblies }
else if (included.Count(a => a.IsAssembly) >= 2 || included.Any(a => a.Hints != null))
{
    var inputs = included.Where(a => a.IsAssembly).Select(a => new ClosedSetInput {
        AssemblyPath = a.FilePath,
        Hints = a.Hints ?? new ProjectSettingsHints()
    }).ToList();
    var result = await _obfuscationService.ObfuscateClosedSetAsync(inputs, outputDir, settings, forcePreservePublic: settings.SymbolRenaming.PreservePublicApi && userEnabledPreservePublic, ...);
    // Map result: success → all included Success + output paths; failure → all included Error, Output.Error, snackbar; do not mark skipped as Error
}
else
    existing ObfuscateEachAsync(included)
```

If `outputDir` is empty, use `Path.GetDirectoryName(included[0].FilePath)` like today.

Log the skip list at start: `"Skipping App.Tests: Test project"`.

- [ ] **Step 1: Write failing tests** for `ShouldUseClosedSet`: 2 dlls true; 1 dll false; 1 dll with Hints true; 1 dll + skipped test true if hints; merge not tested here.

If MainViewModel tests already mock `IObfuscationService`, add a test that two files call `ObfuscateClosedSetAsync` not `ObfuscateAsync`. If the VM is untested, add `ShouldUseClosedSet` as `internal` and test it; still **call** `ObfuscateClosedSetAsync` from `ObfuscateAsync` in the implementation step.

- [ ] **Step 2: Run tests** — expected FAIL

- [ ] **Step 3: Implement MainViewModel branch.** Confirm `IObfuscationService` in UI is the Core service (not the VS wrapper).

- [ ] **Step 4: Run**

```
dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj
dotnet test Tests/Obfy.Console.Tests/Obfy.Console.Tests.csproj
dotnet test Tests/Obfy.UI.Tests/Obfy.UI.Tests.csproj
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.UI Tests/Obfy.UI.Tests
git commit -m "feat(ui): run closed-set obfuscation for solution sessions"
```

---

## Self-review (spec coverage)

| Spec requirement | Task |
|------------------|------|
| Accept `.sln` / `.slnx` / project | 2, 4, 8, 9 |
| XML-only, no MSBuild, no Directory.Build.props | 3, 4 |
| Skip tests with exact name rules (`Contest` kept) | 1, 4 |
| Skip missing output / missing project | 4 |
| Release over Debug; multi-TFM | 3, 4 |
| Hints: XAML, AOT, Blazor, Unity, ASP.NET | 4 |
| Closed-set public API vs libraries-only | 4 (plan), 5–6 (AssemblyRef) |
| JSON `runtimeProfile` not overwritten | 4 applier |
| `--preserve-public` escape hatch | 6 `forcePreservePublic`, 8 |
| Rename once + TypeRef rewrite | 5 |
| Other obfuscators per module, rename not twice | 6 |
| All-or-nothing write | 6 |
| Load failure drops that module, recomputes set | 6 |
| CLI dry-run / exit 0/1/2 / one solution | 8 |
| Extra dll with sln | 8 |
| UI expand, skip rows, Obfuscate not on drop | 9 |
| UI closed-set on Obfuscate | 10 |
| Single-file path unchanged | 7 (no change to `ObfuscateAsync`) |
| `--merge` uses existing merge | 8, 10 |
| VS/Rider/MSBuild/auto-build/BAML/Unity Start out of scope | no task |

No TBD remaining. Type names are consistent: `ProtectionSession`, `ProjectProtectionEntry`, `ProjectSettingsHints`, `ClosedSetInput`, `ClosedSetResult`, `ISolutionAnalyzer.Analyze`, `IClosedSetProcessor.ExecuteAsync`, `IObfuscationService.ObfuscateClosedSetAsync`, `SymbolRenamingObfuscator.RenameClosedSet`.

# Native Windows Packer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the default win-x64 packing path with a framework-dependent native CLR-host stub so the packed output is an unmanaged PE (no CLR directory, user IL only as AES-256 overlay ciphertext).

**Architecture:** Check in a prebuilt win-x64 C host (`nethost`/`hostfxr`) with `Obfy.PackedBootstrap` as RCDATA. At pack time, C# encrypts the obfuscated assembly, stamps the stub, appends the overlay, writes `.runtimeconfig.json`, and replaces the managed PE. `packing.rid: portable` keeps today’s managed launcher.

**Tech Stack:** C# / .NET 10 (`Obfy.Core`), net8.0 (`Obfy.PackedBootstrap`), C17 + MSVC (`Obfy.NativeHost`), dnlib 4.5.0, xUnit, Shouldly, Moq. No new NuGet packages. Windows BCrypt in the stub. `nethost.lib` from the `Microsoft.NETCore.App.Host.win-x64` pack.

**Spec:** `docs/superpowers/specs/2026-09-14-tier3-native-packer-design.md`

## Global Constraints

- Execution starts in an isolated worktree via `superpowers:using-git-worktrees` (`git worktree add .worktrees/<branch> -b <branch>`). Do not implement on `main`.
- Do not add licensing, RASP, crash reporting, Pre-JIT, self-contained runtime, x86/ARM64 stubs, or VM v2.
- Do not add `Obfy.NativeHost` to `Obfy.sln`. Dist is checked in; `dotnet build` never invokes `cl`.
- `Obfy.PackedBootstrap` is net8.0, no package references, no reference to `Obfy.Core` / Autofac / dnlib / NLog.
- Default `packing.rid` is `win-x64`. Unknown rid fails validation before load. Empty/null rid is `win-x64`.
- On win-x64 success, `effectiveOutput` **is** the native EXE. No sibling `{name}.launcher.exe`. No sibling managed user PE.
- AES-256-CBC PKCS7. Key = `Convert.FromHexString(IncrementalCache.ComputeKey(inputPath, settings))` (no second hash). IV = first 16 bytes of HMAC-SHA256(key, UTF-8 `"obfy-pack-iv"`). Do **not** use `EncryptionHelper.EncryptBytesAes` (it prepends the IV).
- Overlay magic is ASCII `OBP1` (`4F 42 50 31`). Header size 72. Version 1. Flags bit 0 = AES. Sentinel in the stub is ASCII `OBPYOVL1` (`uint64_t ObfyOverlayOffset = 0x314C564F5950424FULL`).
- Bootstrap export is `[UnmanagedCallersOnly(EntryPoint = "ObfyPackedRun")] public static int ObfyPackedRun(IntPtr payload, int length)`. Strip `GetCommandLineArgs()[0]` before invoking `Main(string[])`.
- Packing stays off in Minimal, Standard, and Aggressive. No `--pack` / `--rid` CLI flags. `rid` is config-only.
- NativeAOT / Unity IL2CPP / Blazor WASM still clear `Packing.Enabled`. Warning text: `Native packing disabled for {label}: it emits a framework-dependent host that is not used on this runtime.`
- Class libraries still fail after the managed write (`Packing failed` + `entry point`).
- Tests: xUnit + Shouldly + Moq. Execute the native EXE **directly** (not `dotnet {exe}`). Skip **run** assertions when `!OperatingSystem.IsWindows()`. Pack/format assertions run on every OS.
- Existing packing e2e tests are switched to `rid: portable` in Task 3 so they stay green as managed-launcher regressions. Win-x64 execute coverage is Task 4.
- `build.ps1` must pass `/Brepro` (deterministic PE). CI rebuilds and `git diff --exit-code -- Src/Obfy.NativeHost/dist`. If CI’s compiler produces a different but valid binary, commit that dist and re-run.
- Every task’s `dotnet test` filter must stay green before commit.
- Do not flip Competitive-Analysis **Code virtualization** to Yes until PR #23 is on `main` (Task 5).

## File map

**Create**

- `Src/Obfy.PackedBootstrap/Obfy.PackedBootstrap.csproj`
- `Src/Obfy.PackedBootstrap/PackedBootstrap.cs`
- `Src/Obfy.NativeHost/host.c`
- `Src/Obfy.NativeHost/host.rc`
- `Src/Obfy.NativeHost/build.ps1`
- `Src/Obfy.NativeHost/dist/Obfy.NativeHost.exe` (checked-in binary)
- `Src/Obfy.Core/Utilities/NativePacker.cs`
- `Tests/Obfy.Tests/NativeHostEmbedTests.cs`
- `Tests/Obfy.Tests/NativePackerTests.cs`

**Modify**

- `Obfy.sln` — add PackedBootstrap under Src (GUID `{B1C2D3E4-F5A6-4789-8ABC-DEF012345678}`)
- `Src/Obfy.Core/Obfy.Core.csproj` — embed `..\Obfy.NativeHost\dist\Obfy.NativeHost.exe`
- `Src/Obfy.Core/Utilities/ManagedLauncherPacker.cs` — XML comment (Task 5)
- `Src/Obfy.Core/Services/ObfuscationService.cs` — rid branch + incremental hit path
- `Src/Obfy.Core/Utilities/IncrementalCache.cs` — win-x64 hit = output + `{name}.runtimeconfig.json`
- `Src/Obfy.Core/Models/ObfySettings.cs` — `PackingSettings.Rid` + `Validate`
- `Src/Obfy.Core/Models/ObfuscationResult.cs` — XML comment (Task 5)
- `Src/Obfy.Core/Utilities/RuntimeProfileGating.cs` — warning copy
- `schemas/obfy.schema.json` — packing.rid
- `.github/workflows/ci.yml` — MSVC + bootstrap + `build.ps1` + dist freshness
- `Tests/Obfy.Tests/EndToEndObfuscationTests.cs` — portable rid on old tests (Task 3); win-x64 execute (Task 4)
- `Tests/Obfy.Tests/IncrementalCacheTests.cs`
- `Tests/Obfy.Tests/RuntimeProfileGatingTests.cs`
- `Tests/Obfy.Tests/ObfySettingsTests.cs`
- `Tests/Obfy.Tests/DecompilerResistanceTests.cs` (Task 4)
- `Tests/Obfy.VisualStudio.Tests/InPlaceOutputCopyTests.cs` (Task 4)
- `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml` + `HelpCatalog.cs` (Task 5)
- `Tests/Obfy.UI.Tests/Help/HelpCatalogTests.cs` (Task 5)
- Docs listed in Task 5

Do not add a ProjectReference from `Obfy.Tests` to `Obfy.PackedBootstrap` or `Obfy.NativeHost`.

---

### Task 1: Bootstrap, native host, embed smoke

**Files:**
- Create: `Src/Obfy.PackedBootstrap/Obfy.PackedBootstrap.csproj`
- Create: `Src/Obfy.PackedBootstrap/PackedBootstrap.cs`
- Create: `Src/Obfy.NativeHost/host.c`
- Create: `Src/Obfy.NativeHost/host.rc`
- Create: `Src/Obfy.NativeHost/build.ps1`
- Create: `Src/Obfy.NativeHost/dist/Obfy.NativeHost.exe`
- Create: `Tests/Obfy.Tests/NativeHostEmbedTests.cs`
- Modify: `Obfy.sln`
- Modify: `Src/Obfy.Core/Obfy.Core.csproj`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `nethost.lib` / `hostfxr.h` from `Microsoft.NETCore.App.Host.win-x64`; PackedBootstrap DLL as RCDATA `BOOTSTRAP`
- Produces:
  - `public static class Obfy.Runtime.PackedBootstrap` with `[UnmanagedCallersOnly(EntryPoint = "ObfyPackedRun")] public static int ObfyPackedRun(IntPtr payload, int length)`
  - `public const string NativePacker.EmbeddedName` is **not** created yet. Tests look up `"Obfy.NativeHost.exe"` on `typeof(PipelineContext).Assembly`
  - Stub exports no CLR directory; contains exactly one ASCII `OBPYOVL1` sentinel; `main` returns 1 with `Packed host: invalid payload.` when overlay is missing

- [ ] **Step 1: Write the failing embed test**

Create `Tests/Obfy.Tests/NativeHostEmbedTests.cs`:

```csharp
using System.Reflection.PortableExecutable;
using System.Text;
using Obfy.Core.Pipeline;
using Shouldly;

namespace Obfy.Tests;

public class NativeHostEmbedTests
{
    public const string EmbeddedName = "Obfy.NativeHost.exe";
    public static readonly byte[] Sentinel = "OBPYOVL1"u8.ToArray();

    [Fact]
    public void EmbeddedResource_IsPresentOnObfyCore()
    {
        typeof(PipelineContext).Assembly
            .GetManifestResourceNames()
            .ShouldContain(EmbeddedName);
    }

    [Fact]
    public void EmbeddedHost_IsPeWithoutClrDirectory_AndHasSingleSentinel()
    {
        using var stream = typeof(PipelineContext).Assembly
            .GetManifestResourceStream(EmbeddedName);
        stream.ShouldNotBeNull();
        using var ms = new MemoryStream();
        stream!.CopyTo(ms);
        var bytes = ms.ToArray();

        bytes[0].ShouldBe((byte)'M');
        bytes[1].ShouldBe((byte)'Z');

        using var pe = new PEReader(new MemoryStream(bytes));
        pe.PEHeaders.PEHeader.ShouldNotBeNull();
        pe.PEHeaders.PEHeader!.Magic.ShouldBe(PEMagic.PE32Plus);
        pe.PEHeaders.CorHeader.ShouldBeNull();
        pe.PEHeaders.PEHeader.Subsystem.ShouldBe(Subsystem.WindowsCui);

        CountOccurrences(bytes, Sentinel).ShouldBe(1);
    }

    internal static int CountOccurrences(byte[] haystack, byte[] needle)
    {
        var n = 0;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                n++;
        }
        return n;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~NativeHostEmbedTests`

Expected: FAIL (`ShouldContain` / resource null — `Obfy.NativeHost.exe` is not embedded)

- [ ] **Step 3: Add PackedBootstrap, NativeHost, embed, CI**

`Src/Obfy.PackedBootstrap/Obfy.PackedBootstrap.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Obfy.Runtime</RootNamespace>
    <AssemblyName>Obfy.PackedBootstrap</AssemblyName>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
  </PropertyGroup>
</Project>
```

`Src/Obfy.PackedBootstrap/PackedBootstrap.cs`:

```csharp
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Obfy.Runtime;

public static class PackedBootstrap
{
    [UnmanagedCallersOnly(EntryPoint = "ObfyPackedRun")]
    public static int ObfyPackedRun(IntPtr payload, int length)
    {
        try
        {
            if (payload == IntPtr.Zero || length <= 0)
            {
                Console.Error.WriteLine("Packed host: invalid payload.");
                return 1;
            }

            var bytes = new byte[length];
            Marshal.Copy(payload, bytes, 0, length);

            var alc = AssemblyLoadContext.Default;
            alc.Resolving += static (context, name) =>
            {
                if (string.IsNullOrEmpty(name.Name))
                    return null;
                var fileName = name.Name + ".dll";
                var probe = Path.Combine(AppContext.BaseDirectory, fileName);
                if (!File.Exists(probe) && !string.IsNullOrEmpty(name.CultureName))
                    probe = Path.Combine(AppContext.BaseDirectory, name.CultureName, fileName);
                return File.Exists(probe) ? context.LoadFromAssemblyPath(probe) : null;
            };

            var assembly = alc.LoadFromStream(new MemoryStream(bytes));
            var entry = assembly.EntryPoint;
            if (entry == null)
            {
                Console.Error.WriteLine("Packed host: payload assembly has no entry point.");
                return 1;
            }

            var all = Environment.GetCommandLineArgs();
            var args = all.Length <= 1 ? Array.Empty<string>() : all[1..];
            object?[]? invokeArgs = entry.GetParameters().Length == 0 ? null : new object[] { args };
            var result = entry.Invoke(null, invokeArgs);

            if (result is Task<int> ti)
                return ti.GetAwaiter().GetResult();
            if (result is Task t)
            {
                t.GetAwaiter().GetResult();
                return 0;
            }

            return result is int code ? code : 0;
        }
        catch (TargetInvocationException ex)
        {
            Console.Error.WriteLine(ex.InnerException ?? ex);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Packed host failed: " + ex.Message);
            return 1;
        }
    }
}
```

`Src/Obfy.NativeHost/host.rc` (path filled by `build.ps1` via `/d`):

```
#define RT_RCDATA 10
BOOTSTRAP RCDATA BOOTSTRAP_DLL
```

`build.ps1` should generate a one-line rc that points at the built bootstrap DLL, e.g. write `host.generated.rc`:

```
BOOTSTRAP RCDATA "C:\\abs\\path\\Obfy.PackedBootstrap.dll"
```

`Src/Obfy.NativeHost/host.c` — required behavior (keep this exact contract):

- `#define WIN32_LEAN_AND_MEAN` then `windows.h`, `bcrypt.h`, `stdio.h`, `stdint.h`, plus `nethost.h`, `coreclr_delegates.h`, `hostfxr.h`.
- `uint64_t ObfyOverlayOffset = 0x314C564F5950424FULL;` must appear **once** in the image (do not take its address in a way that duplicates the literal elsewhere; the packer searches the file bytes).
- `wmain`: open self via `GetModuleFileNameW` + `CreateFileW`; if file size `< ObfyOverlayOffset + 72` or `ObfyOverlayOffset == 0x314C564F5950424FULL` (unpatched sentinel), print `Packed host: invalid payload.` and return 1.
- Overlay at `ObfyOverlayOffset`: magic 4 bytes `OBP1`; version `uint32` == 1; header size `uint32` == 72; flags bit 0 set; skip tfm; payload length `uint32`; IV 16 bytes; key 32 bytes; ciphertext `length` bytes. Any mismatch → same stderr, return 1.
- Decrypt with BCrypt AES-256 CBC + `BCRYPT_BLOCK_PADDING` (PKCS7). Heap-allocate plaintext; keep it alive until `ObfyPackedRun` returns.
- `get_hostfxr_path` → load `hostfxr` → `hostfxr_initialize_for_runtime_config` on sibling `.runtimeconfig.json` (same path as exe, extension replaced with `.runtimeconfig.json`).
- Load RCDATA `BOOTSTRAP` from `GetModuleHandleW(NULL)` via `FindResourceW`/`LoadResource`/`LockResource`.
- `hdt_load_assembly_bytes` on those bytes; `hdt_get_function_pointer` with type `Obfy.Runtime.PackedBootstrap, Obfy.PackedBootstrap`, method `ObfyPackedRun`, delegate `UNMANAGEDCALLERSONLY_METHOD`.
- Typedef `int32_t (__stdcall *obfy_packed_run_fn)(uint8_t* payload, int32_t length);` — on x64 Windows this is the Microsoft x64 convention (`UnmanagedCallersOnly` default). Call it with plaintext pointer + length. Return that int.
- Link `bcrypt.lib` and `nethost.lib`. Subsystem console. `/Brepro`.

`Src/Obfy.NativeHost/build.ps1` (outline the implementer must follow):

```powershell
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repo = Resolve-Path (Join-Path $here "..\..")
dotnet build (Join-Path $repo "Src\Obfy.PackedBootstrap\Obfy.PackedBootstrap.csproj") -c Release --nologo
$bootstrap = Join-Path $repo "Src\Obfy.PackedBootstrap\bin\Release\net8.0\Obfy.PackedBootstrap.dll"
if (-not (Test-Path $bootstrap)) { throw "PackedBootstrap.dll missing: $bootstrap" }

$packRoot = Join-Path ${env:ProgramFiles} "dotnet\packs\Microsoft.NETCore.App.Host.win-x64"
$pack = Get-ChildItem $packRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
$native = Join-Path $pack.FullName "runtimes\win-x64\native"
# vswhere + vcvars64, or rely on ilammy/msvc-dev-cmd in CI
$dist = Join-Path $here "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$rc = Join-Path $here "host.generated.rc"
Set-Content -Path $rc -Value "BOOTSTRAP RCDATA `"$($bootstrap -replace '\\','\\')`"" -Encoding ascii
rc.exe /nologo /fo (Join-Path $here "host.res") $rc
cl.exe /nologo /O2 /Brepro /W3 /WX /DUNICODE /D_UNICODE /I $native `
  host.c /Fe:(Join-Path $dist "Obfy.NativeHost.exe") `
  /link /Brepro /INCREMENTAL:NO /SUBSYSTEM:CONSOLE host.res bcrypt.lib (Join-Path $native "nethost.lib")
```

If `cl` is not on PATH, locate `vcvars64.bat` with vswhere (`-latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64`) and invoke through `cmd /c`.

Add PackedBootstrap to `Obfy.sln` with GUID `{B1C2D3E4-F5A6-4789-8ABC-DEF012345678}`: Project entry, all six Debug/Release × Any CPU/x64/x86 mappings (ActiveCfg = Any CPU, same pattern as VmRuntime), NestedProjects under `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`.

`Src/Obfy.Core/Obfy.Core.csproj` add:

```xml
<ItemGroup>
  <EmbeddedResource Include="..\Obfy.NativeHost\dist\Obfy.NativeHost.exe">
    <LogicalName>Obfy.NativeHost.exe</LogicalName>
  </EmbeddedResource>
</ItemGroup>
```

Do **not** add a ProjectReference to NativeHost.

`.github/workflows/ci.yml` — insert **before** `dotnet restore` is fine; required **before** `dotnet build`:

```yaml
    - name: Setup MSVC
      uses: ilammy/msvc-dev-cmd@v1

    - name: Build native packer host
      shell: pwsh
      run: pwsh -File Src/Obfy.NativeHost/build.ps1

    - name: Native host dist is current
      run: git diff --exit-code -- Src/Obfy.NativeHost/dist
```

Run `build.ps1` locally, commit `dist/Obfy.NativeHost.exe`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~NativeHostEmbedTests`

Expected: PASS

Also run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~EndToEndObfuscationTests.Packing`

Expected: PASS (service still uses the managed launcher)

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.PackedBootstrap Src/Obfy.NativeHost Src/Obfy.Core/Obfy.Core.csproj Obfy.sln Tests/Obfy.Tests/NativeHostEmbedTests.cs .github/workflows/ci.yml
git commit -m "feat: add native packer host and embed it in Obfy.Core"
```

---

### Task 2: Overlay writer (`NativePacker`)

**Files:**
- Create: `Src/Obfy.Core/Utilities/NativePacker.cs`
- Create: `Tests/Obfy.Tests/NativePackerTests.cs`

**Interfaces:**
- Consumes: embedded `Obfy.NativeHost.exe`; `IncrementalCache.ComputeKey`; AES via `System.Security.Cryptography.Aes`
- Produces:
  - `public static class NativePacker` with `public const string EmbeddedName = "Obfy.NativeHost.exe"`
  - `public static readonly byte[] Sentinel = "OBPYOVL1"u8.ToArray();` (or `ReadOnlySpan<byte>` + tests use `"OBPYOVL1"u8`)
  - `public static string RuntimeConfigPathFor(string assemblyPath)` → `Path.ChangeExtension(assemblyPath, ".runtimeconfig.json")`
  - `public static string Pack(string assemblyPath, ObfySettings settings, string inputPath)` → returns `assemblyPath` after replacing the file with stub+overlay
  - Throws `InvalidOperationException` containing `entry point` when the module has no entry point (same wording family as `ManagedLauncherPacker`)
  - Throws `InvalidOperationException` containing `native host is missing` if the resource is absent
  - Throws `InvalidOperationException` containing `native host is corrupt` if the sentinel count is not 1

- [ ] **Step 1: Write failing packer tests**

Create `Tests/Obfy.Tests/NativePackerTests.cs`. Reuse the compile helpers from `EndToEndObfuscationTests` (duplicate the small `CompileToExe` / `CompileToAssembly` locals — do not make EndToEnd public). Include:

```csharp
[Fact]
public void Pack_ReplacesManagedPe_WithNativeHostAndOverlay()
{
    // compile Main()=>11 exe, copy to output path, Pack(output, settings, input)
    // File.Exists(output) true
    // File.Exists(ManagedLauncherPacker.LauncherPathFor(output)) false
    // PEReader CorHeader is null
    // bytes contain ASCII OBP1
    // NativePacker.RuntimeConfigPathFor(output) exists
    // dnlib ModuleDefMD.Load(output) throws (BadImageFormatException or similar)
}

[Fact]
public void Pack_LibraryWithoutEntryPoint_LeavesManagedPe()
{
    // compile DLL, Pack throws, message contains "entry point"
    // original DLL still loadable with ModuleDefMD.Load
}

[Fact]
public void Pack_SameInputSettings_ProducesIdenticalOverlay()
{
    // Pack twice with copies of the same managed bytes + same input/settings
    // output byte arrays SequenceEqual
}

[Fact]
public void Pack_DifferentInput_ProducesDifferentCiphertext()
{
    // two different Main bodies; overlays (bytes after first OBP1) differ
}

[Fact]
public void Pack_CopiesSubsystemFromManagedPe()
{
    // compile with OutputKind.WindowsApplication (WinExe)
    // packed PEHeader.Subsystem == WindowsGui
}
```

Use `System.Reflection.PortableExecutable` for CLR/subsystem asserts.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~NativePackerTests`

Expected: FAIL (`NativePacker` not found)

- [ ] **Step 3: Implement `NativePacker`**

```csharp
public static class NativePacker
{
    public const string EmbeddedName = "Obfy.NativeHost.exe";
    public const int HeaderSize = 72;
    public static readonly byte[] Magic = "OBP1"u8.ToArray();
    public static readonly byte[] Sentinel = "OBPYOVL1"u8.ToArray();

    public static string RuntimeConfigPathFor(string assemblyPath) =>
        Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");

    public static string Pack(string assemblyPath, ObfySettings settings, string inputPath)
}
```

`Pack` algorithm (must match spec):

1. `File.Exists(assemblyPath)` else throw `Packing requires an existing assembly file.`
2. `ModuleDefMD.Load(File.ReadAllBytes(assemblyPath))`; if `EntryPoint == null` throw `Packing requires an assembly with an entry point.` Dispose the module.
3. `managed = File.ReadAllBytes(assemblyPath)`. Read subsystem via `PEReader` (`PEHeader.Subsystem` as `ushort` written back later).
4. `key = Convert.FromHexString(IncrementalCache.ComputeKey(inputPath, settings))` (must be 32 bytes). `iv = HMACSHA256.HashData(key, "obfy-pack-iv"u8)[..16]`.
5. AES-256-CBC PKCS7: `aes.Key = key; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;` `TransformFinalBlock` on `managed`. Ciphertext does not prepend IV.
6. Load embedded stub (`typeof(NativePacker).Assembly.GetManifestResourceStream(EmbeddedName)`). Null → `native host is missing`. Count sentinel; not 1 → `native host is corrupt`. Write overlay offset (`(ulong)stub.Length`) as little-endian uint64 over the sentinel. Patch `IMAGE_OPTIONAL_HEADER.Subsystem` at the PE32+ offset (use `PEHeaders.PEHeaderStartOffset` + 68 for PE32Plus Subsystem, which is `int` offset 68 from optional header start; PE32 is 68 as well for Subsystem — lock: `PEHeaderStartOffset + 68` as `ushort`).
7. Temp file in the destination directory (`assemblyPath + ".obfypack"`). Write stub, then 72-byte header, then ciphertext. Header: magic, version `1`, header size `72`, flags `1`, tfm major `(uint)Environment.Version.Major`, payload length `(uint)ciphertext.Length`, IV, key.
8. Write runtimeconfig JSON **identical** to `ManagedLauncherPacker` (tfm `net{major}.0`, `rollForward` `LatestMinor`, framework `Microsoft.NETCore.App` version `{major}.0.0`).
9. Replace: `File.Replace(temp, assemblyPath, destinationBackupFileName: null)` on Windows; else `File.Delete(assemblyPath); File.Move(temp, assemblyPath)`. Catch: delete temp + runtimeconfig; do not delete a leftover managed PE if replace failed before dest was touched. Re-throw as `InvalidOperationException` with `Packing failed` only if you wrap — spec says `NativePacker.Pack` throws `InvalidOperationException`; `ObfuscationService` prefixes `Packing failed:`. **Throw the inner messages without the `Packing failed:` prefix** (service adds it).
10. Return `assemblyPath`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~NativePackerTests|FullyQualifiedName~NativeHostEmbedTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Utilities/NativePacker.cs Tests/Obfy.Tests/NativePackerTests.cs
git commit -m "feat: stamp native host overlay and replace managed output"
```

---

### Task 3: Service, rid, incremental cache, gating

**Files:**
- Modify: `Src/Obfy.Core/Models/ObfySettings.cs` (`PackingSettings`)
- Modify: `Src/Obfy.Core/Services/ObfuscationService.cs`
- Modify: `Src/Obfy.Core/Utilities/IncrementalCache.cs`
- Modify: `Src/Obfy.Core/Utilities/RuntimeProfileGating.cs`
- Modify: `schemas/obfy.schema.json`
- Modify: `Tests/Obfy.Tests/ObfySettingsTests.cs`
- Modify: `Tests/Obfy.Tests/IncrementalCacheTests.cs`
- Modify: `Tests/Obfy.Tests/RuntimeProfileGatingTests.cs`
- Modify: `Tests/Obfy.Tests/EndToEndObfuscationTests.cs` (portable rid only)
- Modify: `Tests/Obfy.Tests/ObfySchemaContractTests.cs` — no code change if schema gains `rid` (contract walks serialized properties)

**Interfaces:**
- Consumes: `NativePacker.Pack`, `NativePacker.RuntimeConfigPathFor`, `ManagedLauncherPacker.Pack`
- Produces:
  - `PackingSettings.Rid` (`string`, default `"win-x64"`)
  - `PackingSettings.IsNativeWin64` / `IsPortable` as below
  - `ObfySettings.Validate()` rejects enabled packing with rid not `win-x64`/`portable`
  - win-x64 pack warning `"Packed native host: " + packedPath` with `packedPath == effectiveOutput`
  - portable pack warning `"Packed launcher: "` unchanged
  - incremental hit packed path: win-x64 → `effectiveOutput`; portable → `LauncherPathFor`

```csharp
public class PackingSettings
{
    public bool Enabled { get; set; }
    public string Rid { get; set; } = "win-x64";

    public bool IsNativeWin64 =>
        string.IsNullOrWhiteSpace(Rid) ||
        Rid.Equals("win-x64", StringComparison.OrdinalIgnoreCase);

    public bool IsPortable =>
        Rid.Equals("portable", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 1: Write/adjust failing tests**

Add to `ObfySettingsTests.cs`:

```csharp
[Fact]
public void Validate_PackingEnabledUnknownRid_Throws()
{
    var settings = new ObfySettings { Packing = { Enabled = true, Rid = "linux-x64" } };
    Should.Throw<ValidationException>(() => settings.Validate())
        .Message.ShouldContain("rid");
}

[Fact]
public void Clone_KeepsPackingRid()
{
    var original = new ObfySettings { Packing = { Enabled = true, Rid = "portable" } };
    var clone = original.Clone();
    clone.Packing.Rid.ShouldBe("portable");
}

[Fact]
public void Validate_PackingEnabledNullRid_TreatedAsWinX64()
{
    var settings = new ObfySettings { Packing = { Enabled = true, Rid = null! } };
    settings.Validate(); // must not throw
}
```

Change `IncrementalCacheTests.TryHit_PackingEnabledWithoutLauncher_IsFalse` to create the output file, enable packing (default rid win-x64), **not** create runtimeconfig, expect false. Add:

```csharp
[Fact]
public void TryHit_PackingWinX64WithoutRuntimeConfig_IsFalse() { /* output exists, packing on, no runtimeconfig */ }

[Fact]
public void TryHit_PackingWinX64WithRuntimeConfig_IsTrue()
{
    // output + cache + NativePacker.RuntimeConfigPathFor(output)
}

[Fact]
public void TryHit_PackingPortableWithoutLauncher_IsFalse()
{
    settings.Packing.Enabled = true;
    settings.Packing.Rid = "portable";
}
```

Change `RuntimeProfileGatingTests` expected substring from `"Managed launcher packing disabled"` to `"Native packing disabled"`.

In `EndToEndObfuscationTests.PackingSettings()` set `Packing.Rid = "portable"` so existing launcher e2e stays valid.

Add e2e (still Task 3, no native run):

```csharp
[Fact]
public async Task Packing_UnknownRid_FailsBeforeWrite()
{
    // settings Rid = "linux-x64", Enabled = true
    // result.Success false, ErrorMessage contains "rid" or "Invalid settings"
    // output file should not exist
}

[Fact]
public async Task Packing_DefaultRid_WritesNativeHostAndNoLauncher()
{
    // PackingSettings without portable (new helper NativePackingSettings: Enabled true, default rid)
    // Success; PackedLauncherPath == output; no .launcher.exe; CorHeader null; runtimeconfig exists
    // do not Run the exe here
}
```

Schema packing.properties.rid:

```json
"rid": {
  "type": "string",
  "enum": ["win-x64", "portable", "Win-x64", "Portable"],
  "default": "win-x64",
  "description": "win-x64 emits a native CLR-host stub that replaces the managed PE. portable keeps the managed {name}.launcher.exe beside the obfuscated assembly."
}
```

Use enum `["win-x64","portable"]` only (spec). Case-insensitive runtime validation still accepts `Win-x64`. Schema can list lowercase only.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~Validate_PackingEnabledUnknownRid_Throws|FullyQualifiedName~TryHit_PackingWinX64|FullyQualifiedName~Packing_DefaultRid|FullyQualifiedName~Apply_DisablesEmbedding`

Expected: FAIL (missing `Rid`, old gating string still in product, default packing still managed launcher)

- [ ] **Step 3: Implement**

`ObfySettings.Validate()` after `ValidateObject(Virtualization);`:

```csharp
ValidateObject(Packing);
if (Packing.Enabled && !Packing.IsNativeWin64 && !Packing.IsPortable)
    throw new ValidationException("packing.rid must be win-x64 or portable.");
```

`IncrementalCache.TryHit` packing branch:

```csharp
if (settings.Packing.Enabled)
{
    if (settings.Packing.IsPortable)
    {
        if (!File.Exists(ManagedLauncherPacker.LauncherPathFor(outputPath)) ||
            !File.Exists(ManagedLauncherPacker.RuntimeConfigPathFor(outputPath)))
            return false;
    }
    else if (!File.Exists(NativePacker.RuntimeConfigPathFor(outputPath)))
        return false;
}
```

`ObfuscationService` pack block:

```csharp
if (settings.Packing.IsPortable)
{
    packedPath = ManagedLauncherPacker.Pack(effectiveOutput);
    context.Warnings.Add("Packed launcher: " + packedPath);
}
else
{
    packedPath = NativePacker.Pack(effectiveOutput, settings, inputPath);
    context.Warnings.Add("Packed native host: " + packedPath);
}
```

Incremental **hit** path (the early return): if packing enabled and `IsPortable`, `packed = LauncherPathFor(...)`; else `packed = effectiveOutputEarly`. Still add a warning `"Packed launcher: "` vs `"Packed native host: "` matching the path kind.

Gating warning: `Native packing disabled for {label}: it emits a framework-dependent host that is not used on this runtime.`

Update packing schema description to mention native default + portable escape hatch.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```
dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~Packing|FullyQualifiedName~NativePacker|FullyQualifiedName~NativeHostEmbed|FullyQualifiedName~IncrementalCache|FullyQualifiedName~RuntimeProfileGating|FullyQualifiedName~ObfySettings|FullyQualifiedName~ObfySchemaContract
```

Expected: PASS. Existing packing e2e uses portable and still `RunLauncher` via `dotnet`.

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Models/ObfySettings.cs Src/Obfy.Core/Services/ObfuscationService.cs Src/Obfy.Core/Utilities/IncrementalCache.cs Src/Obfy.Core/Utilities/RuntimeProfileGating.cs schemas/obfy.schema.json Tests/Obfy.Tests
git commit -m "feat: pack win-x64 as a native host from packing.enabled"
```

---

### Task 4: Execute e2e + decompiler + in-place sidecar

**Files:**
- Modify: `Tests/Obfy.Tests/EndToEndObfuscationTests.cs`
- Modify: `Tests/Obfy.Tests/DecompilerResistanceTests.cs`
- Modify: `Tests/Obfy.VisualStudio.Tests/InPlaceOutputCopyTests.cs`

**Interfaces:**
- Consumes: Task 3 service wiring
- Produces: Windows-only `RunNative(string exe, ...)` helper; decompiler test that packed exe is not a managed module

- [ ] **Step 1: Write failing execute tests**

Add helper next to `RunLauncher`:

```csharp
private static int RunNative(string exe, string extraArgs = "", string? workingDirectory = null)
{
    OperatingSystem.IsWindows().ShouldBeTrue("native packed EXE execute tests are Windows-only");
    var start = new System.Diagnostics.ProcessStartInfo(exe)
    {
        Arguments = extraArgs,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false
    };
    if (!string.IsNullOrEmpty(workingDirectory))
        start.WorkingDirectory = workingDirectory;
    using var process = System.Diagnostics.Process.Start(start);
    process.ShouldNotBeNull();
    process!.StandardOutput.ReadToEndAsync().GetAwaiter().GetResult();
    var stderr = process.StandardError.ReadToEndAsync().GetAwaiter().GetResult();
    process.WaitForExit(15000).ShouldBeTrue("native host timed out: " + stderr);
    return process.ExitCode;
}
```

Wrap each **run** assert with `if (OperatingSystem.IsWindows())`. Pack-format asserts always run.

Facts (new, default rid / `NativePackingSettings()` = Enabled true, Rid omitted):

- `Packing_Native_MainReturns11` — `RunNative(output) == 11`; `PackedLauncherPath == output`; no `.launcher.exe`
- `Packing_Native_ForwardsMainArgs` — `RunNative(output, "ping") == 7`
- `Packing_Native_WaitsForAsyncTaskMain` — 9
- `Packing_Native_ResolvesSiblingAssemblies` — copy `Lib.dll` next to native EXE; `RunNative` with different cwd == 13
- `Packing_Native_WithAntiTamper_Runs` — 11
- `Packing_Native_TruncatedOverlay_Exits1` — Pack, truncate file by 8 bytes, `RunNative` == 1 (Windows only)
- Keep portable tests from Task 3.

`DecompilerResistanceTests`:

```csharp
[Fact]
public async Task NativePackedExe_CannotBeLoadedAsManagedModule()
{
    // obfuscate+pack win-x64; ModuleDefMD.Load(output) throws; PEReader.CorHeader is null
}
```

`InPlaceOutputCopyTests` add:

```csharp
[Fact]
public void Copy_CopiesNativeRuntimeConfigSidecar()
{
    // temp App.exe + App.runtimeconfig.json → dest gets both
}
```

Keep the existing `.launcher.*` test (portable sidecars).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~Packing_Native`

Expected: FAIL (facts not present, or run returns 1 if host/bootstrap still broken — implement host fully in Task 1 so this should fail as missing tests first; after adding tests, they fail until host+packer actually execute)

If Task 1 stub already decrypts and Task 2 already stamps, these tests may **pass** as soon as they are written. That is acceptable: they still lock the contract. If they fail, fix host/packer (wrong calling convention, runtimeconfig path, AES padding). Do not weaken overlay or switch to `dotnet exec`.

- [ ] **Step 3: Fix host/packer if execute fails**

Common fixes allowed without a spec change:

- `get_function_pointer` type string must be `Obfy.Runtime.PackedBootstrap, Obfy.PackedBootstrap`
- runtimeconfig path = `ChangeExtension(exe, ".runtimeconfig.json")`
- BCrypt padding flag `BCRYPT_BLOCK_PADDING`
- Keep payload buffer alive across `ObfyPackedRun`
- `hostfxr_initialize_for_runtime_config` needs a **file** — already written by packer

If flattening is irrelevant here (bootstrap is not going through Obfy’s CF pass; it is a separate assembly). Do not inject bootstrap into the user module.

- [ ] **Step 4: Run the focused suite**

Run:

```
dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~Packing|FullyQualifiedName~NativePacker|FullyQualifiedName~DecompilerResistanceTests.NativePacked
dotnet test Tests/Obfy.VisualStudio.Tests/Obfy.VisualStudio.Tests.csproj --filter FullyQualifiedName~InPlaceOutputCopyTests
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Tests/Obfy.Tests/EndToEndObfuscationTests.cs Tests/Obfy.Tests/DecompilerResistanceTests.cs Tests/Obfy.VisualStudio.Tests/InPlaceOutputCopyTests.cs
git commit -m "test: run native packed EXEs and reject managed loads"
```

---

### Task 5: Docs and product copy

**Files:**
- Modify: `docs/Techniques.md` packing section
- Modify: `docs/Configuration.md` packing
- Modify: `docs/CLI.md` config-only note
- Modify: `docs/Competitive-Analysis.md` — Native packing **Yes**; Code virtualization **Yes** only if PR #23 is on this branch/`main`
- Modify: `docs/Roadmap.md` PF-09
- Modify: `CHANGELOG.md` Unreleased **breaking**
- Modify: `README.md` / `CLAUDE.md` packing/technique lines
- Modify: `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml`
- Modify: `Src/Obfy.UI/Help/HelpCatalog.cs`
- Modify: `Tests/Obfy.UI.Tests/Help/HelpCatalogTests.cs`
- Modify: `Src/Obfy.Core/Models/ObfySettings.cs` `PackingSettings` XML comments
- Modify: `Src/Obfy.Core/Models/ObfuscationResult.cs` `PackedLauncherPath` XML
- Modify: `Src/Obfy.Core/Utilities/ManagedLauncherPacker.cs` XML: portable path only
- Modify: `docs/Testing-Roadmap.md` if a TR packing row exists; else skip

**Interfaces:**
- Consumes: shipped Task 1–4 behavior
- Produces: copy that matches the spec honesty line

- [ ] **Step 1: Write the failing help-catalog test**

In `HelpCatalogTests.TechniqueHeadings` replace `"Managed launcher"` with `"Native packer (win-x64)"`.

Add a fact if one snapshots the note text; otherwise the heading array is enough.

Settings panel: no dedicated UI test for tooltip string unless one already snapshots it (`SettingsPanelTests`). If a test contains `"Pack managed launcher"`, update it.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Tests/Obfy.UI.Tests/Obfy.UI.Tests.csproj --filter FullyQualifiedName~HelpCatalogTests`

Expected: FAIL (heading still `"Managed launcher"`)

- [ ] **Step 3: Update copy and docs**

Help catalog note: `"Native packer (win-x64)"` → `"Replaces the output with a native Windows host (framework-dependent). Set packing.rid to portable for the managed launcher."`

SettingsPanel expander Header can stay a group name; change:

- Header `"Managed launcher"` → `"Packing"`
- Toggle Content `"Pack as native Windows host"`
- `AutomationProperties.Name` `"Enable native Windows packing"`
- ToolTip `"Pack as a native Windows host (win-x64). Set packing.rid to portable for the managed launcher."`

Techniques.md packing section: native win-x64 stub, overlay AES, no sibling user PE, FDD, not Pre-JIT, portable launcher, key in overlay = obfuscation not confidentiality.

Configuration.md: `packing.rid` default `win-x64`.

CLI.md: packing still config-only; **breaking** `packing.enabled` is native on win-x64.

Competitive-Analysis:

- Feature **Native packing / native EXE** = **Yes** with footnote: win-x64 FDD CLR-host stub; `portable` = managed launcher; not Pre-JIT; not self-contained; not ARM64.
- **Code virtualization** = **Yes** with the VM v1 skip-set footnote **only if** `VirtualizationObfuscator` on this branch no longer contains `CreateExecute`. If PR #23 is not merged, leave virtualization Partial and say so in the PR description; do not lie.
- Conclusion: Obfy is a managed-VM + native-stub tier-3 tool on CoreCLR/Windows when both cells are Yes. Still not licensing/RASP. Do not write “covers Reactor.”

Roadmap PF-09: Done (native FDD stub). Remaining Future: Pre-JIT / other RIDs / self-contained.

CHANGELOG Unreleased:

```
### Breaking
- `packing.enabled` now emits a native win-x64 host that replaces the obfuscated PE. Set `packing.rid` to `portable` for the previous managed `{name}.launcher.exe`.
```

CLAUDE.md packing line: native FDD stub; not a Pre-JIT packer.

`PackedLauncherPath` XML: path of the native host (win-x64) or the managed launcher (portable).

- [ ] **Step 4: Run tests**

Run:

```
dotnet test Tests/Obfy.UI.Tests/Obfy.UI.Tests.csproj --filter FullyQualifiedName~HelpCatalog
dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~RuntimeProfileGating|FullyQualifiedName~ObfySchemaContract|FullyQualifiedName~Packing
```

Expected: PASS

Then: `dotnet test` (solution, default filter)

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add docs CHANGELOG.md README.md CLAUDE.md schemas/obfy.schema.json Src/Obfy.UI Src/Obfy.Core/Models Src/Obfy.Core/Utilities/ManagedLauncherPacker.cs Tests/Obfy.UI.Tests
git commit -m "docs: describe native packing and mark the tier-3 cells honestly"
```

---

## Spec coverage (self-review)

| Spec requirement | Task |
|------------------|------|
| Prebuilt C stub + RCDATA bootstrap + embed | 1 |
| Dist checked in, `/Brepro`, CI freshness | 1 |
| Overlay format, AES key/IV, sentinel, subsystem patch | 2 |
| Replace managed PE; no launcher sidecar on win-x64 | 2, 3 |
| `packing.rid` win-x64 / portable; unknown rid fails | 3 |
| Incremental cache hit files | 3 |
| Gating warning copy | 3 |
| Portable launcher regression | 3 |
| Execute Main/args/async/sibling/anti-tamper | 4 |
| Truncated overlay exit 1 | 4 |
| dnlib cannot load packed exe | 4 |
| In-place runtimeconfig sidecar | 4 |
| Docs, breaking changelog, UI/help | 5 |
| Virtualization Yes footnote gated on PR #23 | 5 |
| No Pre-JIT / licensing / other RIDs | Global constraints |

No TBD/TODO left. `NativePacker.Pack` signature is `Pack(string assemblyPath, ObfySettings settings, string inputPath)` in Tasks 2–3. `PackedLauncherPath` on win-x64 equals `effectiveOutput`.

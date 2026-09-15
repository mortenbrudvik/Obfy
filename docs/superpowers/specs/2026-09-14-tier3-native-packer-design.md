# Tier 3 protection ceiling: native Windows packer

**Date:** 2026-09-14  
**Status:** Locked after review  
**Product:** Obfy packing post-save (`Obfy.Core`)  
**Surfaces:** Core engine, packing settings/UI copy, incremental cache, docs  
**Not in this spec:** VM v2 (EH/generics/byref), Pre-JIT, self-contained runtime, licensing/RASP, x86/ARM64 stubs, MSBuild PackageReference

## Goal

Replace the win-x64 packing path with a **framework-dependent native CLR-host stub** so the packed output is an unmanaged PE: no CLR directory, user IL only as AES-256-CBC ciphertext in an overlay, decrypted in memory and run via `hostfxr`. After this ships, Competitive-Analysis.md marks **Native packing / native EXE** as Yes, with the limits in this spec called out — the Reactor stub analogue without Pre-JIT or a self-contained runtime.

This is the second sub-project of “make Obfy a Tier 3 obfuscation tool.” The first is general IL virtualization (locked spec `docs/superpowers/specs/2026-09-13-tier3-general-vm-design.md`, PR #23). Licensing, crash reporting, and RASP stay out of Obfy.

## Success criteria

- With `packing.enabled: true` and default `rid: win-x64`, the file at `effectiveOutput` is a PE **without** a CLR data directory. dnlib / ILSpy cannot load it as a managed module. There is no sibling `{name}.launcher.exe` and no second managed PE of the user assembly.
- Running that EXE on Windows x64 with `Microsoft.NETCore.App` installed returns the same entry-point results as today’s managed launcher: `Main() => 11`, `Main(string[])` args (exe name stripped), async `Task<int> Main`, sibling DLL probe from the EXE directory.
- `packing.rid: portable` still emits `{name}.launcher.exe` + `.runtimeconfig.json` and leaves the managed PE in place.
- Class libraries still fail packing (entry point required); the managed PE is left.
- NativeAOT / Unity IL2CPP / Blazor WASM still turn packing off with a warning.
- Same input + settings + Obfy version → same overlay ciphertext (incremental cache still hits). Different input hashes → different ciphertext.
- Opt-in: off in Minimal, Standard, and Aggressive. No new CLI flag.

## Background

Today `ManagedLauncherPacker` compiles a C# console host, embeds the obfuscated assembly as `packed.dll`, and writes `{name}.launcher.exe` **beside** the still-decompilable managed PE. Run with `dotnet {name}.launcher.exe`. Competitive-Analysis.md correctly marks this Partial. Roadmap PF-09: the managed FDD launcher is not native code generation.

The current code *does* prove the pipeline shape we keep: packing is **post-save** in `ObfuscationService` after method-IL XOR and anti-tamper have patched the PE; it requires an entry point; source inputs skip; ALC `LoadFromStream` is how anti-tamper stays compatible. What does not meet the Yes bar is a managed host and a sibling user PE.

Commercial context: .NET Reactor’s native EXE stub is the independent-review default. ArmDot uses BoxedApp. Babel Ultimate is a managed VM without a native stub. This spec matches Reactor’s *on-disk* property (unmanaged PE, payload not a CLR image) without Pre-JIT of methods to machine code.

Program B (locked in design review): VM v1 lands and is marked **Yes** with the skip-set footnote; native packing lands and is marked **Yes** with the limits below. Do not start VM v2 or licensing.

## Goals and non-goals

### Goals

- Prebuilt win-x64 C host (`nethost` / `hostfxr`) shipped inside `Obfy.Core` as an embedded resource.
- Pack-time C# only: encrypt, copy stub, patch subsystem, append overlay, write `.runtimeconfig.json`, replace the managed PE.
- Any Obfy tool host (including Linux CI) can emit a Windows packed EXE from the checked-in stub.
- `packing.rid: portable` keeps the managed launcher.
- AES-256-CBC payload; key is `IncrementalCache.ComputeKey` hex-decoded (same 32 bytes as the VM seed).
- GUI vs console is a PE `Subsystem` patch on the same stub.
- Update docs, UI/help copy, Competitive-Analysis (both packing **and** virtualization cells if PR #23 has landed), Roadmap PF-09.

### Non-goals (out of this spec)

- Pre-JIT / compiling user methods to x64.
- Self-contained runtime, single-file bundler, NativeAOT of the user app.
- win-x86, win-arm64, or ELF hosts.
- Packing class libraries (no entry point).
- Overlay mutation beyond AES-256-CBC + key-in-header.
- Unique generated native stub per build.
- Licensing, HWID, DRM, RASP, crash reporting.
- VM v2 (EH, generic calls, byref, Approach B).
- Turning packing on in any preset.
- First-class NativeAOT / IL2CPP / Blazor packing.
- MSBuild PackageReference, VS Code TaskProvider, Unity Editor plugin.

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Product target | Honest matrix Yes on native packing (Reactor stub, not Pre-JIT) | Locked in brainstorming. Licensing/RASP stay out. |
| Architecture | Prebuilt C stub + encrypted overlay | Pack-time stays C#. Linux CI can emit a Windows EXE. No second runtime (rejected NativeAOT host). |
| RID switch | `packing.rid`: `win-x64` (default) or `portable` | One on/off (`enabled`). Target RID, not tool-host OS. Unknown rid fails. |
| Delivery | Replace `effectiveOutput` with the native EXE | Sibling managed PE is why today’s packing is Partial. |
| Hosting | `hostfxr` FDD + tiny managed bootstrap with `UnmanagedCallersOnly` | `hostfxr` cannot invoke an arbitrary renamed `Main` / `Task<int> Main` without a known export. |
| Bootstrap | Separate `Obfy.PackedBootstrap` net8.0 assembly, RCDATA inside the stub | Reuse today’s `PackedHost` invoke + sibling `Resolving`. Not user code. |
| Crypto | AES-256-CBC PKCS7; IV and key in overlay header | Same honesty as strings/VM. Key is cache-key bytes so incremental hits are byte-identical. Do not use `EncryptionHelper.EncryptBytesAes` as the overlay blob (that helper prepends the IV). |
| Stub build | `build.ps1` + checked-in `dist/`; **not** in `Obfy.sln` | `dotnet build` must work without MSVC. CI is `windows-latest` and fails if dist is stale. |
| nethost | Statically link `nethost.lib`; locate `hostfxr` at run time | No extra DLL beside the packed EXE. |
| AES in the stub | Windows BCrypt | Stub is win-x64 only; no OpenSSL. |
| Args | Strip `GetCommandLineArgs()[0]` | Matches today’s launcher (`Main(string[] args)` does not include the exe path). |
| UI | Existing toggle only; `rid` is config-only | YAGNI. Tooltip explains portable. |
| Breaking | Existing `packing.enabled: true` becomes native win-x64 | Required for the Yes bar. Portable is the escape hatch. |

## Architecture

```
pipeline + SaveAsync (managed PE, method-IL XOR + anti-tamper already applied)
        │
        ▼
packing.enabled?
        │
        ├─ rid == portable → ManagedLauncherPacker (unchanged)
        │
        └─ rid == win-x64 (default)
                │
                ▼
         NativePacker
           encrypt payload (AES-256-CBC, key = ComputeKey hex)
           copy embedded Obfy.NativeHost.exe over effectiveOutput
           patch PE Subsystem from the managed PE
           patch overlay-offset sentinel
           append overlay
           write {name}.runtimeconfig.json
           managed PE is gone (replaced, not left beside)
```

Priority: packing is not an `IObfuscator`. It stays after `SaveAsync` in `ObfuscationService`.

### Runtime (packed EXE)

```
Obfy.NativeHost.exe
  parse overlay via patched sentinel
  BCrypt decrypt payload to a heap buffer
  hostfxr_initialize_for_runtime_config(sibling .runtimeconfig.json)
  load_assembly_bytes(embedded PackedBootstrap)
  get_function_pointer(ObfyPackedRun, UNMANAGEDCALLERSONLY_METHOD)
  ObfyPackedRun(payload, length)   // buffer stays alive
        │
        ▼
  PackedBootstrap: LoadFromStream + invoke entry + sibling Resolving
```

User IL never sits on disk as a PE on the win-x64 success path.

## Components

### `Src/Obfy.PackedBootstrap/`

net8.0 class library. No package references. No reference to `Obfy.Core`, Autofac, dnlib, or NLog. Public surface is one export:

```csharp
namespace Obfy.Runtime;

public static class PackedBootstrap
{
    [UnmanagedCallersOnly(EntryPoint = "ObfyPackedRun")]
    public static int ObfyPackedRun(IntPtr payload, int length)
}
```

Behavior (same as current `PackedHost`, minus compiling a new C# exe per pack):

- Copy `payload`/`length` to a `byte[]`, `AssemblyLoadContext.Default.LoadFromStream`.
- `Environment.GetCommandLineArgs()`; pass `args[1..]` to `Main(string[])`. Parameterless `Main` gets no args. `Task` / `Task<int>` are waited. Return int or `0`.
- `Resolving`: probe `AppContext.BaseDirectory` / `{Culture}/{name}.dll` (same as today’s launcher).
- `TargetInvocationException` → print inner to stderr, return `1`. Other exceptions → `"Packed host failed: " + message`, return `1`.
- Missing entry point → stderr, return `1` (should not happen; packer already required one).

`InternalsVisibleTo` is not required. Tests never call `ObfyPackedRun` in-process; they run the packed EXE.

### `Src/Obfy.NativeHost/`

C, win-x64, subsystem **console** in the checked-in binary (packer may patch to GUI). Links `nethost.lib` from the `Microsoft.NETCore.App.Host.win-x64` pack. Uses `get_hostfxr_path` then `hostfxr_initialize_for_runtime_config`, `hdt_load_assembly_bytes`, `hdt_get_function_pointer`.

Placeholder in `.rdata`/`.data` (exactly 8 bytes, little-endian):

```c
uint64_t ObfyOverlayOffset = 0x314C564F5950424FULL; /* ASCII "OBPYOVL1" */
```

If the packer cannot find **exactly one** occurrence of those 8 bytes, packing fails.

Bootstrap DLL is linked as `RCDATA` named `BOOTSTRAP` at stub build time. The host loads that resource with `FindResource`/`LoadResource` (not from disk).

Decrypt: BCrypt AES-256 CBC with PKCS7 padding. Header fields at the overlay offsets below. On bad magic, version ≠ 1, truncated file, BCrypt failure: print `Packed host: invalid payload.` to stderr and `return 1`.

Do **not** add this project to `Obfy.sln`. Build with `Src/Obfy.NativeHost/build.ps1` (vswhere + `cl` + `rc`). Output: `Src/Obfy.NativeHost/dist/Obfy.NativeHost.exe`. That file is **checked in**. `build.ps1` must pass `/Brepro` (or equivalent zero timestamp / deterministic timestamp) so two builds of the same sources are byte-identical. CI on `windows-latest` runs `build.ps1` and fails the job if `git diff --exit-code -- Src/Obfy.NativeHost/dist` is dirty.

`dotnet build` of Obfy never invokes `cl`. Linux/macOS contributors pack using the checked-in dist.

### `NativePacker` (`Obfy.Core/Utilities/NativePacker.cs`)

C# only. Embed the dist EXE:

```xml
<EmbeddedResource Include="..\Obfy.NativeHost\dist\Obfy.NativeHost.exe">
  <LogicalName>Obfy.NativeHost.exe</LogicalName>
</EmbeddedResource>
```

Public API:

```csharp
public static class NativePacker
{
    public const string EmbeddedName = "Obfy.NativeHost.exe";
    public static string RuntimeConfigPathFor(string assemblyPath);
    public static string Pack(string assemblyPath, ObfySettings settings, string inputPath);
}
```

`Pack`:

1. Require existing `assemblyPath` and an entry point (same `ModuleDefMD.Load` check as `ManagedLauncherPacker`).
2. Read managed bytes. Record `Subsystem` from the PE optional header.
3. `key = Convert.FromHexString(IncrementalCache.ComputeKey(inputPath, settings))` (32 bytes; no second hash). **Lock:** IV is derived from HMAC-SHA256(key, `"obfy-pack-iv"`) first 16 bytes, so the same key+plaintext yields the same overlay.
4. AES-256-CBC PKCS7 encrypt the managed bytes with that key and IV. Ciphertext does **not** prepend the IV.
5. Load embedded stub bytes. Find the 8-byte sentinel; write the overlay offset (`stub.Length` as `uint64` LE) over it. Patch `IMAGE_OPTIONAL_HEADER.Subsystem` to the recorded value. Write stub, then overlay, to a temp file in the destination directory.
6. Write `{name}.runtimeconfig.json` to a temp name. `tfm` / `Microsoft.NETCore.App` prefer `TargetFrameworkAttribute` on the managed PE, then the input sibling runtimeconfig, then `Environment.Version.Major` (same fallback as today’s launcher).
7. Replace `assemblyPath` with the temp native EXE (`File.Replace` on Windows; `File.Move(..., overwrite: true)` elsewhere — never delete the destination first). Then publish the runtimeconfig. On failure, delete temp native + unpublished runtimeconfig; do **not** delete the managed PE or a pre-existing JSON.

`RuntimeConfigPathFor` is `Path.ChangeExtension(assemblyPath, ".runtimeconfig.json")` (e.g. `App.obf.exe` → `App.obf.runtimeconfig.json`).

### Settings

```json
{
  "packing": {
    "enabled": false,
    "rid": "win-x64"
  }
}
```

```csharp
public class PackingSettings
{
    public bool Enabled { get; set; }

    /// <summary>win-x64 (native stub) or portable (managed launcher). Default win-x64.</summary>
    public string Rid { get; set; } = "win-x64";
}
```

Validation (`ObfySettings.Validate`): if `Enabled` and `Rid` is not `win-x64` or `portable` (ordinal ignore-case), throw `ValidationException` — the run fails **before** load, so no managed PE is written. Empty/`null` rid is treated as `win-x64`.

`ApplyPreset` still sets `Packing.Enabled = false` and does not need to touch `Rid`.

Schema: `schemas/obfy.schema.json` packing object gains `rid` enum `["win-x64","portable"]`.

### `ObfuscationService`

After successful `SaveAsync`, if packing is on and target is assembly:

- `win-x64` → `packedPath = NativePacker.Pack(effectiveOutput, settings, inputPath)`; warning `"Packed native host: " + packedPath`.
- `portable` → `ManagedLauncherPacker.Pack(effectiveOutput)` as today; warning `"Packed launcher: " + packedPath`.

`PackedLauncherPath` is that path. On win-x64 it **equals** `effectiveOutput` / `OutputPath`.

Incremental **hit** (update `IncrementalCache.TryHit`):

- Packing off: output + `.obfycache` (unchanged).
- Packing on + `portable`: output + launcher exe + launcher runtimeconfig (unchanged).
- Packing on + `win-x64`: output file exists **and** `NativePacker.RuntimeConfigPathFor(outputPath)` exists. Do **not** require `{name}.launcher.exe`. Hit result’s `PackedLauncherPath` is `outputPath`.

Cache is still written only after a successful pack.

### Overlay format

Little-endian. Appended immediately after the stub PE bytes (file overlay, not a section).

| Offset | Size | Field |
|--------|------|--------|
| 0 | 4 | Magic ASCII `OBP1` (`4F 42 50 31`) |
| 4 | 4 | Version `1` |
| 8 | 4 | Header size `72` |
| 12 | 4 | Flags (bit 0 set = AES-256-CBC; other bits 0) |
| 16 | 4 | TFM major (`Environment.Version.Major`) |
| 20 | 4 | Payload length (ciphertext bytes) |
| 24 | 16 | IV |
| 40 | 32 | AES key |
| 72 | N | Ciphertext |

Magic on disk is the four ASCII bytes `O` `B` `P` `1`. Header size is 72 so a v2 can grow the header; v1 host rejects version ≠ 1.

Key-in-binary is obfuscation, not confidentiality. Same honesty line as strings/VM.

### Gating, CLI, UI

`RuntimeProfileGating`: when packing is cleared, warning text is  
`Native packing disabled for {label}: it emits a framework-dependent host that is not used on this runtime.`  
(covers both rids). Still uses `AllowsPeMutation` (Default profile only).

CLI: packing stays config-only (no `--pack`). No `--rid`.

UI `SettingsPanel.xaml` toggle: `AutomationProperties.Name` and tooltip become “Pack as a native Windows host (win-x64). Set packing.rid to portable for the managed launcher.” Help catalog: replace “Managed launcher” with “Native packer (win-x64)” and one line that portable keeps the managed launcher.

No rid combo box.

## Pipeline interactions

| Pass / feature | Interaction |
|----------------|-------------|
| Method IL encryption / anti-tamper | Already in the PE `SaveAsync` writes. Packer encrypts those bytes. |
| Virtualization | Interpreter lives inside the payload. Packing wraps the whole assembly. |
| Anti-tamper at run | Bootstrap still `LoadFromStream`; existing ALC skip stays. Keep the packing+anti-tamper warning. |
| Dependency embedding | Inside the payload; `AssemblyResolve` runs after load. |
| Incremental cache | Seed **is** the AES key. IV is HMAC-SHA256(key, `"obfy-pack-iv"`)[:16]. Same key+plaintext ⇒ same overlay. |
| Signing | Happens on the managed PE before pack. The native EXE is not strong-named. |
| VS/Rider in-place copy | Copies every sidecar in the temp dir; `.runtimeconfig.json` is included. No API change. Portable still produces `.launcher.*` sidecars. |
| Source mode | Packing skipped with warning (unchanged). |
| Merge | Packing runs on the merged entry-point assembly (`MergeAndObfuscateAsync` → `ObfuscateAsync`). |
| Closed-set / solution / multi-assembly | Packing runs per written **entry-point** output after `SaveAsync`; libraries stay managed (skip warning). Sidecars are copied on commit. |

## Error handling

| Situation | Result |
|-----------|--------|
| No entry point | Fail `Packing failed: … entry point`. Managed PE **left**. |
| Unknown `rid` | `ValidationException` before load. No output. |
| Embedded stub missing | Fail `Packing failed: native host is missing`. Managed PE left. |
| Sentinel missing or duplicated | Fail `Packing failed: native host is corrupt`. Managed PE left. |
| Encrypt / write / replace throws | Fail `Packing failed: …`. Delete partial native/runtimeconfig; managed PE left. |
| Source input | Success + “Packing skipped”. |
| Profile gates packing off | Warning; no pack. |
| Packing + anti-tamper | Success + existing warning. |
| Host cannot find runtime / runtimeconfig | Packed process exits `1` at **run** time. Obfuscation succeeded. |
| Bad overlay / decrypt | Host stderr `Packed host: invalid payload.` exit `1`. |

## Testing

### Packer (any OS)

- Embedded resource `Obfy.NativeHost.exe` is present on `Obfy.Core`.
- `Pack` of an exe: output has **no** CLR directory (`IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR` RVA 0 / size 0); file starts with `MZ`; overlay magic `OBP1` at the patched offset; no `{name}.launcher.exe`; `PackedLauncherPath == output`.
- dnlib `ModuleDefMD.Load(packedExe)` throws or is not a valid managed module (`DecompilerResistanceTests` style).
- Library without entry point: fail, managed output remains, no native overwrite.
- Unknown rid: validate fail.
- `rid: portable`: sibling launcher exists; managed PE still loadable; existing `RunLauncher` via `dotnet` still used.
- NativeAOT profile: packing disabled (update gating test copy).
- Incremental: win-x64 hit requires runtimeconfig; deleting it is a miss; same input+settings ⇒ identical overlay bytes; different input ⇒ different ciphertext.
- Pack fail (library) does not write `.obfycache`.
- `InPlaceOutputCopy` copies `App.runtimeconfig.json` next to `App.exe`. Keep the portable `.launcher.*` test.

### Execute (Windows x64 only)

Skip the **run** assertions on non-Windows (`OperatingSystem.IsWindows()`). Pack assertions still run.

Start the packed EXE **directly** (not `dotnet {exe}`). Reuse the current e2e cases:

- `Main() => 11`
- args `ping` → 7
- async `Task<int>` → 9
- sibling `Lib.dll` → 13 (copy Lib next to the native EXE; run with a different cwd as today)
- packing + anti-tamper → 11

### Native host fixture

A unit test (C# reading a fixture, or packing then truncating the overlay) that a truncated/bad-magic EXE exits `1`. Windows only.

### Copy / schema / help

- Help catalog string, Settings tooltip/automation name, `PackingSettings` XML comments, schema `rid` enum.
- `ObfuscationResult.PackedLauncherPath` XML comment: native host **or** managed launcher.

## Documentation to update (same release)

1. `docs/Techniques.md` — packing section: native win-x64 stub, portable launcher, no sibling user PE, FDD, not Pre-JIT. Honesty line.
2. `docs/Configuration.md` — `packing.rid`.
3. `docs/CLI.md` — config-only note; breaking: `packing.enabled` is native on win-x64.
4. `docs/Competitive-Analysis.md` — **Native packing = Yes** with footnote (win-x64 FDD stub; portable = managed launcher; not Pre-JIT; not self-contained; not ARM64). **Code virtualization = Yes** with the VM v1 skip-set footnote (depends on PR #23 merged). Executive summary: Obfy is a managed-VM + native-stub tier-3 tool on CoreCLR/Windows; still not licensing/RASP. Do not claim “covers Reactor.”
5. `docs/Roadmap.md` — PF-09 v1 done; remaining = Pre-JIT / other RIDs / self-contained (leave as Future, do not schedule).
6. `CHANGELOG.md` — **breaking** packing default. `README.md` / `CLAUDE.md` technique/packing lines.
7. UI tooltip + `HelpCatalog` + gating warning string.
8. `PackingSettings` / `ManagedLauncherPacker` XML comments — portable path is the managed launcher; default is native.

Honesty line (keep in Techniques and Competitive-Analysis): packing hides the managed PE on disk; the decryption key is in the overlay; anyone who runs or inspects the EXE can recover IL. Not confidentiality.

## File map

| File | Role |
|------|------|
| Create `Src/Obfy.PackedBootstrap/Obfy.PackedBootstrap.csproj` | net8.0 bootstrap |
| Create `Src/Obfy.PackedBootstrap/PackedBootstrap.cs` | `ObfyPackedRun` |
| Create `Src/Obfy.NativeHost/host.c` (and `build.ps1`, `.rc`, `dist/Obfy.NativeHost.exe`) | win-x64 stub |
| Modify `Obfy.sln` | Add PackedBootstrap only (not NativeHost) |
| Modify `Src/Obfy.Core/Obfy.Core.csproj` | Embed `dist/Obfy.NativeHost.exe` |
| Create `Src/Obfy.Core/Utilities/NativePacker.cs` | Encrypt, stamp, replace |
| Modify `ManagedLauncherPacker.cs` | XML comment: portable path only |
| Modify `ObfuscationService.cs` | Rid branch; hit path `PackedLauncherPath` |
| Modify `IncrementalCache.cs` | win-x64 hit = output + `{name}.runtimeconfig.json` |
| Modify `ObfySettings.cs` / validator / schema | `PackingSettings.Rid` |
| Modify `RuntimeProfileGating.cs` | Warning copy |
| Modify `ObfuscationResult.cs` | XML comment |
| Modify UI SettingsPanel + HelpCatalog | Copy |
| Tests as above | including `RuntimeProfileGatingTests`, `HelpCatalogTests`, `InPlaceOutputCopyTests`, `ObfySchemaContractTests` |
| Docs as above | |
| Modify `.github/workflows/ci.yml` | After restore: `dotnet build PackedBootstrap`; `pwsh Src/Obfy.NativeHost/build.ps1`; `git diff --exit-code -- Src/Obfy.NativeHost/dist` |

`Obfy.PackedBootstrap` must not take a dependency on Autofac, dnlib, or NLog. `UnmanagedCallersOnly` is required so the C host can `get_function_pointer` without a managed delegate type in the stub.

## Locked decisions (this review)

1. Approach 1: prebuilt C stub + overlay, not NativeAOT host, not SDK apphost.
2. Target RID `win-x64` from any tool host; `portable` keeps the managed launcher.
3. On win-x64 success, no sibling managed user PE; `effectiveOutput` **is** the native EXE.
4. Virtualization cell becomes Yes with the v1 skip set (PR #23); this spec does not expand the ISA.
5. AES-256-CBC; key = `ComputeKey` hex; IV = HMAC-SHA256(key, `"obfy-pack-iv"`) first 16 bytes (deterministic).
6. Bootstrap `UnmanagedCallersOnly` + `LoadFromStream`; strip argv[0].
7. Stub not in the .sln; dist checked in; `/Brepro` so CI `git diff` is meaningful.
8. Packing stays off in every preset; config-only; no `--pack`.
9. Class libraries still fail after managed write.
10. FDD; machine-wide `Microsoft.NETCore.App`; no self-contained.

## Open questions

None. Product bar, stub architecture, RID, delivery, crypto, bootstrap, errors, presets, and docs cells were locked in design review.

## PR Plan

Each PR is independently reviewable and leaves `dotnet test` green. **Depends on:** PR #23 (general IL VM) merged to `main` before the Competitive-Analysis virtualization cell is flipped to Yes. Packer PRs 1–4 can land without that flip; PR 5 must not claim virtualization Yes until #23 is in.

### PR 1 — Bootstrap + stub + embed smoke

**Title:** `feat: add native packer host and embed it in Obfy.Core`  
**Files:** `Obfy.PackedBootstrap`, `Obfy.NativeHost` (C, `build.ps1`, `dist/`), `Obfy.Core.csproj` embed, `Obfy.sln`, CI freshness step, a test that the embedded resource exists and the dist PE has no CLR directory.  
**Depends on:** none (packer-side)  
**Description:** No `NativePacker` yet. Stub `main` may return `1` with “not packed”. Proves `cl` + RCDATA + embed.

### PR 2 — Overlay writer

**Title:** `feat: stamp native host overlay and replace managed output`  
**Files:** `NativePacker.cs`, packer unit tests (magic, no CLR, sentinel, library fail leaves managed PE).  
**Depends on:** PR 1  
**Description:** Does not wire `ObfuscationService`. Tests call `NativePacker.Pack` directly.

### PR 3 — Service, rid, incremental cache

**Title:** `feat: pack win-x64 as a native host from packing.enabled`  
**Files:** `ObfuscationService`, `PackingSettings.Rid`, validator, schema, `IncrementalCache`, gating warning, `ObfuscationResult` comment. Portable path regression tests.  
**Depends on:** PR 2  

### PR 4 — Execute e2e + decompiler

**Title:** `test: run native packed EXEs and reject managed loads`  
**Files:** `EndToEndObfuscationTests` (win-x64 default), `DecompilerResistanceTests`, Windows-only run helper, `InPlaceOutputCopyTests` runtimeconfig case.  
**Depends on:** PR 3  

### PR 5 — Docs and product copy

**Title:** `docs: describe native packing and mark the tier-3 cells honestly`  
**Files:** Techniques, Configuration, CLI, Competitive-Analysis, Roadmap, CHANGELOG (breaking), README/CLAUDE, SettingsPanel, HelpCatalog, XML comments.  
**Depends on:** PR 4; virtualization Yes footnote depends on PR #23  

## Tier 3 leftover (not this spec)

After this spec **and** PR #23 ship, Obfy is a **managed-VM + native-stub** tier-3 tool on CoreCLR / Windows x64, still without:

1. **VM v2** — EH, generics (including calls), byref, `switch`, Approach B handler generation.
2. **Packer v2** — Pre-JIT, other RIDs, self-contained runtime.
3. **Adoption, not ceiling** — MSBuild PackageReference, documented Linux CI as a *supported host* (the tool can already emit a win-x64 stub from Linux), VS Code TaskProvider.

Do not start (1) or (2) until execute tests on real packed EXEs have been sitting in CI and the Partial→Yes packing cell has been in Competitive-Analysis without a caveat that the host is still managed.

# Competitive Analysis: Obfy vs Professional .NET Obfuscators

A sourced comparison of Obfy against commercial and open-source .NET obfuscators, as of September 2026.

## How to read this document

Obfuscation is a deterrent, not confidentiality. String, constant, resource, and method-IL “encryption” embed the key in the output. Virtualization embeds an interpreter. Packing hides the managed PE on disk; the decryption key is in the overlay. Anyone who runs or inspects the assembly (or packed EXE) can recover plaintext. Do not ship real secrets (keys, tokens, credentials) inside an assembly and rely on any tool on this page to keep them secret.

**Legend**

| Cell | Meaning |
|------|---------|
| **Yes** | Documented as a first-class product feature |
| **Partial** | Exists, but with a material limit (called out under the table) |
| **Recipe** | Example config / runtime gating, not a vendor plugin or SKU |
| **—** | Not documented, not offered, or not applicable |

Feature cells for commercial tools are **vendor-documented claims**, not independent lab results. NDepend’s 2026 evaluation is the only recent third-party write-up that tested several tools on a real commercial codebase. Vendor roundups (PreEmptive, Softanics/ArmDot) are useful for prices and positioning; treat their “best of” conclusions as marketing.

GitHub stars and last-push dates were rechecked on 15 September 2026. Shop prices were last sampled on 13 September 2026.

---

## Executive Summary

Obfy is a new (first public commit 31 December 2025), MIT-licensed .NET obfuscator with a CLI, Fluent WPF UI, Visual Studio 2022 extension, Rider plugin, Microsoft Store listing, and a dual pipeline: **dnlib assembly obfuscation** plus **Roslyn source obfuscation**. Among free tools it is the broadest *conventional* protection stack (rename, strings, constants, resources, control flow, anti-debug/dump/tamper/decompiler, method-IL XOR, merge, closed-set / solution rename, embed, watermark, reports) plus a **win-x64 FDD native packing stub**. It is not a general IL virtualizer and not a licensing/RASP product. Native packing is a framework-dependent CLR-host stub (not Pre-JIT, not self-contained, not ARM64). Closed-set / solution runs pack each entry-point output.

**Positioning that still holds**

- **Vs free tools:** Broader technique set than Obfuscar (rename-first) and LoGiC.NET (archived). Different trade than BitMono (anti-decompiler / Unity / plugins vs Obfy’s control-flow + constants + resources + source mode + closed-set + desktop/IDE / Store UX).
- **Vs budget commercial ($249–$499):** Matches the *everyday* layer (rename, strings, CF, anti-debug/tamper). Native packing is a win-x64 FDD CLR-host stub, not Pre-JIT. Does **not** match .NET Reactor, ArmDot, Babel Ultimate, or Eazfuscator on general code virtualization or built-in licensing.
- **Vs enterprise (Dotfuscator, SmartAssembly):** Covers static obfuscation needs; lacks RASP, crash analytics, quote-based support SLAs, and (for Dotfuscator) Overload Induction / configurable runtime response.

**What changed since the previous revision of this file**

- Native packing / native EXE stays **Yes** (win-x64 FDD stub). Closed-set / solution runs pack each entry-point output. Code virtualization stays **Partial**: the shipping pass still uses `CreateExecute`; [PR #23](https://github.com/mortenbrudvik/Obfy/pull/23) (general IL VM wiring) is open. `VmEncoder` / `Obfy.VmRuntime` exist on `main` and are not the pipeline. Licensing/RASP remain commercial gaps. Do not write that Obfy covers Reactor.
- Closed-set / solution obfuscation (1.3.0) is now in the integration matrix. Microsoft Store is a first-class Windows install path. Linux CLI is CI-tested (`linux-cli` on Ubuntu), not “theoretical.”
- MSBuild and Azure DevOps cells for Obfy are **Partial** (`Exec` / script, not a PackageReference or official task). BitMono no longer listed as having a `dotnet tool` that Obfy lacks.
- GitHub: Obfy 1 star / 0 forks, last push 15 Sep 2026. Obfuscar last push 14 Sep 2026. Shop prices were not re-sampled.

---

## Tools Compared

### Commercial (actively sold)

| Tool | Vendor | License model | Public price (single seat) | First released | Latest noted release |
|------|--------|---------------|----------------------------|----------------|----------------------|
| **Dotfuscator Professional** | PreEmptive (Idera) | Subscription, quote | Not public. Historical reports $2,000+/yr; one 2026 roundup cited ~$4,250+/yr. Community Edition is free for personal use inside Visual Studio. | 2003 | 7.8.0 (27 Jul 2026) |
| **SmartAssembly** | Redgate | Annual subscription | List price is JS-loaded on red-gate.com (no static USD on the pricing HTML). ComponentSource lists Standard ~CAD $1,116/user/year (~USD $800). Third-party roundups cite ~$710/user/year; one Redgate product-page scrape in this research pass showed **$777 / $1,188** per license-year for Standard/Pro. Budget **~$700–$1,200/user/year** until you get a quote. | 2004 | 8.4.11 (31 Aug 2026) |
| **.NET Reactor** | Eziriz | Perpetual + 1 yr updates | **$249** single (no build server). **$549** company (CI included). Renewal $99 / $179. | 2004 | 7.5.0.0 (12 Nov 2025) |
| **Babel Obfuscator** | babelfor.net | Perpetual + 1 yr maintenance | **€350** Enterprise (Windows CLI + UI). **€1,250** Ultimate (NuGet / Linux / macOS). Maintenance €150 / €550. | 2006 | 11.7.0 (9 May 2026) |
| **Eazfuscator.NET** | Gapotchenko | Perpetual + 1 yr updates | **$399** single (16-core cap). **$1,699** site. Renewal $99 / $419. | 2007 | 2026.2 (11 Aug 2026) |
| **ArmDot** | Softanics | Perpetual + 1 yr updates | **$499** single. **$1,799** site. Renewal 50% of list (~$250 / ~$900). | ~2018 | 2026.6 (27 Apr 2026) |
| **Agile.NET** | SecureTeam | Perpetual + maintenance, or subscription | **$795** + $195/yr developer. Enterprise **$150/month** billed annually. | ~2000s (CodeVeil lineage) | 6.6.x (2026) |
| **DNGuard HVM** | V.I.P. Protection / dnguard.net | Perpetual + 1 yr maintenance | **$899** Professional (no HVM). **$1,299** Enterprise (HVM). | 2000s | 4.9.6 (13 Apr 2026) |
| **Spices.Net Obfuscator** | 9Rays.Net | Commercial | Quote / SKU-based (not used as a primary comparator below) | 2000s | 5.26.2.17 (17 Feb 2026), claims .NET 10 + VS 2026 |

**Not primary comparators (stale, niche, or Framework-only)**

| Tool | Why it is sidelined |
|------|---------------------|
| **Crypto Obfuscator** (ssware.com) | Last public build 2020 / 17 Mar 2021. Site still sells v2020 ($149–$399). Do not treat as a modern-.NET option. |
| **ILProtector** (vgrsoft.net) | **$149** / **$499** site. Documents .NET Framework 2.0–4.8 Windows desktop only. Unpackers exist. Not a .NET 5+ tool. yck1509 pointed ConfuserEx users here in 2016; that is historical, not a current recommendation. |
| **Skater .NET** (Rustemsoft) | Still sold ($80–$300). Low independent visibility; vendor is migrating users to “Opaquer”. |
| **Dotfuscator Community** | Bundled with Visual Studio. Personal/non-commercial. Rename + limited control flow. No string encryption, no MSBuild, library mode on by default. |

### Open source

| Tool | GitHub | Stars (15 Sep 2026) | License | Last push | Status |
|------|--------|---------------------|---------|-----------|--------|
| **Obfy** | [mortenbrudvik/Obfy](https://github.com/mortenbrudvik/Obfy) | 1 | MIT | 15 Sep 2026 | Active. Created 31 Dec 2025. |
| **Obfuscar** | [obfuscar/obfuscar](https://github.com/obfuscar/obfuscar) | 3,188 | MIT | 14 Sep 2026 | Active. v3.0 beta (SRM, no Mono.Cecil). |
| **BitMono** | [bitmono-project/BitMono](https://github.com/bitmono-project/BitMono) | 559 | MIT | 13 Sep 2026 | Active. Latest tag **0.45.0** the same day. Unity UPM, GitHub Action, MSBuild, bitmono.dev. |
| **JIEJIE.NET** | [dcsoft-yyf/JIEJIE.NET](https://github.com/dcsoft-yyf/JIEJIE.NET) | 887 | **GPL-2.0** | 9 Apr 2026 | Chinese-first docs. Last GitHub *tag* is 2022-11-07; the 2026 push is a README/resource-encryption note, not a tagged release. |
| **ConfuserEx** | [yck1509/ConfuserEx](https://github.com/yck1509/ConfuserEx) | 3,765 | MIT | May 2019 | **Archived** (Jan 2019). Framework 2.0–4.5. |
| **mkaring/ConfuserEx** | [mkaring/ConfuserEx](https://github.com/mkaring/ConfuserEx) | 2,904 | MIT | API `pushed_at` 7 Jun 2024 | Largest fork by stars. Default-branch HEAD is 15 Apr 2022; latest tag v1.6.0 (17 Jan 2022). The 2024 push may not be `master`. Still Framework-era. |
| **neo-ConfuserEx** | [XenocodeRCE/neo-ConfuserEx](https://github.com/XenocodeRCE/neo-ConfuserEx) | 861 | MIT | 28 Jul 2026 | Revived in 2026 (`v1.0.0-rc2`: signed integrity + selective KoiVM). README still lists Framework 2.0–4.7.2 only. |
| **LoGiC.NET** | [AnErrupTion/LoGiC.NET](https://github.com/AnErrupTion/LoGiC.NET) | 516 | MIT | 23 Aug 2023 | **Archived.** Do not recommend. |

---

## Feature Comparison Matrix

### Core obfuscation

| Feature | Obfy | Dotfuscator Pro | SmartAssembly | .NET Reactor | Babel | Eazfuscator | ArmDot |
|---------|:----:|:---------------:|:-------------:|:------------:|:-----:|:-----------:|:------:|
| **Symbol renaming** | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| **String encryption** | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| **Control flow** | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| **Metadata removal / pruning** | Yes | Yes | Yes | Yes | Yes | Yes | — |
| **Resource encryption** | Yes | — | Yes | Yes | Yes | Yes | Yes |
| **Constant / value encryption** | Yes | — | — | Yes | Yes | via VM / data virt. | — |

Obfuscar’s “string hiding” is reversible XOR; its own docs warn against using it for sensitive strings. BitMono encrypts strings (including UnmanagedString) but does not flatten control flow.

### Advanced protection

| Feature | Obfy | Dotfuscator Pro | SmartAssembly | .NET Reactor | Babel | Eazfuscator | ArmDot |
|---------|:----:|:---------------:|:-------------:|:------------:|:-----:|:-----------:|:------:|
| **Code virtualization** | Partial | — | — | Yes | Ultimate | Yes | Yes |
| **Native packing / native EXE** | Yes | — | — | Yes | — | — | App virt. (Windows, BoxedApp) |
| **MSIL / method encryption** | Yes | — | — | NecroBit | Ultimate | via VM | via VM |
| **Anti-debug** | Yes | Yes (RASP) | — | Yes | Yes | — | Implicit via VM |
| **Anti-tamper** | Yes | Yes (RASP) | Pro | Yes | Yes | — | Experimental |
| **Anti-dump** | Yes | — | — | — | — | — | — |
| **Watermark** | Yes | Yes | — | Yes | — | — | — |
| **Built-in licensing / DRM** | — | Shelf Life expiry | — | Yes | Separate product | — | Yes |
| **RASP / runtime response** | — | Yes | Tamper (Pro) | — | — | — | — |
| **Crash / error reporting** | — | — | Yes | — | — | — | — |

**Obfy limits (do not collapse these to “Yes” in marketing copy)**

- **Virtualization:** bytecode interpreter for selected **static `int` methods** only (`ldc.i4`, `ldarg`, `ldloc`/`stloc`, add/sub/mul, `ceq`/`cgt`/`clt`, signed branches; ≤8 params / ≤16 locals; no EH/generics; unsigned compares skipped). Not a general IL VM. `VmEncoder` / `Obfy.VmRuntime` exist on `main`; the shipping pass still injects `CreateExecute`. Do not flip this cell to Yes until [PR #23](https://github.com/mortenbrudvik/Obfy/pull/23) merges and that interpreter is gone.
- **Native packing:** win-x64 framework-dependent CLR-host stub; `packing.rid: portable` keeps the managed launcher. Closed-set / solution runs pack each entry-point output. Not Pre-JIT, not self-contained, not ARM64. Packing hides the managed PE on disk; the decryption key is in the overlay. Anyone who runs or inspects the EXE can recover IL. Not confidentiality.
- **Method IL encryption:** per-method XOR in the PE, Windows, skips generics.
- **Anti-dump:** PE-header wipe plus in-process `0xC3` patch of `dbghelp!MiniDumpWriteDump` on Windows x86/x64. ARM64 skipped. Gated off NativeAOT / IL2CPP / Blazor WASM. External dumpers are unaffected.
- **Anti-de4dot:** decoy ConfusedBy/Dotfuscator attributes (name-based detector bait), not a de4dot block.

Reactor’s NecroBit is IL replacement/encryption with a cross-platform claim (Windows/Linux/macOS). Babel’s code encryption is a managed VM and is **not supported on MAUI or Blazor**. Eazfuscator generates a new VM per run and markets “homomorphic encryption” on suitable circuits — treat that as a vendor claim. ArmDot generates a unique VM per build.

### Naming modes

| Mode | Obfy | Dotfuscator | SmartAssembly | .NET Reactor | Babel |
|------|:----:|:-----------:|:-------------:|:------------:|:-----:|
| **Unreadable / Unicode** | Yes | Yes | Yes | Yes | Yes |
| **Sequential (a, b, c)** | Yes | Yes | Yes | Yes | Yes |
| **Hash-based** | Yes | — | — | — | — |
| **Random** | Yes | Yes | Yes | Yes | Yes |
| **Overload induction** | — | Yes (patented) | — | — | overloaded renaming |

Eazfuscator does **not** emit a mapping file. It encrypts symbols with a private key. NDepend rejected it for that reason: encrypted names still encode original length, and a 2019 write-up showed the scheme can be attacked. Mapping files remain the production-debug standard (Obfy, Reactor, Dotfuscator, SmartAssembly, Babel, Obfuscar).

### String algorithms (where documented)

| Algorithm | Obfy | Dotfuscator | .NET Reactor | Babel | Obfuscar |
|-----------|:----:|:-----------:|:------------:|:-----:|:--------:|
| **Named AES-256** | Yes | — | — | Code encryption (not strings) | — |
| **XOR** | Yes | — | undocumented | Documented (random integer key) + HASH table | Yes (hide only) |
| **Vendor-named / unspecified** | — | Yes (7.8 “AI-resistant”) | Yes (algorithm not named) | Plugins | — |

Most commercial pages say “string encryption” without naming a cipher. Do not treat the old AES-256 checkmarks for Dotfuscator / Reactor / Babel *strings* as vendor-documented. Babel *does* cite AES for **code** encryption (the managed VM), and documents XOR + HASH for strings. Dotfuscator 7.8.0 (Jul 2026) added “AI-Resistant String Encryption” — a vendor name, not an evaluated property.

### Control flow

| Mode | Obfy | Dotfuscator | SmartAssembly | .NET Reactor | Babel |
|------|:----:|:-----------:|:-------------:|:------------:|:-----:|
| **Switch flattening / state machine** | Yes | Yes | Yes | Yes | Yes |
| **Opaque predicates** | Yes | Yes | Yes | Yes | Yes |
| **Combined** | Yes | Yes | Yes | Yes | Yes |
| **Intensity / iterations** | Yes | Yes | — | Yes | Yes (per-algorithm iterations) |

Babel documents extra algorithms (`goto`, `if`, `switch`, `case`, `call`, `value`, `token`, `underflow`). `token` / `underflow` are not verifiable and are auto-disabled on modern .NET.

---

## Platform and framework support

| Platform | Obfy | Dotfuscator | SmartAssembly | .NET Reactor | Babel | Eazfuscator | ArmDot |
|----------|:----:|:-----------:|:-------------:|:------------:|:-----:|:-----------:|:------:|
| **.NET 11 (preview)** | — | — | Yes (8.4.9) | — | — | Preliminary (2026.2) | — |
| **.NET 10** | Yes | Yes (7.5.0) | Yes (8.4.0) | Yes | Yes (11.5+) | Yes | Yes (2025.9) |
| **.NET 8 / 9** | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| **.NET 6 / 7** | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| **.NET Core 3.x** | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| **.NET Framework 4.x** | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| **.NET Standard** | Yes | Yes | Yes | Yes | Yes | Yes | — |
| **MAUI** | Recipe | Yes (Android, iOS, WinUI 3, Mac, Tizen) | — | Timing note only | Yes (no code encryption) | Yes | — |
| **Blazor** | Recipe | Yes (7.8.0) | — | Yes | Yes (no code encryption) | Improved | — |
| **Unity** | Recipe | — | — | Yes | — | Yes | Yes (name preservation 2026.6) |
| **Xamarin** | — | Yes | — | Yes | Yes | Yes | Yes |
| **NativeAOT** | Recipe + gating | Preview notes (7.6.0) | — | — | — | — | debug-info fix for AOT/trim |
| **Tool runs on Linux / macOS** | Partial | CLI yes, GUI Windows | Windows only | Windows only | Ultimate yes | Yes (Linux 2026.1, macOS 2026.2) | Yes |

Obfy Unity / Blazor / MAUI / NativeAOT support is `runtimeProfile` gating plus example `obfy.json` files and scenario tests — **not** an Editor plugin, NuGet SDK, or first-class SKU. Do not claim first-class Unity/MAUI/Blazor until a Unity Development Player job and MAUI iOS/Android jobs exist (see [Roadmap.md](Roadmap.md) and [Testing-Roadmap.md](Testing-Roadmap.md)).

Obfy Linux/macOS is **Partial**: the CLI is a `net10.0` global tool and `Obfy.Console.Tests` runs on Ubuntu every PR (`linux-cli`). GUI, VS, method-IL encryption, and the native packer are Windows. Do not advertise a Linux GUI or a non-Windows packer.

SmartAssembly does not support UWP. Its “What Can I Obfuscate?” index still listed .NET 5–9 after 8.4.0’s release notes added .NET 10. .NET 8 support arrived a full year after .NET 8 shipped (Nov 2024); .NET 9 and 10 followed much faster.

---

## Integration and tooling

| Feature | Obfy | Dotfuscator | SmartAssembly | .NET Reactor | Babel | Eazfuscator | ArmDot | Obfuscar | BitMono |
|---------|:----:|:-----------:|:-------------:|:------------:|:-----:|:-----------:|:------:|:--------:|:-------:|
| **CLI** | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| **GUI** | Yes (WPF) | Yes | Yes | Yes | Windows | Yes | Yes | — | Web (bitmono.dev) |
| **MSBuild** | Partial | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| **Visual Studio** | Yes (2022) | Yes | Yes | Yes | Yes | Yes (2005–2022) | — | — | — |
| **Rider** | Yes | — | — | Yes | — | Yes (2019.1+) | — | — | — |
| **VS Code** | Stub (schema + matcher) | — | — | — | — | — | — | — | — |
| **NuGet / `dotnet tool`** | Yes (`Obfy`) | Private pkg | Yes | Some pkgs | Ultimate | Yes (smoothest) | Yes | Yes | Yes |
| **GitHub Actions** | Yes (dotnet) | Yes | — | Official action | Yes | Yes | Yes | Yes | Official action |
| **Azure DevOps** | Partial | Yes | Yes | Official task | Yes | Yes | Yes | Yes | Yes |
| **Solution / closed-set** | Yes | — | — | — | — | — | — | — | — |
| **Mapping file** | Yes | Yes | Yes | Yes | Yes | Encrypted symbols | — | Partial | — |
| **Source-level (Roslyn)** | Yes | — | — | — | — | — | — | — | — |
| **Config wizard** | Yes | — | — | — | — | Attribute-first | Attributes | XML | JSON |

Obfy ships as `dotnet tool install --global Obfy` (framework-dependent, .NET 10). **MSBuild** and **Azure DevOps** are **Partial**: documented `Exec` / script snippets, not an in-graph PackageReference or an official pipeline task. Teams that want “add a package, Release builds are protected” still pick Eazfuscator, ArmDot, or BitMono.Integration.

**Solution / closed-set:** drop or pass a `.sln` / `.csproj`, or two or more assemblies, and Obfy renames in-set public APIs together (library-mode apps stay skip/keep via settings). Commercial tools usually get a similar effect via merge/link or a vendor project file; this row is Obfy’s first-class CLI/UI path, not a claim that others cannot take more than one assembly. Closed-set / solution runs pack each entry-point output.

Reactor’s **$249 single-developer license excludes build servers**. CI needs the $549 company license.

---

## Enterprise and ISV extras

| Feature | Obfy | Dotfuscator | SmartAssembly | .NET Reactor | Babel | ArmDot | DNGuard |
|---------|:----:|:-----------:|:-------------:|:------------:|:-----:|:------:|:-------:|
| **Error reporting / crash analytics** | — | — | Yes (differentiator) | — | — | — | — |
| **License / trial / HWID** | — | Shelf Life | — | Yes | Separate SKU | Yes | Yes (Enterprise) |
| **DLL merging** | Yes | Linking | Yes | Yes | Enterprise+ | — | — |
| **Solution / closed-set rename** | Yes | — | — | — | — | — | — |
| **Assembly embedding** | Yes | — | Yes | Yes | Yes | Yes (managed + unmanaged) | — |
| **RASP** | — | Yes | Tamper (Pro) | — | — | — | HVM runtime |
| **Incremental obfuscation** | Yes (`{output}.obfycache`) | Yes | — | Yes | — | — | — |
| **Strong-name re-sign** | Yes | Yes | Yes | Yes | Yes | — | — |

---

## Detailed tool analysis

### Obfy

**GitHub:** [mortenbrudvik/Obfy](https://github.com/mortenbrudvik/Obfy) (1 star, 0 forks, MIT, created 31 Dec 2025)

**Strengths**

- Free, MIT, actively developed
- Dual pipeline: assembly (dnlib) and source (Roslyn) — unique in this set
- Full conventional stack: rename, AES-256/XOR strings, constants, resources, control flow, reference proxies, metadata removal
- Anti-debug (scattered, kernel32 gated off NativeAOT/IL2CPP/Blazor WASM), anti-dump (Windows x86/x64), anti-tamper (SHA-256), anti-decompiler (junk + `SuppressIldasm` + decoy attributes)
- Method IL XOR (Windows), limited static-int virtualization, dependency embedding, assembly merge, closed-set / solution rename, watermark, HTML/JSON reports
- Native win-x64 FDD packing stub (`packing.enabled`; `portable` keeps the managed launcher). Closed-set / solution runs pack each entry-point output
- `runtimeProfile` gating so NativeAOT / Unity IL2CPP / Blazor WASM do not get `VirtualProtect` / AssemblyResolve helpers they cannot run
- Incremental cache (SHA-256 of Obfy version + input bytes + settings)
- Fluent WPF UI, VS 2022 extension, Rider plugin, CLI wizard, Microsoft Store listing, MSIX, and Setup.exe
- Honest docs: encryption is obfuscation, not confidentiality

**Weaknesses**

- No general IL VM; no licensing/DRM/RASP. Encoder/runtime exist on `main`; the shipping pass is still the int-only interpreter ([PR #23](https://github.com/mortenbrudvik/Obfy/pull/23) open)
- Unity / MAUI / Blazor / NativeAOT are recipes, not first-class plugins
- No MSBuild PackageReference — CI uses `dotnet tool install` / `Exec`, not a build task that cannot be skipped
- GUI, VS extension, method-IL encryption, and native packing are Windows-centric. CLI is CI-tested on Ubuntu
- Tiny public community (1 star, 0 forks). Unproven on third-party commercial codebases
- Source mode is a subset: strings, renaming, control flow only

**Best for:** Modern .NET teams that want a free, documented, IDE-friendly obfuscator with more than renaming, and that accept “raise the cost of casual ILSpy/dnSpy” rather than “defeat a motivated reverse engineer.”

---

### Dotfuscator (PreEmptive)

**Strengths**

- Longest commercial history; Community Edition ships with Visual Studio
- Overload Induction renaming
- RASP: configurable anti-debug, anti-tamper, Shelf Life, Android root check, custom/probabilistic responses
- CLI runs on Windows / Mac / Linux; .NET 10 (7.5.0), AOT notes (7.6.0), Blazor (7.8.0)
- Mapping files, incremental obfuscation, watermarking, XAML renaming (WPF/UWP/Xamarin)

**Weaknesses**

- Quote-only Professional pricing; NDepend left after repeated ~80% yearly hikes
- **No code virtualization** despite being the expensive enterprise option
- Community Edition is personal-use, rename-centric, no string encryption, no MSBuild
- No built-in licensing beyond Shelf Life expiry

**Best for:** Regulated enterprise, existing PreEmptive contracts, teams that need runtime detection/response more than a VM.

---

### SmartAssembly (Redgate)

**Strengths**

- Automated error reporting and feature-usage reporting (the real differentiator)
- Rename, control flow, strings, reference proxies, prune, merge, embed, resource compression/encryption, tamper (Pro)
- Mapping files; MSBuild / Azure DevOps
- .NET 10 (Nov 2025) and .NET 11 preview (Aug 2026); SQL Server 2025 for the reporting backend

**Weaknesses**

- No virtualization, no anti-debug, no licensing API
- Windows-only tool
- Subscription TCO over 5 years dwarfs Reactor / Eazfuscator / ArmDot
- .NET 8 was a year late; MAUI/Blazor/Unity are not a documented strength
- No UWP

**Best for:** Desktop/.NET teams that already live in Redgate tooling and want crash reporting plus solid (not extreme) obfuscation.

---

### .NET Reactor (Eziriz)

NDepend’s 2026 pick after testing Dotfuscator, Eazfuscator, Babel, and Obfuscar.

**Strengths**

- Broadest protection-per-dollar: NecroBit, VM, native EXE stub, Pre-JIT of small methods to x86, anti-debug/tamper/decompiler, hide calls, merge/embed, compression, licensing SDK
- Documents .NET 5–10, Xamarin, Unity, Blazor. MAUI appears as a trimming / “Protection Timing = After Compile” note, not as a first-class row on the main frameworks list
- VS + Rider add-ins, Azure DevOps task, GitHub Action
- Mapping files, declarative `[Obfuscation]`, strong-name + Authenticode
- $249 perpetual (single) / $549 company; 20+ years of releases

**Weaknesses**

- Tool itself is **Windows-only** (CLI and GUI)
- Single-developer license **cannot** run on build servers
- No source-level obfuscation
- “No tool can decompile protected code” is marketing; de4dot-class tools and dedicated unpackers exist for many Reactor versions

**Best for:** Windows-centric ISVs who want virtualization + licensing without enterprise quotes. The default commercial recommendation in independent write-ups.

---

### Babel Obfuscator

**Strengths**

- Managed virtualization and AES code encryption without a native stub (Ultimate)
- Cross-platform **tool** on Ultimate (Windows/macOS/Linux NuGet)
- Broad target list: MAUI, Blazor, Xamarin, UWP, nanoFramework, Mono (Unity is **not** on Babel’s own general-features page)
- Mapping files, merge, anti-tamper, dynamic proxy, value encryption, plugin encryptors
- 11.7.0 (May 2026): AI-friendly CLI (`--format=json|ndjson`), .NET 10 SDK, `--strict-exit`
- One-time €350 Enterprise is cheap if you do not need the VM/NuGet tier

**Weaknesses**

- Code encryption **unsupported on MAUI and Blazor**
- NDepend never got a bug-free run on their assemblies
- Enterprise vs Ultimate split: the cheap SKU is Windows-only and weaker
- Smaller Western mindshare than Reactor / Eazfuscator

**Best for:** Cross-platform CI that must run obfuscation on Linux runners, and teams that want a managed VM without native dependencies.

---

### Eazfuscator.NET (Gapotchenko)

**Strengths**

- NuGet-first: add the package, Release builds are protected
- Code/data virtualization with a new VM per run; resource encryption; merge/embed; XAML renaming
- .NET 5–11 (11 is preliminary), Unity / MonoGame / XNA, MAUI, Blazor, Linux (2026.1) and macOS (2026.2) hosts. Assembly *embedding* is documented as incompatible with Native AOT; “.NET Native” on the feature page is the old UWP toolchain, not `PublishAot`
- Rider 2026.2, VS 2026, SLNX, deterministic obfuscation (Site License)
- $399 perpetual is the “install and forget” commercial default

**Weaknesses**

- **No mapping files** — encrypted symbols instead. NDepend ruled it out. Symbol size leaks original name length; a 2019 break of the scheme exists
- No dedicated anti-debug / anti-tamper / licensing
- Single-developer license caps at 16 CPU cores
- “Homomorphic encryption” is a vendor claim, not an independently reviewed primitive

**Best for:** Small teams that want virtualization with almost no build-graph work, and that can live without mapping files (or that use Eazfuscator’s own stack-trace decoder).

---

### ArmDot (Softanics)

**Strengths**

- Tool runs on Windows, Linux, and macOS; NuGet + MSBuild attributes
- Per-build unique VM; string/resource encryption; control flow
- Built-in licensing (HWID, RSA serials, trials) virtualized with the same VM
- Windows application virtualization (BoxedApp) can embed unmanaged DLLs
- Public $499 price; .NET 10 since 2025.9; Unity name-preservation fixes in 2026.6
- Ships source-code license as a third SKU

**Weaknesses**

- Vendor also writes the most-cited 2026 “honest comparison” — discount that roundup’s ranking
- Anti-tamper experimental; no dedicated anti-debug
- Smaller public track record than Reactor / Dotfuscator
- VM cost is real (Softanics cites ~10–20% on virtualized methods — typical for the category)

**Best for:** ISVs who need Linux CI + virtualization + licensing in one purchase, and who do not want Eziriz’s Windows-only tool constraint.

---

### Agile.NET (SecureTeam)

**Strengths**

- Method virtualization (custom VM opcodes) and per-method IL encryption
- Rename, CF, strings, resources, call obfuscation
- Historical niche: NinjaTrader / algo-trading shops

**Weaknesses**

- Windows-focused; $795 + $195/yr or $150/month enterprise is poor value next to Reactor $249
- A 2026 generic **devirtualizer** exists ([dawwinci/agile-net-devirtualizer](https://github.com/dawwinci/agile-net-devirtualizer)) that reconstructs CIL from the shipped `AgileDotNet.VMRuntime.dll`
- Thin independent review footprint (PreEmptive notes no G2/Capterra presence)

**Best for:** Existing SecureTeam customers. Not a first pick for new projects.

---

### DNGuard HVM

**Strengths**

- HVM: IL → encrypted pseudocode handed to the JIT one method at a time; claims dump resistance
- .NET 10 (4.9.4, Nov 2025); licensing SDK on Enterprise
- Positions itself as “not an obfuscator” so reflection-heavy apps break less

**Weaknesses**

- $1,299 Enterprise is 5× Reactor for a narrower ecosystem
- Windows-centric; little public .NET 5+ war stories compared with Reactor/Eazfuscator
- Professional SKU ($899) omits HVM — the feature you would be paying for

**Best for:** Teams that specifically want JIT-hook IL hiding and can pay enterprise prices.

---

### ConfuserEx and forks (open source, Framework-era)

**Upstream** [yck1509/ConfuserEx](https://github.com/yck1509/ConfuserEx): archived January 2019, 3,765 stars. Last meaningful code 2018. Supports Framework 2.0–4.5. Dedicated unpackers (NoFuserEx, de4dot-cex, KoiVM lifters) exist.

**mkaring/ConfuserEx**: 2,904 stars. Default-branch HEAD is 15 April 2022; latest tag v1.6.0 (17 January 2022). GitHub `pushed_at` is 7 June 2024 (may be a non-`master` branch). Best-known “still builds” fork. Still not a .NET 8/10 obfuscator.

**neo-ConfuserEx**: 861 stars, `v1.0.0-rc2` on 28 July 2026 (signed integrity + selective KoiVM). README still Framework 2.0–4.7.2. A 2026 revival of the ConfuserEx/KoiVM line, not a modern-.NET successor. yck1509’s 1 July 2016 discontinue post pointed users at Eazfuscator, ILProtector, .NETGuard, and SmartAssembly — there is **no official ConfuserEx commercial successor**.

**Do not use any ConfuserEx lineage on new .NET 8/10 projects.** Fine as a historical reference or for locked Framework 4.x binaries with a modest threat model.

---

## Open-source deep dive

### Open-source feature comparison

| Feature | Obfy | Obfuscar | BitMono | JIEJIE.NET | ConfuserEx (hist.) | LoGiC.NET |
|---------|:----:|:--------:|:-------:|:----------:|:------------------:|:---------:|
| **Symbol renaming** | Yes | Yes | Yes | Yes | Yes | Yes |
| **String encryption** | Yes | XOR hide | Yes | Yes | Yes | Yes |
| **Control flow** | Yes | — | — | Yes | Yes | Yes |
| **Anti-debug** | Yes | — | Yes | — | Yes | — |
| **Anti-decompiler** | Yes | SuppressIldasm | Yes (PE tricks) | — | Yes | — |
| **Anti-tamper** | Yes | — | — | — | Yes | — |
| **Anti-dump** | Yes | — | — | — | Yes | — |
| **Constant encryption** | Yes | — | — | — | Yes | Yes |
| **Resource encryption** | Yes | — | — | Yes | Yes | — |
| **Assembly merging** | Yes | — | — | — | Embed | — |
| **Solution / closed-set** | Yes | — | — | — | — | — |
| **Metadata removal** | Yes | — | Yes | — | — | — |
| **Watermark** | Yes | — | Optional | — | — | — |
| **Method IL encryption** | Yes | — | — | — | Tamper/encrypt | — |
| **Unity plugin** | Recipe | Community configs | UPM / unitypackage | — | — | — |
| **Source (Roslyn) mode** | Yes | — | — | — | — | — |
| **dotnet tool / NuGet** | Yes | Yes | Yes | — | — | — |

### .NET version support (open source)

| Platform | Obfy | Obfuscar | BitMono | JIEJIE.NET | ConfuserEx / forks | LoGiC.NET |
|----------|:----:|:--------:|:-------:|:----------:|:------------------:|:---------:|
| **.NET 10** | Yes | Yes (2.2.49+) | Yes | — | — | — |
| **.NET 8 / 9** | Yes | Yes | Yes | Yes | — | Claimed historically |
| **.NET 6 / 7** | Yes | Yes | Yes | Yes | — | Claimed historically |
| **.NET Framework** | Yes | Yes | Yes (some protections) | Yes | Yes (this is the target) | Yes |

---

### Obfuscar

**GitHub:** [obfuscar/obfuscar](https://github.com/obfuscar/obfuscar) — 3,188 stars, 469 forks, last push 14 Sep 2026

The default open-source *name*. Maintained by Lex Li / LeXtudio. Combined NuGet downloads are in the millions (NDepend cited 760k on the main package in 2026; Softanics cited 2.6M+ across packages).

**What it actually does:** massive overload renaming, optional XOR string hiding, `SuppressIldasm`, skip/keep rules, mapping log, strong-name re-sign, global tool + NuGet. v3.0 beta (latest `3.0.0-beta.20`, 29 Aug 2026) replaces Mono.Cecil with `System.Reflection.Metadata`.

**What it does not do:** control flow, virtualization, anti-debug, anti-tamper, useful string cryptography.

NDepend could not get bug-free output (TypeLoadException, enum-value holes). Lex Li has publicly called out design flaws. v3 is the attempt to fix the pipeline; it does not add protection techniques.

```bash
dotnet tool install --global Obfuscar.GlobalTool
# v3:
dotnet tool install --global Obfuscar.GlobalTool --prerelease
```

**Best for:** Open-source projects and libraries that only need public-API-preserving renaming, and that want a `dotnet tool` on Linux CI.

---

### BitMono

**GitHub:** [bitmono-project/BitMono](https://github.com/bitmono-project/BitMono) — 559 stars, last push 13 Sep 2026

The most *aggressive* maintained OSS protector. AsmResolver (not dnlib). Started for Mono; now documents .NET 6–10 plus Framework, with per-protection platform caveats.

**Protections:** StringsEncryption, UnmanagedString, BitDotNet / BitDecompiler / BitMethodDotnet (break dnSpy/ILSpy/dnlib/AsmResolver loaders), CallToCalli, FullRenamer, NoNamespaces, AntiDebugBreakpoints, AntiDecompiler, AntiDe4dot, AntiILdasm, BillionNops, DotNetHook, ObjectReturnType, LocalVariableEncoding, BitTimeDateStamp, plus a plugins folder.

**Distribution that Obfy lacks:** `BitMono.Integration` MSBuild package, official GitHub Action, Unity `.unitypackage` (2018–19) and UPM `.tgz` (2020+), in-browser obfuscator at [bitmono.dev](https://bitmono.dev). Obfy already ships `dotnet tool install --global Obfy`.

**Gaps vs Obfy:** no control-flow flattening, no constant/resource encryption as first-class peers, no Roslyn source mode, no WPF/VS/Rider product UX. Some protections are Mono/Unity-specific and will no-op or warn elsewhere.

**Best for:** Unity/Mono, people who want decompilers to *crash*, and teams that want a plugin engine. Pair with Obfy’s CF/constants only if you are willing to run two tools — they are not designed as a pipeline.

---

### JIEJIE.NET

**GitHub:** [dcsoft-yyf/JIEJIE.NET](https://github.com/dcsoft-yyf/JIEJIE.NET) — 887 stars, **GPL-2.0**, last meaningful update 1 Jan 2026 (resource-encryption fix)

IL-analysing obfuscator (rename, strings, control flow, `StringsSelector`). Fast, small. Docs and community are Chinese-first. GPL-2.0 is a problem if you want to vendor the engine; MIT Obfy/Obfuscar/BitMono are not.

**Best for:** Users who read Chinese docs and can accept GPL. Not a default Western OSS pick.

---

### LoGiC.NET — archived

Last push 23 August 2023, repository archived, 516 stars. Rename, strings, CF, integer confusion. **Do not recommend for new work.** Historical “more than Obfuscar, less than ConfuserEx” option only.

---

### Open-source comparison summary

| Criteria | Winner | Notes |
|----------|--------|-------|
| **Most conventional techniques** | Obfy | Rename + strings + constants + resources + CF + anti-* + merge + closed-set + method-IL |
| **Most decompiler-breaking tricks** | BitMono | PE/loader attacks, UnmanagedString, Unity packs |
| **Most used / most stable rename** | Obfuscar | 3.2k stars, millions of NuGet installs, v3 rewrite |
| **Best Unity story (OSS)** | BitMono | UPM + unitypackage. Obfy is a recipe. |
| **Best IDE / desktop UX (OSS)** | Obfy | WPF + VS 2022 + Rider + Store. BitMono is CLI/web. |
| **Best `dotnet tool` / NuGet** | Obfuscar / BitMono / Obfy | Obfy is `dotnet tool install -g Obfy`; Obfuscar has more installs |
| **Best docs for beginners** | Obfuscar (community) and Obfy (product docs) | ConfuserEx wiki is stale |
| **Do not use on new .NET 10** | ConfuserEx lineage, LoGiC.NET | Framework-era or archived |

### When to choose which open-source tool

| Scenario | Recommended | Why |
|----------|-------------|-----|
| **Modern .NET, more than renaming, free** | Obfy | Broadest conventional stack + closed-set + UI/IDE |
| **Rename only, Linux CI, maximum installs** | Obfuscar | Global tool, v3, huge community |
| **Unity / break dnSpy** | BitMono | UPM + anti-decompiler protections |
| **Source files, not assemblies** | Obfy | Only Roslyn source mode in this set |
| **Vendor the engine in a proprietary product** | Obfy or Obfuscar (MIT) | JIEJIE is GPL-2.0 |
| **Legacy Framework 4.x, already ConfuserEx** | mkaring/ConfuserEx | Still builds; not for new .NET |

---

## Pricing comparison (5-year TCO, single developer)

| Tool | Model | Year 1 | Years 2–5 (stay current) | 5-year TCO |
|------|-------|--------|--------------------------|------------|
| **Obfy** | MIT | $0 | $0 | **$0** |
| **Obfuscar / BitMono** | MIT | $0 | $0 | **$0** |
| **.NET Reactor** | Perpetual | $249 | $99/yr optional | **~$249–$645** |
| **Eazfuscator.NET** | Perpetual | $399 | $99/yr optional | **~$399–$795** |
| **Babel Enterprise** | Perpetual | €350 (~$380) | €150/yr optional | **~$380–$1,000** |
| **ArmDot** | Perpetual | $499 | ~$250/yr optional | **~$499–$1,500** |
| **Agile.NET** | Perpetual + maint. | $795 | $195/yr | **~$1,575** |
| **Babel Ultimate** | Perpetual | €1,250 (~$1,350) | €550/yr optional | **~$1,350–$3,500** |
| **DNGuard Enterprise** | Perpetual | $1,299 | maintenance extra | **~$1,299+** |
| **SmartAssembly** | Subscription | ~$700–$1,200 | same each year | **~$3,500–$6,000** |
| **Dotfuscator Pro** | Subscription, quote | unknown | unknown | **historically $10k+**; get a quote |
| **ConfuserEx** | MIT | $0 | $0 | $0 — **do not use on modern .NET** |

Reactor company license is $549 (needed for CI). Eazfuscator site license is $1,699. ArmDot site is $1,799. Perpetual tools keep working forever on the last version you downloaded; you pay renewals only for new .NET years and support.

---

## Gap analysis: what Obfy is missing

### High value, high cost

| Feature | Difficulty | Value | Who has it | Notes |
|---------|------------|-------|------------|-------|
| **General code virtualization** | Very high | High | Reactor, Babel Ultimate, Eazfuscator, ArmDot, Agile, DNGuard | Shipping interpreter is static `int` methods only. `VmEncoder` / `Obfy.VmRuntime` exist on `main`; [PR #23](https://github.com/mortenbrudvik/Obfy/pull/23) wires them. Until that merges, this is still the protection-ceiling gap. |
| **MSBuild PackageReference** | Medium | High | Eazfuscator, ArmDot, Babel Ultimate, BitMono.Integration | Tool install is restored; an in-build task is still a separate product. |

### Medium value

| Feature | Difficulty | Value | Who has it | Notes |
|---------|------------|-------|------------|-------|
| **Pre-JIT / other RIDs / self-contained packing** | Very high | Medium | Reactor (native EXE + Pre-JIT), ArmDot (BoxedApp), DNGuard | PF-09 v1 is a win-x64 FDD CLR-host stub. Remaining native-code generation stays Future. |
| **First-class Unity Editor / UPM** | Medium | Medium | BitMono (UPM), Eazfuscator, Reactor, ArmDot | Recipe + `UnityIl2Cpp` profile exist. |
| **Linux/macOS as a supported host** | Medium | Medium | Obfuscar, BitMono, ArmDot, Babel Ultimate, Eazfuscator 2026, Dotfuscator CLI | CLI is CI-tested on Ubuntu. GUI, VS, method-IL, and the native packer are Windows. Remaining gap is a first-class advertised Linux host, not the job. |
| **XAML-aware renaming (WPF/MAUI)** | Medium | Medium | Dotfuscator, Eazfuscator | Obfy has `preserveXaml` defaults, not a XAML rewriter. |

### Low priority / out of scope

| Feature | Difficulty | Value | Notes |
|---------|------------|-------|-------|
| **License management / HWID** | High | Low for an OSS obfuscator | Reactor, ArmDot, Babel Licensing, DNGuard. Separate product. |
| **Error reporting** | High | Low | SmartAssembly’s reason to exist. Use Sentry/AppCenter. |
| **RASP** | High | Medium for mobile/games | Dotfuscator’s enterprise wedge. |

---

## Recommendations for Obfy development

Aligned with [Roadmap.md](Roadmap.md). Competitive pressure, not a commitment.

### Near term (adoption, not protection ceiling)

1. **MSBuild PackageReference** — `dotnet tool install` covers CI; an in-graph obfuscate task (Eazfuscator-style) is still missing.
2. **Keep Linux CLI honest** — Ubuntu `linux-cli` already runs `Obfy.Console.Tests`. Do not claim GUI, VS, method-IL, or native packing on Linux/macOS.
3. **VS Code beyond the stub** (TaskProvider + marketplace), already on the roadmap as DX-03.

### Medium term (platform)

4. Unity Editor / UPM, or at least a tested Development Player job, before claiming Unity.
5. MAUI iOS/Android tests before claiming MAUI.
6. Stronger XAML renaming if WPF/MAUI users hit binding breaks.

### Long term (protection ceiling)

7. **General IL virtualization** — encoder/runtime are on `main`; [PR #23](https://github.com/mortenbrudvik/Obfy/pull/23) switches `VirtualizationObfuscator` off `CreateExecute`. Do not mark the matrix Yes until that ships. Skip set (EH, generics, byref, …) stays. Agile’s public devirtualizer is still the caution: a weak VM is worse than none.
8. **Pre-JIT / other RIDs / self-contained packing** — PF-09 v1 (native FDD stub) is done. Remaining native-code generation stays Future. Do not start until demand is clear.

Do not chase licensing, crash reporting, or RASP. Those are adjacent products (Reactor/ArmDot, SmartAssembly, Dotfuscator).

---

## When to choose each tool

| Scenario | Recommended |
|----------|-------------|
| **Free, modern .NET, more than renaming** | Obfy |
| **Free rename-only, Linux CI, huge community** | Obfuscar |
| **Free, Unity, break decompilers** | BitMono |
| **Free, merge / closed-set + UI** | Obfy |
| **Maximum protection per dollar** | .NET Reactor ($249 / $549 company) |
| **Linux CI + VM + licensing, public price** | ArmDot ($499) |
| **NuGet-first + VM, no mapping-file need** | Eazfuscator.NET ($399) |
| **Linux CI + managed VM, no native stub** | Babel Ultimate (€1,250) |
| **Crash reporting + obfuscation** | SmartAssembly |
| **Enterprise RASP / compliance / VS-blessed** | Dotfuscator Professional (quote) |
| **Unity commercially supported** | Eazfuscator, Reactor, ArmDot; BitMono (OSS) |
| **Do not use** | ConfuserEx on .NET 5+; LoGiC.NET; Crypto Obfuscator v2020; ILProtector for new work |

---

## Conclusion

The 2026 .NET obfuscator market has three honest tiers:

1. **Rename-grade (free):** Obfuscar. Fine for libraries and OSS. Not protection against a person with ILSpy and an afternoon.
2. **Conventional protection (free or cheap):** Obfy (free), BitMono (free, decompiler-hostile), Eazfuscator ($399), Babel Enterprise (€350). Rename + strings + CF + some anti-*. This is what most applications actually need.
3. **Virtualization / packing / licensing (paid):** .NET Reactor is the independent-review default. ArmDot if the tool must run on Linux. Babel Ultimate for a managed VM on Linux CI. Dotfuscator if you buy RASP and a vendor that Microsoft already ships. SmartAssembly if you buy crash reporting.

Obfy now ships a native win-x64 FDD packing stub (closed-set entry points included). Code virtualization stays Partial until the shipping pass leaves `CreateExecute` ([PR #23](https://github.com/mortenbrudvik/Obfy/pull/23) is the wiring PR). It is a managed-VM + native-stub tier-3 tool on CoreCLR/Windows **only when both cells are Yes**. Today packing is Yes and virtualization is Partial. Still not licensing/RASP. Do not write that it covers Reactor.

**Use Obfy** when you want MIT-licensed, documented, modern-.NET obfuscation with a UI and IDE plugins, and your threat model is casual reverse engineering.

**Do not use Obfy (alone)** when you need a general VM, Pre-JIT or self-contained packing, ISV licensing, RASP, or a Unity Editor workflow — buy Reactor/ArmDot/Eazfuscator or add BitMono for Unity.

---

## Sources

GitHub statistics rechecked 15 September 2026. Vendor pages and shop prices last sampled 13 September 2026 unless a page carries its own date.

### Independent / mixed

- [NDepend: In the Jungle of .NET Obfuscator Tools](https://blog.ndepend.com/in-the-jungle-of-net-obfuscator-tools/) (25 May 2026) — only recent third-party hands-on of Dotfuscator, Eazfuscator, Babel, Obfuscar, Reactor
- [Softanics: Best Free and Paid .NET Obfuscators Compared in 2026](https://www.softanics.com/net-obfuscation/tools) — useful table; **written by ArmDot’s vendor**
- [PreEmptive: 8 .NET Obfuscators Compared for 2026](https://www.preemptive.com/blog/net-obfuscator-2/) (6 Aug 2026) — useful roundup; **written by Dotfuscator’s vendor**
- [Silent Signal: Decrypting Eazfuscator.NET encrypted symbol names](https://blog.silentsignal.eu/2019/05/10/decrypting-eazfuscator-net-encrypted-symbol-names/) (2019)

### Commercial vendors

- [Dotfuscator](https://www.preemptive.com/products/dotfuscator/) · [Compare editions](https://www.preemptive.com/products/dotfuscator/compare-dotfuscator-editions/) · [Changelog (7.5–7.8)](https://support.preemptive.com/hc/en-us/articles/31784634997137-Changelog) · [.NET 10 post](https://www.preemptive.com/blog/dotfuscator-net-10-support-protecting-your-modern-applications-with-speed/)
- [SmartAssembly](https://www.red-gate.com/products/smartassembly/) · [Requirements (.NET 10)](https://documentation.red-gate.com/sa8/getting-started/requirements) · [8.4 release notes](https://documentation.red-gate.com/sa/release-notes-and-other-versions/smartassembly-8-4-release-notes) · [ComponentSource prices](https://www.componentsource.com/product/smartassembly/prices)
- [.NET Reactor](https://www.eziriz.com/dotnet_reactor.htm) · [Features](https://www.eziriz.com/reactor_features.htm) · [Store](https://eziriz.com/order.htm) · [Renewals](https://www.eziriz.com/renewal.htm) · [Downloads](https://www.eziriz.com/downloads.htm)
- [Babel Obfuscator](https://babelfor.net/products/babel-obfuscator/) · [Shop](https://babelfor.net/shop/) · [11.7.0 notes](https://babelfor.net/releasenotes/babel-1170/) · [Code encryption limits](https://docs.babelfor.net/obfuscator/code-encryption) · [General features](https://docs.babelfor.net/obfuscator/introduction/general-features)
- [Eazfuscator.NET features](https://www.gapotchenko.com/eazfuscator.net/features) · [Purchase](https://www.gapotchenko.com/eazfuscator.net/purchase) · [What’s new (2026.1/2026.2)](https://www.gapotchenko.com/eazfuscator.net/changes)
- [ArmDot](https://www.softanics.com/armdot) · [Buy](https://www.softanics.com/armdot/buy) · [Changelog](https://www.softanics.com/armdot/changelog)
- [Agile.NET pricing](https://secureteam.net/acode-pricing)
- [DNGuard purchase](https://dnguard.net/purchase.php) · [Changelog](https://dnguard.net/changelog.php)
- [ILProtector](https://www.vgrsoft.net/Products/ILProtector) — Framework-only
- [Crypto Obfuscator order page](https://www.ssware.com/cryptoobfuscator/order.htm) — last product line 2020
- [Spices.Net 5.26.2.17](https://www.softpedia.com/progChangelog/Spices-Obfuscator-Changelog-45113.html) (17 Feb 2026)

### Open source

- [Obfuscar](https://github.com/obfuscar/obfuscar) · [Docs](https://docs.lextudio.com/obfuscar/) · [NuGet 3.0.0-beta.20](https://www.nuget.org/packages/Obfuscar)
- [BitMono](https://github.com/bitmono-project/BitMono) · [docs.bitmono.dev](https://docs.bitmono.dev/en/latest/)
- [JIEJIE.NET](https://github.com/dcsoft-yyf/JIEJIE.NET)
- [ConfuserEx (archived)](https://github.com/yck1509/ConfuserEx)
- [mkaring/ConfuserEx](https://github.com/mkaring/ConfuserEx)
- [neo-ConfuserEx](https://github.com/XenocodeRCE/neo-ConfuserEx)
- [LoGiC.NET (archived)](https://github.com/AnErrupTion/LoGiC.NET)
- [Obfy](https://github.com/mortenbrudvik/Obfy)
- [NotPrab/.NET-Obfuscator list](https://github.com/NotPrab/.NET-Obfuscator)

### Obfy product docs (this repo)

- [Techniques.md](Techniques.md) — what each Obfy pass actually does
- [Roadmap.md](Roadmap.md) — shipped vs remaining VM / Pre-JIT packing / platform tests
- [Platforms.md](Platforms.md) — runtime profiles

---

*Last updated: 15 September 2026. GitHub statistics sampled the same day. Shop prices last sampled 13 September 2026.*

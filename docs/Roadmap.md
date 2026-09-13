# Obfy Product Roadmap & Backlog

A strategic development plan. Protection hardening that was scoped as v1.4–v1.5 (helpers, anti-debug scatter, per-method XOR keys, `[Obfuscation]`, JSON/XAML defaults, runtime-profile gating) is **on main** (Unreleased relative to the 1.3.0 tag). Remaining work is platform depth, a real native packer, and whether to grow the limited IL interpreter into a general VM.

---

## Vision

**Make Obfy the go-to open-source .NET obfuscator** by closing the feature gap with commercial tools while remaining simple, modern-.NET-first, and safe to turn on.

**Target:** Cover typical obfuscation needs with protection that survives casual decompilation (ILSpy / dnSpy / de4dot-class tools), without claiming confidentiality or virtualization-grade resistance.

---

## Current position

Assembly pipeline (priority order):

| Priority | Technique | Status |
|----------|-----------|--------|
| 8 | Dependency embedding (`Obfy.Embedded.*` + AssemblyResolve) | Shipped (unreleased) |
| 10 | String encryption (AES-256 / XOR, index XOR, resource strings) | Shipped |
| 11 | Constant encryption | Shipped |
| 15 | Resource encryption | Shipped |
| 18 | Anti-debug (`IsAttached`, `IsLogging`, kernel32, scatter, timing) | Shipped |
| 19 | Anti-dump (PE wipe + in-process MiniDump hook, Windows x86/x64) | Shipped (unreleased) |
| 20 | Anti-decompiler (junk types, `SuppressIldasm`, decoy attributes) | Shipped |
| 21 | Watermark (`WatermarkAttribute`) | Shipped |
| 22 | Anti-tamper (whole-file SHA-256) | Shipped |
| 24 | Virtualization (simple static int methods only) | Shipped (unreleased; limited) |
| 25 | Method IL encryption (per-method XOR in PE, decrypt at load) | Shipped (unreleased) |
| 30 | Control flow (CFG flatten + opaque predicates; helpers included) | Shipped |
| 40 | Reference proxy (`calli` trampolines; optional external) | Shipped (unreleased) |
| 50 | Symbol renaming (types, methods, fields, properties, events, namespaces) | Shipped |
| 90 | Metadata removal | Shipped |

Source mode is a subset: strings, renaming, control flow only. `[Obfuscation]` is honored in both modes.

**Still true:** method encryption skips generics and is Windows-only. Anti-dump MiniDump hook is in-process x86/x64 only. Virtualization is not a general IL VM. Packing is a managed FDD launcher, not native. Unity / Blazor / MAUI are recipes, not first-class plugins.

---

## Priority definitions

| Priority | Definition | Target |
|----------|------------|--------|
| **P0** | Next release — remaining unreleased-on-main cut | next tag after 1.3.0 |
| **P1** | Near-term — platform recipes → tested support | following minor |
| **P2** | Mid-term — tooling expansion | later minor |
| **P3** | Later — advanced / high-cost protection | v2.0+ |

---

## Product backlog

### Protection features

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| PF-01 | Resource encryption | P0 | Low | Medium | ✅ Done |
| PF-02 | Constant encryption | P0 | Low | Medium | ✅ Done |
| PF-03 | Anti-tamper detection | P1 | Medium | High | ✅ Done |
| PF-04 | Anti-decompiler | P1 | Medium | Medium | ✅ Done |
| PF-05 | Anti-dump PE-header wipe | P2 | Medium | Medium | ✅ Done (not dumper-hook parity) |
| PF-07 | Method IL encryption | P2 | High | High | ✅ Done (XOR, Windows, no generics) |
| PF-10 | Harden runtime helpers | P0 | Medium | High | ✅ Done |
| PF-14 | Control-flow polish | P0 | Low | Medium | ✅ Done |
| PF-12 | Stronger anti-debug | P0 | Medium | High | ✅ Done |
| PF-13 | Method encryption 1.1 | P0 | Medium | High | ✅ Done (per-method keys + skip warnings) |
| PF-11 | `[Obfuscation]` + include/exclude rules | P1 | Medium | High | ✅ Done |
| PF-17 | Safer default exclusions (JSON/XML/WPF/COM) | P1 | Low | High | ✅ Done |
| PF-16 | NativeAOT / Unity protection gating | P1 | Low | Medium | ✅ Done (`runtimeProfile`) |
| PF-15 | Optional external call proxies | P2 | Medium | Medium | ✅ Done (`proxyExternalCalls`) |
| PF-06 | Watermarking | P2 | Low | Low | ✅ Done |
| PF-18 | Anti-de4dot signatures | P2 | Low | Low | ✅ Done (detector bait; does not block de4dot) |
| PF-19 | Dumper-hook anti-dump | P3 | High | Medium | ✅ Partial (in-process MiniDumpWriteDump `0xC3` only) |
| PF-08 | Code virtualization | P3 | Very High | High | ✅ Partial (simple static int methods). General VM remains Future |
| PF-09 | Native code generation | P3 | Very High | Medium | ✅ Partial (managed FDD launcher; native packer remaining) |

### Developer experience

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| DX-01 | WPF desktop application | P1 | Medium | High | ✅ Done |
| DX-02 | Visual Studio extension | P1 | Medium | High | ✅ Done |
| DX-04 | JetBrains Rider plugin | P2 | Low | Medium | ✅ Done |
| DX-05 | Configuration wizard | P2 | Low | Medium | ✅ Done |
| DX-03 | VS Code extension | P2 | Low | Medium | ✅ Partial (schema + problem matcher stub in `Src/Obfy.VSCode`) |
| DX-06 | Real-time preview | P3 | High | Medium | ✅ Done (post-run ILSpy preview in desktop Results tab) |

### Platform support

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| PS-01 | Unity support (Mono / IL2CPP tested) | P2 | Medium | High | ✅ Partial (recipe + `UnityIl2Cpp` profile; no Editor plugin) |
| PS-02 | Xamarin/MAUI support | P2 | Medium | Medium | ✅ Partial (recipe + `preserveXaml`; no MAUI profile) |
| PS-03 | Blazor WebAssembly | P2 | Low | Medium | ✅ Partial (recipe + `BlazorWasm` profile) |
| PS-04 | NativeAOT compatibility | P2 | High | Medium | ✅ Partial (gating via `NativeAot`; remaining techniques not AOT-proved) |

### Utility features

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| UF-01 | Assembly merging | P1 | Medium | Medium | ✅ Done |
| UF-04 | Obfuscation report | P1 | Low | Medium | ✅ Done |
| UF-03 | Assembly signing (re-sign after obfuscation) | P1 | Low | Medium | ✅ Done |
| UF-02 | Dependency embedding | P2 | Medium | Medium | ✅ Done |
| UF-05 | Incremental obfuscation | P3 | High | Medium | ✅ Done (`incremental.enabled`; `{output}.obfycache`) |

### Quality and testing

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| QT-01 | Console CLI tests | P1 | Medium | High | ✅ Done |
| QT-02 | ViewModel unit tests | P2 | Medium | Medium | ✅ Done |
| QT-03 | Code coverage CI | P2 | Low | Medium | ✅ Done |
| QT-04 | UI automation tests | P3 | High | Low | ✅ Done |
| QT-05 | Aggressive pipeline e2e (compile → run) | P0 | Medium | High | ✅ Done (unreleased) |
| QT-06 | Decompiler-resistance fixtures | P0 | Medium | High | ✅ Done |
| QT-07 | Scenario / SDK project tests | P1 | Medium | High | ✅ Done (Phase 1 — [Testing-Roadmap.md](Testing-Roadmap.md)) |

---

## Open work

### Next tag (cut Unreleased)

Ship the work already on main: anti-dump, method encryption, reference proxy, helper hardening, `[Obfuscation]`, runtime profiles, packing, incremental cache, limited virtualization, MSIX. Update the 1.3.0 tag’s changelog into a dated release.

### DX-03: VS Code beyond the stub

`Src/Obfy.VSCode` validates `obfy.json` and matches CLI `Error:` / `⚠` lines. Remaining: a TaskProvider, marketplace listing, and docs in the README install path.

### Testing: scenario coverage

Engine unit/e2e tests are in place (`QT-01`–`QT-06`). Missing: real SDK projects (WPF app + class library solutions), CI-honest FlaUI, and platform compile → obfuscate → run jobs. Plan: [Testing-Roadmap.md](Testing-Roadmap.md) (`TR-*`, including `QT-07`).

### PS-01 / PS-02 / PS-03: real platform tests

Recipes exist (`examples/unity`, `blazor`, `maui`). Do not claim first-class Unity/MAUI/Blazor support until there are compile → obfuscate → run projects (Unity Development Player, MAUI iOS/Android without method encryption, published Blazor `_framework` DLLs). Those jobs are Phase 3 in the testing roadmap (`TR-30`–`TR-33`).

### PS-04: NativeAOT beyond gating

`runtimeProfile: NativeAot` already disables `VirtualProtect` / anti-dump / AssemblyResolve. Remaining: prove rename + strings + control flow + in-module proxies on a `PublishAot` app.

### PF-09 remaining: native packer

The managed `{name}.launcher.exe` is not native code generation. A Windows native host is still the commercial differentiator; do not start until demand is clear.

### PF-08 remaining: general virtualization

The current interpreter handles only simple static `int` arithmetic. A custom VM for arbitrary IL is still a research spike, then a design — not a drive-by expansion of `maxMethods`.

### PF-19 remaining: external dumpers

In-process `MiniDumpWriteDump` `0xC3` does not stop ProcDump, Task Manager, `dbgcore`, or `ReadProcessMemory`. Stronger dump resistance is P3.

---

## Roadmap

### Phase 1–3 (v1.1–v1.3) ✅

Foundation, protection plus, and developer experience. See changelog 1.1.0 / 1.2.0 / 1.3.0.

Shipped: resource/constant encryption, reports, WPF UI, anti-tamper, anti-decompiler, merge, VS extension, Rider plugin, config wizard, CLI/UI tests, coverage CI, FlaUI tests.

### Unreleased on main (next tag)

Already in the tree; not yet cut as a release:

- Anti-dump PE-header wipe and in-process MiniDump hook
- In-module `calli` reference proxies and optional external proxies
- Method IL encryption (per-method keys)
- Native `IsDebuggerPresent` / scatter / timing anti-debug
- CFG flattening, random dispatcher states, TickCount opaque predicates
- Event / namespace renaming; XOR-encoded string decrypt indices; three decrypt entry points
- Control flow and proxies on runtime helpers
- `[Obfuscation]` + inclusions; JSON/XML/COM/XAML defaults
- `runtimeProfile` gating; signing; dependency embedding; watermark; decoy attributes
- Incremental cache; managed launcher; limited virtualization
- Unity / Blazor / MAUI recipes; VS Code stub; MSIX
- Aggressive compile → obfuscate → load → run tests; decompiler-resistance fixtures

### Phase 4: Protection hardening ✅ (on main)

PF-10, PF-14, PF-12, PF-13, QT-06 are implemented. See Techniques.md for current behavior and limits.

### Phase 5: Safe by default ✅ (on main)

PF-11, PF-17, PF-16, UF-03 are implemented.

### Phase 6: Platform expansion (next)

| Feature | Notes |
|---------|--------|
| PS-01 Unity | Real Mono + IL2CPP test projects on top of the existing recipe |
| DX-03 VS Code | TaskProvider and marketplace path beyond the stub |
| PS-03 Blazor WASM | Compatibility pass on published `_framework` DLLs |
| PS-02 MAUI | iOS/Android run without method encryption / anti-dump |

### Phase 7: Advanced protection (v2.0+)

| Feature | Notes |
|---------|--------|
| PS-04 NativeAOT (real) | After gating, make remaining techniques AOT-safe |
| PF-19 Dumper-hook anti-dump | Beyond in-process `MiniDumpWriteDump` `0xC3` |

### Future (v3.0+)

| Feature | Notes |
|---------|--------|
| PF-08 Code virtualization | Research spike → design → implementation. Selective methods only. Grow or replace the current int-only interpreter. |
| PF-09 Native code generation | Windows launcher first |

---

## Why this order

| Tempting item | Why it waits |
|---------------|--------------|
| General code virtualization | Highest protection, highest cost. The int-only interpreter is a spike, not a product VM. |
| Native packer | Platform-specific, high maintenance. Managed FDD launcher already exists. |
| Unity “support” | Wizard exclusions plus a recipe are not support. Test real players next. |

---

## Dependencies and risks

| Feature | Depends on | Risk |
|---------|------------|------|
| NativeAOT remaining techniques | PF-16 gating (done) | Over-claiming AOT safety. Keep PE tricks gated. |
| Unity test projects | PF-16 (done) | Reflection-heavy plugins need extra exclusions. |
| General VM | PF-08 spike | Scope explosion. Do not expand opcodes ad hoc. |

---

## Success metrics by phase

| Phase | KPI | Target |
|-------|-----|--------|
| v1.3 | IDE + wizard | VS and Rider usable |
| next tag | Decompiler resistance + safe defaults | Decryptors/anti-debug not one-NOP; JSON sample works; AOT/IL2CPP do not get `VirtualProtect` |
| later | Platform coverage | Unity Development Player + VS Code TaskProvider |
| v2.0+ | Mid-tier extras | Stronger dump resistance, AOT-safe remaining techniques |
| v3.0 | Commercial-grade option | Selective virtualization, if demand holds |

---

## Documentation to update per release

When shipping a phase, update:

1. `CHANGELOG.md` — user-facing changes
2. `docs/Techniques.md` — how the technique actually works and its limits
3. `docs/Configuration.md` — new settings
4. `docs/CLI.md` — new flags
5. `docs/Competitive-Analysis.md` — feature matrix
6. `README.md` / `CLAUDE.md` — technique table if the pipeline changes
7. `docs/Testing.md` / `CONTRIBUTING.md` — test counts
8. `docs/Testing-Roadmap.md` — flip `TR-*` status when a testing item lands

---

## Contribution opportunities

| Feature | Good for contributors | Notes |
|---------|----------------------|-------|
| VS Code TaskProvider (DX-03) | Yes | Stub already in `Src/Obfy.VSCode` |
| Unity / MAUI / Blazor test projects | Yes | Recipes exist; need runnable apps |
| Documentation | Yes | Always |
| General virtualization (PF-08) | No | Needs architecture |
| Native packer (PF-09) | No | Platform-specific |

---

## Review schedule

- **Per investigation / release:** adjust remaining P1/P2 if breakage data disagrees with this order
- **Quarterly:** demand check before starting v3.0 general virtualization
- **Do not** restart competitive-analysis-driven checkbox work (Unity plugin, full VM) ahead of a real test project

---

*Last updated: September 2026*
*Based on: Competitive Analysis + protection investigation (2026-09) + docs accuracy pass*

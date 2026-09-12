# Obfy Product Roadmap & Backlog

A strategic development plan based on the competitive analysis and the September 2026 protection investigation.

The investigation finding: Obfy already covers the core .NET obfuscation stack. The next gains come from making existing techniques harder to undo, then from compatibility (so Aggressive builds do not break WPF, JSON, or AOT), not from starting a custom VM.

---

## Vision

**Make Obfy the go-to open-source .NET obfuscator** by closing the feature gap with commercial tools while remaining simple, modern-.NET-first, and safe to turn on.

**Target:** Cover typical obfuscation needs with protection that survives casual decompilation (ILSpy / dnSpy / de4dot-class tools), without claiming confidentiality or virtualization-grade resistance.

---

## Current position

Assembly pipeline (priority order):

| Priority | Technique | Status |
|----------|-----------|--------|
| 10 | String encryption (AES-256 / XOR, index XOR, resource strings) | Shipped |
| 11 | Constant encryption | Shipped |
| 15 | Resource encryption | Shipped |
| 18 | Anti-debug (`IsAttached`, `IsLogging`, `IsDebuggerPresent`) | Shipped |
| 19 | Anti-dump (PE wipe + in-process MiniDump hook, Windows x86/x64) | Shipped (unreleased) |
| 20 | Anti-decompiler (junk types, `SuppressIldasm`, decoy attributes) | Shipped |
| 21 | Watermark (`WatermarkAttribute`) | Shipped |
| 22 | Anti-tamper (whole-file SHA-256) | Shipped |
| 25 | Method IL encryption (XOR in PE, decrypt at load) | Shipped (unreleased) |
| 30 | Control flow (CFG flatten + opaque predicates) | Shipped |
| 40 | Reference proxy (`calli` trampolines, in-module only) | Shipped (unreleased) |
| 50 | Symbol renaming (types, methods, fields, properties, events, namespaces) | Shipped |
| 90 | Metadata removal | Shipped |

Source mode is a subset: strings, renaming, control flow only.

**What this does not mean:** decryptors, anti-debug `Check`, and anti-tamper `Verify` are still skipped by control flow and proxies. Method encryption uses a single-byte XOR key and skips generics. Anti-debug is two call sites. Those are the v1.4 targets.

---

## Priority definitions

| Priority | Definition | Target |
|----------|------------|--------|
| **P0** | Next release — protection quality or crash-on-obfuscate | v1.4 |
| **P1** | Near-term — compatibility and safer defaults | v1.5 |
| **P2** | Mid-term — platform and tooling expansion | v1.6 |
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
| PF-10 | Harden runtime helpers | P0 | Medium | High | Next |
| PF-14 | Control-flow polish | P0 | Low | Medium | Next |
| PF-12 | Stronger anti-debug | P0 | Medium | High | Next |
| PF-13 | Method encryption 1.1 | P0 | Medium | High | Next |
| PF-11 | `[Obfuscation]` + include/exclude rules | P1 | Medium | High | Planned |
| PF-17 | Safer default exclusions (JSON/XML/WPF/COM) | P1 | Low | High | Planned |
| PF-16 | NativeAOT / Unity protection gating | P1 | Low | Medium | Planned |
| PF-15 | Optional external call proxies | P2 | Medium | Medium | Planned |
| PF-06 | Watermarking | P2 | Low | Low | ✅ Done |
| PF-18 | Anti-de4dot signatures | P2 | Low | Low | ✅ Done (detector bait; does not block de4dot) |
| PF-19 | Dumper-hook anti-dump | P3 | High | Medium | ✅ Partial (in-process MiniDumpWriteDump `0xC3` only) |
| PF-08 | Code virtualization | P3 | Very High | High | Future |
| PF-09 | Native code generation | P3 | Very High | Medium | ✅ Partial (managed FDD launcher; native packer remaining) |

### Developer experience

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| DX-01 | WPF desktop application | P1 | Medium | High | ✅ Done |
| DX-02 | Visual Studio extension | P1 | Medium | High | ✅ Done |
| DX-04 | JetBrains Rider plugin | P2 | Low | Medium | ✅ Done |
| DX-05 | Configuration wizard | P2 | Low | Medium | ✅ Done |
| DX-03 | VS Code extension | P2 | Low | Medium | Backlog |
| DX-06 | Real-time preview | P3 | High | Medium | ✅ Done (post-run ILSpy preview in desktop Results tab) |

### Platform support

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| PS-01 | Unity support (Mono / IL2CPP tested) | P2 | Medium | High | Backlog |
| PS-02 | Xamarin/MAUI support | P2 | Medium | Medium | Backlog |
| PS-03 | Blazor WebAssembly | P2 | Low | Medium | Backlog |
| PS-04 | NativeAOT compatibility | P2 | High | Medium | Backlog (gating first: PF-16) |

### Utility features

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| UF-01 | Assembly merging | P1 | Medium | Medium | ✅ Done |
| UF-04 | Obfuscation report | P1 | Low | Medium | ✅ Done |
| UF-03 | Assembly signing (re-sign after obfuscation) | P1 | Low | Medium | ✅ Done |
| UF-02 | Dependency embedding | P2 | Medium | Medium | Backlog |
| UF-05 | Incremental obfuscation | P3 | High | Medium | ✅ Done (unreleased) |

### Quality and testing

| ID | Feature | Priority | Effort | Value | Status |
|----|---------|----------|--------|-------|--------|
| QT-01 | Console CLI tests | P1 | Medium | High | ✅ Done |
| QT-02 | ViewModel unit tests | P2 | Medium | Medium | ✅ Done |
| QT-03 | Code coverage CI | P2 | Low | Medium | ✅ Done |
| QT-04 | UI automation tests | P3 | High | Low | ✅ Done |
| QT-05 | Aggressive pipeline e2e (compile → run) | P0 | Medium | High | ✅ Done (unreleased) |
| QT-06 | Decompiler-resistance fixtures | P0 | Medium | High | Next (with PF-10) |

---

## Open work — details

### PF-10: Harden runtime helpers (v1.4)

**Problem:** `ControlFlowObfuscator` and `ReferenceProxyObfuscator` skip `Obfy.Runtime.*`. String/constant decryptors, anti-debug `Check`, anti-tamper `Verify`, and method-body decrypt remain clean IL. After renaming they look odd, but de4dot-class tools hunt a single `Decrypt(int)` with a static cache.

**Do:**

- Apply control flow to helper methods except `.cctor` and P/Invoke stubs.
- Optionally proxy helper calls the same way as user code.
- Split string decrypt into several variants, or inline a unique snippet per call site so one NOP / one signature does not recover every string.
- Keep module initializers correct and test that Aggressive assemblies still load.

**Success:** ILSpy does not show a trivial `Decrypt(int index)` that returns cached plaintext; e2e Aggressive still runs.

### PF-14: Control-flow polish (v1.4)

**Problem:** The linear flatten fallback uses sequential states `0, 1, 2…` (trivial to unflatten). CFG flattening already uses random states. Constructors are skipped. Methods with exception handlers only get opaque predicates (acceptable for now).

**Do:**

- Use random dispatcher states in the linear path (same as CFG).
- Add more always-true / always-false predicate forms (still based on runtime values, not foldable constants).
- Leave EH flattening and constructor flattening out of v1.4.

**Success:** Linear-flattened methods are not a sequential state machine; existing control-flow tests still pass.

### PF-12: Stronger anti-debug (v1.4)

**Problem:** Checks run at the entry point and/or module initializer only. `Debugger.IsAttached`, `IsLogging`, and `IsDebuggerPresent` plus `Environment.Exit(1)` — one NOP on `Check` disables protection.

**Do:**

- Scatter additional checks into a configurable fraction of methods (intensity).
- Add `CheckRemoteDebuggerPresent` and/or `NtQueryInformationProcess` (Windows; swallow failures elsewhere).
- Add a cheap timing check (BitMono-style breakpoint detection).
- Randomize the failure path (not always `Exit(1)`).
- Do not claim debugger immunity; document as a cost-raiser.

**Success:** Removing a single `Check` call is not enough; undebugged e2e still proceeds.

### PF-13: Method encryption 1.1 (v1.4)

**Problem:** One XOR byte for every method body. Generic methods and generic types are skipped (most modern C#). Windows `VirtualProtect` only. Easy once the decryptor is found.

**Do:**

- Per-method (or per-batch) keys instead of a single byte.
- Document platform limits in Techniques.md: Windows, no generics, not NativeAOT.
- Warn at obfuscation time when a large share of methods are skipped (generics).
- Do not promise AES method bodies or generic support in v1.4.

**Success:** Dumping the PE no longer yields plaintext IL for encrypted methods without the per-method keys; generic skip rate is visible in the report.

### PF-11: `[Obfuscation]` attribute and include rules (v1.5)

**Problem:** No `System.Reflection.ObfuscationAttribute` support (zero hits in the repo). Commercial tools and ConfuserEx honor `[Obfuscation(Exclude = true)]` and feature-scoped exclude/apply. Rules are exclusion-only except resource include patterns.

**Do:**

- Honor `[Obfuscation(Exclude = true)]` and `[Obfuscation(Exclude = false)]` on types and members.
- Honor `Feature` for at least `renaming`, `controlflow`, `strings`, `constants` (unknown features ignored, not errors).
- Add optional include patterns on symbol renaming / string encryption (same wildcard language as exclusions).
- Document in Configuration.md and Advanced.md.

**Success:** A library can keep a public DTO and a license type un-renamed from source attributes without a config file.

### PF-17: Safer default exclusions (v1.5)

**Problem:** Default excluded attributes are `Serializable`, `DataContract`, `DataMember`. `JsonPropertyName`, `JsonProperty`, and `XmlElement` are documented as common additions but are not default — a frequent post-obfuscation crash. No `ComVisible`, BAML / `x:Name`, or WPF binding-path preservation.

**Do:**

- Default-exclude `JsonPropertyNameAttribute`, `JsonPropertyAttribute`, `XmlElementAttribute`, `XmlAttributeAttribute`.
- Skip renaming `ComVisible(true)` types and members.
- For WPF: preserve public instance properties on types that look like dependency objects / view models when a `wpf` or `preserveXaml` setting is on (off by default; on in a Desktop wizard preset).
- Keep the setting overridable.

**Success:** A typical System.Text.Json app obfuscates under Standard without extra config; WPF desktop preset does not rename binding properties.

### PF-16: NativeAOT / Unity protection gating (v1.5)

**Problem:** Anti-dump and method encryption P/Invoke `kernel32` / `VirtualProtect`. NativeAOT and Unity IL2CPP will fail or silently no-op. The Unity wizard only adds `UnityEngine.*` / `Unity.*` namespace exclusions.

**Do:**

- Detect (or take a setting for) NativeAOT / IL2CPP / non-Windows.
- Auto-disable method encryption and anti-dump with a report warning instead of emitting broken P/Invoke.
- Do not claim Unity support until PS-01 has real test projects.

**Success:** Aggressive on a `PublishAot` candidate assembly produces a loadable output; warnings name the skipped protections.

### PF-15: Optional external call proxies (v1.6)

In-module `calli` proxies already hide user-to-user calls. Framework calls (`String.get_Length`, `Console.WriteLine`) stay visible.

Add an opt-in flag to proxy selected external calls. Default remains in-module only (safer). Conservative skip list for skipped corlib members that are easy to get wrong.

### UF-03: Assembly signing (v1.5)

Re-sign with an SNK/PFX after obfuscation so strong-named libraries remain loadable. Config: path + optional password env var. Fail the run if signing is requested and fails.

### PF-06 / PF-18: Watermarking and anti-de4dot (v2.0) ✅

Shipped. Watermark embeds a plaintext build/customer id as pinned `Obfy.Runtime.WatermarkAttribute`. Anti-de4dot adds decoy `ConfusedByAttribute` / `DotfuscatorAttribute` names; this does not block de4dot.

### PF-08 / PF-09: Virtualization and native packer (v3.0+)

Custom VM bytecode and a native launcher are the remaining commercial differentiators (.NET Reactor, Babel, Eazfuscator). Do **not** start until PF-10 through PF-13 are done. Virtualization is a research spike first, then a design, not a drive-by feature.

---

## Roadmap

### Phase 1–3 (v1.1–v1.3) ✅

Foundation, protection plus, and developer experience. See changelog 1.1.0 / 1.2.0.

Shipped: resource/constant encryption, reports, WPF UI, anti-tamper, anti-decompiler, merge, VS extension, Rider plugin, config wizard, CLI/UI tests, coverage CI, FlaUI tests.

### Unreleased on main (rolls into v1.4)

Already in the tree; not yet cut as a release:

- Anti-dump PE-header wipe
- In-module `calli` reference proxies
- Method IL encryption
- Native `IsDebuggerPresent`
- CFG flattening, TickCount opaque predicates
- Event / namespace renaming
- XOR-encoded string decrypt indices
- Aggressive compile → obfuscate → load → run tests

### Phase 4: Protection hardening (v1.4)

**Theme:** Make Aggressive output harder to reverse without adding a new technique family.

| Feature | Notes |
|---------|--------|
| PF-10 Harden runtime helpers | Control-flow (and optional proxy) on decryptors / Check / Verify |
| PF-14 Control-flow polish | Random linear states; more predicates |
| PF-12 Stronger anti-debug | Scattered checks, extra native APIs, timing |
| PF-13 Method encryption 1.1 | Per-method keys; skip-rate warnings; documented limits |
| QT-06 Decompiler-resistance fixtures | Snapshot or ILSpy-based assertions that decryptors are not trivial |

**Success metrics:**

- Aggressive e2e still runs
- String decrypt is not a single obvious method in decompiler output
- Anti-debug survives removal of one call site
- Report shows method-encryption skip counts

**Out of scope:** virtualization, EH flattening, generic method encryption, Unity.

### Phase 5: Safe by default (v1.5)

**Theme:** Aggressive/Standard should not break common apps.

| Feature | Notes |
|---------|--------|
| PF-11 `[Obfuscation]` + include rules | Source-level control, ConfuserEx/Dotfuscator parity |
| PF-17 Safer default exclusions | JSON/XML defaults; COM; optional WPF preserve |
| PF-16 AOT / Unity gating | Disable Windows PE tricks with warnings |
| UF-03 Assembly signing | Re-sign after obfuscation |

**Success metrics:**

- System.Text.Json sample works on Standard with empty extra exclusions
- `[Obfuscation(Exclude = true)]` is honored in tests
- `PublishAot`-style assemblies do not get `VirtualProtect` stubs unless Windows PE encryption is explicitly forced

### Phase 6: Platform expansion (v1.6)

**Theme:** Broader hosts, still no VM.

| Feature | Notes |
|---------|--------|
| PS-01 Unity support | Real Mono + IL2CPP test projects; IL2CPP-safe subset |
| DX-03 VS Code extension | Tasks, schema, problem matcher |
| UF-02 Dependency embedding | SmartAssembly / .NET Reactor parity |
| PF-15 External call proxies | Opt-in |
| PS-03 Blazor WASM | Compatibility pass |
| PS-02 MAUI | Compatibility pass |

**Success metrics:** documented Unity recipe; VS Code users can obfuscate without the WPF app.

### Phase 7: Advanced protection (v2.0)

| Feature | Notes |
|---------|--------|
| PF-06 Watermarking | ✅ Build/customer id (plaintext CA) |
| PF-18 Anti-de4dot | ✅ Decoy attributes (does not block de4dot) |
| PF-19 Dumper-hook anti-dump | ✅ Partial: in-process `MiniDumpWriteDump` `0xC3` only |
| PS-04 NativeAOT (real) | After PF-16 gating, make remaining techniques AOT-safe |

### Future (v3.0+)

| Feature | Notes |
|---------|--------|
| PF-08 Code virtualization | Research spike → design → implementation. Selective methods only. |
| PF-09 Native code generation | Windows launcher first |
| UF-05 Incremental obfuscation | CI cache |
| DX-06 Real-time preview | UI |

---

## Why this order

| Tempting item | Why it waits |
|---------------|--------------|
| Code virtualization | Highest protection, highest cost. Worthless if decryptors and anti-debug are still one NOP. |
| Native packer | Platform-specific, high maintenance. |
| Unity “support” | Wizard exclusions are not support. Gate PE tricks first (PF-16), then test real projects. |
| Watermark / anti-de4dot | Cheap theater; does not raise the bar. |
| External proxies | Useful, but in-module proxies already exist; opt-in after helpers are hardened. |

---

## Dependencies and risks

| Feature | Depends on | Risk |
|---------|------------|------|
| PF-10 Helper hardening | Control-flow correctness on injected IL | Broken decryptors → unrunnable assemblies. Mitigate with Aggressive e2e. |
| PF-12 Scattered anti-debug | Helper hardening so `Check` is not obvious | False positives with profilers. Keep a disable flag; document. |
| PF-13 Per-method keys | Existing PE encryptor | Wrong RVA/key mapping. Keep round-trip e2e. |
| PF-11 Attributes | Exclusion helpers | Feature names must be stable. Ignore unknown features. |
| PF-16 Gating | PE-using obfuscators (dump, method crypt) | Over-gating weakens Aggressive on Windows. Gate only when AOT/IL2CPP/non-Windows is detected or selected. |
| PF-08 VM | PF-10–13 done | Scope explosion. Spike only. |

---

## Success metrics by phase

| Phase | KPI | Target |
|-------|-----|--------|
| v1.3 | IDE + wizard | VS and Rider usable |
| v1.4 | Decompiler resistance | Decryptors and anti-debug are not one-signature / one-NOP |
| v1.5 | Safe defaults | JSON + `[Obfuscation]` samples pass without extra config |
| v1.6 | Platform coverage | Unity recipe + VS Code |
| v2.0 | Mid-tier extras | Watermark, stronger dump resistance |
| v3.0 | Commercial-grade option | Selective virtualization, if demand holds |

---

## Documentation to update per release

When shipping a phase, update:

1. `CHANGELOG.md` — user-facing changes
2. `docs/Techniques.md` — how the technique actually works and its limits
3. `docs/Configuration.md` — new settings
4. `docs/CLI.md` — new flags
5. `docs/Competitive-Analysis.md` — feature matrix (currently stale vs unreleased work)
6. `README.md` / `CLAUDE.md` — technique table if the pipeline changes

---

## Contribution opportunities

| Feature | Good for contributors | Notes |
|---------|----------------------|-------|
| VS Code extension (DX-03) | Yes | Standalone |
| Default exclusion lists (PF-17) | Yes | Tests required |
| Watermarking (PF-06) | Yes | Isolated |
| Documentation | Yes | Always |
| Helper hardening (PF-10) | No | Core correctness |
| Virtualization (PF-08) | No | Needs architecture |

---

## Review schedule

- **Per investigation / release:** adjust P0/P1 if decompiler resistance or breakage data disagrees with this order
- **Quarterly:** demand check before starting v3.0 virtualization
- **Do not** restart competitive-analysis-driven checkbox work (Unity, VM, watermark) ahead of v1.4

---

*Last updated: September 2026*
*Based on: Competitive Analysis v1.0 + protection investigation (2026-09)*

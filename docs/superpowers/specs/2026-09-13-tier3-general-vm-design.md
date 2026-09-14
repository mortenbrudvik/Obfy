# Tier 3 protection ceiling: general IL virtualization

**Date:** 2026-09-13  
**Status:** Locked after review  
**Product:** Obfy assembly pipeline (`Obfy.Core`)  
**Surfaces:** Core engine, CLI `--virtualize`, desktop Virtualization toggle, docs  
**Not in this spec:** native packer, licensing/RASP, source-mode VM, Unity Editor, MSBuild PackageReference

## Goal

Replace the int-only `VirtualizationObfuscator` with a **general managed IL virtualizer** that can encode typical C# instance and static methods, mutate the encoding per build, and run on CoreCLR. After this ships, Competitive-Analysis.md can move **Code virtualization** from Partial to Yes, with the limits in this spec called out — the honest Babel-class bar, not Reactor/ArmDot.

This is the first sub-project of “make Obfy a Tier 3 obfuscation tool.” Native packing is a later spec. Licensing, crash reporting, and RASP stay out of Obfy.

## Success criteria

- An eligible method (instance or static; objects, non-generic calls, fields, `newobj` of classes, `ldstr`; int/long/float/double arithmetic) is replaced with a stub that calls `Obfy.Runtime.Vm.Run`. ILSpy on that method does not show the original body. **Not eligible** (left unchanged): EH, generic methods/types/calls (`MethodSpec`), byref / `ref` / `in` / `out` / `Span` / ref structs, custom structs / `Nullable<T>` as param, return, or local, `switch`, `typeof` / `ldtoken`, interpolated strings, `foreach` / `using`, `ldftn` delegates, ctors.
- Two obfuscation runs of **different** inputs produce different opcode maps. Two runs of the **same** input + settings + Obfy version produce the same map (incremental cache still hits).
- Ineligible methods are left unchanged; the run still succeeds.
- `runtimeProfile` NativeAOT / Unity IL2CPP / Blazor WASM turns virtualization off with a warning.
- Opt-in: off in Minimal, Standard, and Aggressive. `--virtualize` / UI toggle / `virtualization.enabled` turn it on.
- Existing `obfy` without `--virtualize` is unchanged.

## Background

Today’s VM (`Src/Obfy.Core/Obfuscators/Assembly/VirtualizationObfuscator.cs`, ~850 lines of hand-emitted IL) encodes only **static `int` methods** (`ldc.i4`, `ldarg`, `ldloc`/`stloc`, add/sub/mul, `ceq`/`cgt`/`clt`, signed branches; ≤8 params / ≤16 locals; unsigned compares skipped). Competitive-Analysis.md correctly marks this Partial. Roadmap PF-08 says do not grow opcodes ad hoc.

The current code *does* prove the pipeline shape we keep: select → encode blob → inject `Obfy.Runtime.<Vm>` → replace bodies with stubs → register a runtime helper (rename, no IL-encrypt). Today that registration is `RuntimeHelperOptions.Interpreter`, which also **disables flatten**. The new VM must **not** use that constant — flatten is on unless the pipeline execute test forces the documented fallback. What does not scale is `CreateExecute` as handwritten CIL, a single int32 stack, and a fixed ISA.

Commercial context (from Competitive-Analysis.md): a weak VM is worse than none (Agile.NET has a public generic devirtualizer). Per-build opcode/handler mutation is the minimum anti-lift bar. Babel Ultimate is the analogue: managed VM, no native stub, code encryption unsupported on MAUI/Blazor.

## Goals and non-goals

### Goals

- C# VM runtime imported into the target (netstandard2.0, no extra dependencies).
- Encoder for the v1 ISA below; skip the rest.
- Per-build opcode permutation + bytecode XOR, seed derived from `IncrementalCache.ComputeKey`.
- Frame stack so virtualized-to-virtualized `call` does not re-enter through stubs (`CallVm`).
- Token-based member resolution (`ldtoken` + `GetMethodFromHandle`), so symbol renaming still works.
- Gate off NativeAOT / Unity IL2CPP / Blazor WASM.
- Update docs, CLI description, UI copy, Competitive-Analysis, Roadmap PF-08.

### Non-goals (out of this spec)

- Native / unmanaged packer (PF-09). The managed `{name}.launcher.exe` stays as-is.
- Licensing, HWID, DRM, RASP, crash reporting.
- Exception handlers, generics (methods, types, **or calls**), byref / `ref` / `in` / `out` / `Span` / ref structs, `switch`, `calli`, `constrained`, `ldloca` / `ldflda`, function pointers, `typeof` / `ldtoken`, interpolators, `foreach` / `using`.
- Encoding `MethodSpec` via `MethodBase.Invoke`. Generic calls skip the method.
- Unique *generated handlers* per build (Approach B / Eazfuscator-style). Opcode shuffle + XOR + rename (+ flatten if tests pass) is the v1 mutation.
- Keeping the int-only interpreter as a second VM.
- Source (Roslyn) virtualization.
- Turning virtualization on in the Aggressive preset.
- First-class NativeAOT / IL2CPP / Blazor / MAUI VM support.
- MSBuild PackageReference, VS Code TaskProvider, Unity Editor plugin.

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Product target | Protection-ceiling OSS (Babel-class managed VM) | Competitive analysis: do not chase licensing/RASP; native packer is a separate product. |
| Architecture | Inject compiled C# runtime; replace current obfuscator | Hand-emitted `CreateExecute` cannot host objects, calls, or per-build mutation. |
| Host | CoreCLR desktop/server only | Reflection `Invoke` is acceptable; PE tricks are already Default-profile-only. Babel also skips MAUI/Blazor for code encryption. |
| Mutation | Permute opcode bytes + XOR blob from cache-key seed | Per-build unique *encoding* without generating a new interpreter. Deterministic ⇒ incremental cache still works. |
| Seed | `IncrementalCache.ComputeKey` decoded from hex (no second hash) | Same 32 bytes the cache already stores. In-memory fallback uses the same `"obfy-cache|" + version + "|"` stamp and `JsonOptions`. No new `virtualization.seed` knob. |
| Strings | UTF-8 inside the XOR’d blob, not `ldstr` in user methods | Virtualization stays at 24. When it is enabled, string/constant encryption **skip** the `Select` set so those methods still have `ldstr` / `ldc.*` when the encoder runs. |
| Virtual dispatch | `CallVm` only for non-virtual `call` to a virtualized method; `callvirt` always `MethodBase.Invoke` | Early-binding a virtualized override would skip subclass overrides. |
| Constructors | Skip `.ctor` / `.cctor` | Uninitialized `this` and type-init ordering are a v2 problem. |
| Frame stack | `Frame[]` + depth, not `Stack<Frame>` | `Stack<T>` is not in corlib. The importer retargets netstandard / System.Runtime / mscorlib / System.Private.CoreLib TypeRefs to `dest.CorLibTypes.AssemblyRef`; the runtime must only use corlib types. |
| Returns | `Init` takes `Type[] returnTypes`; `Ret` boxes to that CLR type | Evaluation-stack `I4` is shared by `bool` / `char` / `uint` / small integers. Boxing as `int` then `unbox.any bool` throws. |
| Presets | Remain off | VM cost is real (~10–20% on virtualized methods in this category). Opt-in avoids surprise. |
| Two VMs | No. Delete the int-only ISA | One runtime, one encoder, one test matrix. |
| `[Obfuscation]` | Add `ObfuscationFeature.Virtualization` (`virtualization` / `virtualize` / `vm`) | Today the pass uses `All`, so only blanket exclude works. |

## Architecture

```
eligible MethodDefs
        │
        ▼
   VmEncoder (two-pass)
        │  bytecode blob + member tokens + method ids
        ▼
   VmImporter
        │  copy Obfy.VmRuntime types into target
        │  emit .cctor: InitializeArray + ldtoken tables + Vm.Init(...)
        ▼
   stubs:  ldc.i4 id; args[]; call Vm.Run; unbox/ret
        │
        ▼
   RuntimeInjection.Register(Flatten=true, Rename=true, EncryptIl=false)
```

Priority stays **24** (after anti-tamper, before method IL encryption). Stubs may be XOR-encrypted in the PE; the interpreter is never IL-encrypted (Linux/macOS CoreCLR must run it). Control-flow at 30 will skip short stubs (`<5` instructions) and, if flatten registration holds, flatten `Vm.Run`.

String encryption (10) and constant encryption (11) run **before** virtualization. When `settings.Virtualization.Enabled`, those passes skip every method in `VmEncoder.Select(candidates, maxMethods)` computed with the same candidate filters the obfuscator uses. That is what keeps virtualized literals in the XOR blob instead of turning them into decryptor `call`s. If a method is selected at 10 and later fails `TryEncode` at 24 (another pass mutated the body), those literals stay plaintext — accepted, debug-logged. Methods that lose the `maxMethods` race are **not** skipped by string/constant encryption.

### Components

**`Src/Obfy.VmRuntime/`** — netstandard2.0 class library, no package references, no reference to `Obfy.Core`. Public surface is one type:

```csharp
namespace Obfy.Runtime;

public static class Vm
{
    public static object Run(int id, object[] args);

    // Called from the injected .cctor only.
    internal static void Init(
        byte[] code,
        int[] starts,
        byte[] opMap,
        byte[] xorKey,
        MethodBase[] methods,
        FieldInfo[] fields,
        Type[] types,
        Type[] returnTypes);
}
```

Embed `Obfy.VmRuntime.dll` as a resource in `Obfy.Core` so the global tool always has it (do not probe next to the exe). Wire it with a `ProjectReference` that does not become a runtime dependency of the tool:

```xml
<ProjectReference Include="..\Obfy.VmRuntime\Obfy.VmRuntime.csproj">
  <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
  <OutputItemType>EmbeddedResource</OutputItemType>
  <LogicalName>Obfy.VmRuntime.dll</LogicalName>
</ProjectReference>
```

`Obfy.Core` stays `net10.0`; the runtime assembly is `netstandard2.0` so imported IL retargets to Framework 4.x and modern corlibs. Use `object[]` (not `object?[]`) on `Run` in both the runtime and the stubs.

**`VmImporter`** (`Obfy.Core/Virtualization/VmImporter.cs`) — `ModuleDef.Load` the embedded bytes, **move** `Obfy.Runtime.Vm` and nested types onto the target (do not `Importer.Import` as a TypeRef), retarget corlib TypeRefs whose resolution scope is `netstandard` / `System.Runtime` / `mscorlib` / `System.Private.CoreLib` to `dest.CorLibTypes.AssemblyRef`, emit the `.cctor` that fills tables via `ldtoken` + `RuntimeMethodHandle`/`RuntimeFieldHandle`/`RuntimeTypeHandle` and `GetMethodFromHandle` / `GetFieldFromHandle` / `GetTypeFromHandle`, then `Vm.Init`. The type is already on `module.Types` after the move — call `RuntimeInjection.Register(...)` only. Do **not** call `RuntimeInjection.AddType` (that does `module.Types.Add` and would duplicate `Obfy.Runtime.Vm`). After copy, no `Obfy.VmRuntime` AssemblyRef remains.

```csharp
new RuntimeHelperOptions
{
    FlattenControlFlow = true,
    Rename = true,
    EncryptIl = false
}
```

Fallback allowed without a spec change: if the full-pipeline execute test fails because flattening broke the dispatcher, set `FlattenControlFlow = false`. Rename and XOR/permutation stay.

**`VmEncoder`** — pure functions over `MethodDef`. No module mutation.

**`VmSeed`** — `byte[32]` from `Convert.FromHexString(IncrementalCache.ComputeKey(...))` when `context.InputPath` is a readable file (no second hash). Otherwise SHA-256 of the same `"obfy-cache|" + version + "|"` stamp `IncrementalCache` uses, plus `module.ToArray()` and settings JSON with the same `JsonOptions`. Derive `opMap` (256 bytes, a permutation of internal opcodes in the used range and `0xFF` elsewhere) and an 8-byte `xorKey` via HMAC-SHA256(seed, counter). Do not use `System.Random`.

`VmSeed.Apply` walks the blob with a locked size function (`VmIsa.EncodedSize`): permute **opcode** bytes only, then XOR every byte. Operand widths: see ISA table. `Ldstr` size is `1 + 2 + utf8Length`.

**`VirtualizationObfuscator`** — orchestration only: gate, select, encode, import, stub, stats. Delete `CreateExecute` / int opcodes.

### Stub shape

Instance methods put `this` at `args[0]` (no box — it is already a reference). Value parameters are boxed. Void methods pop the `object` result (`null`). Value returns `unbox.any` the method’s `MethodSig.RetType`. `Run` boxes the evaluation-stack value using `returnTypes[id]` so `bool` / `char` / `uint` / `ulong` / small integers do not `unbox.any` a boxed `int`/`long`. The stub must stay under 5 instructions of “real” work if possible so control-flow will not flatten it; a short `newarr` + `call` + `ret` is expected and acceptable even if instruction count exceeds 5 — flattening a stub is harmless.

### `ldstr`

Encoder writes `Ldstr` + `u16` length + UTF-8 bytes into the method’s bytecode. They are XOR’d with the blob. They never appear as IL `ldstr` in the original method **when that method is actually virtualized**. Document: virtualized strings are VM-encoded, not passed through the AES/XOR string-encryption pass, because that pass skipped the `Select` set.

## Eligibility (skip unchanged)

A method is skipped (`SkippedItem.UnsupportedMethod` + debug log) when any of:

- No body; abstract; P/Invoke; runtime impl; native
- `.ctor` or `.cctor`
- Generic method or generic declaring type
- Exception handlers
- Runtime helper (`ObfuscatorHelpers.IsRuntimeHelper`) or global module type
- `[Obfuscation]` / exclusions reject `ObfuscationFeature.Virtualization`
- Any parameter, return, or local is byref, pointer, function pointer, typedref, or byref-like (`IsByRefLike`)
- Any parameter, return, or local is a value type outside the primitive family (`I1`/`U1`/`I2`/`U2`/`I4`/`U4`/`I8`/`U8`/`R4`/`R8`/`Boolean`/`Char`) — includes custom structs, generic structs, and `Nullable<T>`. Class/array/string params, returns, and locals are allowed (`O`)
- Encoder hits an unsupported opcode, a generic method call (`MethodSpec`), `newobj` of a value type, or a stack-height mismatch at a join
- Argument or local index does not fit `u8` (256)

Encodable types: void; the primitive family above; `object`, `string`, class types, interfaces, arrays of encodable types. `void Foo(Point p)` and `int? Bar(int? x)` skip.

Selection order is deterministic: `DeclaringType.FullName`, then `Method.FullName`. Successful encodes count toward `maxMethods` (1–256, default 32). Further *eligible* methods after the cap produce the existing truncation warning. Zero encodes produce the existing “no eligible methods” warning and **success**.

## ISA (internal opcodes)

Stack-based. Operands are little-endian. Branch targets are `u16` offsets from the start of that method’s bytecode (same as today’s VM). Member indices are `u16` into the importer tables.

| Group | Ops | Encoded size |
|-------|-----|----------------|
| Constants / locals | `LdcI4` + i32, `LdcI8` + i64, `LdcR4` + f32 bits, `LdcR8` + f64 bits, `Ldnull`, `Ldstr` + u16 len + utf8, `Ldarg`/`Starg`/`Ldloc`/`Stloc` + u8 | `LdcI4`/`LdcR4` = 5; `LdcI8`/`LdcR8` = 9; `Ldnull` = 1; `Ldstr` = 3 + len; arg/loc = 2 |
| Stack | `Dup`, `Pop` | 1 |
| Arith | `Add`, `Sub`, `Mul`, `Div`, `Rem`, `DivUn`, `RemUn`, `And`, `Or`, `Xor`, `Not`, `Neg`, `Shl`, `Shr`, `ShrUn` | 1 |
| Conv | `ConvI4`, `ConvI8`, `ConvR4`, `ConvR8`, `ConvU4`, `ConvU8` | 1 |
| Compare | `Ceq`, `Cgt`, `CgtUn`, `Clt`, `CltUn` | 1 |
| Branch | `Br`, `Brtrue`, `Brfalse`, `Beq`, `Bne`, `Blt`, `Ble`, `Bgt`, `Bge`, and unsigned `BltUn`/`BleUn`/`BgtUn`/`BgeUn`. **No `switch`.** | 3 (`opcode` + `u16`) |
| Objects | `Newobj`, `Call`, `Callvirt` + u16 table index; `CallVm` + u16 id + u8 argc; `Ldfld`/`Stfld`/`Ldsfld`/`Stsfld` + u16 | Call/Newobj/field = 3; `CallVm` = 4. `argc` includes `this` for instance `call` |
| Types / arrays | `Box`, `UnboxAny`, `Castclass`, `Isinst`, `Newarr` + u16 type index; `Ldlen`; `LdelemI4`/`I8`/`R4`/`R8`/`Ref`, `Stelem` same | typed = 3; `Ldlen`/ldelem/stelem = 1 |
| Control | `Throw`, `Ret` | 1 |

CIL prefixes `volatile` / `unaligned` **and `nop`** are ignored (the following instruction is encoded). Debug e2e compiles (`OptimizationLevel.Debug`) emit `nop`; skipping them is required so today’s `Add(int,int)` still encodes. Everything else, including `switch`, `leave`, `constrained`, `calli`, `localloc`, `initobj`, address-loads, `tail`, `jmp`, `cpblk`, `initblk`, `ldtoken`, `ldftn`, is **unsupported** → skip the method.

Normalize `ldc.i4.s` / `ldc.i4.0`… / `ldarg_0`… / `ldloc.s` the way the current encoder already does.

`VmIsa.EncodedSize(blob, offset)` returns the byte length of the instruction at `offset` (reads the `u16` length for `Ldstr`). `VmSeed.Apply` uses only this function to find opcode positions.

### Calls

Two-pass: assign ids to the selected set, then encode.

- `call` whose resolved `MethodDef` is in the selected set → `CallVm` + `u16` id + `u8` argc (push a frame; do not `Invoke` the stub). `argc` is `Method.Parameters.Count` including `this` for instance `call`.
- Any `callvirt`, or `call` to a non-virtualized method, or a `MemberRef` → `Call` / `Callvirt` + method-table index. Handler uses `MethodBase.Invoke`, unwraps `TargetInvocationException` and throws `InnerException` (or the original if inner is null).
- A `MethodSpec` (generic method call, including `List<T>.Add`) → skip the **caller** (`UnsupportedOpcode`). Do not encode it as `Call` + `Invoke`.
- `newobj` of a class → constructor `Invoke`. `newobj` of a value type → skip the method.

### Value stack

```csharp
enum VmType : byte { I4, I8, R4, R8, O }

struct VmValue
{
    public VmType Type;
    public long Bits;    // I4/I8/R4/R8
    public object Ref;   // O
}
```

Handlers throw `InvalidOperationException` with prefix `"Obfy VM: "` on underflow, overflow, invalid opcode, branch out of range, or type mismatch. Same policy as the current interpreter (fail at runtime, not at obfuscation time).

Frames:

```csharp
struct Frame
{
    public int MethodId;
    public int Ip;
    public VmValue[] Args;
    public VmValue[] Locals;
    public VmValue[] Stack;
    public int Sp;
}
```

`Run` loops on a `Frame[]` plus an integer depth (explicit, not C# recursion through `Run`, and not `Stack<Frame>`).

On entry to a method, boxed `args` become `VmValue`s: boxed primitives (`bool`, `char`, integer and floating types) unbox into `Bits` with the matching `VmType`; **everything else** — including `this`, `string`, class references, arrays, and `null` — is `VmType.O`.

On `Ret`, box the top of stack using `returnTypes[methodId]` (`bool` → `bool`, `uint` → `uint`, `int` → `int`, refs as-is). Void: return `null`.

On-the-fly fetch: `internalOp = opMap[code[ip] ^ xorKey[ip % 8]]`. Do not decrypt the whole blob at `Init`.

## Settings, CLI, UI, gating

Settings stay:

```json
{
  "virtualization": {
    "enabled": false,
    "maxMethods": 32
  }
}
```

`MaxMethods` still 1–256; out of range **fails** the run (existing behavior). No `seed` field.

- CLI `--virtualize`: keep the flag; change description from “simple static int methods” to “Enable IL virtualization of eligible methods (no EH/generic calls/byref/custom structs; CoreCLR only)”.
- UI `SettingsPanel.xaml` toggle: Content `"Virtualize methods"`, tooltip matching Techniques.md limits. Help catalog line “Virtualize simple methods” → “Virtualize methods”.
- Presets: all leave `enabled = false` (existing `ApplyPreset_Aggressive_ResetsPackingVirtualizationAndIncremental` stays valid). Aggressive’s enum description (“all techniques enabled”) is already inaccurate for packing/virtualization/incremental; PR 8 does not need to redefine Aggressive — virtualization stays a deliberate opt-in.

**Gating.** Add `RuntimeProfileGating.AllowsManagedVm(profile)` which is true only for `RuntimeProfile.Default`. `Apply` sets `settings.Virtualization.Enabled = false` and warns when the profile is NativeAOT / Unity IL2CPP / Blazor WASM, same pattern as method encryption. `VirtualizationObfuscator.IsEnabled` remains `settings.Virtualization.Enabled` (gating already mutated the clone).

## Pipeline interactions

| Pass | Interaction |
|------|-------------|
| String encryption (10) / constant encryption (11) | When virtualization is enabled, skip the `VmEncoder.Select` set so those methods still have `ldstr` / `ldc.*`. Virtualized literals then live in the XOR blob. Methods that lose the `maxMethods` race still go through string/constant encryption. |
| Method encryption (25) | May XOR stub IL (Windows, Default profile). Must not XOR `Vm` (`EncryptIl = false`). |
| Control flow (30) | Stubs: whatever the flattener does is fine. `Vm.Run`: flatten if registered; fallback documented above. |
| Reference proxy (40) | Runs after; may proxy calls *from* stubs. `CanProxy` returns false when the resolved declaring type is in `InjectedHelperMap` or `ObfuscatorHelpers.IsRuntimeHelper` — do not proxy `Vm.Run`. |
| Symbol renaming (50) | Renames `Vm` and user types. Member tables use tokens, not names. |
| Incremental cache | Unchanged. Seed **is** the cache key (hex-decoded, no second hash), so a hit is the same encoding. |
| Packing | Unchanged managed launcher. |

Source mode: virtualization stays assembly-only (`SupportsTargetType` = Assembly).

## Error handling

| Situation | Result |
|-----------|--------|
| Method ineligible / unsupported opcode | Skip; `SkippedItem`; run succeeds |
| `maxMethods` reached with more eligible methods | Warning; run succeeds |
| Enabled but zero encodes | Warning; run succeeds |
| `MaxMethods` not in 1–256 | `ObfuscationResult.Failed` |
| Embedded runtime missing / import throws | `ObfuscationResult.Failed` (`Virtualization failed: …`) |
| Profile gates it off | Warning from `RuntimeProfileGating`; obfuscator does not run |
| Interpreter fault | `InvalidOperationException` at runtime |
| User code throws through `Invoke` | Inner exception rethrown |

## Testing

Replace and extend `Tests/Obfy.Tests/VirtualizationObfuscatorTests.cs`. Existing tests that asserted “instance methods unchanged” and “unsigned compare skipped” are **wrong** under this spec and must be rewritten: instance methods and `cgt.un` / `bgt.un` are encoded when otherwise eligible.

**Encoder (no execute)**

- Skip: EH, generic type/method, generic **call** (`MethodSpec`), byref param, custom-struct / `Nullable<T>` param, return, or local, `.ctor`, `switch`, `nop`-only methods still encode (nops ignored), unsupported opcode, `[Obfuscation(Exclude=true, Feature="virtualization")]`.
- Encode: i4 add (regression of today’s happy path), i8/r4/r8 arith, unsigned compare, instance method, `ldstr`, `call` to non-virtualized, `callvirt`, `newobj` class, field get/set, `CallVm` when A `call`s virtualized B (`opcode + u16 id + u8 argc`).
- Stack-height mismatch at a join → skip.
- `maxMethods` truncation warning; zero eligible warning; `MaxMethods = 0` fails.

**Execute (write module + collectible `AssemblyLoadContext`, same pattern as `EndToEndObfuscationTests`)**

- Encoded methods return the same values as the original C# (arith, branches, string concat via `ldstr` + `call`, instance field, virtual `callvirt` dispatch to a subclass whose override is **not** `CallVm`’d).
- `bool` and `uint` return types (`bool Gt(int a, int b) => a > b`; `uint Id(uint x) => x`) — must not throw `InvalidCastException`.
- Instance `this`: `public int GetN() => N;` on a class instance, invoked through the stub.
- `CallVm`: static A calls static B, both virtualized; B’s body is not entered via the stub.
- Two different input hashes → different `opMap` / blob; results match.
- Same seed → identical blob.
- `VmSeed.Apply` then run: `Hi()` returning `"hi"` (`Ldstr` variable length) and a branched method, not only `Add`.
- Full pipeline: virtualization + string encryption + control flow + rename; load and run `Add(int,int)` **and** a method returning a string longer than `MinStringLength` (default 3), e.g. `"hello"`.
- NativeAOT profile: virtualization disabled, original method body remains.

**Decompiler**

- After virtualization, the original method’s instructions contain `call` to `Run` and do not contain the original `add`/`ldstr` sequence (`DecompilerResistanceTests` style).

**Gating / settings / CLI / UI / injection**

- `RuntimeProfileGatingTests` (or existing gating tests): NativeAOT clears `Virtualization.Enabled`.
- CLI description test only if one exists; parse `--virtualize` already covered.
- `SettingsViewModel` mapping unchanged (still a bool). Tooltip/help catalog tests if they snapshot copy.
- `RuntimeInjectionTests.Virtualization_RegistersInterpreterOptions` is rewritten: type `Vm`, method `Run`, `FlattenControlFlow: true` (or the documented false fallback), `EncryptIl: false`, `Rename: true`.

**Reference proxy**

- With proxy + virtualization, `Vm.Run` is not rewritten to a `<RefProxy>` call.

## Documentation to update (same release)

1. `docs/Techniques.md` — replace the stale “ldc.i4/ldarg/add/sub/mul/ret only” paragraph with this ISA and limits. (`Techniques.md` is already behind the int VM.)
2. `docs/Configuration.md` — eligibility table.
3. `docs/CLI.md` — `--virtualize` description.
4. `docs/Competitive-Analysis.md` — Code virtualization **Yes** with a limits footnote that names the skip set (EH, generic methods/types/**calls**, byref, custom structs/`Nullable<T>`, switch, typeof, interpolators, foreach/using; CoreCLR; per-build encoding not a unique generated VM; not a native packer). Do not write the footnote as “we don’t virtualize generic method *bodies*” while leaving generic *calls* implied-supported. Executive summary: Obfy meets the **managed-VM** protection-ceiling bar (Babel-class); it is still not a native packer or a licensing/RASP product. Do not claim “covers Reactor.”
5. `docs/Roadmap.md` — PF-08: general VM v1 done; remaining = EH/generics/byref and Approach B mutation. PF-09 native packer still Future.
6. `CHANGELOG.md`, `README.md` / `CLAUDE.md` technique table.
7. UI tooltip + `HelpCatalog` + CLI option description.
8. `VirtualizationSettings` and `VirtualizationObfuscator` XML comments — drop “static int only”.

Honesty line (keep in Techniques and Competitive-Analysis): virtualization embeds an interpreter; it is a deterrent, not confidentiality.

## File map

| File | Role |
|------|------|
| Create `Src/Obfy.VmRuntime/Obfy.VmRuntime.csproj` | netstandard2.0 runtime |
| Create `Src/Obfy.VmRuntime/Vm.cs` | `Run` / `Init` / frames / handlers |
| Modify `Src/Obfy.Core/Obfy.Core.csproj` | ProjectReference (compile) + `EmbeddedResource` of the runtime DLL |
| Create `Src/Obfy.Core/Virtualization/VmIsa.cs` | Internal opcode enum + `EncodedSize` |
| Create `Src/Obfy.Core/Virtualization/VmEncoder.cs` | Eligibility + two-pass encode |
| Create `Src/Obfy.Core/Virtualization/VmSeed.cs` | Cache-key seed, `opMap`, `xorKey`, `Apply` |
| Create `Src/Obfy.Core/Virtualization/VmImporter.cs` | Load embed, import, `.cctor`, register helper |
| Modify `VirtualizationObfuscator.cs` | Orchestration; delete handwritten interpreter; XML comment |
| Modify `StringEncryptionObfuscator.cs` / `ConstantEncryptionObfuscator.cs` | Skip `Select` set when virtualization is enabled |
| Modify `ReferenceProxyObfuscator.CanProxy` | Skip injected helpers / `Obfy.Runtime` |
| Modify `RuntimeProfileGating.cs` | `AllowsManagedVm` + disable + warning |
| Modify `ObfuscationAttributeRules.cs` | `ObfuscationFeature.Virtualization` |
| Modify `ObfySettings.cs` | `VirtualizationSettings` XML comment |
| Modify `RuntimeInjection.cs` | Only if a new helper option is required (prefer reusing Flatten/Rename/EncryptIl) |
| Modify `Obfy.sln` | Add VmRuntime project |
| Tests as above | including `RuntimeInjectionTests` |
| Docs / CLI / UI copy as above | |

`Obfy.VmRuntime` must not take a dependency on Autofac, dnlib, or NLog. Handlers stay ordinary C# so rename + flatten can process them. Static fields initialize to empty arrays (`Array.Empty<T>()`) so CS8618 does not fail the build under `TreatWarningsAsErrors`.

## Locked decisions (this review)

These were open contradictions; they are now locked:

1. Virtualization stays at 24. String/constant encryption skip the `Select` set so `ldstr` still reaches the encoder.
2. `RuntimeInjection.Register` only after the type move; never `AddType`.
3. `CallVm` is opcode + `u16` id + `u8` argc (`argc` includes `this`).
4. Ignore `nop` as well as `volatile` / `unaligned`.
5. Frame stack is `Frame[]` + depth (corlib only).
6. `Ret` boxes via `returnTypes[id]`; entry maps non-primitives including `this` to `O`.
7. Generic calls (`MethodSpec`) skip; Competitive-Analysis Yes footnote lists the full skip set.
8. Any non-primitive valuetype param/return/local skips (`Nullable<T>` included).
9. Do not proxy `Vm.Run` (`CanProxy` skips registered helpers).
10. Seed is hex-decoded `ComputeKey`; in-memory prefix is `"obfy-cache|"`.

## Open questions

None. Product target, coverage bar, host, architecture, mutation, seed, errors, presets, pipeline order, ISA layout, and the items above were locked in design review.

## PR Plan

Each PR is independently reviewable and leaves `dotnet test` green.

### PR 1 — Runtime project + importer smoke

**Title:** `feat: add Obfy.VmRuntime and embed it for dnlib import`  
**Files:** `Obfy.VmRuntime`, `Obfy.Core.csproj`, `VmImporter` skeleton, `Obfy.sln`, a test that imports `Vm` into an empty module and finds `Run`.  
**Depends on:** none  
**Description:** No encoder yet. `Run` can throw `NotSupportedException`. Proves embed + corlib retarget. `Init` includes `returnTypes`. Static fields are initialized. Runtime uses only corlib types (`Frame[]`, not `Stack<T>`).

### PR 2 — Encoder eligibility and ISA bytes

**Title:** `feat: encode eligible CIL into the Obfy VM ISA`  
**Files:** `VmIsa.cs`, `VmEncoder.cs`, encoder unit tests (skip + encode, no execute).  
**Depends on:** PR 1  
**Description:** Two-pass id assignment with `CallVm` as opcode + u16 id + u8 argc. `EncodedSize` locked. Ignores `nop`. Skips non-primitive valuetype params/returns/locals and `MethodSpec`. Does not mutate method bodies.

### PR 3 — Interpreter: stack, arith, branches, ldstr

**Title:** `feat: execute VM arith, branches, and encoded strings`  
**Files:** `Vm.cs` handlers, execute tests via ALC.  
**Depends on:** PR 2  
**Description:** `Init` + fetch with identity `opMap` and zero `xorKey` is enough here. Stubs still produced by a test helper, not the obfuscator. `bool`/`uint` returns and instance `this` are in scope as soon as `Ret`/entry boxing exists.

### PR 4 — Object model

**Title:** `feat: VM calls, fields, newobj, and token tables`  
**Files:** `Vm.cs`, `VmImporter` `.cctor` tables, execute tests for instance fields, `callvirt` dispatch, `newobj`.  
**Depends on:** PR 3  

### PR 5 — CallVm frames + arrays / box / cast / throw

**Title:** `feat: VM frames for virtualized calls and remaining object ops`  
**Files:** `Vm.cs` frame loop, encoder `CallVm`, execute tests.  
**Depends on:** PR 4  

### PR 6 — Per-build map, XOR, profile gating

**Title:** `feat: permute VM opcodes from the incremental-cache seed`  
**Files:** `VmSeed.cs`, fetch XOR, `RuntimeProfileGating`, gating tests, seed-stability tests including `Apply` on `Ldstr` and branches.  
**Depends on:** PR 3 (can land parallel to 4–5 once fetch is in)  

### PR 7 — Replace VirtualizationObfuscator + pipeline e2e

**Title:** `feat: virtualize typical methods through the assembly pipeline`  
**Files:** `VirtualizationObfuscator.cs` rewrite, attribute feature, string/constant encryption skip of the `Select` set, `ReferenceProxyObfuscator.CanProxy`, rewrite of `VirtualizationObfuscatorTests`, `RuntimeInjectionTests`, full-pipeline ALC test (including a string longer than `MinStringLength`), decompiler stub assertion. Delete int-only `CreateExecute`.  
**Depends on:** PRs 2–6  

### PR 8 — Docs and product copy

**Title:** `docs: describe general IL virtualization limits honestly`  
**Files:** Techniques, Configuration, CLI, Competitive-Analysis, Roadmap, CHANGELOG, README/CLAUDE, CLI option text, `SettingsPanel.xaml`, `HelpCatalog.cs`, `VirtualizationSettings` / `VirtualizationObfuscator` XML comments.  
**Depends on:** PR 7 (behavior must already match the copy)

## Tier 3 leftover (not this spec)

After this spec ships, Obfy is a **managed-VM** tier-3 tool on CoreCLR, still without:

1. **Native packer (PF-09)** — replace `{name}.launcher.exe` with an unmanaged Windows host. Separate design; medium value, very high cost.
2. **VM v2** — EH, generics (including calls), byref, `switch`, Approach B handler generation, NativeAOT-safe call (no `MethodBase.Invoke`).
3. **Adoption, not ceiling** — MSBuild PackageReference, documented Linux CI, VS Code TaskProvider (Competitive-Analysis near-term list).

Do not start (1) or (2) until this v1 has execute tests on real assemblies and the Partial→Yes matrix change has been sitting in Competitive-Analysis without a caveat that the VM is still int-only.

# Configuration Reference

Complete JSON configuration schema for Obfy.

## Schema Overview

```json
{
  "$schema": "https://raw.githubusercontent.com/mortenbrudvik/Obfy/main/schemas/obfy.schema.json",
  "level": "standard",
  "stringEncryption": { ... },
  "constantEncryption": { ... },
  "resourceEncryption": { ... },
  "controlFlow": { ... },
  "symbolRenaming": { ... },
  "protection": { ... },
  "metadata": { ... },
  "assemblyMerge": { ... },
  "dependencyEmbedding": { ... },
  "inclusions": { ... },
  "exclusions": { ... },
  "watermark": { "enabled": false, "id": "" },
  "runtimeProfile": "Default",
  "signing": { "enabled": false, "keyFile": "", "passwordEnvironmentVariable": "" },
  "incremental": { "enabled": false },
  "packing": { "enabled": false, "rid": "win-x64" },
  "virtualization": { "enabled": false, "maxMethods": 32 },
  "postBuildEnabled": false
}
```

Generated configs include `"$schema"` pointing at [`schemas/obfy.schema.json`](../schemas/obfy.schema.json) so editors can validate and autocomplete. Extra properties such as `$schema` are ignored when loading. `obfy config generate` writes PascalCase enum names (`"level": "Standard"`, `"algorithm": "Aes256"`) and omits null signing paths.

A `-c` file is authoritative: `level` in the file is not re-applied as a preset. Copy the Minimal/Standard/Aggressive snippets only if you also include the flags you care about (or generate with `obfy config generate -l …`).

## Complete Schema

The following block is a grammar of keys and enum values, not a valid `obfy.json` (do not copy the `"minimal | standard | …"` unions). Example files further down are valid JSON.

```json
{
  "$schema": "https://raw.githubusercontent.com/mortenbrudvik/Obfy/main/schemas/obfy.schema.json",
  "level": "minimal | standard | aggressive | custom",

  "stringEncryption": {
    "enabled": true,
    "algorithm": "Aes256 | Xor",
    "encryptConstantStrings": true,
    "encryptResourceStrings": true,
    "minStringLength": 3
  },

  "constantEncryption": {
    "enabled": false,
    "algorithm": "Xor | Aes256",
    "encryptIntegers": true,
    "encryptLongs": true,
    "encryptFloats": true,
    "encryptDoubles": true,
    "integerThreshold": 2,
    "longThreshold": 2,
    "skipCommonFloats": true,
    "skipCommonDoubles": true
  },

  "resourceEncryption": {
    "enabled": false,
    "algorithm": "Aes256 | Xor",
    "includePatterns": ["*"],
    "excludePatterns": ["*.resources", "Obfy.Embedded.*"]
  },

  "controlFlow": {
    "enabled": false,
    "mode": "Switch | OpaquePredicate | Combined",
    "intensity": 50
  },

  "symbolRenaming": {
    "enabled": true,
    "mode": "Unreadable | Sequential | Hash | Random",
    "renameTypes": true,
    "renameMethods": true,
    "renameProperties": true,
    "renameFields": true,
    "renameParameters": true,
    "renameEvents": true,
    "renameNamespaces": true,
    "preservePublicApi": false,
    "preserveXaml": false
  },

  "protection": {
    "antiDebug": false,
    "antiTamper": {
      "enabled": false,
      "checkEntryPoint": true,
      "checkModuleInitializer": true
    },
    "antiDecompiler": {
      "enabled": false,
      "injectJunkTypes": true,
      "addSuppressIldasmAttribute": true,
      "addDecoyAttributes": true,
      "junkTypeCount": 5,
      "junkMethodsPerType": 3
    },
    "antiDump": false,
    "referenceProxy": false,
    "proxyExternalCalls": false,
    "methodEncryption": false
  },

  "dependencyEmbedding": {
    "enabled": false,
    "includePatterns": ["*.dll"],
    "excludePatterns": ["*.resources.dll"]
  },

  "metadata": {
    "removeDebugInfo": true,
    "removeAttributes": true,
    "stripDocumentation": true
  },

  "assemblyMerge": {
    "enabled": false,
    "internalize": true,
    "preserveDebugInfo": false,
    "searchDirectories": [],
    "excludePatterns": []
  },

  "inclusions": {
    "namespaces": [],
    "types": [],
    "methods": []
  },

  "exclusions": {
    "namespaces": [],
    "types": [],
    "methods": [],
    "attributes": [
      "SerializableAttribute",
      "DataContractAttribute",
      "DataMemberAttribute",
      "JsonPropertyNameAttribute",
      "JsonPropertyAttribute",
      "XmlElementAttribute",
      "XmlAttributeAttribute"
    ]
  },

  "watermark": {
    "enabled": false,
    "id": ""
  },

  "runtimeProfile": "Default",
  "signing": {
    "enabled": false,
    "keyFile": "",
    "passwordEnvironmentVariable": ""
  },

  "incremental": {
    "enabled": false
  },

  "packing": {
    "enabled": false,
    "rid": "win-x64"
  },

  "virtualization": {
    "enabled": false,
    "maxMethods": 32
  },

  "postBuildEnabled": false
}
```

## Protection Levels

### Minimal

Symbol renaming only - fastest with least protection.

```json
{
  "level": "minimal",
  "stringEncryption": { "enabled": false },
  "controlFlow": { "enabled": false },
  "symbolRenaming": { "enabled": true },
  "protection": { "antiDebug": false },
  "metadata": { "removeDebugInfo": true, "removeAttributes": false }
}
```

### Standard (Default)

Balanced protection with string encryption and symbol renaming.

```json
{
  "level": "standard",
  "stringEncryption": { "enabled": true },
  "controlFlow": { "enabled": false },
  "symbolRenaming": { "enabled": true },
  "protection": { "antiDebug": false },
  "metadata": { "removeDebugInfo": true }
}
```

### Aggressive

Most protections on (intensity 80). Does not enable watermark, packing, incremental, virtualization, embedding, or `proxyExternalCalls`.

```json
{
  "level": "aggressive",
  "stringEncryption": { "enabled": true },
  "constantEncryption": { "enabled": true },
  "resourceEncryption": { "enabled": true },
  "controlFlow": {
    "enabled": true,
    "intensity": 80
  },
  "symbolRenaming": { "enabled": true },
  "protection": {
    "antiDebug": true,
    "antiTamper": { "enabled": true },
    "antiDecompiler": { "enabled": true },
    "antiDump": true,
    "referenceProxy": true,
    "methodEncryption": true
  },
  "metadata": {
    "removeDebugInfo": true,
    "removeAttributes": true
  }
}
```

Aggressive enables the flags above at intensity **80** (not 100). It does **not** turn on `proxyExternalCalls`, watermark, packing, incremental, virtualization, dependency embedding, or signing.

### Custom

Full control over individual settings.

```json
{
  "level": "custom"
}
```

## Settings Reference

### stringEncryption

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `true` | Enable string encryption |
| `algorithm` | enum | `Aes256` | Encryption algorithm: `Aes256` or `Xor` |
| `encryptConstantStrings` | bool | `true` | Encrypt string literals in code |
| `encryptResourceStrings` | bool | `true` | Encrypt embedded resource strings |
| `minStringLength` | int | `3` | Minimum string length to encrypt (1-100) |

**Algorithm Comparison:**

| Algorithm | Security | Performance | Use Case |
|-----------|----------|-------------|----------|
| `Aes256` | High | Slower | Production, sensitive data |
| `Xor` | Medium | Faster | Development, less sensitive code |

### constantEncryption

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Enable constant encryption |
| `algorithm` | enum | `Xor` | Encryption algorithm: `Xor` or `Aes256` |
| `encryptIntegers` | bool | `true` | Encrypt int constants |
| `encryptLongs` | bool | `true` | Encrypt long constants |
| `encryptFloats` | bool | `true` | Encrypt float constants |
| `encryptDoubles` | bool | `true` | Encrypt double constants |
| `integerThreshold` | int | `2` | Skip integers where \|value\| < threshold |
| `longThreshold` | long | `2` | Skip longs where \|value\| < threshold |
| `skipCommonFloats` | bool | `true` | Skip 0.0f, 1.0f, -1.0f |
| `skipCommonDoubles` | bool | `true` | Skip 0.0, 1.0, -1.0 |

**Algorithm Recommendation:**

XOR is recommended for constants because they are accessed frequently at runtime. AES-256 provides stronger encryption but adds more overhead.

**Threshold Settings:**

Thresholds prevent encrypting ubiquitous values like 0, 1, and -1 which appear frequently in loops and conditionals:
- `integerThreshold: 2` skips -1, 0, 1
- `integerThreshold: 10` skips -9 through 9
- `skipCommonFloats/Doubles` skips 0.0, 1.0, -1.0 regardless of threshold

### resourceEncryption

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Enable resource encryption |
| `algorithm` | enum | `Aes256` | Encryption algorithm: `Aes256` or `Xor` |
| `includePatterns` | string[] | `["*"]` | Glob patterns for resources to include |
| `excludePatterns` | string[] | `["*.resources", "Obfy.Embedded.*"]` | Glob patterns for resources to exclude. `Obfy.Embedded.*` keeps packed dependency DLLs plaintext; the obfuscator also hard-skips that prefix. |

**Pattern Examples:**

| Pattern | Matches |
|---------|---------|
| `*` | All resources |
| `*.json` | All JSON files |
| `*.config` | All config files |
| `Config.*` | Resources starting with "Config." |
| `*.resources` | .NET resource files (typically excluded) |

**Note:** Exclude patterns take precedence over include patterns. System resources (`*.resources`) should typically be excluded to avoid runtime issues. Packed dependency resources (`Obfy.Embedded.*`) are also excluded by default and hard-skipped even if this list is overwritten.

### controlFlow

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Enable control flow obfuscation |
| `mode` | enum | `Switch` | Obfuscation mode |
| `intensity` | int | `50` | Complexity level (0-100) |

**Mode Options:**

| Mode | Description |
|------|-------------|
| `Switch` | Convert linear code to switch-based state machine |
| `OpaquePredicate` | Insert always-true/false conditional branches |
| `Combined` | Apply both switch flattening and opaque predicates |

### symbolRenaming

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `true` | Enable symbol renaming |
| `mode` | enum | `Unreadable` | Naming style for renamed symbols |
| `renameTypes` | bool | `true` | Rename classes, structs, enums |
| `renameMethods` | bool | `true` | Rename methods |
| `renameProperties` | bool | `true` | Rename properties |
| `renameFields` | bool | `true` | Rename fields |
| `renameParameters` | bool | `true` | Rename method parameters |
| `renameEvents` | bool | `true` | Rename events and add_/remove_ accessors |
| `renameNamespaces` | bool | `true` | Rename namespaces (public namespaces kept when `preservePublicApi`) |
| `preservePublicApi` | bool | `false` | Keep public members unchanged |
| `preserveXaml` | bool | `false` | Keep public instance properties on `*ViewModel`/`*View`/INPC/`DependencyProperty` types. Desktop wizard and Settings panel can turn this on. |

**Naming Modes:**

| Mode | Example Output | Description |
|------|----------------|-------------|
| `Unreadable` | `_‌‌‍‏‌` | Zero-width and confusing characters |
| `Sequential` | `a`, `b`, `aa` | Short, sequential names |
| `Hash` | `_8a5f2c1d` | Salted SHA-256 of original name (per run) |
| `Random` | `kQzX7pL9mNv` | Random alphanumeric strings |

### protection

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `antiDebug` | bool | `false` | Inject debugger detection checks |
| `antiDump` | bool | `false` | Wipe PE headers in memory at load and in-process first-byte `0xC3` patch of `dbghelp!MiniDumpWriteDump` (Windows X86/X64; ARM64 skipped). External dumpers are unaffected. Gated off NativeAOT / IL2CPP / Blazor WASM. |
| `referenceProxy` | bool | `false` | Hide in-module call targets behind proxy methods |
| `proxyExternalCalls` | bool | `false` | Also proxy selected out-of-module calls. Ignored unless `referenceProxy` is true. |
| `methodEncryption` | bool | `false` | XOR method IL in the PE (Windows) |

**antiTamper Settings:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `antiTamper.enabled` | bool | `false` | Enable anti-tampering protection |
| `antiTamper.checkEntryPoint` | bool | `true` | Verify integrity at entry point |
| `antiTamper.checkModuleInitializer` | bool | `true` | Verify integrity at module init |

**antiDecompiler Settings:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `antiDecompiler.enabled` | bool | `false` | Enable anti-decompiler protection |
| `antiDecompiler.injectJunkTypes` | bool | `true` | Inject decoy types with dead code |
| `antiDecompiler.addSuppressIldasmAttribute` | bool | `true` | Add SuppressIldasm attribute |
| `antiDecompiler.addDecoyAttributes` | bool | `true` | Inject pinned `ConfusedByAttribute` / `DotfuscatorAttribute` (name-based detector bait; does not block de4dot) |
| `antiDecompiler.junkTypeCount` | int | `5` | Number of junk types to inject (1-50) |
| `antiDecompiler.junkMethodsPerType` | int | `3` | Junk methods per type (1-20) |

### metadata

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `removeDebugInfo` | bool | `true` | Remove PDB references and debug symbols |
| `removeAttributes` | bool | `true` | Remove custom attributes |
| `stripDocumentation` | bool | `true` | Strip XML documentation |

### assemblyMerge

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Enable assembly merging |
| `internalize` | bool | `true` | Make merged types internal (improves obfuscation) |
| `preserveDebugInfo` | bool | `false` | Preserve debug information in merged assembly |
| `searchDirectories` | string[] | `[]` | Additional directories to search for dependencies |
| `excludePatterns` | string[] | `[]` | Assembly patterns to exclude from merging (e.g., `System.*`) |

**Use Cases:**

| Scenario | Configuration |
|----------|---------------|
| Merge app + libraries | `"enabled": true, "internalize": true` |
| Keep public APIs exposed | `"enabled": true, "internalize": false` |
| Exclude framework assemblies | `"excludePatterns": ["System.*", "Microsoft.*"]` |

### dependencyEmbedding

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Pack sibling `{AssemblyRef.Name}.dll` files as `Obfy.Embedded.*` resources and load via AssemblyResolve |
| `includePatterns` | string[] | `["*.dll"]` | File-name globs to include (empty means all) |
| `excludePatterns` | string[] | `["*.resources.dll"]` | File-name globs to skip (satellites). Exclude wins. |

Disabled automatically on `NativeAot`, `UnityIl2Cpp`, and `BlazorWasm`. Only files next to the input are packed.

**Use Cases:**

| Scenario | Configuration |
|----------|---------------|
| Embed sibling libraries | `"enabled": true` |
| Skip satellite assemblies | `"excludePatterns": ["*.resources.dll"]` (the default) |

### inclusions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `namespaces` | string[] | `[]` | Namespace patterns to allow (wildcards `*`, `?`) |
| `types` | string[] | `[]` | Type name patterns to allow |
| `methods` | string[] | `[]` | Method name patterns to allow |

Empty lists mean no allow-list. When any list is non-empty, only matching namespaces, types, or methods are candidates (OR). A method-only list still visits types that contain a matching method. Honored by renaming, control flow, strings, and constants. Exclusions still apply.

### exclusions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `namespaces` | string[] | `[]` | Namespace patterns to exclude |
| `types` | string[] | `[]` | Type name patterns to exclude |
| `methods` | string[] | `[]` | Method name patterns to exclude |
| `attributes` | string[] | See below | Attributes marking excluded members |

**Default Excluded Attributes:**
- `SerializableAttribute`
- `DataContractAttribute`
- `DataMemberAttribute`
- `JsonPropertyNameAttribute`
- `JsonPropertyAttribute`
- `XmlElementAttribute`
- `XmlAttributeAttribute`

### watermark

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Embed a customer/build identifier. Requires a non-whitespace `id`. |
| `id` | string | `""` | Plaintext identifier stored as the `WatermarkAttribute` constructor argument and `Id` field. Trimmed on validate. Do not put secrets in `id`. The type `Obfy.Runtime.WatermarkAttribute` is pinned against renaming. |

Opt-in; not flipped by level presets. CLI: `--watermark-id`. Settings panel has a Watermark expander.

### runtimeProfile

| Value | Effect |
|-------|--------|
| `Default` | All protections as configured |
| `NativeAot` | Disables method encryption, anti-dump, and dependency embedding; anti-debug omits kernel32 P/Invoke. Emits report warnings. |
| `UnityIl2Cpp` | Same gating; Unity wizard also excludes `UnityEngine`, `UnityEngine.*`, `Unity`, and `Unity.*` |
| `BlazorWasm` | Same gating for Blazor WebAssembly (no `AppDomain.AssemblyResolve` / `VirtualProtect`); anti-debug omits kernel32 P/Invoke. |

### signing

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Re-sign the output after obfuscation |
| `keyFile` | string | | Path to `.snk` or `.pfx` |
| `passwordEnvironmentVariable` | string | | Name of the env var that holds the PFX password. **Required for `.pfx`/`.p12`**; unused for `.snk`. Empty PFX passwords are not supported. |

Signing runs after PE patches (method-IL XOR, anti-tamper hash) by refreshing the strong-name blob in place. The run fails if signing is enabled and the key cannot be applied.

### incremental

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Skip re-obfuscation when the Obfy version, input file, and settings JSON are unchanged |

Cache file: `{outputPath}.obfycache` (SHA-256 of Obfy assembly version + input bytes + serialized settings). A version bump is a miss. A hit requires the output file to exist. If packing is on, a hit also requires `{name}.runtimeconfig.json` (win-x64) or the managed `{name}.launcher.exe` plus its `.runtimeconfig.json` (portable). Written only after a successful write. Off in every preset. CLI: `--incremental` turns `enabled` on; cache path and packing extras stay in the config.

### virtualization

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Replace eligible simple static `int` methods with a bytecode interpreter stub |
| `maxMethods` | int | `32` | Maximum methods to virtualize (1–256). Out of range fails the run (does not clamp) |

Eligible methods: `static`, non-generic, no exception handlers, `int` return and `int` parameters, ≤8 parameters, ≤16 int-sized locals. Body may include `ldc.i4`, `ldarg`, `ldloc`/`stloc`, `add`/`sub`/`mul`, `ceq`/`cgt`/`clt`, `ret`, and signed branches (`br`/`brtrue`/`brfalse`/`blt`/`bgt`/`ble`/`bge`/`beq`/`bne`). Unsigned compares are skipped so original IL is kept. This is **not** a general IL virtualizer. Off in every preset. CLI: `--virtualize` turns `enabled` on; `maxMethods` stays in the config.

### packing

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | After save, pack the output. Off in every preset. Config-only (no `--pack`). |
| `rid` | string | `win-x64` | `win-x64` replaces the obfuscated PE with a native CLR-host stub. `portable` writes the managed `{name}.launcher.exe` beside the PE. Unknown values fail the run. |

See [Packing (native win-x64 host)](#packing-native-win-x64-host) below. Config-only.

## Example Configurations

### Library with Public API

```json
{
  "level": "standard",
  "symbolRenaming": {
    "enabled": true,
    "preservePublicApi": true
  },
  "exclusions": {
    "namespaces": ["MyLibrary.PublicApi.*"]
  }
}
```

### Console Application

```json
{
  "level": "aggressive",
  "protection": {
    "antiDebug": true,
    "antiTamper": { "enabled": true }
  }
}
```

### Web API with Serialization

JSON/XML property attributes are excluded from renaming by default. A provided `attributes` array **replaces** the constructor defaults (it does not merge). Include the defaults plus extras:

```json
{
  "level": "standard",
  "exclusions": {
    "attributes": [
      "SerializableAttribute",
      "DataContractAttribute",
      "DataMemberAttribute",
      "JsonPropertyNameAttribute",
      "JsonPropertyAttribute",
      "XmlElementAttribute",
      "XmlAttributeAttribute",
      "ProtoMemberAttribute"
    ]
  }
}
```

### Maximum Obfuscation

```json
{
  "level": "aggressive",
  "stringEncryption": {
    "enabled": true,
    "algorithm": "Aes256",
    "minStringLength": 1
  },
  "constantEncryption": {
    "enabled": true,
    "algorithm": "Xor",
    "integerThreshold": 0,
    "longThreshold": 0,
    "skipCommonFloats": false,
    "skipCommonDoubles": false
  },
  "resourceEncryption": {
    "enabled": true,
    "algorithm": "Aes256",
    "excludePatterns": ["*.resources", "Obfy.Embedded.*"]
  },
  "controlFlow": {
    "enabled": true,
    "mode": "Combined",
    "intensity": 100
  },
  "symbolRenaming": {
    "enabled": true,
    "mode": "Unreadable"
  },
  "protection": {
    "antiDebug": true,
    "antiTamper": { "enabled": true },
    "antiDecompiler": {
      "enabled": true,
      "junkTypeCount": 10,
      "junkMethodsPerType": 5
    },
    "antiDump": true,
    "referenceProxy": true,
    "methodEncryption": true
  },
  "metadata": {
    "removeDebugInfo": true,
    "removeAttributes": true,
    "stripDocumentation": true
  }
}
```

## Virtualization (IL interpreter)

`virtualization.enabled` is **off** in every preset. CLI `--virtualize` turns it on. When true, Obfy replaces selected **static `int` methods** with a bytecode interpreter stub.

Supported IL: `ldc.i4`, `ldarg`, `ldloc`/`stloc` (≤16 int-sized locals), `add`/`sub`/`mul`, `ceq`/`cgt`/`clt`, `ret`, and signed branches (`br`/`brtrue`/`brfalse`/`blt`/`bgt`/`ble`/`bge`/`beq`/`bne`). At most 8 `int` parameters; no exception handlers or generics.

Unsigned compares (`cgt.un`, `blt.un`, …) are **skipped** so original IL is kept — encoding them as signed would miscompile `if (a != 0)` for negatives. Methods that cannot be encoded stay native and are reported as skipped. A warning is emitted when the feature is on but nothing was encoded, or when `maxMethods` truncates the set.

## Packing (native win-x64 host)

`packing.enabled` is a delivery option, not a protection level. It is **off** in every preset. Config-only; there is no `--pack` flag.

**Breaking:** `packing.enabled` now emits a native win-x64 host that replaces the obfuscated PE. Set `packing.rid` to `portable` for the previous managed `{name}.launcher.exe`.

When `enabled` is true and `rid` is `win-x64` (the default):

- The file at the output path is an unmanaged PE (no CLR directory).
- User IL is AES-256-CBC (PKCS7) ciphertext in an overlay; the host decrypts in memory and runs via `hostfxr`.
- There is no sibling `{name}.launcher.exe` and no second managed PE of the user assembly.
- A sibling `{name}.runtimeconfig.json` is written. The machine needs `Microsoft.NETCore.App` (framework-dependent).

When `rid` is `portable`:

- `{name}.launcher.exe` — managed console app (run with `dotnet`)
- `{name}.launcher.runtimeconfig.json`
- The original obfuscated file is not replaced.

Packing requires an entry point; class libraries fail the run with `Packing failed: ...`. Source inputs skip packing with a warning. NativeAOT / Unity IL2CPP / Blazor WASM turn packing off with a warning. Closed-set / solution / multi-assembly runs pack each entry-point output and leave libraries managed. Merge packs the merged entry-point assembly. Signing hashes the managed PE **before** pack, so the native EXE is not strong-named. GUI vs console is a PE Subsystem patch on the same stub. Runtimeconfig TFM prefers the assembly `TargetFrameworkAttribute` (then the input sibling JSON), otherwise the Obfy process version.

Not Pre-JIT, not self-contained, not ARM64. Packing hides the managed PE on disk; the decryption key is in the overlay. Anyone who runs or inspects the EXE can recover IL. This is obfuscation, not confidentiality.

## Generating Configuration Files

```bash
# Generate standard configuration
obfy config generate -o obfy.json

# Generate aggressive configuration
obfy config generate -l aggressive -o obfy-aggressive.json

# Generate minimal configuration
obfy config generate -l minimal -o obfy-minimal.json
```

Generated files use PascalCase enum names (`"Standard"`, `"Aes256"`, `"Switch"`) and omit null `signing.keyFile` / `passwordEnvironmentVariable`. They include the fully resolved flags for the requested level.

Generated files include a `$schema` property pointing at `https://raw.githubusercontent.com/mortenbrudvik/Obfy/main/schemas/obfy.schema.json` for editor validation.

## See Also

- [CLI Reference](CLI.md) - Command-line options
- [Techniques](Techniques.md) - How each technique works
- [Advanced](Advanced.md) - Exclusion patterns and best practices

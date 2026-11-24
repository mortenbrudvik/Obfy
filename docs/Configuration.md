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

Generated configs include `"$schema"` pointing at [`schemas/obfy.schema.json`](../schemas/obfy.schema.json) so editors can validate and autocomplete. Keys that start with `$` (such as `$schema`) are ignored when loading. Any other key that is not a setting is ignored too, but the command line prints a warning for it (so a misspelled technique does not silently run with its defaults). Key names are matched case-insensitively. `obfy config generate` writes PascalCase enum names (`"level": "Standard"`, `"algorithm": "Aes256"`) and omits null signing paths.

In the CLI a `-c` file is authoritative: `level` in the file is not re-applied as a preset. Copy the Minimal/Standard/Aggressive snippets only if you also include the flags you care about (or generate with `obfy config generate -l …`).

The desktop app handles a loaded file the same way in effect. When you open a configuration (Ctrl+L, or drop the `.json` file on the window) and the file's level-controlled settings are exactly what its `level` preset sets, the level card stays selected. When they are not (for example `"level": "standard"` with `controlFlow` also on), the app shows and runs the configuration as Custom, so a run uses the switches in the file and not the preset. The file on disk is not rewritten. The level-controlled settings are the protection switches, control flow mode and intensity, the constant encryption algorithm, the options that remove debug information and attributes, external call proxying, and virtualization, packing and the incremental cache (a preset turns those three off). Choosing the level card again applies the preset.

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

Symbol renaming and removal of debug information - fastest with least protection.

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

Balanced protection with string encryption, symbol renaming, debug information removal, and removal of the named compiler and debugger attributes.

```json
{
  "level": "standard",
  "stringEncryption": { "enabled": true },
  "controlFlow": { "enabled": false },
  "symbolRenaming": { "enabled": true },
  "protection": { "antiDebug": false },
  "metadata": { "removeDebugInfo": true, "removeAttributes": true }
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
| `preserveXaml` | bool | `false` | Keep public instance properties on `*ViewModel`/`*View`/INPC/`DependencyProperty` types. The Protection screen of the desktop app can turn this on, and a solution or project run turns it on for WPF, WinForms and MAUI projects. |

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
| `antiTamper.enabled` | bool | `false` | Enable anti-tampering protection. Gated off NativeAOT / IL2CPP / Blazor WASM. |
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
| `removeAttributes` | bool | `true` | Remove nine named compiler and debugger attributes: `CompilerGenerated`, `Nullable`, `NullableContext`, `DebuggerNonUserCode`, `DebuggerStepThrough`, `DebuggerHidden`, `DebuggerDisplay`, `DebuggerBrowsable`, and `SuppressMessage`. Your own attributes are not touched |
| `stripDocumentation` | bool | `true` | Strip XML documentation |

### assemblyMerge

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Enable assembly merging. On the command line this is the same as `--merge`: two or more input assemblies are merged into one. With a single input the setting is ignored (a warning is printed) |
| `internalize` | bool | `true` | Make merged types internal (improves obfuscation). `--internalize` on the command line overrides it only when typed |
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

Opt-in; not flipped by level presets. CLI: `--watermark-id`. The desktop app has a Watermark entry on the Protection screen.

### runtimeProfile

| Value | Effect |
|-------|--------|
| `Default` | All protections as configured |
| `NativeAot` | Disables method encryption, anti-dump, anti-tamper, dependency embedding, native packing, and virtualization; anti-debug omits kernel32 P/Invoke. Emits report warnings. |
| `UnityIl2Cpp` | Same gating; Unity wizard also excludes `UnityEngine`, `UnityEngine.*`, `Unity`, and `Unity.*` |
| `BlazorWasm` | Same gating for Blazor WebAssembly (no `AppDomain.AssemblyResolve` / `VirtualProtect`); anti-debug omits kernel32 P/Invoke; virtualization off (`MethodBase.Invoke`); native packing off. |

The desktop Protection screen greys out method encryption, anti-dump, anti-tamper, dependency embedding, virtualization and native packing for the three profiles other than `Default`, and keeps their stored values. The engine turns the same six off at run time and logs a warning for each.

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
| `enabled` | bool | `false` | Skip re-obfuscation when the Obfy build, input file, and settings JSON are unchanged and the output is still the file the last run wrote |

Cache file: `{outputPath}.obfycache`, a small JSON file. It holds the key (SHA-256 of the Obfy.Core build identity, the input bytes, the serialized settings, and the signing key file and embedded dependency bytes when those are in use); the statistics of the run that wrote the output; three counts (symbol-map entries, skipped items, warnings); and the length and SHA-256 of the output file and, for a packed output, of its `{name}.runtimeconfig.json` (win-x64) or its managed `{name}.launcher.exe` plus `.runtimeconfig.json` (portable). It holds numbers and hashes only: no names, paths or code, and never the symbol map, which lists the original names. The key is the cache identity only. It is not the native packing key.

A hit needs the key to match and every fingerprinted file to still match. A different Obfy build, input or setting is a miss. So is an output (or launcher file) that was replaced, edited or removed since the cache was written, for example the unprotected original copied over it by a build, and so is a locked, damaged or older-layout cache file: the run is done again, never an error. The most common legitimate case is changing the protected output after the run: Authenticode-signing it, or a post-build step that touches it, makes the next incremental run build it again. A hit returns the statistics of the run that wrote the output and says so (`Incremental: reused cached output`). It also says how many items that run skipped and how many warnings it raised, because their text is not kept.

When the caller needs the symbol map or a report, a hit is not taken if the cached run renamed symbols, and the result says why: the CLI's `--map` needs the map, `--report` needs the map and also the skipped items and warnings (so it also rebuilds when the cached run had any). The desktop app builds a report from every run and lists the symbols on its Results screen, so it asks for both: there the output is rebuilt whenever the cached run renamed symbols, skipped items or raised warnings, and the option only saves time when renaming is off and the run was clean.

The cache applies to a single assembly or a merge only. Closed-set (several assemblies, a solution or a project), source-file and directory runs do not use it: with `enabled` on, such a run says so once in a warning (`Incremental cache not used: it covers a single assembly or a merge, not a closed set (...)`, or `... not C# source (...)`; in a closed set it is on the first module's result) and obfuscates everything, and the desktop app shows the warning in its log. It is not used when the output replaces the input (`obfy app.dll -o <folder of app.dll>`); the run says so once. Written only after a successful write (including a successful pack). The entry is written to a temporary file next to the cache file and moved into place, so a crash in the middle of the write, or two runs on the same output, never leave a truncated entry, and an earlier valid entry survives a write that fails; a cache that cannot be written is a warning, not an error. Off in every preset. CLI: `--incremental` turns `enabled` on; cache path and packing extras stay in the config.

### virtualization

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Replace eligible methods with a bytecode interpreter stub. Enable with `--virtualize`. |
| `maxMethods` | int | `32` | Maximum methods to virtualize (1–256). Out of range fails the run (does not clamp) |

Eligible: instance and static methods; objects, non-generic calls, fields, `newobj`, `ldstr`; i4/i8/r4/r8. Skips EH, generic methods/types/**calls**, byref, custom structs/`Nullable<T>`, switch, constructors, `typeof` (`ldtoken`), interpolators that allocate `DefaultInterpolatedStringHandler`, and `using` / enumerator-struct foreach (array foreach can be encoded). Per-build opcode permutation + XOR. Gated off NativeAOT / Unity IL2CPP / Blazor WASM (`runtimeProfile`). Off in every preset. Enable with `--virtualize`. Deterrent, not confidentiality.

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

`virtualization.enabled` is **off** in every preset. Enable with `--virtualize`, the **Virtualization** switch on the desktop Protection screen, or the config flag. When true, Obfy replaces selected eligible **instance and static** methods with a bytecode interpreter stub.

Supported: objects, non-generic calls, fields, `newobj`, `ldstr`; i4/i8/r4/r8 arith; locals, arguments, signed and unsigned compares, branches.

Skipped (original IL is kept): exception handlers; generic methods, generic types, and generic **calls**; byref / `Span`; non-primitive valuetypes (custom structs, `Nullable<T>`, enums); `switch`; constructors; `typeof` (`ldtoken`); interpolators that allocate `DefaultInterpolatedStringHandler`; `using` / enumerator-struct foreach (array foreach can be encoded).

Per-build opcode permutation and XOR. Gated off NativeAOT / Unity IL2CPP / Blazor WASM (`runtimeProfile`). Not a unique generated VM and not a native packer. The interpreter is a deterrent, not confidentiality.

Methods that cannot be encoded stay native and are reported as skipped. A warning is emitted when the feature is on but nothing was encoded, or when `maxMethods` truncates the set. `maxMethods` out of range (not 1–256) fails the run.

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

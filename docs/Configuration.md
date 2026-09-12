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
  "inclusions": { ... },
  "exclusions": { ... },
  "postBuildEnabled": false
}
```

Generated configs include `"$schema"` pointing at [`schemas/obfy.schema.json`](../schemas/obfy.schema.json) so editors can validate and autocomplete. Extra properties such as `$schema` are ignored when loading.

## Complete Schema

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
    "excludePatterns": ["*.resources"]
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
      "junkTypeCount": 5,
      "junkMethodsPerType": 3
    },
    "antiDump": false,
    "referenceProxy": false
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

  "exclusions": {
    "namespaces": [],
    "types": [],
    "methods": [],
    "attributes": [
      "SerializableAttribute",
      "DataContractAttribute",
      "DataMemberAttribute"
    ]
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
  "metadata": { "removeDebugInfo": true }
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

Maximum protection with all techniques enabled.

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
    "referenceProxy": true
  },
  "metadata": {
    "removeDebugInfo": true,
    "removeAttributes": true
  }
}
```

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
| `excludePatterns` | string[] | `["*.resources"]` | Glob patterns for resources to exclude |

**Pattern Examples:**

| Pattern | Matches |
|---------|---------|
| `*` | All resources |
| `*.json` | All JSON files |
| `*.config` | All config files |
| `Config.*` | Resources starting with "Config." |
| `*.resources` | .NET resource files (typically excluded) |

**Note:** Exclude patterns take precedence over include patterns. System resources (`*.resources`) should typically be excluded to avoid runtime issues.

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
| `preserveXaml` | bool | `false` | Keep public instance properties on view-model / XAML types |

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
| `antiDump` | bool | `false` | Wipe PE headers in memory at load (Windows) |
| `referenceProxy` | bool | `false` | Hide in-module call targets behind proxy methods |

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
    "antiTamper": true
  }
}
```

### Web API with Serialization

```json
{
  "level": "standard",
  "exclusions": {
    "attributes": [
      "SerializableAttribute",
      "DataContractAttribute",
      "DataMemberAttribute",
      "JsonPropertyAttribute"
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
    "excludePatterns": ["*.resources"]
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
    "referenceProxy": true
  },
  "metadata": {
    "removeDebugInfo": true,
    "removeAttributes": true,
    "stripDocumentation": true
  }
}
```

## Generating Configuration Files

```bash
# Generate standard configuration
obfy config generate -o obfy.json

# Generate aggressive configuration
obfy config generate -l aggressive -o obfy-aggressive.json

# Generate minimal configuration
obfy config generate -l minimal -o obfy-minimal.json
```

Generated files include a `$schema` property pointing at `https://raw.githubusercontent.com/mortenbrudvik/Obfy/main/schemas/obfy.schema.json` for editor validation.

## See Also

- [CLI Reference](CLI.md) - Command-line options
- [Techniques](Techniques.md) - How each technique works
- [Advanced](Advanced.md) - Exclusion patterns and best practices

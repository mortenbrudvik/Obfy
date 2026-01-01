# Configuration Reference

Complete JSON configuration schema for Obfy.

## Schema Overview

```json
{
  "level": "standard",
  "stringEncryption": { ... },
  "resourceEncryption": { ... },
  "controlFlow": { ... },
  "symbolRenaming": { ... },
  "protection": { ... },
  "metadata": { ... },
  "exclusions": { ... }
}
```

## Complete Schema

```json
{
  "level": "minimal | standard | aggressive | custom",

  "stringEncryption": {
    "enabled": true,
    "algorithm": "Aes256 | Xor",
    "encryptConstantStrings": true,
    "encryptResourceStrings": true,
    "minStringLength": 3
  },

  "resourceEncryption": {
    "enabled": false,
    "algorithm": "Aes256 | Xor",
    "includePatterns": ["*"],
    "excludePatterns": []
  },

  "controlFlow": {
    "enabled": true,
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
    "preservePublicApi": false
  },

  "protection": {
    "antiDebug": true,
    "antiTamper": false,
    "antiDump": false
  },

  "metadata": {
    "removeDebugInfo": true,
    "removeAttributes": true,
    "stripDocumentation": true
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
  }
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
  "resourceEncryption": { "enabled": true },
  "controlFlow": {
    "enabled": true,
    "intensity": 80
  },
  "symbolRenaming": { "enabled": true },
  "protection": {
    "antiDebug": true,
    "antiTamper": true
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

### resourceEncryption

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | `false` | Enable resource encryption |
| `algorithm` | enum | `Aes256` | Encryption algorithm: `Aes256` or `Xor` |
| `includePatterns` | string[] | `["*"]` | Glob patterns for resources to include |
| `excludePatterns` | string[] | `[]` | Glob patterns for resources to exclude |

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
| `enabled` | bool | `true` | Enable control flow obfuscation |
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
| `preservePublicApi` | bool | `false` | Keep public members unchanged |

**Naming Modes:**

| Mode | Example Output | Description |
|------|----------------|-------------|
| `Unreadable` | `_‌‌‍‏‌` | Zero-width and confusing characters |
| `Sequential` | `a`, `b`, `aa` | Short, sequential names |
| `Hash` | `_8a5f2c1d` | SHA-256 hash of original name |
| `Random` | `kQzX7pL9mNv` | Random alphanumeric strings |

### protection

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `antiDebug` | bool | `true` | Inject debugger detection checks |
| `antiTamper` | bool | `false` | Inject anti-tampering checks |
| `antiDump` | bool | `false` | Inject anti-dump protection |

### metadata

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `removeDebugInfo` | bool | `true` | Remove PDB references and debug symbols |
| `removeAttributes` | bool | `true` | Remove custom attributes |
| `stripDocumentation` | bool | `true` | Strip XML documentation |

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
    "antiTamper": true,
    "antiDump": true
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

## See Also

- [CLI Reference](CLI.md) - Command-line options
- [Techniques](Techniques.md) - How each technique works
- [Advanced](Advanced.md) - Exclusion patterns and best practices

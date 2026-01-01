# Obfuscation Techniques

Detailed documentation of each obfuscation technique in Obfy.

## Overview

Obfy applies techniques in a specific order (priority):

| Priority | Technique | Description |
|----------|-----------|-------------|
| 10 | String Encryption | Encrypt string literals |
| 11 | Constant Encryption | Encrypt numeric constants |
| 15 | Resource Encryption | Encrypt embedded resources |
| 30 | Control Flow | Flatten control flow |
| 50 | Symbol Renaming | Rename identifiers |
| 70 | Anti-Debug | Inject debugger detection |
| 90 | Metadata Removal | Strip debug info |

## Assembly Obfuscation (dnlib)

### String Encryption

Encrypts string literals so they don't appear in plain text in the assembly.

**How It Works:**

1. Scans all methods for `ldstr` (load string) IL instructions
2. Encrypts each string using the configured algorithm
3. Injects a runtime decryptor class (`Obfy.Runtime.<StringDecryptor>`)
4. Replaces string loads with decryptor calls

**Before:**
```csharp
Console.WriteLine("Hello World");
```

**After (conceptual):**
```csharp
Console.WriteLine(StringDecryptor.Decrypt(0));
```

**Runtime Decryptor:**
- Stores encrypted strings in a static array
- Caches decrypted strings to avoid repeated decryption
- Key is embedded in the assembly

**Algorithms:**

| Algorithm | Description |
|-----------|-------------|
| **AES-256** | Strong encryption with random IV. Each encryption produces different ciphertext. |
| **XOR** | Fast XOR with key rotation. Lower security but faster startup. |

**Settings:**

```json
{
  "stringEncryption": {
    "enabled": true,
    "algorithm": "Aes256",
    "minStringLength": 3
  }
}
```

**Limitations:**
- Strings shorter than `minStringLength` are not encrypted
- Empty strings are skipped
- Adds slight runtime overhead for first access

---

### Constant Encryption

Encrypts numeric constants (int, long, float, double) so they don't appear as literal values in the assembly.

**How It Works:**

1. Scans all methods for constant-loading IL instructions:
   - `ldc.i4` variants (int constants)
   - `ldc.i8` (long constants)
   - `ldc.r4` (float constants)
   - `ldc.r8` (double constants)
2. Encrypts each constant using XOR encryption
3. Injects a runtime decryptor class (`Obfy.Runtime.<ConstantDecryptor>`)
4. Replaces constant loads with decryptor calls

**Before:**
```csharp
int timeout = 30000;
double pi = 3.14159265359;
```

**After (conceptual):**
```csharp
int timeout = ConstantDecryptor.DecryptInt32(0);
double pi = ConstantDecryptor.DecryptDouble(1);
```

**Runtime Decryptor:**
- Stores encrypted bytes for each constant
- Provides type-specific decrypt methods: `DecryptInt32`, `DecryptInt64`, `DecryptSingle`, `DecryptDouble`
- Key is embedded in the assembly

**Algorithms:**

| Algorithm | Description |
|-----------|-------------|
| **XOR** | Fast XOR with key rotation. Recommended for constants due to frequent access. |

**Threshold Settings:**

To avoid performance overhead from encrypting ubiquitous values, thresholds allow skipping common constants:

| Setting | Default | Effect |
|---------|---------|--------|
| `integerThreshold` | 2 | Skip integers where \|value\| < 2 (skips -1, 0, 1) |
| `longThreshold` | 2 | Skip longs where \|value\| < 2 |
| `skipCommonFloats` | true | Skip 0.0f, 1.0f, -1.0f |
| `skipCommonDoubles` | true | Skip 0.0, 1.0, -1.0 |

**Settings:**

```json
{
  "constantEncryption": {
    "enabled": true,
    "algorithm": "Xor",
    "encryptIntegers": true,
    "encryptLongs": true,
    "encryptFloats": true,
    "encryptDoubles": true,
    "integerThreshold": 2,
    "longThreshold": 2,
    "skipCommonFloats": true,
    "skipCommonDoubles": true
  }
}
```

**Limitations:**
- Only encrypts literal constants, not computed values
- Adds slight runtime overhead for each constant access
- Not enabled by default (only in Aggressive preset)

---

### Resource Encryption

Encrypts embedded resources (images, configs, data files) so they are not visible in plain form.

**How It Works:**

1. Scans all `EmbeddedResource` entries in the assembly
2. Filters resources based on include/exclude patterns
3. Encrypts each resource using the configured algorithm
4. Removes original resources from the assembly
5. Injects a runtime decryptor class (`Obfy.Runtime.<ResourceDecryptor>`)
6. Stores encrypted resources in the decryptor type

**Before:**
```
Assembly
└── Resources
    ├── Config.json (visible)
    ├── Data.xml (visible)
    └── Image.png (visible)
```

**After:**
```
Assembly
├── Obfy.Runtime.<ResourceDecryptor>
│   ├── _k (encryption key)
│   ├── _d (encrypted data)
│   ├── _n (resource names)
│   └── GetResource(string name) → byte[]
└── Resources (empty - all moved to decryptor)
```

**Runtime Access:**
```csharp
// Original code that uses Assembly.GetManifestResourceStream
// will need to call the decryptor instead:
var data = ResourceDecryptor.GetResource("Config.json");
```

**Algorithms:**

| Algorithm | Description |
|-----------|-------------|
| **AES-256** | Strong encryption with random IV. Recommended for sensitive resources. |
| **XOR** | Fast XOR with key rotation. Lower security but faster startup. |

**Settings:**

```json
{
  "resourceEncryption": {
    "enabled": true,
    "algorithm": "Aes256",
    "includePatterns": ["*"],
    "excludePatterns": ["*.resources"]
  }
}
```

**Pattern Matching:**
- `*` - matches all resources
- `*.json` - matches all JSON files
- `Config.*` - matches resources starting with "Config."
- `*.resources` - matches .NET resource files (typically excluded)

**Limitations:**
- Resources accessed via reflection need code changes
- System resources (*.resources) may cause runtime issues if encrypted
- Large resources increase assembly size slightly due to encryption overhead

---

### Control Flow Obfuscation

Transforms the structure of methods to make them harder to analyze.

**Switch Mode:**

Converts linear code into a state machine with a switch dispatcher.

**Before:**
```csharp
void Method()
{
    Step1();
    Step2();
    Step3();
}
```

**After (conceptual):**
```csharp
void Method()
{
    int state = 1234;
    while (true)
    {
        switch (state)
        {
            case 1234: Step1(); state = 5678; break;
            case 5678: Step2(); state = 9012; break;
            case 9012: Step3(); return;
        }
    }
}
```

**Opaque Predicate Mode:**

Inserts conditional branches that always evaluate the same way.

**Before:**
```csharp
DoSomething();
```

**After (conceptual):**
```csharp
int x = GetValue();
if (x * x >= 0)  // Always true
{
    DoSomething();
}
```

**Combined Mode:**

Applies both switch flattening and opaque predicates for maximum obfuscation.

**Intensity:**

The `intensity` setting (0-100) controls how aggressively the technique is applied:
- **0-25**: Light obfuscation, minimal blocks affected
- **26-50**: Moderate obfuscation
- **51-75**: Heavy obfuscation
- **76-100**: Maximum obfuscation, all eligible blocks affected

**Skipped Methods:**
- Constructors
- Methods with exception handlers (try/catch)
- Very short methods (<5 instructions)

**Settings:**

```json
{
  "controlFlow": {
    "enabled": true,
    "mode": "Combined",
    "intensity": 75
  }
}
```

---

### Symbol Renaming

Renames types, methods, fields, properties, and parameters to meaningless names.

**Before:**
```csharp
public class UserService
{
    private readonly IUserRepository _repository;

    public User GetUserById(int userId)
    {
        return _repository.Find(userId);
    }
}
```

**After (Unreadable mode):**
```csharp
public class _‌‌‍‏‌
{
    private readonly _‌‌‍‏‍ _‌‌‍‏‎;

    public _‌‌‍‏‏ _‌‌‍‏​(int _‌‌‍‏‪)
    {
        return _‌‌‍‏‎._‌‌‍‏‫(_‌‌‍‏‪);
    }
}
```

**Naming Modes:**

| Mode | Output | Best For |
|------|--------|----------|
| **Unreadable** | `_‌‌‍‏‌` | Maximum obfuscation. Uses zero-width characters. |
| **Sequential** | `a`, `b`, `aa` | Smallest output size. Predictable naming. |
| **Hash** | `_8a5f2c1d` | Consistent names per symbol. Useful for debugging. |
| **Random** | `kQzX7pL9` | Unpredictable names. Good general choice. |

**What Gets Renamed:**

| Element | Renamed | Notes |
|---------|---------|-------|
| Classes/Structs | Yes | Unless public + `preservePublicApi` |
| Enums | Yes | Unless public + `preservePublicApi` |
| Methods | Yes | Except constructors, entry points |
| Properties | Yes | |
| Fields | Yes | Except const/literal fields |
| Parameters | Yes | |
| Local Variables | No | IL doesn't preserve local names |

**Automatically Preserved:**
- Constructors (`.ctor`, `.cctor`)
- Entry point method
- Virtual methods with overrides
- Interface implementations
- Compiler-generated members

**Settings:**

```json
{
  "symbolRenaming": {
    "enabled": true,
    "mode": "Unreadable",
    "renameTypes": true,
    "renameMethods": true,
    "renameProperties": true,
    "renameFields": true,
    "renameParameters": true,
    "preservePublicApi": false
  }
}
```

---

### Anti-Debug Protection

Injects code that detects and responds to debugging attempts.

**How It Works:**

1. Injects a runtime class (`Obfy.Runtime.<AntiDebug>`)
2. Adds debugger detection checks at entry point
3. Optionally adds checks in module initializer

**Detection Method:**
```csharp
if (System.Diagnostics.Debugger.IsAttached)
{
    Environment.Exit(1);
    // or
    Environment.FailFast("Security violation");
}
```

**Settings:**

```json
{
  "protection": {
    "antiDebug": true,
    "antiTamper": false,
    "antiDump": false
  }
}
```

**Limitations:**
- Can be bypassed by experienced reverse engineers
- May cause issues with legitimate profilers
- Some detection methods can be patched out

---

### Metadata Removal

Removes debugging information and unnecessary attributes from the assembly.

**What Gets Removed:**

**Debug Information:**
- PDB file references
- Sequence points
- Local variable names
- Document references

**Attributes (when `removeAttributes: true`):**
- `DebuggableAttribute`
- `CompilationRelaxationsAttribute`
- `RuntimeCompatibilityAttribute`
- `CompilerGeneratedAttribute`
- `NullableAttribute`, `NullableContextAttribute`
- `DebuggerNonUserCodeAttribute`
- `DebuggerStepThroughAttribute`
- `DebuggerHiddenAttribute`
- `DebuggerDisplayAttribute`
- `DebuggerBrowsableAttribute`
- `SuppressMessageAttribute`

**Documentation:**
- Embedded XML documentation resources

**Settings:**

```json
{
  "metadata": {
    "removeDebugInfo": true,
    "removeAttributes": true,
    "stripDocumentation": true
  }
}
```

---

## Source Code Obfuscation (Roslyn)

Obfy can also obfuscate C# source code files using Roslyn.

### Source String Encryption

Similar to assembly string encryption, but transforms source code.

**Before:**
```csharp
var message = "Hello World";
```

**After:**
```csharp
var message = Decrypt("SGVsbG8gV29ybGQ=", "key");
```

### Source Symbol Renaming

Renames identifiers in source code using semantic analysis.

- Preserves compilation correctness
- Handles overloads and generics
- Maintains references across files

### Source Control Flow

Transforms control flow structures in source code.

- Adds opaque predicates
- Transforms loops
- Maintains compilability

---

## Technique Combinations

### Recommended for Libraries

```json
{
  "level": "standard",
  "symbolRenaming": {
    "preservePublicApi": true
  }
}
```

### Recommended for Applications

```json
{
  "level": "aggressive"
}
```

### Recommended for Sensitive Code

```json
{
  "stringEncryption": {
    "enabled": true,
    "algorithm": "Aes256",
    "minStringLength": 1
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
    "antiDebug": true
  }
}
```

## See Also

- [Configuration](Configuration.md) - Full settings reference
- [Advanced](Advanced.md) - Exclusions and best practices

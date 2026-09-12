# Obfuscation Techniques

Detailed documentation of each obfuscation technique in Obfy.

## Overview

Obfy applies techniques in a specific order (priority):

| Priority | Technique | Description |
|----------|-----------|-------------|
| 10 | String Encryption | Encrypt string literals |
| 11 | Constant Encryption | Encrypt numeric constants |
| 15 | Resource Encryption | Encrypt embedded resources |
| 18 | Anti-Debug | Inject debugger detection |
| 19 | Anti-Dump | Wipe PE headers in memory |
| 20 | Anti-Decompiler | Inject junk types and methods |
| 22 | Anti-Tamper | Verify assembly integrity |
| 25 | Method Encryption | XOR method IL in the PE (Windows) |
| 30 | Control Flow | Flatten control flow |
| 40 | Reference Proxy | Hide call targets behind proxies |
| 50 | Symbol Renaming | Rename identifiers |
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
Console.WriteLine(StringDecryptor.Decrypt(encodedIndex));
// or Decrypt2 / Decrypt3 — call sites round-robin across several entry points
```

**Runtime Decryptor:**
- Stores encrypted strings in a static array
- Caches decrypted strings to avoid repeated decryption
- Key is embedded in the assembly
- Emits three `string Decrypt*(int)` entry points so a single `Decrypt(int)` is not a decompiler signature for every string
- When control flow is enabled, decryptor methods (not `.cctor`) are flattened or given opaque predicates at full intensity
- When reference proxy is enabled, user call sites invoke the decryptor through a `calli` trampoline. A trampoline in `<RefProxy>` cannot `ldftn` a `private` helper (`MethodAccessException` / unverifiable); user `private` methods are still proxied.

**Algorithms:**

| Algorithm | Description |
|-----------|-------------|
| **AES-256** | AES-256 obfuscation with random IV (not confidentiality; the key is in the assembly). Each encryption produces different ciphertext. |
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
- Methods with exception handlers and compiler-generated methods **and types** (async state machines, display classes, iterators) are encrypted
- Control-flow flattening still skips exception-handler methods (rebuilding EH is unsafe); those methods get opaque predicates instead
- String decrypt call sites pass `index XOR seed`, not the raw index, and do not all call the same method
- Resource strings shorter than `minStringLength` stay plaintext; encrypted resource strings are prefixed so `GetString` does not try to decrypt them

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

### Method IL Encryption

XOR-encrypts method IL bytes in the PE image. A module initializer decrypts them in memory with `VirtualProtect` before JIT.

Enabled in the Aggressive preset (`protection.methodEncryption`).

**Limits (not a confidentiality guarantee):**

- Windows only (`kernel32!VirtualProtect`). Decrypt failures are swallowed so module load still succeeds, but encrypted bodies are **not** restored — invoking them will fail. Non-Windows / NativeAOT / IL2CPP are unsupported.
- Generic methods and methods on generic types are skipped (shared IL / instantiations). When a large share of candidates are generic, the run warns.
- Distinct nonzero XOR keys when there are 255 or fewer methods; further methods reuse a key. Zero keys are not applied. Keys still live in the PE; this only stops a single-byte dump from recovering every body.

```json
{
  "protection": {
    "methodEncryption": true
  }
}
```

---

### Control Flow Obfuscation

Transforms the structure of methods to make them harder to analyze.

**Switch Mode:**

Converts linear code into a state-machine dispatcher. The conceptual C# below uses `switch`; emitted IL is `ldloc` / `ldc.i4` / `beq` with random state values, not sequential `switch` indices.

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

Inserts conditional branches that always evaluate the same way, using runtime values (`Environment.TickCount`, `TickCount64` on modern .NET, `ProcessorCount`, `CurrentManagedThreadId`, `GC.MaxGeneration`) so they are not compile-time constants. A decompiler may still see them as opaque, not as proven always-true. `TickCount64` is not emitted for .NET Framework / netstandard 2.0 modules.

**Before:**
```csharp
DoSomething();
```

**After (conceptual):**
```csharp
int x = Environment.TickCount;
if ((x ^ x) == 0)  // Always true, not a compile-time constant
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

**Dispatcher states:** Both CFG flattening and the linear-chunk fallback use random `beq` state values, not sequential `switch` indices `0, 1, 2…`.

**Skipped Methods:**
- Constructors and static constructors (including runtime helper `.cctor`)
- P/Invoke stubs and `calli` trampolines
- Switch-flattening of methods with exception handlers (opaque predicates still apply)
- Very short methods (<5 instructions)

**Runtime helpers:** Decryptors, anti-debug `Check`, anti-tamper `Verify`, anti-dump `Wipe`, and method-body decrypt are control-flowed whenever control flow is enabled, at intensity 100 (user intensity is ignored for those methods). Helpers prefer flattening at intensity 100; EH / short / unflattenable-with-branches fall back to opaque predicates. Flattening can still no-op.

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
| **Hash** | `_8a5f2c1d` | Salted hash per run. Not a dictionary of the original name. |
| **Random** | `kQzX7pL9` | Unpredictable names. Good general choice. |

**What Gets Renamed:**

| Element | Renamed | Notes |
|---------|---------|-------|
| Classes/Structs | Yes | Unless public + `preservePublicApi` |
| Enums | Yes | Unless public + `preservePublicApi` |
| Methods | Yes | Except constructors, entry points |
| Properties | Yes | Getters/setters renamed in lockstep |
| Events | Yes | add_/remove_ accessors renamed in lockstep |
| Fields | Yes | Except const/literal fields |
| Parameters | Yes | |
| Namespaces | Yes | Public namespaces kept when `preservePublicApi` |
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

Injects code that detects and responds to debugging attempts. This raises the cost of casual debugging; it is not debugger immunity.

**How It Works:**

1. Injects a runtime class (`Obfy.Runtime.<AntiDebug>`)
2. Calls `Check` from the module initializer (runs at load)
3. Scatters `Check` into most user methods with a real body (not P/Invoke/abstract/empty/prefix-first) so patching a single call site is not enough

**Detection:**

- `Debugger.IsAttached` and `Debugger.IsLogging()`
- `kernel32!IsDebuggerPresent` and `CheckRemoteDebuggerPresent` (Windows; `DllNotFoundException` and `EntryPointNotFoundException` are swallowed)
- ~1s delta between two `TickCount` reads inside `Check` (pauses/breakpoints in the probe), not a general single-step detector

Failure paths inside `Check` cycle through `Environment.Exit(1)`, `Environment.FailFast`, and `throw` so patching a single API is not enough.

**Settings:**

```json
{
  "protection": {
    "antiDebug": true,
    "antiTamper": false,
    "antiDump": false,
    "referenceProxy": false
  }
}
```

**Limitations:**
- Can be bypassed by experienced reverse engineers
- May cause issues with legitimate profilers
- Native checks are Windows-only; managed checks still run elsewhere

---

### Anti-Dump Protection

Wipes in-memory PE header fields at module load so dumpers that reconstruct the image from the loaded module get a corrupted header. Failures (missing `kernel32`, non-Windows) are swallowed.

Enabled in the Aggressive preset.

---

### Reference Proxy

Replaces in-module `call`/`callvirt` targets with small static proxy methods so call sites no longer name the original method. Framework methods are left alone unless `protection.proxyExternalCalls` is true (opt-in; skips compiler/interop/pointer signatures). Assembly-visible runtime helper entry points (string/constant decrypt, anti-debug `Check`, and similar) are proxied from user code; private helper internals stay as direct calls because a trampoline in another type cannot invoke them.

Enabled in the Aggressive preset (`proxyExternalCalls` stays off).

---

### Anti-Decompiler Protection

Makes reverse engineering harder by cluttering decompiler output with junk types and methods.

**How It Works:**

1. **SuppressIldasm Attribute**: Adds `[SuppressIldasm]` attribute to block ILDasm and some older tools
2. **Junk Types**: Injects decoy types with confusing names in generated namespaces (not a recognizable `Obfy.*` prefix)
3. **Junk Methods**: Adds methods with complex-looking but dead code (loops, math, branches)
4. **Junk Fields**: Adds fake fields to junk types
5. **Confusing Names**: Uses zero-width and look-alike Unicode characters for names

**Injected Junk Type Structure:**

```
_‌‍‏‎._‌‍‏‏ (junk type)
├── Fields
│   ├── _‌‍‏‏ (int)
│   ├── _‌‍‏‐ (string)
│   └── _‌‍‏‑ (byte[])
├── Methods
│   ├── _‌‍‏‒(int, int) → int  (junk math method)
│   ├── _‌‍‏–(int, int) → int  (junk math method)
│   └── .cctor()               (static initializer with confusing code)
```

**Junk Method IL:**

Each junk method contains valid but purposeless IL:
- Nested loops with counter variables
- XOR operations and bitwise math
- Conditional branches that clutter analysis
- Multiple local variables

```csharp
// Conceptual representation of junk method IL
static int JunkMethod(int a, int b)
{
    int loc0 = 0, loc1 = 1;
    while (loc0 < a)
    {
        loc1 = (loc1 + loc0) ^ b;
        loc0++;
    }
    return loc1 & 0xFF;
}
```

**Effectiveness by Decompiler:**

| Decompiler | SuppressIldasm | Junk Types |
|------------|----------------|------------|
| ILDasm | Blocked | N/A |
| dnSpy | Ignored | Visible (adds noise) |
| ILSpy | Ignored | Visible (adds noise) |
| dotPeek | Ignored | Visible (adds noise) |

**Settings:**

```json
{
  "protection": {
    "antiDecompiler": {
      "enabled": true,
      "injectJunkTypes": true,
      "addSuppressIldasmAttribute": true,
      "junkTypeCount": 5,
      "junkMethodsPerType": 3
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `enabled` | false | Enable anti-decompiler protection |
| `injectJunkTypes` | true | Create junk types with dead code |
| `addSuppressIldasmAttribute` | true | Add SuppressIldasm assembly attribute |
| `junkTypeCount` | 5 | Number of junk types to inject (1-50) |
| `junkMethodsPerType` | 3 | Number of junk methods per type (1-20) |

**Limitations:**
- Modern decompilers (dnSpy, ILSpy) ignore SuppressIldasm
- Junk code adds to assembly size
- Experienced analysts can identify and filter junk types
- Works best when combined with other obfuscation techniques

---

### Anti-Tamper Protection

Verifies assembly integrity at runtime by computing and comparing cryptographic hashes.

**How It Works:**

1. During obfuscation:
   - Injects a runtime class (`Obfy.Runtime.<AntiTamper>`)
   - Creates a placeholder hash field (32 zero bytes)
   - Adds verification calls at entry point and/or module initializer
   - After writing the assembly, computes SHA-256 of the whole file with the hash slot zeroed
   - Patches the placeholder with the actual hash

2. At runtime:
   - Reads the assembly file from disk (`Assembly.Location`, then `Environment.ProcessPath`)
   - Recomputes the whole-file hash with the hash slot zeroed
   - Compares with the stored expected hash
   - Exits if mismatch is detected. Memory-only / empty-path loads still skip the check.

**Two-Pass Process:**

The hash must be computed after all obfuscation is complete, but the verification code must be injected before saving. This is solved with a two-pass approach:

```
1. Inject <AntiTamper> type with placeholder (32 zero bytes)
2. Write module to temp file
3. Compute SHA-256 of the file with the hash slot zeroed
4. Patch placeholder with actual hash
5. Write final file
```

**Verification Logic:**

```csharp
static void Verify()
{
    // Get assembly path (handles single-file apps)
    var path = Assembly.GetExecutingAssembly().Location;
    if (string.IsNullOrEmpty(path))
        path = Environment.ProcessPath;  // .NET 6+ fallback
    if (string.IsNullOrEmpty(path))
        return;  // Skip verification gracefully

    // Load and hash method bodies
    var module = ModuleDefMD.Load(path);
    var actualHash = ComputeMethodBodiesHash(module);

    // Compare with expected
    if (!HashesEqual(actualHash, _h))
        Environment.Exit(1);
}
```

**Settings:**

```json
{
  "protection": {
    "antiTamper": {
      "enabled": true,
      "checkEntryPoint": true,
      "checkModuleInitializer": true
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `enabled` | false | Enable anti-tamper protection |
| `checkEntryPoint` | true | Inject verification at entry point |
| `checkModuleInitializer` | true | Inject verification in module initializer |

**Limitations:**
- Requires file system access at runtime (cannot verify in-memory loaded assemblies)
- Single-file published apps: Uses `Environment.ProcessPath` fallback (.NET 6+)
- Assemblies loaded from byte arrays skip verification gracefully
- Adds slight startup overhead for hash computation
- Can be bypassed by patching the verification code (combine with Anti-Debug for better protection)

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

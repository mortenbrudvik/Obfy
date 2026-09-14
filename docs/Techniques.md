# Obfuscation Techniques

Detailed documentation of each obfuscation technique in Obfy.

## Overview

Obfy applies techniques in a specific order (priority):

| Priority | Technique | Description |
|----------|-----------|-------------|
| 8 | Dependency Embedding | Pack sibling DLLs as `Obfy.Embedded.*` resources |
| 10 | String Encryption | Encrypt string literals |
| 11 | Constant Encryption | Encrypt numeric constants |
| 15 | Resource Encryption | Encrypt embedded resources |
| 18 | Anti-Debug | Inject debugger detection (kernel32 omitted on NativeAOT / IL2CPP / Blazor WASM) |
| 19 | Anti-Dump | Wipe PE headers; in-process MiniDumpWriteDump `0xC3` on x86/x64 |
| 20 | Anti-Decompiler | Junk types, SuppressIldasm, decoy ConfusedBy/Dotfuscator attributes |
| 21 | Watermark | Assembly-level pinned WatermarkAttribute |
| 22 | Anti-Tamper | Verify assembly integrity |
| 24 | Virtualization | Replace selected simple static int methods with a bytecode interpreter |
| 25 | Method Encryption | XOR method IL in the PE (Windows) |
| 30 | Control Flow | Flatten control flow |
| 40 | Reference Proxy | Hide call targets behind proxies |
| 50 | Symbol Renaming | Rename identifiers |
| 90 | Metadata Removal | Strip debug info |

**Encryption is obfuscation, not confidentiality.** String, constant, resource, method-IL, and virtualization “encryption” embed the key or interpreter in the output assembly. Anyone who runs or inspects the binary can recover plaintext. Do not ship real secrets (API keys, tokens, credentials) inside an assembly and rely on Obfy to keep them secret.

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
2. Encrypts each constant using the configured algorithm (XOR or AES-256)
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
| **XOR** | Fast XOR with key rotation. Recommended for constants due to frequent access. Default. |
| **AES-256** | AES-256 obfuscation with a random IV (not confidentiality; the key is in the assembly). Higher overhead per constant access. |

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
3. Encrypts each resource in place using the configured algorithm
4. Injects a runtime decryptor class (`Obfy.Runtime.<ResourceDecryptor>`)
5. Rewrites `GetManifestResourceStream` call sites to decrypt matching names

**Before:**
```
Assembly
└── Resources
    ├── Config.json (plaintext)
    ├── Data.xml (plaintext)
    └── Image.png (plaintext)
```

**After:**
```
Assembly
├── Obfy.Runtime.<ResourceDecryptor>  (key + encrypted-name table + load helpers)
└── Resources
    ├── Config.json (ciphertext)
    ├── Data.xml (ciphertext)
    └── Image.png (ciphertext)
```

**Runtime access:** The obfuscator rewrites `Assembly.GetManifestResourceStream(string)`, `GetManifestResourceStream(Type, string)`, and `Module` overloads so existing call sites decrypt automatically. Other load paths (`ResourceManager` for binary resources, `GetManifestResourceNames` plus a custom reader, native unpackers) are **not** rewritten; those resources stay ciphertext and the run emits a warning if no call site was rewritten.

```csharp
// Unchanged source — the call is rewritten in IL:
using var stream = Assembly.GetExecutingAssembly()
    .GetManifestResourceStream("Config.json");
```

**Algorithms:**

| Algorithm | Description |
|-----------|-------------|
| **AES-256** | AES-256 obfuscation with random IV (not confidentiality; the key is in the assembly). |
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
- Only rewritten `GetManifestResourceStream` overloads decrypt at runtime; other load paths see ciphertext
- System resources (`*.resources`) may cause runtime issues if encrypted (excluded by default)
- Packed dependency resources (`Obfy.Embedded.*`) are hard-skipped even if `excludePatterns` is overwritten
- Large resources increase assembly size slightly due to encryption overhead

---

### Method IL Encryption

XOR-encrypts method IL bytes in the PE image. A module initializer decrypts them in memory with `VirtualProtect` before JIT.

Enabled in the Aggressive preset (`protection.methodEncryption`).

**Limits (not a confidentiality guarantee):**

- Windows only (`kernel32!VirtualProtect`). Missing `kernel32` (`DllNotFoundException` / `EntryPointNotFoundException`) is swallowed: the module loads, encrypted bodies stay ciphertext, and invoking them fails. `VirtualProtect` present but returning false is `Environment.FailFast`. Non-Windows / NativeAOT / IL2CPP / Blazor WASM are unsupported (restricted `runtimeProfile`s disable the pass).
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

### Virtualization

Replaces a small set of **simple static `int` methods** with a bytecode interpreter stub. This is not commercial-grade code virtualization (no custom VM for arbitrary IL).

**Eligible methods** (everything else is skipped):

- `static`, non-generic, no exception handlers
- Return `int`; parameters are `int` only; at most 8 parameters; at most 16 int-sized locals
- Body may include `ldc.i4`, `ldarg`, `ldloc`/`stloc`, `add`/`sub`/`mul`, `ceq`/`cgt`/`clt`, `ret`, and signed branches (`br`/`brtrue`/`brfalse`/`blt`/`bgt`/`ble`/`bge`/`beq`/`bne`, short forms included)
- Unsigned compares (`cgt.un`, `blt.un`, …) skip the method so original IL is kept

The shipping interpreter is injected as `Obfy.Runtime.<Vm>.Execute`. A general VM runtime in `Obfy.VmRuntime` is not wired into this pass.

**Settings:**

```json
{
  "virtualization": {
    "enabled": false,
    "maxMethods": 32
  }
}
```

Off in every level preset. `maxMethods` must be 1–256 (default 32); out of range fails the run. CLI: `--virtualize`. Runs at priority 24, before method IL encryption.

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

**Automatically Preserved (renaming):**
- Constructors (`.ctor`, `.cctor`) — also skipped by control-flow flatten
- Module entry point (not every method named `Main`)
- Interface implementations
- `virtual` public/family methods on an **unsealed** type (even with no overrides)
- `SpecialName` / `RuntimeSpecialName` members (property accessors, ctors)

Compiler-generated types (async state machines, display classes, lambdas) are **not** automatically preserved.

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
- NativeAOT, Unity IL2CPP, and Blazor WASM omit kernel32 P/Invoke and keep managed `Debugger` / TickCount checks only (a report warning is emitted)

---

### Anti-Dump Protection

Wipes in-memory PE header fields at module load so dumpers that reconstruct the image from the loaded module get a corrupted header. On Windows it also overwrites the first byte of **in-process** `dbghelp!MiniDumpWriteDump` with x86/x64 `ret` (`0xC3`) after an X86/X64 architecture check; ARM64 is skipped (there is no ARM64 encoding). `VirtualProtect` failure skips the write. Missing `kernel32`/`dbghelp` and non-Windows throws are swallowed so the app still starts. External dumpers (ProcDump, Task Manager, other processes' `MiniDumpWriteDump`), `dbgcore`, and raw `ReadProcessMemory` are unaffected.

PE32 vs PE32+ data-directory layouts are selected from the optional-header magic. Enabled in the Aggressive preset. Not used on NativeAOT / Unity IL2CPP / Blazor WASM.

---

### Reference Proxy

Replaces in-module `call`/`callvirt` targets with small static `calli` trampolines so call sites no longer name the original method. Out-of-module calls (BCL and third-party) are left alone unless `protection.proxyExternalCalls` is true (opt-in; requires `referenceProxy`; skips compiler/interop/pointer/value-type/`constrained.`/generic-instantiation/vararg/ctor signatures). Assembly-visible runtime helper entry points (string/constant decrypt, anti-debug `Check`, and similar) are proxied from user code; private helper internals stay as direct calls because a trampoline in another type cannot invoke them.

Enabled in the Aggressive preset (`proxyExternalCalls` stays off).

---

### Dependency Embedding

Packs `{AssemblyRef.Name}.dll` files that sit next to the input as `Obfy.Embedded.{name}.dll` resources and registers `AppDomain.AssemblyResolve` from the module `.cctor`. Resource encryption hard-skips that prefix so `Assembly.Load` sees plaintext PE bytes. Gated off NativeAOT / Unity IL2CPP / Blazor WASM. Does not probe NuGet or GAC, and does not embed `.exe` files.

---

### Anti-Decompiler Protection

Makes reverse engineering harder by cluttering decompiler output with junk types and methods.

**How It Works:**

1. **SuppressIldasm Attribute**: Adds `[SuppressIldasm]` attribute to block ILDasm and some older tools
2. **Junk Types**: Injects decoy types with confusing names in generated namespaces (not a recognizable `Obfy.*` prefix)
3. **Junk Methods**: Adds methods with complex-looking but dead code (loops, math, branches)
4. **Junk Fields**: Adds fake fields to junk types
5. **Confusing Names**: Uses zero-width and look-alike Unicode characters for names
6. **Decoy attributes** (default on): injects internal `ConfusedByAttribute` / `DotfuscatorAttribute` types and assembly attributes. Type names are pinned against renaming so name-based detectors can see them. This is detector bait, not a de4dot block.

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
      "addDecoyAttributes": true,
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
| `addDecoyAttributes` | true | Inject pinned ConfusedBy/Dotfuscator attributes (name-based detector bait) |
| `junkTypeCount` | 5 | Number of junk types to inject (1-50) |
| `junkMethodsPerType` | 3 | Number of junk methods per type (1-20) |

**Limitations:**
- Modern decompilers (dnSpy, ILSpy) ignore SuppressIldasm
- Junk code adds to assembly size
- Experienced analysts can identify and filter junk types
- Works best when combined with other obfuscation techniques
- Decoy attributes do not stop de4dot; they only present ConfuserEx/Dotfuscator-like names

---

### Watermark

Embeds a customer or build identifier as an assembly-level `Obfy.Runtime.WatermarkAttribute`. The type name is pinned against renaming. The id is stored as a constructor argument and a public `Id` field (plaintext metadata, not confidentiality).

Requires `watermark.enabled` and a non-whitespace `watermark.id`. Not part of level presets. CLI: `--watermark-id` (enables watermarking and sets the trimmed id; whitespace-only is an error). Recover the id from the custom-attribute blob or the `Id` field. Re-obfuscating with the same id is a no-op (warning); a different requested id replaces the constructor argument (warning).

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
   - Reads the assembly file from disk (`Assembly.Location`). `Environment.ProcessPath` is used only when `GetEntryAssembly() == GetExecutingAssembly()` (single-file). Packed ALC / `LoadFromStream` loads skip hashing the launcher.
   - Recomputes the whole-file hash with the hash slot zeroed
   - Compares with the stored expected hash
   - Calls `Environment.FailFast` if the hash is missing or mismatches (IO/crypto failures in `Verify` also FailFast). Memory-only / empty-path loads still skip the check.

**Two-Pass Process:**

The hash must be computed after all obfuscation is complete, but the verification code must be injected before saving. This is solved with a two-pass approach:

```
1. Inject <AntiTamper> type with placeholder (32 zero bytes)
2. Write module to temp file
3. Compute SHA-256 of the file with the hash slot zeroed
4. Patch placeholder with actual hash
5. Write final file
```

**Verification Logic (conceptual):**

The injected helper does **not** use dnlib at runtime. It reads the assembly file and hashes the PE bytes:

```csharp
static void Verify()
{
    var path = Assembly.GetExecutingAssembly().Location;
    if (string.IsNullOrEmpty(path)
        && Assembly.GetEntryAssembly() == Assembly.GetExecutingAssembly())
        path = Environment.ProcessPath;  // single-file only
    if (string.IsNullOrEmpty(path))
        return;  // in-memory / packed ALC loads skip the check

    var bytes = File.ReadAllBytes(path);
    var offset = FindHashOffset(bytes);  // magic marker in the PE
    if (offset < 0)
        Environment.FailFast("Obfy anti-tamper: assembly integrity check failed");

    // Zero the hash slot (and the strong-name signature if present), then SHA-256
    var actual = SHA256.HashData(ZeroedCopy(bytes, offset));
    if (!HashesEqual(actual, storedHash))
        Environment.FailFast("Obfy anti-tamper: assembly integrity check failed");
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
- `DebuggableAttribute` (this pass, not `removeAttributes`)

**Attributes (when `removeAttributes: true`):**
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

Encrypts string literals (and interpolations without alignment/format clauses) with the same XOR or AES-256 setting as assembly mode. Emits a helper `Obfy.Runtime.__ObfyStringDecryptor.Decrypt(string)` (key is a Base64 field in that type — not confidentiality). Attribute arguments are left alone. `[Obfuscation(Exclude = true, Feature = "strings")]` is honored.

**Before:**
```csharp
var message = "Hello World";
```

**After (conceptual):**
```csharp
var message = Obfy.Runtime.__ObfyStringDecryptor.Decrypt("<base64 ciphertext>");
```

### Source Symbol Renaming

Renames identifiers using Roslyn semantic symbols (`ISymbol`), including attributes and record positional properties.

- Preserves compilation correctness
- Handles overloads and generics
- Maintains references across files
- Honors `[Obfuscation]` for `renaming`

### Source Control Flow

Transforms control flow structures in source code.

- Adds opaque predicates
- Switch-flattens eligible methods (skips methods with locals/`break`/`continue` that would emit CS0161)
- Honors `[Obfuscation]` for `controlflow`
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

## Post-processing: incremental cache

`incremental.enabled` (off in every preset; CLI `--incremental`) skips re-obfuscation when the Obfy version, input bytes, and serialized settings have not changed. The cache file is `{outputPath}.obfycache` and stores a SHA-256 of those inputs. A hit also requires the output file to exist; if packing is on, the launcher `.exe` and `.runtimeconfig.json` must exist too. A locked or corrupt cache is a miss. The cache is written only after a successful write (including a successful launcher emit when packing is on).

## Post-processing: managed launcher

`packing.enabled` compiles a framework-dependent managed console host (`{name}.launcher.exe` + `.runtimeconfig.json`) that embeds the obfuscated assembly as `packed.dll` and invokes its entry point. Run with `dotnet {name}.launcher.exe`. This is not native code generation (PF-09 remaining work). Config-only; off in every preset.

The payload stays in memory (`AssemblyLoadContext.LoadFromStream`); anti-tamper skips ALC loads instead of hashing the launcher. Sibling assemblies next to the launcher are resolved from the load context. Packing requires an entry point; class libraries fail the run. Source inputs skip packing with a warning.

## See Also

- [Configuration](Configuration.md) - Full settings reference
- [Advanced](Advanced.md) - Exclusions and best practices

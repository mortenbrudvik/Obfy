# Advanced Usage

Exclusion rules, best practices, and troubleshooting for Obfy.

## `[Obfuscation]` attribute

Obfy honors `System.Reflection.ObfuscationAttribute` on types and members for **assembly** obfuscation (renaming, control flow, strings, constants, plus `Feature=all` for method encryption, reference proxy, anti-debug, and virtualization). Source obfuscators honor the same attribute for renaming, strings, and control flow.

```csharp
[Obfuscation(Exclude = true)]
public class License { }

[Obfuscation(Exclude = true, Feature = "renaming")]
public class Dto { }

[Obfuscation(Exclude = true, Feature = "strings")]
public string GetSecret() => "plain";
```

Supported `Feature` values: `all` (default), `renaming` (`rename`, `symbols`), `controlflow` (`cf`, `control-flow`), `strings` (`stringencryption`), `constants` (`constantencryption`). Unknown features are ignored and recorded as a report warning.

Type-level `ApplyToMembers` (default true) applies the directive to members. `[Obfuscation(Exclude = true, ApplyToMembers = false)]` keeps the type name and still obfuscates members. A member-level `Exclude` value wins over the declaring type.

Optional allow-list (`inclusions`) in `obfy.json`: when any pattern is set, only matching namespaces, types, **or** methods are candidates for renaming, control flow, strings, and constants (exclusions still apply). A method-only list still visits types that contain a matching method.

## Exclusion Rules

Exclusions prevent specific code from being obfuscated.

### Namespace Patterns

Exclude entire namespaces or namespace hierarchies.

```json
{
  "exclusions": {
    "namespaces": [
      "MyApp.PublicApi",
      "MyApp.Models.*",
      "System.*"
    ]
  }
}
```

**Pattern Syntax:**
- `MyApp.Api` - Exact namespace match
- `MyApp.Api.*` - Child namespaces (`MyApp.Api.Foo`); does **not** match `MyApp.Api` itself
- `*Models*` - Any namespace containing "Models"

### Type Patterns

Exclude specific types by name.

```json
{
  "exclusions": {
    "types": [
      "Program",
      "Startup",
      "*Controller",
      "*ViewModel"
    ]
  }
}
```

**Pattern Syntax:**
- `MyClass` - Exact type name
- `*Service` - Types ending with "Service"
- `*Base*` - Types containing "Base"

### Method Patterns

Exclude specific methods by name.

```json
{
  "exclusions": {
    "methods": [
      "Main",
      "Configure*",
      "On*"
    ]
  }
}
```

### Attribute-Based Exclusions

Type-level attributes such as `Serializable` skip the type for renaming, strings, constants, and control flow. Member-level attributes (`JsonPropertyName`, `JsonProperty`, `XmlElement`, `XmlAttribute`) currently skip **symbol renaming** of that member only.

> **Important:** Specifying `exclusions.attributes` **replaces** the 7-item default list below (it does not merge). Include the defaults alongside any custom attributes you add.

```json
{
  "exclusions": {
    "attributes": [
      "SerializableAttribute",
      "DataContractAttribute",
      "DataMemberAttribute",
      "JsonPropertyNameAttribute",
      "JsonPropertyAttribute",
      "XmlElementAttribute",
      "XmlAttributeAttribute"
    ]
  }
}
```

**Default Excluded Attributes (all 7):**
- `SerializableAttribute` - Binary serialization
- `DataContractAttribute` / `DataMemberAttribute` - WCF serialization
- `JsonPropertyNameAttribute` - System.Text.Json
- `JsonPropertyAttribute` - JSON.NET
- `XmlElementAttribute` / `XmlAttributeAttribute` - XML serialization

`ComVisible(true)` types, methods, fields, properties, and events are never renamed. Only an explicit `[ComVisible(true)]` is checked (assembly-level COM visibility is not inferred).

Set `symbolRenaming.preserveXaml` (the Protection screen of the desktop app, or the config file) to keep public instance properties on types that look XAML-bindable: name ends with `ViewModel` or `View`, implements `INotifyPropertyChanged`, declares `DependencyProperty` fields, or has a resolvable base whose name contains `DependencyObject`. Framework WPF bases often fail to resolve, so prefer the `*ViewModel` suffix. Note that `preserveXaml` protects those public properties only — it does **not** scan compiled XAML/BAML, so types referenced by XAML `x:Class` (for example `MainWindow` or `App`) still need manual `exclusions.types` entries.

**Common Additions:**
- `ProtoMemberAttribute` - protobuf-net

### Automatic Exclusions

These apply to **symbol renaming** (constructors are also skipped by control-flow flatten). String and constant encryption still visit constructors and `Main`.

- **Pinned attribute types**: `Obfy.Runtime.WatermarkAttribute` and the global `ConfusedByAttribute` and `DotfuscatorAttribute` decoys keep their name, members and namespace
- **Constructors**: `.ctor`, `.cctor`
- **Module entry point** (not every method named `Main`)
- **Virtual methods**: overrides, interface implementations, finalizers and delegate `Invoke`, in every type
- **Interface implementations**
- **Literal fields**: `const` fields

The helper types Obfy injects (`Obfy.Runtime.*`: the decryptors, the anti-debug and anti-tamper checks, the virtualization interpreter) are renamed with the rest of the module, apart from the pinned attributes above. The other techniques leave them alone (string, constant and resource encryption, reference proxy, virtualization), and control flow handles them on its own terms (see [Techniques.md](Techniques.md)).

## Preserving Public API

For libraries, preserve public-facing members:

```json
{
  "symbolRenaming": {
    "enabled": true,
    "preservePublicApi": true
  }
}
```

This preserves:
- Public types
- Public methods
- Public properties
- Public fields
- Public events

Internal and private members are still renamed.

## Symbol Mapping

Generate a mapping file to debug obfuscated assemblies:

```bash
obfy MyApp.dll -o output/ --map symbols.json
```

**Map File Format:**

```json
{
  "Type:MyNamespace.MyClass": "_a",
  "Method:MyNamespace.MyClass.MyMethod(System.Void ())": "_b",
  "Field:MyNamespace.MyClass._privateField": "_c"
}
```

Keys are prefixed `Type:`, `Method:`, `Field:`, `Property:`, `Event:`, or `Namespace:`. A method key includes the signature, as in `Method:Ns.MyType.Foo(System.Void ())`. Source maps use the Roslyn symbol kind (`NamedType:`, `Parameter:`, and the others). The desktop Results tree nests members under the longest matching `Type:` name.

**Uses:**
- Decode stack traces from production
- Understand obfuscated crash reports
- Verify specific symbols were renamed

## Best Practices

### 1. Test Thoroughly

Always test obfuscated assemblies before deployment:

```bash
# Obfuscate
obfy MyApp.dll -o output/

# Run tests against obfuscated output
dotnet test output/MyApp.Tests.dll
```

### 2. Start with Standard Level

Begin with `standard` and increase protection gradually:

```bash
# Start here
obfy MyApp.dll -l standard -o output/

# If needed, increase
obfy MyApp.dll -l aggressive -o output/
```

### 3. Exclude Serialized Types

Any type that gets serialized/deserialized needs member names preserved (`JsonPropertyNameAttribute`, `JsonPropertyAttribute`, `DataContractAttribute`, `DataMemberAttribute`, `SerializableAttribute`, `XmlElementAttribute`, and `XmlAttributeAttribute` are excluded by default; if you set `exclusions.attributes`, include those defaults plus any extras because the array replaces them):

```json
{
  "exclusions": {
    "types": ["*Dto", "*Model", "*Request", "*Response"],
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

### 4. Preserve Reflection Targets

Code accessed via reflection needs exclusions:

```json
{
  "exclusions": {
    "types": ["*Plugin", "*Handler"],
    "methods": ["Invoke", "Execute", "Handle"]
  }
}
```

### 5. Handle Dependency Injection

DI frameworks often use reflection. Preserve (keeping the 7 default serialization attributes if your app also serializes models):

```json
{
  "exclusions": {
    "types": ["*Service", "*Repository", "*Factory"],
    "attributes": [
      "SerializableAttribute",
      "DataContractAttribute",
      "DataMemberAttribute",
      "JsonPropertyNameAttribute",
      "JsonPropertyAttribute",
      "XmlElementAttribute",
      "XmlAttributeAttribute",
      "InjectAttribute",
      "ServiceAttribute"
    ]
  }
}
```

### 6. Use Configuration Files

For repeatable builds, use config files:

```bash
# Generate base config
obfy config generate -o obfy.json

# Customize and commit to source control
# Use in CI/CD
obfy MyApp.dll -c obfy.json -o output/
```

### 7. Obfuscate Release Builds Only

```bash
# Development
dotnet build -c Debug

# Production with obfuscation
dotnet build -c Release
obfy bin/Release/net10.0/MyApp.dll -o dist/
```

## Performance Considerations

Obfuscation passes trade runtime CPU/memory or output size for reverse-engineering cost in very different ways. The table below summarizes relative runtime and size impact across all techniques, followed by technique-specific tuning notes.

| Technique | Startup / Load Cost | Per-Call / Steady-State Cost | Output Size Impact | Main Tuning Levers |
|-----------|---------------------|------------------------------|--------------------|--------------------|
| **Symbol Renaming** | None | None | Neutral to tiny (`Sequential` shrinks `#Strings`; `Unreadable`/`Hash`/`Random` grow it slightly) | `mode` |
| **Metadata Removal** | None | None | Shrinks output (drops PDB info, 9 compiler/debugger attributes, XML docs) | `removeDebugInfo`, `removeAttributes`, `stripDocumentation` |
| **Watermark** | None | None | Negligible (one attribute type + assembly attribute) | — |
| **String Encryption** | Low–moderate (`.cctor` populates encrypted `byte[][]` table) | Low (decrypted once per literal, then read from static `string[]` cache) | Small–moderate (`<StringDecryptor>` + encrypted payloads) | `algorithm` (`Xor` vs `Aes256`), `minStringLength` |
| **Constant Encryption** | Low (`.cctor` populates encrypted `byte[][]` table) | Moderate–high on hot paths (**uncached**: decrypts and allocates `byte[]` on every access) | Moderate (`<ConstantDecryptor>` + per-constant array init in `.cctor`) | `algorithm` (`Xor` default), `integerThreshold`, `longThreshold`, `skipCommonFloats`, `skipCommonDoubles` |
| **Resource Encryption** | None at module load | Low–high per load (buffers into `MemoryStream` and decrypts full resource on each call; uncached) | Small (AES-256 IV/padding + `<ResourceDecryptor>`) | `algorithm`, `includePatterns` / `excludePatterns` |
| **Method IL Encryption** | Low–moderate one-time (`.cctor` calls `VirtualProtect` + XOR-decrypts bodies in memory) | None once decrypted and JIT-compiled | Small (`<MethodCrypt>` + per-method table) | Not sensitive to hot paths once loaded; Windows-only (`Default` profile) |
| **Control Flow** | None (longer JIT time on large/flattened methods) | Low–high (`Switch` dispatcher breaks straight-line flow/inlining; `OpaquePredicate` adds branches and runtime property checks) | Moderate–high IL growth (runtime helpers are transformed at intensity `100` whenever enabled) | `mode` (`OpaquePredicate` < `Switch` < `Combined`), `intensity` (`0–100`) |
| **Reference Proxy** | None | Low (one extra static `call` + `ldftn`/`calli` trampoline per proxied site; can inhibit JIT inlining) | Low–moderate (trampoline methods + nested proxy types for non-public targets) | `proxyExternalCalls` (`false` leaves BCL/third-party calls direct) |
| **Virtualization** | Low (interpreter `Init`) | **Highest** (managed bytecode loop, heap-backed VM frames, boxing, `MethodBase.Invoke` reflection) | Moderate fixed VM runtime + XOR-encoded bytecode blob | Keep `maxMethods` small (default `32`, max `256`) and target cold, sensitive methods only |
| **Anti-Debug** | Low (`.cctor` check) | Moderate–high on call-heavy code (`<AntiDebug>.Check` injected into most non-empty methods: `Debugger` + `TickCount` + `kernel32` P/Invoke on `Default` profile) | Small (`<AntiDebug>` + one `call` per instrumented method) | Exclude tight inner-loop types/methods via `[Obfuscation]` or `exclusions` |
| **Anti-Dump** | Very low one-time (`.cctor` wipes PE header fields and patches in-process `MiniDumpWriteDump` on Windows x86/x64) | None | Negligible (`<AntiDump>`) | Disabled on non-`Default` `runtimeProfile` |
| **Anti-Tamper** | Low–moderate one-time (reads assembly from disk via `File.ReadAllBytes` and computes SHA-256; latency scales with file size) | None (`_v` guard runs check once; skipped on empty-location / packed `LoadFromStream` loads) | Negligible (`<AntiTamper>` + 32-byte hash slot) | `checkEntryPoint`, `checkModuleInitializer`; disabled on non-`Default` `runtimeProfile` |
| **Anti-Decompiler** | None | None (dead junk code + passive attributes) | Small–moderate (scales with `junkTypeCount * junkMethodsPerType`) | `injectJunkTypes`, `junkTypeCount` (`1–50`), `junkMethodsPerType` (`1–20`) |
| **Dependency Embedding** | None at module load | Low one-time per embedded DLL on first `AssemblyResolve` (`Assembly.Load(byte[])`) | Adds full uncompressed size of all embedded `Obfy.Embedded.*.dll` resources | `includePatterns` / `excludePatterns` |
| **Packing** | Moderate one-time (AES-256-CBC decrypts payload and loads via `AssemblyLoadContext.LoadFromStream`) | None after load | `win-x64` replaces managed PE with native host stub + encrypted overlay; `portable` adds `{name}.launcher.exe` sidecar | `rid` (`win-x64` vs `portable`) |

### String Encryption

| Setting | Impact |
|---------|--------|
| `Aes256` | Slower first-access decryption and larger encrypted payloads, higher resistance |
| `Xor` | Much faster first-access decryption, lower resistance |
| Higher `minStringLength` | Skips short literals, smaller table and faster startup |

### Constant Encryption

| Setting | Impact |
|---------|--------|
| `Xor` (default) | Recommended for numeric constants because decryption is **uncached** and runs on every access |
| `Aes256` | High per-access CPU/allocation cost; avoid in loops or math-heavy methods |
| Thresholds (`integerThreshold`, `longThreshold`, `skipCommonFloats`, `skipCommonDoubles`) | Skipping `-1`, `0`, `1` prevents heavy overhead on loop counters and conditionals |

### Control Flow

| Setting | Impact |
|---------|--------|
| `OpaquePredicate` | Lowest runtime overhead (adds non-constant runtime checks without a dispatcher loop) |
| `Switch` | Moderate–high overhead; state-machine loop hinders JIT optimizations and increases IL size |
| `Combined` | Highest runtime and size cost |
| Higher `intensity` (`0–100`) | More blocks transformed, larger IL and slower execution; runtime helpers always use `100` when enabled |

### Virtualization and Anti-Debug Hot-Path Guidance

- **Virtualization:** Reserve virtualization for a small number of sensitive methods (`maxMethods` defaults to `32`) that are not inside performance-critical loops. Each virtualized invocation runs inside `Obfy.Runtime.Vm.Run`, using heap-allocated VM frames and `MethodBase.Invoke`.
- **Anti-Debug:** Because `<AntiDebug>.Check` is inserted at the start of most non-empty user methods (and on the `Default` profile performs `kernel32` P/Invoke calls in addition to `Debugger.IsAttached`/`IsLogging`/`TickCount`), call-intensive helper methods or tight inner loops can see measurable slowdown; exclude hot paths with `[Obfuscation(Exclude = true)]` or `exclusions`.

### Symbol Renaming

| Setting | Impact |
|---------|--------|
| Any mode | No runtime impact (metadata-only at obfuscation time) |
| `Unreadable` | Slightly larger `#Strings` metadata heap (multi-byte UTF-8 characters) |
| `Sequential` | Smallest metadata (`a`, `b`, `aa`, ...) |

## Troubleshooting

### Application Crashes After Obfuscation

**Cause:** Serialization or reflection breaking.

**Solution:**
1. Enable verbose mode: `obfy -v`
2. Check what was renamed
3. Add exclusions for affected types

```json
{
  "exclusions": {
    "types": ["*Dto", "*Model"]
  }
}
```

### Missing Method Exception

**Cause:** External code calling renamed methods.

**Solution:**
1. Preserve public API: `--preserve-public`
2. Or exclude specific types

### Serialization Fails

**Cause:** JSON/XML properties renamed.

**Solution:** `JsonPropertyName`, `JsonProperty`, `XmlElement`, and `XmlAttribute` are excluded from renaming by default. For other serializers, add the attribute (for example `ProtoMemberAttribute`) or turn on `symbolRenaming.preserveXaml` for view-models.

### DI Container Fails

**Cause:** Types renamed that DI looks up by name.

**Solution:**
```json
{
  "exclusions": {
    "types": ["*Service", "*Repository"]
  }
}
```

### Anti-Debug Triggers in Development

**Cause:** Debugging obfuscated build.

**Solution:**
- Disable anti-debug for debug builds
- Use separate config files for dev/prod

### Performance Degradation

**Cause:** High control flow intensity.

**Solution:**
```json
{
  "controlFlow": {
    "enabled": true,
    "intensity": 25
  }
}
```

### Assembly Won't Load

**Cause:** Strong name or signing issues.

**Solution:** Set `signing.enabled` and `signing.keyFile` (`.snk` or `.pfx`). For PFX, set `signing.passwordEnvironmentVariable` to the name of an environment variable that holds the password. Signing runs after PE patches and fails the run if the key cannot be applied.

### Single-File Executable Error

**Error:** `Failed to load input: .NET data directory RVA is 0`

**Cause:** Single-file .NET apps (`PublishSingleFile=true`) bundle all assemblies into a native host that dnlib cannot read.

**Solution:** Obfuscate before publishing to single-file:

```bash
# 1. Build release (not single-file)
dotnet build -c Release

# 2. Obfuscate the DLLs
obfy bin/Release/net10.0/MyApp.dll -o bin/Release/net10.0/ -l aggressive

# 3. Publish single-file (uses obfuscated assemblies)
dotnet publish -c Release -p:PublishSingleFile=true
```

The obfuscated code gets bundled into the single-file output.

**MSBuild Integration:**

```xml
<Target Name="Obfuscate" AfterTargets="Build" BeforeTargets="Publish"
        Condition="'$(Configuration)' == 'Release'">
  <Exec Command="obfy $(TargetPath) -c obfy.json -o $(TargetDir)" />
</Target>
```

## Integration with Build Systems

### MSBuild

```xml
<Target Name="Obfuscate" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
  <Exec Command="obfy $(TargetPath) -c obfy.json -o $(OutputPath)obfuscated/" />
</Target>
```

### GitHub Actions

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '10.x'
- name: Build and Obfuscate
  run: |
    dotnet build -c Release
    dotnet tool install --global Obfy
    mkdir -p dist
    cp -r bin/Release/net10.0/* dist/
    obfy dist/MyApp.dll -l standard -o dist/
```

Obfy writes only the protected assembly (plus a `.runtimeconfig.json` when `packing.enabled` is set), so keep sidecar `.runtimeconfig.json`, `.deps.json`, and dependency files alongside the output (as shown above, or copy them after `-o dist/`). Use `-l standard` on Linux (or disable `methodEncryption` and `antiDump` in `obfy.json`), because `-l aggressive` enables Windows-only `kernel32` protections whose encrypted method bodies stay undecrypted off Windows.

### Azure DevOps

```yaml
- script: |
    dotnet tool install --global Obfy
    obfy $(Build.ArtifactStagingDirectory)/*.dll -o $(Build.ArtifactStagingDirectory)/protected/
  displayName: 'Obfuscate assemblies'
```

## See Also

- [CLI Reference](CLI.md) - Command-line options
- [Configuration](Configuration.md) - Full settings reference
- [Techniques](Techniques.md) - How each technique works

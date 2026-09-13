# Advanced Usage

Exclusion rules, best practices, and troubleshooting for Obfy.

## `[Obfuscation]` attribute

Obfy honors `System.Reflection.ObfuscationAttribute` on types and members for **assembly** obfuscation (renaming, control flow, strings, constants, plus `Feature=all` for method encryption, reference proxy, and anti-debug). Source obfuscators honor the same attribute for renaming, strings, and control flow.

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
- `MyApp.Api.*` - Namespace and all children
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

```json
{
  "exclusions": {
    "attributes": [
      "SerializableAttribute",
      "DataContractAttribute",
      "DataMemberAttribute",
      "JsonPropertyAttribute",
      "XmlElementAttribute"
    ]
  }
}
```

**Default Excluded Attributes:**
- `SerializableAttribute` - Binary serialization
- `DataContractAttribute` / `DataMemberAttribute` - WCF serialization
- `JsonPropertyNameAttribute` - System.Text.Json
- `JsonPropertyAttribute` - JSON.NET
- `XmlElementAttribute` / `XmlAttributeAttribute` - XML serialization

`ComVisible(true)` types, methods, fields, properties, and events are never renamed. Only an explicit `[ComVisible(true)]` is checked (assembly-level COM visibility is not inferred).

Set `symbolRenaming.preserveXaml` (Desktop wizard / Settings panel) to keep public instance properties on types that look XAML-bindable: name ends with `ViewModel` or `View`, implements `INotifyPropertyChanged`, declares `DependencyProperty` fields, or has a resolvable base whose name contains `DependencyObject`. Framework WPF bases often fail to resolve, so prefer the `*ViewModel` suffix.

**Common Additions:**
- `ProtoMemberAttribute` - protobuf-net

### Automatic Exclusions

These are always excluded regardless of settings:

- **Runtime namespace**: `Obfy.Runtime.*`
- **Constructors**: `.ctor`, `.cctor`
- **Entry points**: `Main` method
- **Virtual overrides**: Methods overriding base class
- **Interface implementations**: Methods implementing interfaces
- **Literal fields**: `const` fields

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
  "MyNamespace.MyClass": "_a",
  "MyNamespace.MyClass.MyMethod": "_b",
  "MyNamespace.MyClass._privateField": "_c"
}
```

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

Any type that gets serialized/deserialized needs member names preserved:

```json
{
  "exclusions": {
    "types": ["*Dto", "*Model", "*Request", "*Response"],
    "attributes": ["JsonPropertyAttribute", "DataMemberAttribute"]
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

DI frameworks often use reflection. Preserve:

```json
{
  "exclusions": {
    "types": ["*Service", "*Repository", "*Factory"],
    "attributes": ["InjectAttribute", "ServiceAttribute"]
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

### String Encryption

| Setting | Impact |
|---------|--------|
| AES-256 | Slower startup, higher security |
| XOR | Faster startup, lower security |
| Higher `minStringLength` | Fewer strings encrypted, faster |

### Control Flow

| Setting | Impact |
|---------|--------|
| Higher `intensity` | Slower execution, better protection |
| `Combined` mode | Most overhead, maximum obfuscation |
| `Switch` mode | Moderate overhead |

### Symbol Renaming

| Setting | Impact |
|---------|--------|
| Any mode | No runtime impact (compile-time only) |
| `Unreadable` | Slightly larger metadata |
| `Sequential` | Smallest metadata |

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

### Visual Studio Extension

For Visual Studio 2022 users, the Obfy extension provides the easiest integration:

1. **Install the extension** from the VSIX file
2. **Right-click your project** in Solution Explorer
3. **Enable Post-Build Obfuscation** to automatically protect on every build
4. **Configure settings** via the Obfy Settings dialog

**Extension Features:**
- Right-click → **Obfuscate** for on-demand protection
- Right-click → **Enable Post-Build Obfuscation** for automatic builds
- Right-click → **Obfy Settings** to configure level and protections
- **Tools → Options → Obfy** for global defaults
- Per-project settings stored in `obfy.json`
- Output window shows progress and statistics

**Release-Only Obfuscation:**

By default, the extension only runs obfuscation for Release builds. Configure this in Tools → Options → Obfy → "Release Only".

### JetBrains Rider Extension

For JetBrains Rider users, the Obfy plugin provides seamless integration:

1. **Install the plugin** from the ZIP file via Settings → Plugins → Install from Disk
2. **Right-click your project** in Solution Explorer
3. **Enable Post-Build Obfuscation** to automatically protect on every build
4. **Configure settings** via the Settings dialog

**Plugin Features:**
- Right-click → **Obfy** → **Obfuscate** for on-demand protection
- Right-click → **Obfy** → **Enable Post-Build Obfuscation** for automatic builds
- Right-click → **Obfy** → **Settings** to configure level and protections
- **Settings → Tools → Obfy** for global defaults
- Per-project settings stored in `obfy.json`
- Tool window shows progress and statistics

**Release-Only Obfuscation:**

By default, the plugin only runs obfuscation for Release builds. Configure this in Settings → Tools → Obfy → "Only obfuscate on Release builds".

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
- name: Obfuscate
  run: |
    dotnet tool install --global Obfy
    obfy bin/Release/net10.0/MyApp.dll -l aggressive -o dist/
```

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

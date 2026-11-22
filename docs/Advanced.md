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

Set `symbolRenaming.preserveXaml` (the Protection screen of the desktop app, or the config file) to keep public instance properties on types that look XAML-bindable: name ends with `ViewModel` or `View`, implements `INotifyPropertyChanged`, declares `DependencyProperty` fields, or has a resolvable base whose name contains `DependencyObject`. Framework WPF bases often fail to resolve, so prefer the `*ViewModel` suffix.

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
- Right-click → **Obfy Settings...** to configure level and protections
- Right-click → **Cancel Obfuscation** stops every run that is going, manual or post-build, not only the project you right-clicked. The command is on the project menu, and it is shown only while a run is active
- **Tools → Options → Obfy** for global defaults
- Per-project settings stored in `obfy.json`
- The Obfy pane of the Output window shows progress and statistics

**Release-Only Obfuscation:**

By default, the extension only runs obfuscation for Release builds. Configure this in Tools → Options → Obfy → "Release Only".

**Cancelling and the time limit:**

A run the extension starts ends when the CLI exits, when you choose **Cancel Obfuscation**, or when the time limit passes. The limit is 30 minutes. Change it under Tools → Options → Obfy → General → **Run Timeout (minutes)**: a value below 1 counts as 30, and a value above 1440 (one day) counts as 1440. The option keeps the number you typed; only the run's limit changes. There is no CLI flag and no `obfy.json` setting for this limit. It applies only to runs the extension starts, and the extension does not pass it on the `obfy` command line.

**Cancel Obfuscation** stops every run that is in flight. A run is also cancelled when the solution closes, when the extension is unloaded, and when the same project is built, rebuilt or deployed again. A Clean does not cancel it. Cancelling or timing out ends the CLI and every process it started. The CLI is in a job that ends that process tree when Visual Studio exits.

A cancelled run is a warning in the Obfy pane of the Output window, not an error dialog. A manual run says "Obfuscation was cancelled." A post-build run says "Post-build obfuscation cancelled for <project>". A timeout is an error that names the limit that was applied and where to change it. With the default it reads: "Obfuscation timed out after 30 minutes and the Obfy CLI was stopped. The time limit is under Tools > Options > Obfy > General > Run Timeout (minutes)." A manual timeout also opens that text in a message box. A post-build timeout is only the Output window line, prefixed with the project name ("Post-build obfuscation of <project>: ..."). If the CLI does not exit when it is stopped, the pane also records an error, "The Obfy CLI process did not exit after it was asked to stop.", and a manual cancel or a timeout says the process may still be running.

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

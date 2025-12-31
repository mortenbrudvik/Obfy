# Advanced Usage

Exclusion rules, best practices, and troubleshooting for Obfy.

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

Members with specific attributes are automatically excluded.

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
- `DataContractAttribute` - WCF serialization
- `DataMemberAttribute` - WCF serialization

**Common Additions:**
- `JsonPropertyAttribute` - JSON.NET
- `JsonPropertyNameAttribute` - System.Text.Json
- `XmlElementAttribute` - XML serialization
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
obfy bin/Release/net8.0/MyApp.dll -o dist/
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

**Solution:**
```json
{
  "exclusions": {
    "attributes": [
      "JsonPropertyAttribute",
      "JsonPropertyNameAttribute",
      "XmlElementAttribute"
    ]
  }
}
```

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

**Solution:**
- Re-sign assembly after obfuscation
- Or disable strong naming during obfuscation

### Single-File Executable Error

**Error:** `Failed to load input: .NET data directory RVA is 0`

**Cause:** Single-file .NET apps (`PublishSingleFile=true`) bundle all assemblies into a native host that dnlib cannot read.

**Solution:** Obfuscate before publishing to single-file:

```bash
# 1. Build release (not single-file)
dotnet build -c Release

# 2. Obfuscate the DLLs
obfy bin/Release/net8.0/MyApp.dll -o bin/Release/net8.0/ -l aggressive

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
- name: Obfuscate
  run: |
    dotnet tool install --global Obfy
    obfy bin/Release/net8.0/MyApp.dll -l aggressive -o dist/
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

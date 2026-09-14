# MSBuild Integration Example

Demonstrates automatic obfuscation during the build process.

## How It Works

The `.csproj` file includes an MSBuild target that runs Obfy after each Release build:

```xml
<Target Name="Obfuscate" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
  <Exec Command="obfy &quot;$(TargetPath)&quot; -c obfy.json -o &quot;$(TargetDir)&quot;" />
</Target>
```

## Prerequisites

```bash
dotnet tool install --global Obfy
```

Or use the Windows installer so `obfy` is on PATH.

## Usage

### Debug Build (No Obfuscation)

```bash
dotnet build -c Debug
dotnet run -c Debug
```

### Release Build (Automatic Obfuscation)

```bash
dotnet build -c Release
# Output is automatically obfuscated!

dotnet run -c Release
```

## Build Output

```
MSBuild version ...
  MsBuildIntegration -> bin\Release\net10.0\MsBuildIntegration.dll
  Obfuscating bin\Release\net10.0\MsBuildIntegration.dll...
  Obfuscation complete!
```

## Configuration

This example’s `obfy.json` is a **custom** file (`-c` does not re-apply the Aggressive preset). It sets `"level": "aggressive"` as a label, then only:

- String encryption (AES-256, min length 2)
- Control flow Switch at intensity **50** (product Aggressive is 80)
- Unreadable symbol renaming
- Anti-debug
- Metadata removal

It does **not** turn on anti-tamper, anti-dump, anti-decompiler, reference proxy, method encryption, resource encryption, or constant encryption.

## CI/CD Integration

For CI/CD pipelines, add obfy installation:

### GitHub Actions

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '10.x'
- run: dotnet tool install --global Obfy
- name: Build and Obfuscate
  run: dotnet build -c Release
```

### Azure DevOps

```yaml
- script: dotnet build -c Release
  displayName: 'Build and Obfuscate'
```

## Customization

### Obfuscate Before Publish

For single-file publishing:

```xml
<Target Name="Obfuscate" AfterTargets="Build" BeforeTargets="Publish"
        Condition="'$(Configuration)' == 'Release'">
  <Exec Command="obfy &quot;$(TargetPath)&quot; -c obfy.json -o &quot;$(TargetDir)&quot;" />
</Target>
```

### Skip Obfuscation

Add a property to conditionally skip:

```xml
<Target Name="Obfuscate" AfterTargets="Build"
        Condition="'$(Configuration)' == 'Release' AND '$(SkipObfuscation)' != 'true'">
  ...
</Target>
```

Then build with:

```bash
dotnet build -c Release -p:SkipObfuscation=true
```

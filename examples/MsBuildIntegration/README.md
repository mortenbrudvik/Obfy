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

Install Obfy globally:

```bash
dotnet tool install --global Obfy
```

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
  MsBuildIntegration -> bin\Release\net8.0\MsBuildIntegration.dll
  Obfuscating bin\Release\net8.0\MsBuildIntegration.dll...
  Obfuscation complete!
```

## Configuration

This example uses aggressive protection:

- **String Encryption**: AES-256 with minimum 2-character strings
- **Control Flow**: Switch dispatcher at 50% intensity
- **Symbol Renaming**: Unreadable characters
- **Anti-Debug**: Debugger detection enabled
- **Metadata Removal**: All debug info stripped

## CI/CD Integration

For CI/CD pipelines, add obfy installation:

### GitHub Actions

```yaml
- name: Install Obfy
  run: dotnet tool install --global Obfy

- name: Build and Obfuscate
  run: dotnet build -c Release
```

### Azure DevOps

```yaml
- script: dotnet tool install --global Obfy
  displayName: 'Install Obfy'

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

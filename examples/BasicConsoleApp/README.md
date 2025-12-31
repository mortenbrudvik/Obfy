# Basic Console App Example

A simple console application demonstrating Obfy obfuscation.

## Build and Obfuscate

```bash
# Build the project
dotnet build -c Release

# Obfuscate the output
obfy bin/Release/net8.0/BasicConsoleApp.dll -c obfy.json -o bin/Release/net8.0/obfuscated/

# Run the obfuscated version
dotnet bin/Release/net8.0/obfuscated/BasicConsoleApp.dll
```

## What Gets Obfuscated

| Element | Before | After |
|---------|--------|-------|
| Class name | `Program` | `_‌‍‏` |
| Method names | `ProcessUserData`, `PerformCalculation` | `_‌‍‎`, `_‌‍‏` |
| String literals | `"Welcome to the application!"` | Encrypted, decrypted at runtime |
| Constants | `SecretApiKey`, `ConnectionString` | Encrypted values |

## Configuration

The `obfy.json` configuration uses standard protection:
- **String Encryption**: AES-256 encryption for all strings
- **Symbol Renaming**: Unreadable character sequences
- **Metadata Removal**: Debug info and attributes stripped

## Verify Obfuscation

Use a decompiler like ILSpy or dnSpy to compare:
- `bin/Release/net8.0/BasicConsoleApp.dll` (original)
- `bin/Release/net8.0/obfuscated/BasicConsoleApp.dll` (obfuscated)

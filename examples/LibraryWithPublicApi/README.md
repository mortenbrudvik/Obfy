# Library with Public API Example

Demonstrates how to obfuscate a class library while preserving its public API.

## Key Concept

When distributing a library, you want to:
- **Preserve** public class, method, and property names (for consumers)
- **Obfuscate** internal implementation details
- **Encrypt** sensitive strings

## Build and Obfuscate

```bash
# Build the library
dotnet build -c Release

# Obfuscate with public API preservation
obfy bin/Release/net10.0/LibraryWithPublicApi.dll -c obfy.json -o bin/Release/net10.0/obfuscated/
```

## What Happens

### Preserved (Public API)

| Element | Status |
|---------|--------|
| `Calculator` class | Preserved |
| `Add()`, `Multiply()` methods | Preserved |
| `OperationCount` property | Preserved |
| `IDataProcessor` interface | Preserved |
| `DataProcessor` class | Preserved |
| `Process()`, `GetResult()` methods | Preserved |

### Obfuscated (Internal)

| Element | Before | After |
|---------|--------|-------|
| `InternalHelper` class | `InternalHelper` | `_‌‍‏` |
| `PerformAddition()` | `PerformAddition` | `_‌‍‎` |
| `_internalSecret` field | `_internalSecret` | `_‌‍‏` |
| String literals | `"internal-key-12345"` | Encrypted |

## Configuration Highlights

```json
{
  "symbolRenaming": {
    "preservePublicApi": true  // Key setting!
  },
  "metadata": {
    "removeAttributes": false,  // Keep XML docs for public API
    "stripDocumentation": false
  }
}
```

## Usage by Consumers

After obfuscation, consumers can still use your library normally:

```csharp
using MyLibrary;

var calc = new Calculator();
var result = calc.Add(5, 3);  // Works exactly the same!
Console.WriteLine(calc.OperationCount);
```

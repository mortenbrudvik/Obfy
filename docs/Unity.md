# Unity recipe

Obfy can obfuscate Unity **Mono** player assemblies. IL2CPP compiles managed code to C++ and **must not** receive PE-header tricks.

## IL2CPP-safe subset

Set `runtimeProfile` to `UnityIl2Cpp` (the wizard Game/Unity preset does this). That **disables**:

- Method IL encryption (`VirtualProtect`)
- Anti-dump PE wipe
- Dependency embedding (`AssemblyResolve`)

**Safe to keep:** symbol renaming (with `UnityEngine.*` / `Unity.*` exclusions), string/constant encryption, control flow, reference proxy (in-module). Anti-debug also injects `kernel32!IsDebuggerPresent`; leave it off for IL2CPP unless you have confirmed the player still runs.

## Config

See `examples/unity/obfy.json`. `Library/ScriptAssemblies` is editor output, not the player/IL2CPP staging assemblies. Run Obfy on the managed player DLLs **before** IL2CPP, not on the native output:

```bash
obfy Assembly-CSharp.dll -c obfy.json -o Build/Obfuscated/
```

## Limits

- Do not claim IL2CPP “protection” beyond renaming/strings that survive `global-metadata.dat`.
- Test a Development Player after obfuscation; reflection-heavy plugins often need extra exclusions.
- This is a recipe, not a Unity Editor plugin.

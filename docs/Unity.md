# Unity recipe

Obfy can obfuscate Unity **Mono** player assemblies. IL2CPP compiles managed code to C++ and **must not** receive PE-header tricks.

## IL2CPP-safe subset

Set `runtimeProfile` to `UnityIl2Cpp` (the wizard Game/Unity preset does this). That **disables**:

- Method IL encryption (`VirtualProtect`)
- Anti-dump PE wipe
- Dependency embedding (`AssemblyResolve`)

**Safe to keep:** symbol renaming (with `UnityEngine.*` / `Unity.*` exclusions), string/constant encryption, control flow, reference proxy (in-module), anti-debug managed checks.

## Config

See `examples/unity/obfy.json`. Typical post-build:

```bash
obfy Library/ScriptAssemblies/Assembly-CSharp.dll -c obfy.json -o Build/Obfuscated/
```

Run this **before** IL2CPP (Player build), on the managed DLLs, not on the native output.

## Limits

- Do not claim IL2CPP “protection” beyond renaming/strings that survive `global-metadata.dat`.
- Test a Development Player after obfuscation; reflection-heavy plugins often need extra exclusions.
- This is a recipe, not a Unity Editor plugin.

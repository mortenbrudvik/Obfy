# Platform notes

## Unity

See [Unity.md](Unity.md). Use `runtimeProfile: UnityIl2Cpp` for IL2CPP players.

## Blazor WebAssembly

Use `runtimeProfile: BlazorWasm` (wizard preset). Method encryption, anti-dump, and AssemblyResolve embedding are disabled. Anti-debug still runs; kernel32 P/Invoke is omitted and a report warning is emitted. Obfuscate the published managed DLLs (`_framework/*.dll`) **before** they are served. Publish with `WasmEnableWebcil=false` if you need DLLs rather than webcil `.wasm`. Trimming and linker XML often need extra rename exclusions.

Example: `examples/blazor/obfy.json`. **Tested:** `BlazorWasmTests` publishes a fixture, obfuscates `_framework/BlazorWasmApp*.dll`, and invokes a probe type (`Category=Platform`).

## MAUI

Turn on `symbolRenaming.preserveXaml` so bindings keep working. Exclude `Microsoft.Maui` and `Microsoft.Maui.*` (`*` does not match the namespace itself). There is no MAUI runtime profile, so method encryption, anti-dump, anti-tamper, and virtualization are **not** auto-disabled. On iOS/Android, missing `kernel32` is swallowed (the module loads; the first encrypted call then fails) — leave method encryption and anti-dump off, as the example does. `VirtualProtect` returning false is FailFast, not the missing-export case.

Example: `examples/maui/obfy.json`. **Tested (Windows subset):** `MauiWindowsTests` runs `dotnet new maui` and obfuscates the Windows TFM output when the MAUI workload is installed (`Category=Platform`; skipped otherwise). iOS/Android are not tested.

## NativeAOT

`runtimeProfile: NativeAot` disables `VirtualProtect`-based method encryption, anti-dump, and AssemblyResolve embedding. Anti-debug still runs; kernel32 P/Invoke is omitted and a report warning is emitted. Anti-tamper and opt-in virtualization are **not** gated. Remaining techniques (rename, strings, control flow, in-module proxies) still apply to the IL that the AOT compiler sees **if you obfuscate before `dotnet publish`**.

**Tested:** `NativeAotTests` obfuscates the managed IL, then `dotnet publish -p:PublishAot=true`, and runs the native exe (`Category=Platform`).

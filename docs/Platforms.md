# Platform notes

## Unity

See [Unity.md](Unity.md). Use `runtimeProfile: UnityIl2Cpp` for IL2CPP players.

## Blazor WebAssembly

Use `runtimeProfile: BlazorWasm` (wizard preset). PE-header protections and dependency embedding are disabled. Obfuscate the published managed DLLs (`_framework/*.dll`) **before** they are served. Trimming and linker XML may need extra rename exclusions.

Example: `examples/blazor/obfy.json`.

## MAUI

Turn on `symbolRenaming.preserveXaml` so bindings keep working. Exclude `Microsoft.Maui.*`. Method encryption/anti-dump are Windows-only; they will not help iOS/Android.

Example: `examples/maui/obfy.json`.

## NativeAOT

`runtimeProfile: NativeAot` disables `VirtualProtect`-based method encryption, anti-dump, and AssemblyResolve embedding. Remaining techniques (rename, strings, control flow, in-module proxies) still apply to the IL that the AOT compiler sees **if you obfuscate before `dotnet publish`**.

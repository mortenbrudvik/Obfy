# Platform notes

## Target frameworks

Protected assemblies run on .NET Core 3.1 (`netcoreapp3.1`), .NET 5 through .NET 10, and .NET Framework 4.6.1 through 4.8 (`net461` to `net48`). A `netstandard2.0` library runs on any of those. No technique is turned off for a target. The injected helper code names framework types (`File`, `Environment`, `Marshal`, the hashing and AES types, and others) through the assemblies the input's own core library reference points at, and the version of that reference decides where each type lives (the `System.Runtime` facade of net6 has no `RuntimeInformation`, the `System.Security.Cryptography` assembly does not exist before .NET 7, and `File` is not in the net5 facade), so the output loads on the framework it was built for.

Three members depend on the target, and each is left out or replaced where the target lacks it:

- Anti-tamper's single-file fallback through `Environment.ProcessPath` is emitted for .NET 6 and later only. On other targets an assembly with an empty `Location` is skipped, as [Techniques.md](Techniques.md) describes.
- Control-flow opaque predicates draw on `Environment.TickCount64` only where the core library has it (.NET Core 3.0 and later), not on .NET Framework or netstandard 2.0 modules. The other runtime values they use are on every target.
- Anti-dump reads the process architecture through `RuntimeInformation`, which .NET Framework has from 4.7.1; on a .NET Framework target before 4.7.1 (net461, for one) it reads the pointer size instead.

The test suite builds and starts a protected SDK app on each of these targets whose runtime and reference assemblies are installed, and skips the others with a reason in the test output (`ProtectedConsoleStartupTests`; see [Testing.md](Testing.md)). Method encryption and anti-dump need Windows. The platform profiles below add their own restrictions.

## Unity

See [Unity.md](Unity.md). Use `runtimeProfile: UnityIl2Cpp` for IL2CPP players.

## Blazor WebAssembly

Use `runtimeProfile: BlazorWasm` (wizard preset). Method encryption, anti-dump, AssemblyResolve embedding, virtualization, and native packing are disabled. Anti-debug still runs; kernel32 P/Invoke is omitted and a report warning is emitted. Obfuscate the published managed DLLs (`_framework/*.dll`) **before** they are served. Publish with `WasmEnableWebcil=false` if you need DLLs rather than webcil `.wasm`. Trimming and linker XML often need extra rename exclusions.

Example: `examples/blazor/obfy.json`. **Tested:** `BlazorWasmTests` publishes a fixture, obfuscates `_framework/BlazorWasmApp*.dll`, and invokes a probe type (`Category=Platform`).

## MAUI

Turn on `symbolRenaming.preserveXaml` so bindings keep working. Exclude `Microsoft.Maui` and `Microsoft.Maui.*` (`*` does not match the namespace itself). There is no MAUI runtime profile, so method encryption, anti-dump, anti-tamper, and virtualization are **not** auto-disabled. On iOS/Android, missing `kernel32` is swallowed (the module loads; the first encrypted call then fails) — leave method encryption and anti-dump off, as the example does. `VirtualProtect` returning false is FailFast, not the missing-export case.

Example: `examples/maui/obfy.json`. **Tested (Windows subset):** `MauiWindowsTests` runs `dotnet new maui` and obfuscates the Windows TFM output when the MAUI workload is installed (`Category=Platform`; skipped otherwise). iOS/Android are not tested.

## NativeAOT

`runtimeProfile: NativeAot` disables `VirtualProtect`-based method encryption, anti-dump, AssemblyResolve embedding, virtualization, and native packing. Anti-debug still runs; kernel32 P/Invoke is omitted and a report warning is emitted. Anti-tamper is **not** gated. Remaining techniques (rename, strings, control flow, in-module proxies) still apply to the IL that the AOT compiler sees **if you obfuscate before `dotnet publish`**.

**Tested:** `NativeAotTests` obfuscates the managed IL, then `dotnet publish -p:PublishAot=true`, and runs the native exe (`Category=Platform`).

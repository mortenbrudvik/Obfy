# Platform notes

## Target frameworks

Obfy is built and tested for .NET Core 3.1 (`netcoreapp3.1`), .NET 5 through .NET 10, and .NET Framework 4.6.1 and 4.8 (`net461`, `net48`). The other .NET Framework 4.x versions get the same `mscorlib` scoping (and the 4.7.1 architecture rule below) but have no test of their own. A `netstandard2.0` library gets `netstandard` scoping; its protected output is checked against the netcoreapp3.1, net6.0, net10.0 and net48 runtimes by reference, and no netstandard library is run. The target framework turns no technique off, except that native packing needs a payload that targets .NET 8 or later (`packing.rid: portable` is the alternative the error suggests). The injected helper code names framework types (`File`, `Environment`, `Marshal`, the hashing and AES types, and others) through the assemblies the input's own core library reference points at, and the version of that reference decides where each type lives (the `System.Runtime` facade of net6 has no `RuntimeInformation`, the `System.Security.Cryptography` assembly does not exist before .NET 7, and `File` is not in the net5 facade), so the output loads on the framework it was built for.

Three members depend on the target, and each is left out or replaced where the target lacks it:

- Anti-tamper's single-file fallback through `Environment.ProcessPath` is emitted for .NET 6 and later only. On other targets an assembly with an empty `Location` is skipped, as [Techniques.md](Techniques.md) describes.
- Control-flow opaque predicates draw on `Environment.TickCount64` only where the core library has it (.NET Core 3.0 and later), not on .NET Framework or netstandard 2.0 modules. The other runtime values they use are on every target.
- Anti-dump reads the process architecture through `RuntimeInformation`, which .NET Framework has from 4.7.1; on a .NET Framework target before 4.7.1 (net461, for one) it reads the pointer size instead.

The test suite builds and starts a protected SDK app on net10.0 to net5.0, netcoreapp3.1, net48 and net461, each where its runtime and reference assemblies are installed, and skips the others with a reason in the test output (`ProtectedConsoleStartupTests`; see [Testing.md](Testing.md)). Method encryption and anti-dump need Windows. The platform profiles below add their own restrictions.

### Trimming (`PublishTrimmed`)

Injected runtime helpers may synthesize framework assembly references that the input assembly did not reference directly — for example `System.Runtime.InteropServices` (`Marshal`, `DllNotFoundException`), `System.Security.Cryptography` / `System.Security.Cryptography.Algorithms` / `System.Security.Cryptography.Primitives` (`SHA256`, `Aes`, `ICryptoTransform`), or `System.IO.FileSystem` and `System.Runtime.Extensions` on older targets. Obfuscating **after** `PublishTrimmed=true` can fail at run time if the IL trimmer already stripped framework types or assemblies that Obfy's injected helpers reference. Obfuscate **before** trimming when possible (so the trimmer sees the added references) and test trimmed output carefully.

## Unity

See [Unity.md](Unity.md). Use `runtimeProfile: UnityIl2Cpp` for IL2CPP players.

## Blazor WebAssembly

Use `runtimeProfile: BlazorWasm` (wizard preset). Method encryption, anti-dump, anti-tamper, AssemblyResolve embedding, virtualization, and native packing are disabled. Anti-debug still runs; kernel32 P/Invoke is omitted and a report warning is emitted. Obfuscate the published managed DLLs (`_framework/*.dll`) **before** they are served. Publish with `WasmEnableWebcil=false` if you need DLLs rather than webcil `.wasm`. Trimming and linker XML often need extra rename exclusions.

Example: `examples/blazor/obfy.json`. **Tested:** `BlazorWasmTests` publishes a fixture, obfuscates `_framework/BlazorWasmApp*.dll`, and invokes a probe type (`Category=Platform`).

## WPF

Turn on `symbolRenaming.preserveXaml` so public instance properties on ViewModels, Views, `INotifyPropertyChanged`, and `DependencyObject` types keep their binding names. Because `preserveXaml` does not inspect compiled XAML/BAML, types referenced by XAML `x:Class` (such as `MainWindow` or `App`) still require manual `exclusions.types` entries alongside `preserveXaml: true`.

Example: `Tests/Obfy.ScenarioTests/Fixtures/WpfApp/obfy.json`. **Tested:** `WpfAppTests` and `WpfSolutionTests` obfuscate with `preserveXaml: true` and `MainWindow` excluded, and run the smoke window.

## MAUI

Turn on `symbolRenaming.preserveXaml` so bindings keep working. Exclude `Microsoft.Maui` and `Microsoft.Maui.*` (`*` does not match the namespace itself). There is no MAUI runtime profile, so method encryption, anti-dump, anti-tamper, and virtualization are **not** auto-disabled. On iOS/Android, missing `kernel32` is swallowed (the module loads; the first encrypted call then fails) — leave method encryption and anti-dump off, as the example does. `VirtualProtect` returning false is FailFast, not the missing-export case.

Example: `examples/maui/obfy.json`. **Tested (Windows subset):** `MauiWindowsTests` runs `dotnet new maui` and obfuscates the Windows TFM output when the MAUI workload is installed (`Category=Platform`; skipped otherwise). iOS/Android are not tested.

## NativeAOT

`runtimeProfile: NativeAot` disables `VirtualProtect`-based method encryption, anti-dump, anti-tamper (runtime integrity checking cannot hash an IL DLL on disk under NativeAOT), AssemblyResolve embedding, virtualization, and native packing. Anti-debug still runs; kernel32 P/Invoke is omitted and a report warning is emitted. Remaining techniques (rename, strings, control flow, in-module proxies) still apply to the IL that the AOT compiler sees **if you obfuscate before `dotnet publish`**.

**Tested:** `NativeAotTests` obfuscates the managed IL, then `dotnet publish -p:PublishAot=true`, and runs the native exe (`Category=Platform`).

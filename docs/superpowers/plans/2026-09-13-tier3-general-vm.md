# General IL Virtualization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the int-only `VirtualizationObfuscator` with a general managed IL VM that virtualizes typical CoreCLR methods and mutates encoding per build.

**Architecture:** Compile `Obfy.VmRuntime` (netstandard2.0) into an embedded resource, copy `Obfy.Runtime.Vm` into the target with dnlib, encode eligible CIL to a custom ISA, and replace bodies with stubs that call `Vm.Run`. Opcode bytes are permuted and XOR'd from `IncrementalCache.ComputeKey`. Native packer, licensing, EH/generics/byref, and AOT/Unity/Blazor are out of scope.

**Tech Stack:** C# / .NET 10 (`Obfy.Core`), netstandard2.0 (`Obfy.VmRuntime`), dnlib 4.5.0, xUnit, Shouldly, Moq. No new NuGet packages.

**Spec:** `docs/superpowers/specs/2026-09-13-tier3-general-vm-design.md`

## Global Constraints

- Execution starts in an isolated worktree via `superpowers:using-git-worktrees` (`git worktree add .worktrees/<branch> -b <branch>`). Do not implement on `main`.
- Do not add licensing, RASP, crash reporting, or a native packer.
- Do not keep the int-only interpreter as a second VM. Delete `CreateExecute` in Task 7.
- Do not register the runtime with `RuntimeHelperOptions.Interpreter` (that constant disables flatten). Use `FlattenControlFlow = true`, `Rename = true`, `EncryptIl = false`. Fallback without a spec change: if Task 7's full-pipeline execute test fails because flattening broke the dispatcher, set `FlattenControlFlow = false`. Rename and XOR stay.
- `Obfy.VmRuntime` is netstandard2.0, no package references, no reference to `Obfy.Core` / Autofac / dnlib / NLog.
- Do not leave an `AssemblyRef` to `Obfy.VmRuntime` in the target. Copy the `TypeDef`; retarget corlib TypeRefs whose scope is `netstandard` / `System.Runtime` / `mscorlib` / `System.Private.CoreLib` to `dest.CorLibTypes.AssemblyRef`. The runtime must only use corlib types: `Frame[]` + depth, **not** `System.Collections.Generic.Stack<T>` (that type is not in corlib and will not resolve after retarget).
- After the type move, `RuntimeInjection.Register` only. Do not call `AddType` (that would `module.Types.Add` a type already on the module).
- Do not use `System.Random`. Seed is `Convert.FromHexString(IncrementalCache.ComputeKey(...))` with **no second hash**. In-memory fallback uses the same `"obfy-cache|" + version + "|"` stamp and `JsonOptions` as `ComputeKey`. Fetch is `opMap[code[ip] ^ xorKey[ip % 8]]`. Do not decrypt the whole blob in `Init`.
- `CallVm` encoding is opcode + `u16` id + `u8` argc. `argc` includes `this` for instance `call`. `CallVm` only for non-virtual `call` whose resolved `MethodDef` is in the selected set. `callvirt` always goes through `MethodBase.Invoke`. `MethodSpec` (generic call) skips the caller.
- Skip `.ctor` / `.cctor`. Skip `switch`, EH, generic methods/types/calls, byref, any non-primitive valuetype param/return/local (custom structs, generic structs, `Nullable<T>`), valuetype `newobj`.
- Ignore CIL `nop`, `volatile`, and `unaligned`.
- `Run` is `object Run(int id, object[] args)` in the runtime and in stubs. Do not write `object?[]`.
- When virtualization is enabled, `StringEncryptionObfuscator` and `ConstantEncryptionObfuscator` skip the `VmEncoder.Select` set so `ldstr` / `ldc.*` still reach the encoder.
- `CanProxy` returns false for injected helpers / `Obfy.Runtime` types. Do not proxy `Vm.Run`.
- `Init` takes `Type[] returnTypes`. `Ret` boxes using that CLR type (`bool`/`uint` must not `unbox.any` a boxed `int`). Non-primitive args including `this` and `string` map to `VmType.O`.
- Virtualization stays off in Minimal, Standard, and Aggressive. `--virtualize` / UI toggle / `virtualization.enabled` remain the opt-in.
- Gate off NativeAOT / Unity IL2CPP / Blazor WASM. Host is CoreCLR only.
- Tests: xUnit + Shouldly + Moq. Do not switch to NSubstitute. Execute tests load a **fresh collectible `AssemblyLoadContext`** after writing the module — do not call `Vm.Init` on the copy of `Obfy.VmRuntime` loaded into the test process (static state races under parallel xUnit).
- Existing tests that look for type name `<Vm>` or method `Execute` are updated in Task 7 to `Vm` / `Run`.
- `SymbolRenaming_UnknownFeature_EmitsWarning` currently uses Feature `"virtualization"` as unknown. When Task 7 adds `ObfuscationFeature.Virtualization`, change that test's feature string to `"not-a-real-feature"`.
- Every task's `dotnet test` filter must stay green before commit.

## File map

**Create**

- `Src/Obfy.VmRuntime/Obfy.VmRuntime.csproj` — netstandard2.0 runtime
- `Src/Obfy.VmRuntime/Vm.cs` — `Run` / `Init` / frames / handlers
- `Src/Obfy.Core/Virtualization/VmIsa.cs` — internal opcode enum (stable names, not on-disk bytes)
- `Src/Obfy.Core/Virtualization/VmSkipReasons.cs` — skip-reason strings
- `Src/Obfy.Core/Virtualization/VmMemberTables.cs` — interned method/field/type tokens
- `Src/Obfy.Core/Virtualization/VmEncoder.cs` — eligibility + two-pass encode
- `Src/Obfy.Core/Virtualization/VmImporter.cs` — embed load, copy type, `.cctor`, stub
- `Src/Obfy.Core/Virtualization/VmSeed.cs` — cache-key seed, `opMap`, `xorKey`
- `Tests/Obfy.Tests/Virtualization/VmImporterTests.cs`
- `Tests/Obfy.Tests/Virtualization/VmEncoderTests.cs`
- `Tests/Obfy.Tests/Virtualization/VmExecuteHarness.cs`
- `Tests/Obfy.Tests/Virtualization/VmRuntimeExecuteTests.cs`
- `Tests/Obfy.Tests/Virtualization/VmSeedTests.cs`

**Modify**

- `Obfy.sln` — add VmRuntime under Src
- `Src/Obfy.Core/Obfy.Core.csproj` — embed VmRuntime (no runtime assembly reference)
- `Tests/Obfy.Tests/Obfy.Tests.csproj` — no extra reference if execute tests go through the embed; do **not** add a ProjectReference to VmRuntime
- `Src/Obfy.Core/Obfuscators/Assembly/VirtualizationObfuscator.cs` — orchestration only (Task 7)
- `Src/Obfy.Core/Utilities/RuntimeProfileGating.cs` — `AllowsManagedVm` + disable + warning
- `Src/Obfy.Core/Utilities/ObfuscationAttributeRules.cs` — `ObfuscationFeature.Virtualization`
- `Src/Obfy.Core/Obfuscators/Assembly/StringEncryptionObfuscator.cs` — skip `VmEncoder.Select` set when virtualization is enabled (Task 7)
- `Src/Obfy.Core/Obfuscators/Assembly/ConstantEncryptionObfuscator.cs` — same skip (Task 7)
- `Src/Obfy.Core/Obfuscators/Assembly/ReferenceProxyObfuscator.cs` — `CanProxy` skips injected helpers (Task 7)
- `Src/Obfy.Core/Models/ObfySettings.cs` — `VirtualizationSettings` XML comment (Task 8)
- `Tests/Obfy.Tests/VirtualizationObfuscatorTests.cs` — rewrite for instance methods, unsigned compares, `Run`
- `Tests/Obfy.Tests/EndToEndObfuscationTests.cs` — `<Vm>`/`Execute` → `Vm`/`Run`; add instance/string/pipeline cases
- `Tests/Obfy.Tests/RuntimeInjectionTests.cs` — type `Vm`, method `Run`, flatten on (or documented false fallback)
- `Tests/Obfy.Tests/RuntimeProfileGatingTests.cs` — NativeAOT clears virtualization
- `Tests/Obfy.Tests/AssemblyObfuscatorTests.cs` — unknown-feature string
- `Src/Obfy.Console/Program.cs` — `--virtualize` description
- `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml` — toggle copy
- `Src/Obfy.UI/Help/HelpCatalog.cs` — “Virtualize methods”
- `Tests/Obfy.UI.Tests/Help/HelpCatalogTests.cs` — note text
- Docs: Techniques, Configuration, CLI, Competitive-Analysis, Roadmap, CHANGELOG, README, CLAUDE.md, `schemas/obfy.schema.json`, Testing-Roadmap TR-50

Do not modify `RuntimeInjection.cs` unless a new named options constant is required. Inline `new RuntimeHelperOptions { ... }` at the import site.

---

### Task 1: Embed VmRuntime and import it into a target module

**Files:**
- Create: `Src/Obfy.VmRuntime/Obfy.VmRuntime.csproj`
- Create: `Src/Obfy.VmRuntime/Vm.cs`
- Create: `Src/Obfy.Core/Virtualization/VmImporter.cs`
- Modify: `Src/Obfy.Core/Obfy.Core.csproj`
- Modify: `Obfy.sln`
- Test: `Tests/Obfy.Tests/Virtualization/VmImporterTests.cs`

**Interfaces:**
- Consumes: `PipelineContext`, `RuntimeInjection.Register`, `RuntimeHelperOptions`
- Produces:
  - `public static class Obfy.Runtime.Vm` with `public static object Run(int id, object[] args)` and `internal static void Init(byte[] code, int[] starts, byte[] opMap, byte[] xorKey, MethodBase[] methods, FieldInfo[] fields, Type[] types, Type[] returnTypes)`
  - `public static class VmImporter` with `public const string EmbeddedName = "Obfy.VmRuntime.dll"`
  - `public static TypeDef VmImporter.Import(PipelineContext context, byte[] code, int[] starts, byte[] opMap, byte[] xorKey, IReadOnlyList<IMethod> methods, IReadOnlyList<IField> fields, IReadOnlyList<ITypeDefOrRef> types, IReadOnlyList<ITypeDefOrRef> returnTypes)`
  - Imported type is registered with flatten on, rename on, encrypt-IL off
  - Target module has no `AssemblyRef` named `Obfy.VmRuntime`

- [ ] **Step 1: Write the failing importer test**

Create `Tests/Obfy.Tests/Virtualization/VmImporterTests.cs`:

```csharp
using dnlib.DotNet;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Virtualization;
using Shouldly;

namespace Obfy.Tests.Virtualization;

public class VmImporterTests
{
    [Fact]
    public void Import_CopiesVmType_WithoutVmRuntimeAssemblyRef()
    {
        var module = new ModuleDefUser("T", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        var asm = new AssemblyDefUser("T", new Version(1, 0, 0, 0));
        asm.Modules.Add(module);
        var context = PipelineContext.ForAssembly(module, new ObfySettings());
        var identity = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        var type = VmImporter.Import(
            context,
            code: Array.Empty<byte>(),
            starts: Array.Empty<int>(),
            opMap: identity,
            xorKey: new byte[8],
            methods: Array.Empty<IMethod>(),
            fields: Array.Empty<IField>(),
            types: Array.Empty<ITypeDefOrRef>(),
            returnTypes: Array.Empty<ITypeDefOrRef>());

        type.FullName.ShouldBe("Obfy.Runtime.Vm");
        type.FindMethod("Run").ShouldNotBeNull();
        type.FindMethod("Init").ShouldNotBeNull();
        type.FindStaticConstructor().ShouldNotBeNull();
        module.GetAssemblyRefs().ShouldNotContain(r => r.Name == "Obfy.VmRuntime");
        RuntimeInjection.ShouldRename(context, type).ShouldBeTrue();
        RuntimeInjection.ShouldEncryptIl(context, type).ShouldBeFalse();
        RuntimeInjection.ShouldFlattenControlFlow(context, type).ShouldBeTrue();
    }

    [Fact]
    public void EmbeddedResource_IsPresentOnObfyCore()
    {
        typeof(Obfy.Core.Pipeline.PipelineContext).Assembly
            .GetManifestResourceNames()
            .ShouldContain(VmImporter.EmbeddedName);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmImporterTests`

Expected: FAIL (project / type `VmImporter` not found)

- [ ] **Step 3: Add the runtime project, embed it, implement a throwing `Vm` and a copying importer**

`Src/Obfy.VmRuntime/Obfy.VmRuntime.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>Obfy.Runtime</RootNamespace>
    <AssemblyName>Obfy.VmRuntime</AssemblyName>
  </PropertyGroup>
</Project>
```

Do not add `InternalsVisibleTo`. Tests never call `Init` in-process.

`Src/Obfy.VmRuntime/Vm.cs`:

```csharp
using System;
using System.Reflection;

namespace Obfy.Runtime;

public static class Vm
{
    static byte[] _code = Array.Empty<byte>();
    static int[] _starts = Array.Empty<int>();
    static byte[] _opMap = Array.Empty<byte>();
    static byte[] _xorKey = Array.Empty<byte>();
    static MethodBase[] _methods = Array.Empty<MethodBase>();
    static FieldInfo[] _fields = Array.Empty<FieldInfo>();
    static Type[] _types = Array.Empty<Type>();
    static Type[] _returnTypes = Array.Empty<Type>();

    public static object Run(int id, object[] args)
    {
        throw new NotSupportedException("Obfy VM: interpreter not implemented");
    }

    internal static void Init(
        byte[] code,
        int[] starts,
        byte[] opMap,
        byte[] xorKey,
        MethodBase[] methods,
        FieldInfo[] fields,
        Type[] types,
        Type[] returnTypes)
    {
        _code = code ?? throw new ArgumentNullException(nameof(code));
        _starts = starts ?? throw new ArgumentNullException(nameof(starts));
        _opMap = opMap ?? throw new ArgumentNullException(nameof(opMap));
        _xorKey = xorKey ?? throw new ArgumentNullException(nameof(xorKey));
        _methods = methods ?? throw new ArgumentNullException(nameof(methods));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _returnTypes = returnTypes ?? throw new ArgumentNullException(nameof(returnTypes));
    }
}
```

Initialize static fields (`Array.Empty<T>()`) so CS8618 does not fail `Obfy.VmRuntime` under `Directory.Build.props` `TreatWarningsAsErrors`. Use `object[]` on `Run` in the runtime and in stubs. Keep the `Init` null-checks.

Add to `Src/Obfy.Core/Obfy.Core.csproj` (keep existing `PackageReference`s):

```xml
<ItemGroup>
  <ProjectReference Include="..\Obfy.VmRuntime\Obfy.VmRuntime.csproj">
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
    <OutputItemType>EmbeddedResource</OutputItemType>
    <LogicalName>Obfy.VmRuntime.dll</LogicalName>
  </ProjectReference>
</ItemGroup>
```

Add the project to the solution:

```bash
dotnet sln Obfy.sln add Src/Obfy.VmRuntime/Obfy.VmRuntime.csproj --solution-folder Src
```

`Src/Obfy.Core/Virtualization/VmImporter.cs` — load the embed, **move** `Obfy.Runtime.Vm` onto the target (do not `Importer.Import` as a TypeRef), retarget corlib TypeRefs whose resolution scope is `netstandard` / `System.Runtime` / `mscorlib` / `System.Private.CoreLib` to `dest.CorLibTypes.AssemblyRef`, delete any existing `.cctor`, emit a new `.cctor` that:

1. Builds `byte[]` code via `ldc.i4 length` + `newarr byte` + `RuntimeHelpers.InitializeArray` on an RVA field (empty array is `ldc.i4.0` + `newarr` with no RVA)
2. Builds `int[]` starts the same way (or a loop of `stelem.i4` when short)
3. Builds `byte[]` opMap and xorKey the same way
4. Builds `MethodBase[]` / `FieldInfo[]` / `Type[]` / `Type[] returnTypes` of the given lengths; for each entry `ldtoken` + `GetMethodFromHandle` / `GetFieldFromHandle` / `GetTypeFromHandle`
5. `call` `Vm.Init`

Then `RuntimeInjection.Register(context, vmType, new RuntimeHelperOptions { FlattenControlFlow = true, Rename = true, EncryptIl = false })`. Do not call `AddType` (the type is already on the module).

If the embed is missing, throw `InvalidOperationException` with message starting `Virtualization failed:`.

Walk nested types when retargeting. After the move, `module.GetAssemblyRefs()` must not contain `Obfy.VmRuntime`. `Vm.cs` must not reference `Stack<T>` or any type outside corlib — implement the frame stack in Task 3 as `Frame[]` + depth.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmImporterTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.VmRuntime Src/Obfy.Core/Obfy.Core.csproj Src/Obfy.Core/Virtualization/VmImporter.cs Tests/Obfy.Tests/Virtualization/VmImporterTests.cs Obfy.sln
git commit -m "feat: embed Obfy.VmRuntime and import Vm into the target module"
```

---

### Task 2: ISA enum, skip reasons, and encoder

**Files:**
- Create: `Src/Obfy.Core/Virtualization/VmIsa.cs`
- Create: `Src/Obfy.Core/Virtualization/VmSkipReasons.cs`
- Create: `Src/Obfy.Core/Virtualization/VmMemberTables.cs`
- Create: `Src/Obfy.Core/Virtualization/VmEncoder.cs`
- Test: `Tests/Obfy.Tests/Virtualization/VmEncoderTests.cs`

**Interfaces:**
- Consumes: dnlib `MethodDef` / `Instruction`
- Produces:
  - `public enum VmOp : byte` with the values below (these are **internal** opcodes, not on-disk bytes)
  - `public static int VmIsa.EncodedSize(byte[] blob, int offset)` — byte length of the instruction at `offset`. Used by `VmSeed.Apply` to find opcode positions. Sizes: 1-byte ops = 1; `Ldarg`/`Starg`/`Ldloc`/`Stloc` = 2; `LdcI4`/`LdcR4` = 5; `LdcI8`/`LdcR8` = 9; `Ldstr` = 3 + utf8 length (read the `u16` at `offset+1`); branches / `Newobj`/`Call`/`Callvirt`/fields/`Box`/`UnboxAny`/`Castclass`/`Isinst`/`Newarr` = 3; `CallVm` = 4 (`opcode + u16 id + u8 argc`)
  - `public static class VmSkipReasons` with the const strings below
  - `public sealed class VmMemberTables` with `List<IMethod> Methods`, `List<IField> Fields`, `List<ITypeDefOrRef> Types`, and `ushort AddMethod(IMethod)`, `ushort AddField(IField)`, `ushort AddType(ITypeDefOrRef)` (intern by token/full name)
  - `public static bool VmEncoder.TryEncode(MethodDef method, IReadOnlyDictionary<MethodDef, int> virtualizedIds, VmMemberTables tables, out byte[] code, out string? skipReason)`
  - `public static IReadOnlyList<MethodDef> VmEncoder.Select(IEnumerable<MethodDef> candidates, int maxMethods)` — deterministic order `DeclaringType.FullName` then `Method.FullName`, first N that `TryEncode` with an empty id map (pass 1). Pass 2 is Task 7.

**`VmOp` values (lock these numbers):**

```csharp
namespace Obfy.Core.Virtualization;

public enum VmOp : byte
{
    LdcI4 = 1, LdcI8 = 2, LdcR4 = 3, LdcR8 = 4, Ldnull = 5, Ldstr = 6,
    Ldarg = 7, Starg = 8, Ldloc = 9, Stloc = 10, Dup = 11, Pop = 12,
    Add = 13, Sub = 14, Mul = 15, Div = 16, Rem = 17, DivUn = 18, RemUn = 19,
    And = 20, Or = 21, Xor = 22, Not = 23, Neg = 24, Shl = 25, Shr = 26, ShrUn = 27,
    ConvI4 = 28, ConvI8 = 29, ConvR4 = 30, ConvR8 = 31, ConvU4 = 32, ConvU8 = 33,
    Ceq = 34, Cgt = 35, CgtUn = 36, Clt = 37, CltUn = 38,
    Br = 39, Brtrue = 40, Brfalse = 41, Beq = 42, Bne = 43,
    Blt = 44, Ble = 45, Bgt = 46, Bge = 47,
    BltUn = 48, BleUn = 49, BgtUn = 50, BgeUn = 51,
    Newobj = 52, Call = 53, Callvirt = 54, CallVm = 55,
    Ldfld = 56, Stfld = 57, Ldsfld = 58, Stsfld = 59,
    Box = 60, UnboxAny = 61, Castclass = 62, Isinst = 63,
    Newarr = 64, Ldlen = 65,
    LdelemI4 = 66, LdelemI8 = 67, LdelemR4 = 68, LdelemR8 = 69, LdelemRef = 70,
    StelemI4 = 71, StelemI8 = 72, StelemR4 = 73, StelemR8 = 74, StelemRef = 75,
    Throw = 76, Ret = 77
}
```

**Skip-reason constants:**

```csharp
namespace Obfy.Core.Virtualization;

public static class VmSkipReasons
{
    public const string NoBody = "no body";
    public const string Constructor = "constructor";
    public const string Generic = "generic";
    public const string ExceptionHandlers = "exception handlers";
    public const string ByRef = "byref";
    public const string NonPrimitiveValuetype = "non-primitive valuetype";
    public const string UnsupportedOpcode = "unsupported opcode";
    public const string ValuetypeNewobj = "valuetype newobj";
    public const string Switch = "switch";
    public const string StackHeightMismatch = "stack height mismatch";
    public const string IndexOutOfRange = "index out of range";
    public const string InvalidBytecode = "invalid bytecode";
}
```

- [ ] **Step 1: Write the failing encoder tests**

Create `Tests/Obfy.Tests/Virtualization/VmEncoderTests.cs`. Reuse the same module-construction pattern as `VirtualizationObfuscatorTests` (mscorlib CLR40 `ModuleDefUser`). Include at least:

```csharp
[Fact]
public void TryEncode_SimpleAdd_WritesLdargAddRet()
{
    var (method, tables) = Encode(body =>
    {
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Add));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    });
    method.ShouldNotBeNull();
    // ldarg 0, ldarg 1, add, ret — LdcI4 is covered by the first-opcode table below
    method!.Code[0].ShouldBe((byte)VmOp.Ldarg);
    method.Code[1].ShouldBe((byte)0);
    method.Code[2].ShouldBe((byte)VmOp.Ldarg);
    method.Code[3].ShouldBe((byte)1);
    method.Code[4].ShouldBe((byte)VmOp.Add);
    method.Code[5].ShouldBe((byte)VmOp.Ret);
}

[Fact]
public void TryEncode_UnsignedCompare_Succeeds()
{
    Encode(body =>
    {
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        body.Instructions.Add(Instruction.Create(OpCodes.Cgt_Un));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }).Code[4].ShouldBe((byte)VmOp.CgtUn);
}

[Fact]
public void TryEncode_InstanceMethod_Succeeds()
{
    // MethodSig.CreateInstance(int32), ldc.i4.7; ret
    skipReason.ShouldBeNull();
}

[Fact]
public void TryEncode_Ldstr_WritesUtf8Payload()
{
    // code contains VmOp.Ldstr, u16 length, UTF-8 bytes of "hi"
}

[Fact]
public void TryEncode_CallToVirtualizedStatic_WritesCallVm()
{
    // callee is static int Inner(int x); ids[callee] = 7
    // CallVm encoding is locked: opcode, u16 id (little-endian 7), u8 argc (1)
    code[0].ShouldBe((byte)VmOp.CallVm);
    BitConverter.ToUInt16(code, 1).ShouldBe((ushort)7);
    code[3].ShouldBe((byte)1);
}

[Fact]
public void TryEncode_Callvirt_WritesCallNotCallVm()
{
    // even if the MethodDef is in virtualizedIds
}

[Fact]
public void TryEncode_Switch_Skips() { /* Details == VmSkipReasons.Switch */ }
[Fact]
public void TryEncode_Eh_Skips() { /* ExceptionHandlers */ }
[Fact]
public void TryEncode_Generic_Skips() { /* Generic */ }
[Fact]
public void TryEncode_Ctor_Skips() { /* Constructor */ }
[Fact]
public void TryEncode_ByRefParam_Skips() { /* ByRef */ }
[Fact]
public void TryEncode_CustomStructLocal_Skips() { /* NonPrimitiveValuetype */ }
[Fact]
public void TryEncode_CustomStructParam_Skips() { /* void Foo(Point p) → NonPrimitiveValuetype */ }
[Fact]
public void TryEncode_NullableReturn_Skips() { /* int? Bar(int x) → NonPrimitiveValuetype */ }
[Fact]
public void TryEncode_GenericCall_Skips() { /* call List<int>.Add → UnsupportedOpcode (MethodSpec) */ }
[Fact]
public void TryEncode_ValuetypeNewobj_Skips() { /* ValuetypeNewobj */ }
[Fact]
public void TryEncode_UnsupportedOpcode_Skips() { /* localloc → UnsupportedOpcode */ }
[Fact]
public void TryEncode_StackHeightMismatch_Skips() { /* StackHeightMismatch */ }
[Fact]
public void Select_IsDeterministicAndRespectsMaxMethods()
{
    // three eligible adds named C, A, B; maxMethods=2 → A then B (FullName order)
}
```

Add one fact per remaining first-opcode (same `Encode` helper, assert `code[0]`):

| CIL | `code[0]` |
|---|---|
| `ldc.i8 1; ret` | `LdcI8` |
| `ldc.r4 1f; ret` | `LdcR4` |
| `ldc.r8 1d; ret` | `LdcR8` |
| `newobj class C::.ctor(); pop; ret` | `Newobj` (and `tables.Methods` count 1) |
| `ldarg.0; ldfld int32 C::N; ret` | `Ldfld` |
| `ldarg.0; box int32; ret` | `Box` |
| `ldarg.0; unbox.any int32; ret` | `UnboxAny` |
| `ldarg.0; castclass C; ret` | `Castclass` |
| `ldarg.0; isinst C; ret` | `Isinst` |
| `ldc.i4.2; newarr int32; pop; ret` | `Newarr` |
| `ldarg.0; ldlen; conv.i4; ret` | `Ldlen` (`conv.i4` is encoded as `ConvI4`) |
| `ldarg.0; ldc.i4.0; ldelem.ref; ret` | `LdelemRef` |
| `newobj Exception::.ctor(); throw` | `Throw` (last opcode, allowed instead of `Ret`) |

Normalize `ldc.i4.s` / `ldc.i4.0`… / `ldarg_0`… / `ldloc.s` / `br.s` the same way `VirtualizationObfuscator.TryReadLdcI4` does today. Ignore `nop`, `volatile`, `unaligned`. A method whose body is `nop; ldarg.0; ldarg.1; add; ret` must encode the same as without `nop`.

Add `EncodedSize_LdstrAndCallVm_MatchesLockedWidths`: after encoding `ldstr "hi"` the instruction length is `3 + 2`; after encoding `CallVm` the instruction length is 4. `VmIsa.EncodedSize` must return those values so Task 6's `Apply` can walk the blob.

Little-endian i32/i64/u16. `Ldstr` format: opcode, `u16` length, UTF-8 bytes (no XOR here; Task 6 applies XOR to the concatenated blob).

Branch targets: `u16` offset from the start of **that method's** bytecode.

Stack-height tracker: maintain an integer height; at each instruction record height-before; at a join, if the recorded height disagrees, skip. `ret`/`throw` do not join.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmEncoderTests`

Expected: FAIL (`VmEncoder` not found)

- [ ] **Step 3: Implement encoder**

`TryEncode` algorithm:

1. If no body / abstract / PInvoke / native / runtime → `NoBody`
2. If `.ctor` or `.cctor` → `Constructor`
3. If generic method or generic declaring type → `Generic`
4. If `HasExceptionHandlers` → `ExceptionHandlers`
5. If any param, return, or local is byref / pointer / fnptr / typedref / `IsByRefLike` → `ByRef`
6. If any param, return, or local is a value type that is not the primitive family (`I1` `U1` `I2` `U2` `I4` `U4` `I8` `U8` `R4` `R8` `Boolean` `Char`) → `NonPrimitiveValuetype`. This includes custom structs, generic structs, and `Nullable<T>`. Class/array/string params, returns, and locals are allowed.
7. If any arg or local index > 255 → `IndexOutOfRange`
8. Walk instructions; encode or skip as above. Ignore `nop` / `volatile` / `unaligned`. `call` to a `MethodDef` present in `virtualizedIds` → `CallVm` + u16 id + u8 argc (`Method.Parameters.Count` including `this` for instance `call`). `callvirt` always `Callvirt` + `tables.AddMethod`. `newobj` of value type → `ValuetypeNewobj`. `switch` → `Switch`. Generic `MethodSpec` → `UnsupportedOpcode` (do not emit `Call` + `Invoke`).
9. If the last opcode is not `Ret` and not `Throw` → `InvalidBytecode`
10. On success `code` is the byte array; `skipReason` is null

`Select`: sort by `(DeclaringType.FullName, FullName)`, try encode with `virtualizedIds: empty` and a throwaway `VmMemberTables`, take until `maxMethods`.

Do not mutate the method body.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmEncoderTests|FullyQualifiedName~VmImporterTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Virtualization Tests/Obfy.Tests/Virtualization/VmEncoderTests.cs
git commit -m "feat: encode eligible CIL into the Obfy VM ISA"
```

---

### Task 3: Interpreter — stack, arith, branches, ldstr

**Files:**
- Modify: `Src/Obfy.VmRuntime/Vm.cs`
- Create: `Tests/Obfy.Tests/Virtualization/VmExecuteHarness.cs`
- Test: `Tests/Obfy.Tests/Virtualization/VmRuntimeExecuteTests.cs`

**Interfaces:**
- Consumes: `VmImporter.Import`, `VmEncoder.TryEncode`, `VmOp`
- Produces:
  - `Vm.Run` executes bytecode for arith / conv / compare / branch / ldstr / ldarg / ldloc / stloc / dup / pop / ldc* / ldnull / ret
  - Nested `enum VmType : byte { I4, I8, R4, R8, O }`
  - Nested `struct VmValue { public VmType Type; public long Bits; public object Ref; }`
  - Nested `struct Frame { public int MethodId; public int Ip; public VmValue[] Args; public VmValue[] Locals; public VmValue[] Stack; public int Sp; }`
  - Fetch: `byte op = opMap[code[ip] ^ xorKey[ip % xorKey.Length]]` then `ip++`
  - Faults throw `InvalidOperationException` whose message starts with `"Obfy VM: "`
  - `VmExecuteHarness.Invoke(ModuleDef module, string typeName, string methodName, params object[] args)` writes the module to a temp DLL, loads a collectible ALC, invokes, unloads

This task uses an **identity** `opMap` (`opMap[i] = (byte)i`) and an 8-byte zero `xorKey`. Object-model opcodes may still throw `"Obfy VM: invalid opcode"` if hit.

- [ ] **Step 1: Write failing execute tests**

`Tests/Obfy.Tests/Virtualization/VmRuntimeExecuteTests.cs`:

```csharp
[Fact]
public void Run_Add_ReturnsSum()
{
    var result = EncodeImportInvoke(
        "public static class Lib { public static int Add(int a, int b) => a + b; }",
        "Lib", "Add", 2, 3);
    result.ShouldBe(5);
}

[Fact]
public void Run_Ldstr_ReturnsString()
{
    EncodeImportInvoke(
        "public static class Lib { public static string Hi() => \"hi\"; }",
        "Lib", "Hi").ShouldBe("hi");
}

[Fact]
public void Run_SignedAndUnsignedBranches()
{
    EncodeImportInvoke("public static class Lib { public static int Pick(int a, int b) => a < b ? 1 : 0; }",
        "Lib", "Pick", 1, 2).ShouldBe(1);
}

[Fact]
public void Run_BoolReturn_DoesNotThrow()
{
    EncodeImportInvoke("public static class Lib { public static bool Gt(int a, int b) => a > b; }",
        "Lib", "Gt", 3, 1).ShouldBe(true);
}

[Fact]
public void Run_UintReturn_DoesNotThrow()
{
    EncodeImportInvoke("public static class Lib { public static uint Id(uint x) => x; }",
        "Lib", "Id", 7u).ShouldBe(7u);
}

[Fact]
public void Run_InstanceThis_GetN()
{
    const string src = """
        public class Box {
            public int N;
            public int GetN() => N;
        }
        """;
    // compile, encode GetN, construct Box via ALC, set N=4, invoke GetN through the stub
    EncodeImportInvokeInstance(src, "Box", "GetN", instanceN: 4).ShouldBe(4);
}
```

Also: `long` add, `double` mul, `if (x == 0)` (`brtrue`/`brfalse`), `ldloc`/`stloc` temp. `Run_InstanceThis_GetN` may wait until Task 4 if the harness cannot construct instances yet — if so, write it in Task 3 as a skipped/failing fact and implement the `O` unbox path now so Task 4 only adds `newobj`/`ldfld`. Prefer implementing it here: compile, encode only `GetN`, `Activator.CreateInstance` + set `N`, invoke the stub.

Harness (compile C# with the same `TRUSTED_PLATFORM_ASSEMBLIES` pattern as `EndToEndObfuscationTests.CompileToAssembly`, load with `ModuleDefMD.Load`, encode the named method, `VmImporter.Import` with identity map and `returnTypes` from the method’s `MethodSig.RetType`, replace that method with a stub that calls `Run(0, args)`, write, ALC invoke).

Stub shape (same as spec):

```
ldc.i4 id
ldc.i4 argc
newarr object
; for each arg i: dup; ldc.i4 i; ldarg i; box if valuetype; stelem.ref
call Vm.Run
unbox.any RetType  ; or pop+ret for void; or castclass for refs
ret
```

Put stub writing on `VmImporter` as `public static void WriteStub(MethodDef method, MethodDef run, int id)` so Task 7 reuses it. Instance methods: `this` at `args[0]` unboxed.

For this task, compile **static** methods only so you can ignore `this`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmRuntimeExecuteTests`

Expected: FAIL (`NotSupportedException` / `Obfy VM: interpreter not implemented`)

- [ ] **Step 3: Implement the interpreter loop for this subset**

Replace `Run`'s throw with a `Frame[]` + integer depth loop (spec). Do **not** use `Stack<Frame>`. `Init` already stored fields including `_returnTypes`. On entry, push a frame: `Ip = _starts[id]`, `Args` from boxed `args`, `Locals = new VmValue[256]`, `Stack = new VmValue[64]`, `Sp = 0`.

Arg unbox: boxed primitives (`bool`, `char`, sbyte/byte/short/ushort/int/uint, long/ulong, float, double) go to `Bits` with the matching `VmType` (`bool`/`char`/small integers → `I4`, `uint` → `I4` bits, `ulong` → `I8` bits). **Everything else** — `this`, `string`, class references, arrays, `null` — is `VmType.O`. There is no “unbox i4 only” path; a missing `O` path throws or treats `this` as an int.

Dispatch `switch ((VmOp)op)` for opcodes 1–51 and 6 (`Ldstr`) and 77 (`Ret`). `Ret` pops a frame; if none remain, box the top of stack using `_returnTypes[id]`: `bool` → boxed `bool`, `char` → boxed `char`, `uint` → boxed `uint`, `ulong` → boxed `ulong`, `int` → boxed `int`, `I8`/`R4`/`R8` similarly, `O` → `Ref`. Do **not** always box `I4` as `int` — `unbox.any bool` on a boxed `int` throws `InvalidCastException`. Void (`typeof(void)` or empty return): return `null`.

Helpers: `Push`, `Pop` (underflow → `"Obfy VM: stack underflow"`), `ReadI32`/`ReadI64`/`ReadU16`/`ReadU8` from `_code` at `Ip` (XOR each byte with `xorKey[ip % len]` **before** using it as payload — payload bytes are also stored XOR'd once Task 6 encrypts the blob; with a zero key this is a no-op). Invalid opcode / `opMap` 0xFF → `"Obfy VM: invalid opcode"`. Branch target `start + u16` outside `[start, end)` → `"Obfy VM: branch out of range"`. `end` is next start or `_code.Length`.

Binops pop right then left. `CgtUn`/`CltUn` use unsigned compares on `Bits`.

Do not implement Call/Newobj/fields/arrays yet — leave those cases throwing invalid opcode.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmRuntimeExecuteTests|FullyQualifiedName~VmEncoderTests|FullyQualifiedName~VmImporterTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.VmRuntime/Vm.cs Src/Obfy.Core/Virtualization/VmImporter.cs Tests/Obfy.Tests/Virtualization
git commit -m "feat: execute VM arith, branches, and encoded strings"
```

---

### Task 4: Object model — calls, fields, newobj, token tables

**Files:**
- Modify: `Src/Obfy.VmRuntime/Vm.cs`
- Modify: `Src/Obfy.Core/Virtualization/VmImporter.cs` (`.cctor` already emits token tables from Task 1 — verify non-empty tables round-trip)
- Test: `Tests/Obfy.Tests/Virtualization/VmRuntimeExecuteTests.cs` (add facts)

**Interfaces:**
- Consumes: `VmOp.Call`, `Callvirt`, `Newobj`, `Ldfld`, `Stfld`, `Ldsfld`, `Stsfld`; `MethodBase[]` / `FieldInfo[]` from `Init`
- Produces: handlers that `Invoke` / get/set fields; `TargetInvocationException` unwraps to `InnerException` (or the original if inner is null)

- [ ] **Step 1: Write failing execute tests**

```csharp
[Fact]
public void Run_NewobjAndInstanceField()
{
    const string src = """
        public class Box {
            public int N;
            public static int Go(int n) { var b = new Box(); b.N = n; return b.N; }
        }
        """;
    EncodeImportInvoke(src, "Box", "Go", 9).ShouldBe(9);
}

[Fact]
public void Run_Callvirt_UsesVirtualDispatch()
{
    const string src = """
        public class A { public virtual int V() => 1; }
        public class B : A { public override int V() => 2; }
        public static class Lib { public static int Hit(A a) => a.V(); }
        """;
    // encode Lib.Hit only (A.V / B.V stay real methods)
    EncodeImportInvoke(src, "Lib", "Hit", /* instance of B */ ...).ShouldBe(2);
}
```

For the dispatch test, compile, encode only `Lib.Hit`, construct `B` in the test via ALC (`Activator.CreateInstance`) and pass it in. `Hit` is `callvirt` → `Invoke`, not `CallVm`.

Also: static field get/set; `call` to a **non-virtualized** helper (`string.Concat` or a second method left as CIL).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmRuntimeExecuteTests.Run_NewobjAndInstanceField|FullyQualifiedName~VmRuntimeExecuteTests.Run_Callvirt_UsesVirtualDispatch`

Expected: FAIL (`Obfy VM: invalid opcode`)

- [ ] **Step 3: Implement Call / Callvirt / Newobj / field handlers**

Pop `argc` args (instance methods: receiver is the last popped after args, i.e. push order is receiver then args — match CIL evaluation stack). `argc` from `MethodBase.GetParameters().Length` plus 1 if instance / ctor.

`MethodBase.Invoke(target, args)`. Constructors: `Invoke(null, args)` on the `ConstructorInfo`.

Field get: pop receiver (null for static), `FieldInfo.GetValue`, push. Set: pop value then receiver, `SetValue`.

Convert `VmValue` ↔ `object` with the field/parameter type (`Convert.ChangeType` only for primitives; refs as-is).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmRuntimeExecuteTests|FullyQualifiedName~VmEncoderTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.VmRuntime/Vm.cs Tests/Obfy.Tests/Virtualization/VmRuntimeExecuteTests.cs
git commit -m "feat: VM calls, fields, and newobj via token tables"
```

---

### Task 5: CallVm frames, arrays, box/cast, throw

**Files:**
- Modify: `Src/Obfy.VmRuntime/Vm.cs`
- Test: `Tests/Obfy.Tests/Virtualization/VmRuntimeExecuteTests.cs`

**Interfaces:**
- Consumes: `VmOp.CallVm`, `Box`, `UnboxAny`, `Castclass`, `Isinst`, `Newarr`, `Ldlen`, `Ldelem*`, `Stelem*`, `Throw`
- Produces: `CallVm` pushes a new `Frame` with `MethodId` / `Ip = _starts[id]` / copied args; does **not** `Invoke` the stub. `Throw` throws `Pop().Ref` as `Exception` (type mismatch → `"Obfy VM: "`).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Run_CallVm_StaticACallsStaticB()
{
    const string src = """
        public static class Lib {
            public static int Inner(int x) => x + 1;
            public static int Outer(int x) => Inner(x) * 2;
        }
        """;
    // encode BOTH methods; Outer's call must be CallVm
    EncodeImportInvokeBoth(src, "Lib", "Outer", 3).ShouldBe(8);
}

[Fact]
public void Run_CallVm_DoesNotEnterStubForCallee()
{
    // After encode+stub both methods, callee stub body is never the path Inner uses
    // when Outer is invoked. Assert Outer(3)==8 is enough if Inner's stub would
    // recurse infinitely without CallVm.
}

[Fact]
public void Run_ArrayBoxCastThrow()
{
    const string src = """
        public static class Lib {
            public static int Go() {
                var a = new int[2];
                a[1] = 4;
                object o = 5;
                int n = (int)o;
                return a[1] + n;
            }
        }
        """;
    EncodeImportInvoke(src, "Lib", "Go").ShouldBe(9);
}

[Fact]
public void Run_Throw_Propagates()
{
    const string src = "public static class Lib { public static int Boom() { throw new System.InvalidOperationException(\"x\"); } }";
    Should.Throw<TargetInvocationException>(() => EncodeImportInvoke(src, "Lib", "Boom"))
        .InnerException.ShouldBeOfType<InvalidOperationException>();
}
```

Harness: when encoding multiple methods, pass 1 `Select` then pass 2 `TryEncode` with the id map, concatenate blobs, `starts[i] = offset`, one `Import`, `WriteStub` each, invoke Outer.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmRuntimeExecuteTests.Run_CallVm`

Expected: FAIL (Call treated as Invoke of a stub that still has original IL **or** invalid opcode). After stubs replace both bodies, without `CallVm` `Outer` would `Invoke` `Inner`'s stub which calls `Run` — that can still work. **Assert the encoded bytes of Outer contain `CallVm`**, and that a recursive `Run` through stubs is not required: temporarily make `WriteStub` for Inner throw if invoked (`ldstr` + `newobj InvalidOperationException` + `throw`) so only `CallVm` passes.

- [ ] **Step 3: Implement remaining handlers**

`CallVm`: pop argc (from the callee's `starts` method — store `int[] _argCounts` in `Init` **or** infer from the encoded method's first frames). Simpler: encode `CallVm` as `opcode + u16 id + u8 argc` in Task 2 if not already; if Task 2 only wrote `u16 id`, add `u8 argc` now and update the encoder test `TryEncode_CallToVirtualizedStatic_WritesCallVm`.

`CallVm` encoding is already opcode + u16 id + u8 argc (Task 2). Push a frame with that argc; do not return to the dispatcher via C# recursion.

`Newarr`: pop length, `Array.CreateInstance(_types[idx], len)`. `Ldlen` / `ldelem` / `stelem` use `Array`. `Box`/`UnboxAny` use `_types[idx]`. `Castclass` throw `InvalidCastException` on failure; `Isinst` push null.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~Vm`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.VmRuntime/Vm.cs Src/Obfy.Core/Virtualization/VmEncoder.cs Tests/Obfy.Tests/Virtualization
git commit -m "feat: VM frames for virtualized calls plus arrays and throw"
```

---

### Task 6: Per-build opcode map, XOR, profile gating

**Files:**
- Create: `Src/Obfy.Core/Virtualization/VmSeed.cs`
- Modify: `Src/Obfy.Core/Utilities/RuntimeProfileGating.cs`
- Modify: `Tests/Obfy.Tests/RuntimeProfileGatingTests.cs`
- Test: `Tests/Obfy.Tests/Virtualization/VmSeedTests.cs`
- Modify: `Tests/Obfy.Tests/Virtualization/VmRuntimeExecuteTests.cs` (XOR'd blob still runs)

**Interfaces:**
- Consumes: `IncrementalCache.ComputeKey`, `PipelineContext.InputPath`, `ObfySettings`
- Produces:
  - `public static byte[] VmSeed.Compute(PipelineContext context)` — 32 bytes
  - `public static byte[] VmSeed.CreateOpMap(byte[] seed)` — length 256
  - `public static byte[] VmSeed.CreateXorKey(byte[] seed)` — length 8
  - `public static void VmSeed.Apply(byte[] blob, byte[] opMap, byte[] xorKey)` — permute opcode bytes, then XOR the whole blob. Lock this:
    - Walk with `VmIsa.EncodedSize(blob, i)` (variable-length: `Ldstr` is `3 + utf8Length`, `CallVm` is 4, branches are 3, `LdcI8` is 9). There is no other parser.
    - At each opcode position, replace the byte with the inverse map (`encoded` such that `opMap[encoded] == internalOp`). Operand bytes stay as-is.
    - Then XOR **every** byte: `blob[i] ^= xorKey[i % 8]`
  - Encoder still emits internal opcodes. `Apply` runs on the concatenated blob before `Import`. A parser that only handles 1-byte ops will pass `Add` and fail `ldstr`/branches at runtime.
  - `RuntimeProfileGating.AllowsManagedVm(RuntimeProfile profile)` is true only for `RuntimeProfile.Default`
  - `Apply` sets `settings.Virtualization.Enabled = false` and warns `$"Virtualization disabled for {label}: the managed VM uses MethodBase.Invoke and is not supported on this runtime."` when virtualization was on and `!AllowsManagedVm`

**Seed material:**

- If `context.InputPath` is a readable file: `seed = Convert.FromHexString(IncrementalCache.ComputeKey(context.InputPath, context.Settings))`. ComputeKey already returns hex SHA-256. **Do not** hash those 32 bytes again.
- Else: SHA-256 of the same payload `IncrementalCache.ComputeKey` would hash: UTF-8 `"obfy-cache|" + version + "|"` + `module.ToArray()` via MemoryStream Write + UTF-8(settings JSON with `IncrementalCache`'s `JsonOptions`). Do not invent a different `"obfy-vm|"` prefix.

**OpMap:** HMAC-SHA256(seed, `"opmap" || counter`) as a byte stream; Fisher-Yates shuffle a list of `1..77`; allocate `map[256]` filled with `0xFF`; for `i in 0..76` `map[shuffled[i]] = (byte)(i+1)`. Inverse: `inv[internal] = shuffled[internal-1]`.

**XorKey:** first 8 bytes of HMAC-SHA256(seed, `"xor"`).

Do not use `System.Random`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void CreateOpMap_IsPermutationOfVmOps()
{
    var map = VmSeed.CreateOpMap(new byte[32]);
    map.Length.ShouldBe(256);
    var used = map.Where(b => b != 0xFF).OrderBy(b => b).ToArray();
    used.ShouldBe(Enumerable.Range(1, 77).Select(i => (byte)i).ToArray());
}

[Fact]
public void Compute_SameInputAndSettings_SameSeed()
{
    // two contexts, same temp file bytes + settings → SequenceEqual
}

[Fact]
public void Compute_DifferentInput_DifferentOpMap()
{
    // file "a" vs file "b"
}

[Fact]
public void Apply_ThenRun_StillAdds()
{
    // Encode add, Apply with non-identity map, Import, invoke Add(2,3)==5
}

[Fact]
public void Apply_ThenRun_StillReturnsHi()
{
    // Encode `public static string Hi() => "hi";`, Apply, invoke == "hi"
    // Ldstr is variable-length; a 1-byte-op parser will corrupt the blob
}

[Fact]
public void Apply_ThenRun_StillBranches()
{
    // Encode Pick(a,b) => a < b ? 1 : 0, Apply, Pick(1,2)==1
}

[Theory]
[InlineData(RuntimeProfile.NativeAot, "NativeAOT")]
[InlineData(RuntimeProfile.UnityIl2Cpp, "Unity IL2CPP")]
[InlineData(RuntimeProfile.BlazorWasm, "Blazor WebAssembly")]
public void Apply_DisablesVirtualizationOnRestrictedProfiles(RuntimeProfile profile, string label)
{
    var settings = new ObfySettings { RuntimeProfile = profile, Virtualization = { Enabled = true } };
    var context = PipelineContext.ForAssembly(new ModuleDefUser("t"), settings);
    RuntimeProfileGating.Apply(settings, context);
    settings.Virtualization.Enabled.ShouldBeFalse();
    context.Warnings.ShouldContain(w => w.Contains("Virtualization disabled") && w.Contains(label));
    RuntimeProfileGating.AllowsManagedVm(profile).ShouldBeFalse();
}

[Fact]
public void Apply_DefaultProfileKeepsVirtualization()
{
    var settings = new ObfySettings { Virtualization = { Enabled = true } };
    RuntimeProfileGating.Apply(settings, PipelineContext.ForAssembly(new ModuleDefUser("t"), settings));
    settings.Virtualization.Enabled.ShouldBeTrue();
    RuntimeProfileGating.AllowsManagedVm(RuntimeProfile.Default).ShouldBeTrue();
}
```

Extend `Apply_DefaultProfileKeepsEmbeddingAndPeProtections` only if it would now fail — it should not (virtualization default is false).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmSeedTests|FullyQualifiedName~Apply_DisablesVirtualization`

Expected: FAIL

- [ ] **Step 3: Implement `VmSeed` and gating**

Add `AllowsManagedVm` next to `AllowsPeMutation`. In `Apply`, after the packing block:

```csharp
if (!AllowsManagedVm(settings.RuntimeProfile) && settings.Virtualization.Enabled)
{
    settings.Virtualization.Enabled = false;
    context.Warnings.Add(
        $"Virtualization disabled for {label}: the managed VM uses MethodBase.Invoke and is not supported on this runtime.");
}
```

Implement `Apply` on the blob in `VmSeed` (or a `VmMutator` method on the same type). Execute harness used by Task 3–5 can keep identity+zero until Task 7; `Apply_ThenRun_StillAdds`, `Apply_ThenRun_StillReturnsHi`, and `Apply_ThenRun_StillBranches` prove XOR+permute across 1-byte ops, `Ldstr`, and branches.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~VmSeedTests|FullyQualifiedName~RuntimeProfileGatingTests|FullyQualifiedName~VmRuntimeExecuteTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Virtualization/VmSeed.cs Src/Obfy.Core/Utilities/RuntimeProfileGating.cs Tests/Obfy.Tests/Virtualization/VmSeedTests.cs Tests/Obfy.Tests/RuntimeProfileGatingTests.cs Tests/Obfy.Tests/Virtualization/VmRuntimeExecuteTests.cs
git commit -m "feat: permute VM opcodes from the incremental-cache seed"
```

---

### Task 7: Replace VirtualizationObfuscator and pipeline e2e

**Files:**
- Modify: `Src/Obfy.Core/Obfuscators/Assembly/VirtualizationObfuscator.cs` (delete handwritten interpreter)
- Modify: `Src/Obfy.Core/Obfuscators/Assembly/StringEncryptionObfuscator.cs` (skip `VmEncoder.Select` set when virtualization is enabled)
- Modify: `Src/Obfy.Core/Obfuscators/Assembly/ConstantEncryptionObfuscator.cs` (same skip)
- Modify: `Src/Obfy.Core/Obfuscators/Assembly/ReferenceProxyObfuscator.cs` (`CanProxy` skips injected helpers / `IsRuntimeHelper`)
- Modify: `Src/Obfy.Core/Utilities/ObfuscationAttributeRules.cs`
- Modify: `Tests/Obfy.Tests/VirtualizationObfuscatorTests.cs`
- Modify: `Tests/Obfy.Tests/EndToEndObfuscationTests.cs`
- Modify: `Tests/Obfy.Tests/RuntimeInjectionTests.cs`
- Modify: `Tests/Obfy.Tests/AssemblyObfuscatorTests.cs`

**Interfaces:**
- Consumes: `VmEncoder`, `VmImporter`, `VmSeed`, `VmImporter.WriteStub`
- Produces: obfuscator that selects → pass-2 encode → concatenate → `VmSeed.Apply` → `Import` → stub each method; `ObfuscationFeature.Virtualization`; stats `ProtectionsApplied = encoded.Count`

**Obfuscator flow:**

```csharp
public bool IsEnabled(ObfySettings settings) => settings.Virtualization.Enabled;

public async Task<ObfuscationResult> ObfuscateAsync(...)
{
    var max = context.Settings.Virtualization.MaxMethods;
    if (max is < 1 or > 256)
        return ObfuscationResult.Failed($"Virtualization.MaxMethods must be 1-256 (got {max}).");

    var candidates = new List<MethodDef>();
    foreach (var type in module.GetTypes())
    {
        if (ObfuscatorHelpers.IsRuntimeHelper(type) || type.IsGlobalModuleType) continue;
        if (!ObfuscationAttributeRules.AllowType(type, context.Settings, ObfuscationFeature.Virtualization, context.Warnings))
            continue;
        foreach (var method in type.Methods)
        {
            if (!ObfuscationAttributeRules.AllowMethod(method, context.Settings, ObfuscationFeature.Virtualization, context.Warnings))
                continue;
            candidates.Add(method);
        }
    }

    var selected = VmEncoder.Select(candidates, max);
    // truncation warning if candidates that TryEncode with empty ids exceed max
    // zero selected → warning "Virtualization was enabled but no eligible methods were encoded." success

    foreach (var method in candidates)
    {
        if (selected.Contains(method))
            continue;
        if (VmEncoder.TryEncode(method, EmptyIds, throwawayTables, out _, out var skipReason))
            continue; // lost the maxMethods race — not a SkippedItem
        context.SkippedItems.Add(SkippedItem.UnsupportedMethod(method.FullName, skipReason));
    }

    var ids = selected.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);
    var tables = new VmMemberTables();
    var encoded = new List<(MethodDef Method, byte[] Code)>();
    foreach (var method in selected)
    {
        if (!VmEncoder.TryEncode(method, ids, tables, out var code, out var reason))
        {
            context.SkippedItems.Add(SkippedItem.UnsupportedMethod(method.FullName, reason));
            continue;
        }
        encoded.Add((method, code));
    }

    var blob = Concat(encoded);
    var starts = PrefixSums(encoded);
    var seed = VmSeed.Compute(context);
    var opMap = VmSeed.CreateOpMap(seed);
    var xorKey = VmSeed.CreateXorKey(seed);
    VmSeed.Apply(blob, opMap, xorKey);

    MethodDef run;
    try
    {
        var returnTypes = encoded.Select(e => e.Method.MethodSig.RetType.ToTypeDefOrRef()).ToList();
        var vmType = VmImporter.Import(context, blob, starts, opMap, xorKey, tables.Methods, tables.Fields, tables.Types, returnTypes);
        run = vmType.FindMethod("Run") ?? throw new InvalidOperationException("Virtualization failed: Run missing.");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return ObfuscationResult.Failed($"Virtualization failed: {ex.Message}", ex);
    }

    for (var i = 0; i < encoded.Count; i++)
        VmImporter.WriteStub(encoded[i].Method, run, i);

    stats.ProtectionsApplied = encoded.Count;
}
```

For truncation: after scanning, count how many candidates `TryEncode` with empty ids; if that count > max, add `Virtualization: maxMethods={max} reached; further eligible methods were skipped.`

Ineligible methods that fail encode are recorded as `SkippedItems` in the snippet above (not only in this prose). Do not add a `SkippedItem` for methods that lost the `maxMethods` race. Current tests expect unsigned-compare methods to be skipped with Details `"unsigned compare"` — **those tests are rewritten**: unsigned compares encode.

**String/constant encryption skip:** when `settings.Virtualization.Enabled`, both `StringEncryptionObfuscator` and `ConstantEncryptionObfuscator` skip methods in `VmEncoder.Select(candidates, maxMethods)` using the same candidate filters (runtime helpers, global module type, `[Obfuscation]` / exclusions for `ObfuscationFeature.Virtualization`). Methods that lose the `maxMethods` race still go through string/constant encryption. If a method is selected at priority 10 and later fails `TryEncode` at 24, its literals stay plaintext — accepted, debug-logged.

**Reference proxy:** in `CanProxy`, after the existing `<RefProxy>` check, return false when the resolved declaring type is in `context.InjectedHelperMap` or `ObfuscatorHelpers.IsRuntimeHelper(declaringType)`. Add a test: virtualization + proxy on, `Add`'s stub `call` target is `Run` (or a renamed `Vm` method), not `<RefProxy>::P*`.

**Attribute feature:**

```csharp
ObfuscationFeature.Virtualization,
```

```csharp
ObfuscationFeature.Virtualization => normalized is "virtualization" or "virtualize" or "vm",
```

Add `"virtualization" or "virtualize" or "vm"` to `IsKnownFeature`. Update the unknown-feature warning list: `supported: all, renaming, controlflow, strings, constants, virtualization.`

Change `SymbolRenaming_UnknownFeature_EmitsWarning` to `feature: "not-a-real-feature"` and assert that string.

**Rewrite `VirtualizationObfuscatorTests`:**

- `CallsExecute` → call operand name `"Run"`
- `Virtualization_ReplacesSimpleAddWithExecuteStub` → type `Vm` (not `<Vm>`), method `Run`, still has `Throw` somewhere in `Run` for invalid opcode
- Delete or invert `Virtualization_DoesNotEncodeUnsignedCompare` / `UnsignedBranch` / `LeavesIneligibleInstanceMethodUnchanged` — instance methods and unsigned compares **are** encoded
- Keep maxMethods fail / warn / exclusion tests
- Add: `[Obfuscation(Exclude=true, Feature="virtualization")]` is not stubbed (use `AddObfuscationAttribute` from assembly tests, or duplicate the helper)
- Add: bool-return and uint-return methods stub and (in e2e) execute
- Add: ineligible custom-struct param / `Nullable<T>` return appear in `SkippedItems`

**Rewrite `RuntimeInjectionTests.Virtualization_RegistersInterpreterOptions`:**

- Type name `Vm` (not `<Vm>`)
- `InjectedHelpers[vm]` equals `new RuntimeHelperOptions { FlattenControlFlow = true, Rename = true, EncryptIl = false }` (or flatten false **only** if Task 7 took the documented fallback)
- `ShouldFlattenControlFlow` matches that choice; `ShouldEncryptIl` false; `ShouldRename` true
- Method on the type is `Run`, not `Execute`

**Rewrite `EndToEndObfuscationTests` helpers:**

```csharp
private static bool CallsRun(MethodDef method) =>
    method.Body.Instructions.Any(i =>
        i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "Run");

private static void AssertVmStub(...) => CallsRun(...).ShouldBeTrue();
```

Type name: `loaded.Types.ShouldContain(t => t.Name == "Vm")` when renaming is off.

Keep `Virtualization_RunsSimpleArithmeticOnRealAssembly` (Add still works). Keep locals/branches tests.

Add:

```csharp
[Fact]
public async Task Virtualization_RunsInstanceAndLdstrOnRealAssembly() { /* Box.Go / Hi */ }

[Fact]
public async Task Virtualization_FullPipeline_RunsEncodedMethod()
{
    // virtualization + string encryption + control flow + rename on
    // public static int Add(int a,int b)=>a+b;
    // load ALC, find the public method via BindingFlags if renamed... 
    // PreservePublicApi = true so Lib.Add is still callable
    LoadAndInvoke(output, "Lib", "Add", 2, 3).ShouldBe(5);
}

[Fact]
public async Task Virtualization_FullPipeline_StringLongerThanMinStringLength_ReturnsHello()
{
    // same full pipeline; method is `public static string Hi() => "hello";`
    // "hello".Length (5) > StringEncryption.MinStringLength default (3).
    // If string encryption is not skipped on the Select set, the encoder sees a
    // decryptor call instead of ldstr and this returns garbage or skips.
    LoadAndInvoke(output, "Lib", "Hi").ShouldBe("hello");
}

[Fact]
public async Task Virtualization_NativeAotProfile_LeavesBody()
{
    settings.RuntimeProfile = RuntimeProfile.NativeAot;
    settings.Virtualization.Enabled = true;
    // after ObfuscateAsync, Add is not a Run stub
}
```

If `Virtualization_FullPipeline_RunsEncodedMethod` fails because flattening broke `Run`, set `FlattenControlFlow = false` on the importer registration and add a one-line comment citing the spec fallback. Do not weaken XOR/rename.

Decompiler assertion (in `VirtualizationObfuscatorTests` or e2e): after obfuscate, `Add`'s instructions contain `call Run` and do **not** contain `OpCodes.Add`.

- [ ] **Step 1: Write/adjust failing tests first** (new e2e facts + rewritten unit tests that expect instance encode). Run them; expect FAIL on `<Vm>` / `Execute` / instance skip.

- [ ] **Step 2: Implement obfuscator rewrite + attribute feature + test updates**

- [ ] **Step 3: Run the focused suite**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~Virtualization|FullyQualifiedName~Vm|FullyQualifiedName~RuntimeProfileGatingTests|FullyQualifiedName~RuntimeInjectionTests|FullyQualifiedName~SymbolRenaming_UnknownFeature`

Expected: PASS

- [ ] **Step 4: Run the full core test project**

Run: `dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj`

Expected: PASS. Fix any leftover `<Vm>` / `Execute` assertions.

- [ ] **Step 5: Commit**

```bash
git add Src/Obfy.Core/Obfuscators/Assembly/VirtualizationObfuscator.cs Src/Obfy.Core/Obfuscators/Assembly/StringEncryptionObfuscator.cs Src/Obfy.Core/Obfuscators/Assembly/ConstantEncryptionObfuscator.cs Src/Obfy.Core/Obfuscators/Assembly/ReferenceProxyObfuscator.cs Src/Obfy.Core/Utilities/ObfuscationAttributeRules.cs Tests/Obfy.Tests
git commit -m "feat: virtualize typical methods through the assembly pipeline"
```

---

### Task 8: Docs and product copy

**Files:**
- Modify: `docs/Techniques.md` (Virtualization section)
- Modify: `docs/Configuration.md` (`### virtualization`)
- Modify: `docs/CLI.md` (`--virtualize` row)
- Modify: `docs/Competitive-Analysis.md` (matrix + limits + conclusion)
- Modify: `docs/Roadmap.md` (PF-08)
- Modify: `docs/Testing-Roadmap.md` (TR-50)
- Modify: `CHANGELOG.md`
- Modify: `README.md` and `CLAUDE.md` technique tables
- Modify: `schemas/obfy.schema.json` virtualization description
- Modify: `Src/Obfy.Console/Program.cs` option Description
- Modify: `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml`
- Modify: `Src/Obfy.UI/Help/HelpCatalog.cs`
- Modify: `Src/Obfy.Core/Models/ObfySettings.cs` (`VirtualizationSettings` XML comment)
- Modify: `Src/Obfy.Core/Obfuscators/Assembly/VirtualizationObfuscator.cs` (class XML comment)
- Test: `Tests/Obfy.UI.Tests/Help/HelpCatalogTests.cs`

**Interfaces:**
- Consumes: behavior from Task 7 (copy must match)
- Produces: honest docs; no “simple static int” leftover on user-facing surfaces

**Locked copy:**

CLI / `Program.cs`:

```text
Enable IL virtualization of eligible methods (no EH/generic calls/byref/custom structs; CoreCLR only)
```

UI toggle `Content`: `Virtualize methods`

UI `ToolTip`: `Replaces eligible methods with a bytecode interpreter (no EH, generic calls, byref, or custom structs). CoreCLR only; off in every preset. Not confidentiality.`

Help catalog Managed launcher note:

```csharp
new HelpNamedNote("Managed launcher", "Pack managed launcher, Incremental cache, and Virtualize methods."),
```

Techniques.md Virtualization section must list: instance+static, objects/non-generic calls/fields/newobj/ldstr, i4/i8/r4/r8, skip EH/generic methods/types/**calls**/byref/custom structs/`Nullable<T>`/switch/ctors/`typeof`/interpolators/`foreach`/`using`, per-build opcode permutation + XOR, CoreCLR only, off in every preset, `--virtualize`, priority 24, string/constant encryption skip the Select set so strings live in the XOR blob, interpreter is a deterrent not confidentiality.

`VirtualizationSettings` and `VirtualizationObfuscator` XML comments must drop “static int only” / “unsigned compares are skipped”.

Configuration.md: drop “Config-only” and “static int only”. Mention `--virtualize`.

Competitive-Analysis.md:

- Advanced protection **Code virtualization** cell for Obfy: **Yes**
- Limits footnote: no EH, generic methods/types/**calls**, byref, custom structs/`Nullable<T>`, switch, typeof, interpolators, foreach/using; CoreCLR; per-build encoding (not a unique generated VM); not a native packer. Do not write this as “we don’t virtualize generic method bodies” while implying generic *calls* work.
- Executive summary / conclusion: Obfy meets the **managed-VM** protection-ceiling bar (Babel-class); still not a native packer or a licensing/RASP product. Do not claim “covers Reactor.”
- Weaknesses bullet “No general IL VM” → remove or replace with the limits footnote

Roadmap PF-08: general VM v1 done; remaining = EH/generics/byref and Approach B handler generation. PF-09 still Future.

Testing-Roadmap TR-50: rewrite to match encoder skip/encode of typical methods, not “unsigned compares skipped”.

CHANGELOG under Unreleased: general IL virtualization; limits; `--virtualize` description.

schema `virtualization.description`: match Techniques eligibility, not static-int.

- [ ] **Step 1: Write the failing help-catalog test**

```csharp
[Fact]
public void Techniques_ManagedLauncher_SaysVirtualizeMethods()
{
    var note = Topic(HelpCatalog.TechniquesId).Blocks.OfType<HelpNamedNote>()
        .Single(n => n.Heading == "Managed launcher");
    note.Text.ShouldContain("Virtualize methods");
    note.Text.ShouldNotContain("Virtualize simple methods");
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Tests/Obfy.UI.Tests/Obfy.UI.Tests.csproj --filter FullyQualifiedName~Techniques_ManagedLauncher`

Expected: FAIL (`Virtualize simple methods`)

- [ ] **Step 3: Update copy and docs as listed**

Do not enable virtualization in Aggressive. Do not claim native packing.

- [ ] **Step 4: Run tests**

Run:

```
dotnet test Tests/Obfy.UI.Tests/Obfy.UI.Tests.csproj --filter FullyQualifiedName~HelpCatalogTests
dotnet test Tests/Obfy.Console.Tests/Obfy.Console.Tests.csproj --filter FullyQualifiedName~Virtualize
dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --filter FullyQualifiedName~Virtualization
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add docs CHANGELOG.md README.md CLAUDE.md schemas/obfy.schema.json Src/Obfy.Console/Program.cs Src/Obfy.UI/Views/Controls/SettingsPanel.xaml Src/Obfy.UI/Help/HelpCatalog.cs Src/Obfy.Core/Models/ObfySettings.cs Src/Obfy.Core/Obfuscators/Assembly/VirtualizationObfuscator.cs Tests/Obfy.UI.Tests/Help/HelpCatalogTests.cs
git commit -m "docs: describe general IL virtualization limits honestly"
```

---

## Self-review

**Spec coverage**

| Spec requirement | Task |
|---|---|
| Embed netstandard2.0 runtime, copy types, no VmRuntime AssemblyRef | 1 |
| Flatten/rename/encrypt-IL registration | 1 (fallback in 7) |
| ISA + eligibility skips + two-pass CallVm (opcode + u16 id + u8 argc) | 2, 5 |
| `EncodedSize` + ignore `nop` + non-primitive valuetype param/return/local + MethodSpec skip | 2 |
| Interpreter arith/branch/ldstr; `Frame[]`; `returnTypes` boxing; `this` as `O` | 3 |
| Token tables, Invoke, callvirt dispatch | 4 |
| Frames, arrays, box/cast/throw | 5 |
| Seed = hex-decoded cache key (no second hash), `"obfy-cache|"` fallback, permute + XOR via `EncodedSize`, `Apply` on Ldstr/branches | 6 |
| NativeAOT/IL2CPP/Blazor gating | 6, 7 e2e |
| Replace obfuscator, delete int VM, stubs, stats, MaxMethods, SkippedItems, string/constant Select skip, CanProxy skip helpers, RuntimeInjectionTests | 7 |
| `ObfuscationFeature.Virtualization` | 7 |
| Full pipeline + decompiler stub | 7 |
| Opt-in presets unchanged | 7 (existing UI tests) + 8 |
| Docs / CLI / UI / Competitive-Analysis Partial→Yes | 8 |
| Native packer / licensing / EH / Approach B | Out of plan (spec non-goals) |

**Type consistency:** `Vm.Run(int, object[])` throughout (never `object?[]`). `Init` includes `Type[] returnTypes`. `VmOp` numbers 1–77. `VmSkipReasons` strings shared by encoder and tests (`NonPrimitiveValuetype` covers param/return/local). `VmImporter.Import` / `WriteStub` signatures used by Tasks 3–7. `VmSeed.Compute/CreateOpMap/CreateXorKey/Apply` used by 6–7. `VmIsa.EncodedSize` used by `Apply`. Stub target name is `Run`, type `Obfy.Runtime.Vm`. Frame stack is `Frame[]` + depth.

**CallVm encoding:** opcode + u16 id + u8 argc (`argc` includes `this`), locked in Task 2, the spec ISA table, and Task 5.

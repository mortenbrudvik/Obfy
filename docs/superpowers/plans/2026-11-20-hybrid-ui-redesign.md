# Obfy hybrid UI redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Obfy's WPF-UI Fluent interface with a thin WPF shell hosting a redesigned React page in WebView2, moving all UI logic into a WPF-free C# layer the page drives over an `apibridge` host object.

**Architecture:** New projects `Obfy.Common`, `Obfy.App` (WPF-free logic lifted from the ViewModels), `Obfy.WebView`, `Obfy.Interop` sit beside a trimmed `Obfy.UI` WPF shell and a `Src/WebApp` React app. The page calls C# through `window.chrome.webview.hostObjects.apibridge.InvokeMethod(service, method, json)` returning `{success,data}` / `{success:false,error:{code,message}}`; C# pushes `{eventName,data}` via `PostWebMessageAsJson`. Each endpoint is a Mediator request+handler under `Obfy.App/Features/<Area>/`, registered by name in `ApiRegistrations`. `Obfy.Core`, the CLI and `obfy.json` are untouched.

**Tech Stack:** net10.0-windows10.0.26100, WPF-UI 4.3.0, Microsoft.Web.WebView2, Mediator (MIT), Autofac + Microsoft.Extensions.Hosting (kept), CommunityToolkit.Mvvm (shell VM only), NLog via Logging.Core (kept). Web: npm, Vite, React 19, TypeScript strict, plain CSS, Vitest. Tests stay on xunit 2.9.3 + Moq + Shouldly (kept).

**Spec:** `docs/superpowers/specs/2026-11-20-hybrid-ui-redesign-design.md`

## Global Constraints

- Keep `Src/Obfy.UI/Obfy.UI.csproj`, `AssemblyName=ObfyUI`, output `ObfyUI.exe`, and `Src/Obfy.UI/Images/app.png` (MsixPackageTests, AppxManifest, both build scripts, UI locator depend on them).
- `TreatWarningsAsErrors=true` for every non-test project; code must compile warning-free.
- Central package management: every new NuGet package needs a `PackageVersion` in `Directory.Packages.props` or restore fails.
- Reference versions (from the sibling apps): Microsoft.Web.WebView2 `1.0.4314.35`, Mediator.Abstractions + Mediator.SourceGenerator `3.0.2` (generator in `Obfy.UI` only). Obfy keeps Autofac 9.3.2, Microsoft.Extensions.Hosting 10.0.12, WPF-UI 4.3.0, CommunityToolkit.Mvvm 8.4.2.
- `build-msix.ps1` throws if any `.pdb` is in the layout: production web builds emit no source maps.
- WebView2 user-data folder must be writable: `%LOCALAPPDATA%\Obfy\WebView2` (never the install dir).
- `Obfy.App` must not reference WPF, and `Obfy.Core` must not gain a Mediator dependency.
- All work in worktree `.worktrees/hybrid-ui`. Commits carry **no** Claude attribution trailers (user memory rule overrides the session reminder).
- Copy is calm sentence-case, no exclamation marks. Hex colours only in `index.css`.
- `Tests/Obfy.Tests/CiUiWorkflowTests.cs` pins the CI "UI tests" step; `MsixPackageTests.cs` pins the identity/paths — both must stay green.

## Review Focus

The spec implies these inputs; each is pinned to the task named.

- **WebView2 runtime absent / fails to start** → the window shows a native failure panel (log folder, Restart, Quit), never a blank window (Task 5).
- **Log/progress events pushed from a `Task.Run` worker thread** → marshalled to the UI thread; no cross-thread `PostWebMessageAsJson` (Task 3 handler test + Task 13).
- **A technique shipped without its explanation** → `techniques.json` contract test fails if any technique/option id the app layer exposes lacks content, or vice-versa (Task 20).
- **A `.json` drop plus input files dropped together** → only the first configuration loads, inputs are added, a notice says so (Task 10, UI-058).
- **Watermark on with an empty id** → run is blocked with reason "Watermark needs an id" and the field shows an inline error (Task 9 validation + Task 17 render).

---

## Stage 1 — Skeleton

### Task 1: Solution and empty projects

**Files:**
- Create: `Src/Obfy.Common/Obfy.Common.csproj`, `Src/Obfy.App/Obfy.App.csproj`, `Src/Obfy.WebView/Obfy.WebView.csproj`, `Src/Obfy.Interop/Obfy.Interop.csproj`
- Modify: `Obfy.sln` (add the four projects), `Directory.Packages.props` (add `Microsoft.Web.WebView2`, `Mediator.Abstractions`, `Mediator.SourceGenerator`)
- Test: none (build is the gate)

**Interfaces:**
- Produces: four buildable projects. `Common` net10.0, no deps. `App` net10.0 → Core, Logging.Core, Common, Mediator.Abstractions. `WebView` net10.0-windows TFM `UseWPF` → Microsoft.Web.WebView2, Common. `Interop` net10.0-windows `UseWPF` → WebView, App, Common, Mediator.Abstractions.

- [ ] **Step 1:** Create the four csproj files with the TFM, `UseWPF` and `ProjectReference`/`PackageReference` sets above; add `Microsoft.Web.WebView2`, `Mediator.Abstractions`, `Mediator.SourceGenerator` to `Directory.Packages.props`.
- [ ] **Step 2:** Add all four to `Obfy.sln` (`dotnet sln Obfy.sln add Src/Obfy.Common/Obfy.Common.csproj …`).
- [ ] **Step 3:** Run `dotnet build Obfy.sln -c Release`. Expected: PASS (new projects empty but compile, warnings-as-errors clean).
- [ ] **Step 4:** Commit (`chore: scaffold hybrid projects`).

### Task 2: JsonUtils in Common

**Files:**
- Create: `Src/Obfy.Common/JsonUtils.cs`
- Test: `Tests/Obfy.App.Tests/Common/JsonUtilsTests.cs` (create the `Obfy.App.Tests` project here: xunit 2.9.3 + Shouldly, ref Common/App; add to sln)

**Interfaces:**
- Produces: `static class JsonUtils { static string Serialize(object); static object? Deserialize(string, Type); static T? Deserialize<T>(string); static JsonSerializerOptions Options }` — camelCase, `JsonStringEnumConverter`, case-insensitive, ISO-8601, Guids as strings.

- [ ] **Step 1:** Write `JsonUtilsTests`: `Serialize_UsesCamelCase` (`{ FileName="x" }` → `"fileName"`), `Deserialize_IsCaseInsensitive`, `Serialize_EnumAsString`.
- [ ] **Step 2:** Run the three tests → FAIL (type missing).
- [ ] **Step 3:** Implement `JsonUtils` with the options above.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit (`feat(common): add JsonUtils`).

### Task 3: Bridge core (Interop)

**Files:**
- Create: `Src/Obfy.Interop/ApiBridge.cs`, `ApiRegistry.cs`, `ApiEventAggregator.cs`, `IpcNames.cs`, `IpcLogger.cs`, `ApiError.cs`
- Test: `Tests/Obfy.Interop.Tests/` (new project: xunit 2.9.3 + Moq + Shouldly, ref Interop/App/Common; add to sln) — `ApiBridgeTests.cs`, `ApiRegistryTests.cs`, `ApiEventAggregatorTests.cs`

**Interfaces:**
- Produces:
  - `[ComVisible(true)][InterfaceType(ComInterfaceType.InterfaceIsDual)] interface IApiBridge { Task<string> InvokeMethod(string serviceName, string methodName, string jsonParams); }`
  - `class ApiBridge(IMediator, ApiRegistry, IpcLogger) : IApiBridge` — looks up the request type, deserializes, `await Task.Run(() => mediator.Send(request))`, returns `{success:true,data}` or `{success:false,error:{code,message}}` via `JsonUtils`. Unknown api → `{success:false,error:{code:"UnknownApi",…}}`. Maps `ObfyApiException`→its code; `IOException`/`UnauthorizedAccessException`/`ArgumentException`→`"BadRequest"` with message; anything else→`"Internal"`, generic message.
  - `class ApiRegistry { void Register<TRequest>(string service, string method); Type? GetRequestType(string service, string method) }` — `ConcurrentDictionary<string,Type>` keyed `"service.method"`.
  - `interface IWebViewInterop { void PostWebMessageAsJson(string json); }` (defined in Task 4; `ApiEventAggregator` consumes it)
  - `class ApiEventAggregator(IWebViewInterop) { void Publish(string eventName, object? data) }` — posts `{eventName,data}` via JsonUtils.
  - `static IpcNames { static class Services {…}; static class Events {…} }` — const strings, filled per endpoint in later tasks.
  - `class ObfyApiException(string code, string message) : Exception`.

- [ ] **Step 1:** Write `ApiBridgeTests`: `InvokeMethod_UnknownApi_ReturnsError`; `InvokeMethod_Success_WrapsData` (register a fake request whose handler returns a value, assert `"success":true` and data); `InvokeMethod_DomainException_PassesCode`; `InvokeMethod_UnknownException_IsGeneric`. `ApiRegistryTests`: `Register_ThenGetRequestType_RoundTrips`; `GetRequestType_Unknown_ReturnsNull`. `ApiEventAggregatorTests`: `Publish_PostsEnvelope` (fake `IWebViewInterop` captures the json, assert `eventName`/`data`).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement the classes above (model shapes on `C:\code\projects\sora\src\Sora.Interop\ApiBridge.cs` for the typed-error form and `C:\code\projects\ma\src\Ma.Interop\`).
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit (`feat(interop): bridge, registry, event aggregator`).

### Task 4: WebView control and handler

**Files:**
- Create: `Src/Obfy.WebView/IWebViewInterop.cs`, `WebViewControl.cs`, `WebViewHandler.cs`
- Test: `Tests/Obfy.Interop.Tests/WebViewHandlerTests.cs`

**Interfaces:**
- Consumes: `IWebViewInterop` (declared here, used by Task 3's aggregator — move the interface to `Obfy.WebView` and have Interop reference it).
- Produces:
  - `class WebViewControl : WebView2, IWebViewInterop` — `PostWebMessageAsJson` marshals via `Dispatcher.BeginInvoke` when off the UI thread; drops with a debug log if `CoreWebView2` is null/disposed.
  - `class WebViewHandler` with ctor `(WebView2 web, string url, string userDataFolder){ HostObject, HostObjectName, DefaultBackgroundColor, AllowedOrigins, EnableDevTools, HostWebRootFolder, InitializeFailed, Unrecoverable }` and static pure helpers `static bool IsAllowedOrigin(Uri, IReadOnlyList<string>)`, `static bool ShouldShowContextMenu(bool isEditable, bool hasSelection)`, `const string DefaultHostName = "obfy.localassets.invalid"`.

- [ ] **Step 1:** Write `WebViewHandlerTests`: `IsAllowedOrigin_ExactMatch_True`, `IsAllowedOrigin_OtherPort_False`, `ShouldShowContextMenu_EditableOrSelection_True`, `ShouldShowContextMenu_Neither_False`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Copy `WebViewControl.cs`/`WebViewHandler.cs`/`IWebViewInterop.cs` in shape from `C:\code\projects\ma\src\Ma.WebView\`, renaming to Obfy and host `obfy.localassets.invalid`. Keep the Release hardening (origin allowlist fail-closed, new-window blocked, context menu gated, DevTools+accelerator keys Debug-only, reload-on-ProcessFailed with `MaxReloads=3`→`Unrecoverable`).
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit (`feat(webview): control and hardened handler`).

### Task 5: WPF shell hosts the page and round-trips one call + event

**Files:**
- Modify: `Src/Obfy.UI/Obfy.UI.csproj` (add refs to WebView/Interop/App/Common and `Mediator.SourceGenerator`), `App.xaml.cs`, `Views/MainWindow.xaml(.cs)`
- Create: `Src/Obfy.UI/DependencyInjection/HybridModule.cs`, `Src/Obfy.UI/ViewModels/ShellViewModel.cs`, `Src/Obfy.UI/Services/FileDialogs.cs`, `Src/Obfy.UI/Services/ThemeController.cs`, `Src/Obfy.App/Features/App/AppInfoRequest.cs`, `Src/Obfy.App/Features/App/ReadyRequest.cs`
- Modify: `Src/Obfy.UI/DependencyInjection/AppModule.cs` (register bridge singletons)
- Test: `Tests/Obfy.UI.Tests/Shell/HybridWiringTests.cs`

**Interfaces:**
- Consumes: `ApiBridge`, `ApiRegistry`, `ApiEventAggregator`, `WebViewControl`, `WebViewHandler`, Mediator.
- Produces: a running `ObfyUI.exe` that loads the page (`http://localhost:5250/` Debug; `https://obfy.localassets.invalid/index.html` → `Assets\` under `AppContext.BaseDirectory`, `DenyCors`, Release), registers `apibridge`, serves `app.info` (returns `{version}`), reveals the window on `app.ready`, shows a native failure panel on WebView2 failure. `IFileDialogs` in `Obfy.App/Services` implemented by `FileDialogs` in UI.

**Interfaces (App):**
- `interface IFileDialogs { Task<string[]> PickFilesAsync(…); Task<string?> PickFolderAsync(…); Task<string?> PickSaveAsync(…); Task<string?> PickKeyAsync(); }`
- `record AppInfoRequest : IRequest<AppInfo>; record AppInfo(string Version);`
- `record ReadyRequest : IRequest<Unit>;`

- [ ] **Step 1:** Write `HybridWiringTests`: `Registrations_ResolveBridgeAndRegistry` (build the Autofac container from `AppModule`+`HybridModule`, resolve `IApiBridge`, `ApiRegistry`, `ApiEventAggregator` as singletons); `ApiInfo_ReturnsInformationalVersion` (send `AppInfoRequest`, assert non-empty version).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Build the shell: `MainWindow` = `ui:FluentWindow`, `ui:TitleBar`, `ContentPresenter` bound to the web view, native failure panel collapsed by default. `ShellViewModel` builds `new WebViewHandler(web, url, paths)` with the `#if DEBUG` origin switch, `DefaultBackgroundColor`, `AllowedOrigins`. `ThemeController` applies the system theme to WPF-UI, palette, web background and `PreferredColorScheme` together, following `SystemEvents.UserPreferenceChanged`. `FileDialogs` wraps Microsoft.Win32 dialogs (lift from the current `FileDialogService`). Register everything; call `ApiRegistrations.RegisterAll` (stub registering only `app.*` for now). Window revealed on `app.ready` with a timeout. Keep `IHost`/`ApplicationHostService`.
- [ ] **Step 4:** Run `HybridWiringTests` → PASS; `dotnet build -c Release` → PASS.
- [ ] **Step 5:** Commit (`feat(ui): hybrid shell with bridge wiring`).

### Task 6: Web app scaffold with a smoke page

**Files:**
- Create under `Src/WebApp/`: `package.json`, `package-lock.json`, `vite.config.ts`, `tsconfig*.json`, `eslint.config.js`, `index.html`, `src/main.tsx`, `src/App.tsx`, `src/index.css`, `src/interop/WebViewApiClient.ts`, `src/interop/mockBridge.ts`, `src/interop/errorMessage.ts`, `src/types/WebView.ts`, `src/types/domain.ts`, `src/hooks/useWebViewEvent.ts`, `src/hooks/usePushedState.ts`, `src/hooks/useAppAction.ts`, `build/contentSecurityPolicy.ts`, `src/interop/__tests__/WebViewApiClient.test.ts`
- Modify: `.gitignore` (add `node_modules/`, `Src/WebApp/dist/`)

**Interfaces:**
- Produces: `apiClient.call<T>(service,method,params?,opts?)`, `apiClient.on<T>(event,cb)` (model on `C:\code\projects\ma\src\WebApp\src\interop\`); a mock bridge answering `app.info`; Vite on port 5250 `strictPort`, output `entryFileNames:'index.js'`, CSP `<meta>` injected at build, `assetsInlineLimit:0`, sourcemaps off in production.

- [ ] **Step 1:** Write `WebViewApiClient.test.ts`: `call_resolvesData_onSuccessEnvelope`; `call_throws_onErrorEnvelope`; `on_receivesPushedEvent` (dispatch a `message` with `{eventName,data}`). Use a fake `window.chrome.webview`.
- [ ] **Step 2:** `npm ci && npm run test:run` → FAIL.
- [ ] **Step 3:** Scaffold the app (copy shapes from Ma), tokens in `index.css` from the approved mockup (accent `#6a3fd1`/`#b7a2f6`, light+dark+forced-colors+focus ring), `App.tsx` renders a smoke line from `app.info`.
- [ ] **Step 4:** `npm run test:run` → PASS; `npm run build` → PASS (type-check clean).
- [ ] **Step 5:** Commit (`feat(web): scaffold React app and interop client`).

### Task 7: Build integration and dev loop

**Files:**
- Modify: `Src/Obfy.UI/Obfy.UI.csproj` (add `NpmInstall`, `BuildFrontend`, `CopyFrontend`, `IncludeFrontendInPublish` targets; `RemoveForeignNativeRuntimes` for win-x64)
- Create: `scripts/dev.ps1`
- Test: `Tests/Obfy.Tests/` — add `HybridBuildTests.cs`

**Interfaces:**
- Produces: `dotnet build` runs `npm ci && npm run build` and copies `dist\**` to `$(TargetDir)Assets\`; publish adds `dist` to `ResolvedFileToPublish` as `Assets\…` and **errors** if `dist` is empty; `-p:SkipFrontendBuild=true` skips it. `scripts/dev.ps1` starts Vite, waits for 5250, runs the shell with `SkipFrontendBuild`.

- [ ] **Step 1:** Write `HybridBuildTests`: `UiCsproj_HasFrontendTargets` (read the csproj, assert `BuildFrontend` and `IncludeFrontendInPublish` present and `SkipFrontendBuild` guarded); `UiCsproj_PublishErrorsOnEmptyDist` (assert the empty-dist `Error` element exists). (Static text assertions, same style as `MsixPackageTests`.)
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Add the targets (copy from `C:\code\projects\sora\src\Sora.UI\Sora.UI.csproj`, `$(WebAppDir)=..\WebApp\`), write `dev.ps1` (from Ma, port 5250).
- [ ] **Step 4:** Run `HybridBuildTests` → PASS; `dotnet build -c Release` embeds `Assets\index.html`; launch once, confirm the smoke line renders from the virtual host.
- [ ] **Step 5:** Commit (`build(ui): wire frontend build into MSBuild`).

### Task 8: File-drop spike and decision

**Files:**
- Modify: `Src/Obfy.UI/Views/MainWindow.xaml(.cs)` (`AllowDrop`, `DragOver`/`Drop`), `Src/Obfy.App/Features/Files/*` (a `files.dropped` path — stub until Task 10)
- Create: `Src/Obfy.App/Features/Files/DropPathsRequest.cs`
- Test: manual spike note recorded in the task; unit test deferred to Task 10

**Interfaces:**
- Produces: a confirmed mechanism — native `DataFormats.FileDrop` over the web area reaches the WPF window → paths pushed to the app layer. Records in the plan file whether `AllowExternalDrop=false` is needed or the `postMessageWithAdditionalObjects` fallback is used.

- [ ] **Step 1:** Add `AllowDrop` + `Drop` reading `e.Data.GetData(DataFormats.FileDrop) as string[]`; log the paths.
- [ ] **Step 2:** Run the app, drag a `.dll` over the page, confirm the paths log. If they do not, set `AllowExternalDrop` and forward from JS (`postMessageWithAdditionalObjects`); record which worked.
- [ ] **Step 3:** Wire the confirmed path to a `DropPathsRequest` stub that logs.
- [ ] **Step 4:** `dotnet build -c Release` → PASS.
- [ ] **Step 5:** Commit (`feat(ui): native file drop into the bridge`).

---

## Stage 2 — Application layer

Each task lifts one ViewModel from `Src/Obfy.UI/ViewModels/` into a `Obfy.App` session class with no WPF types, ports its tests from `Tests/Obfy.UI.Tests/` into `Tests/Obfy.App.Tests/`, applies the named defect fixes, and registers its endpoints. The current ViewModel surfaces are in the spec and the analyst report. `settings.get`-style snapshots carry the derived state the page needs (availability, reasons, validation, `differsFromLevel`) so no rules live in the page.

### Task 9: ProtectionSession (from SettingsViewModel)

**Files:**
- Create: `Src/Obfy.App/Sessions/ProtectionSession.cs`, `Src/Obfy.App/Features/Settings/*.cs` (get, update, applyLevel, openConfig, saveConfig, pickSigningKey), `Src/Obfy.App/Models/SettingsSnapshot.cs`
- Test: `Tests/Obfy.App.Tests/Sessions/ProtectionSessionTests.cs` (port the 56 `SettingsViewModelTests`)

**Interfaces:**
- Produces: `class ProtectionSession` owning `ObfySettings`, level, Custom snapshot, profile availability, validation, loaded-config path + dirty flag; raises `Changed`. `record SettingsSnapshot(string Level, IReadOnlyList<TechniqueState> Techniques, IReadOnlyList<ValidationError> Errors, string? ConfigPath, bool Dirty, …)`; `record TechniqueState(string Id, bool Enabled, bool Available, string? UnavailableReason, bool DiffersFromLevel, object Options)`. Endpoints `settings.get/update/applyLevel/openConfig/saveConfig/pickSigningKey`; event `settings.changed`.

- [ ] **Step 1:** Port `SettingsViewModelTests` → `ProtectionSessionTests` against `ProtectionSession` (drop WPF-specific assertions). **Add fix tests:** `Level_StaysNonCustom_WhenProfileOrWatermarkChanges` (UI-039), `CustomRestore_KeepsExclusions` (UI-055), `Watermark_On_EmptyId_ProducesValidationError` (UI-040).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement `ProtectionSession` by lifting `SettingsViewModel` logic (presets, snapshot, gates, `ToObfySettings`/`FromObfySettings`, `_loadedSettings.Clone()`), replacing INPC with a `Changed` event and the snapshot DTO. Apply UI-039/055/040 fixes: level flips to Custom only for preset-governed settings; Custom restore never touches profile/watermark/signing/packing/merge/embedding/incremental/inclusions/exclusions; validation errors are part of the snapshot. Add the Mediator handlers.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit (`feat(app): ProtectionSession and settings endpoints`).

### Task 10: InputSession (from FilesViewModel)

**Files:**
- Create: `Src/Obfy.App/Sessions/InputSession.cs`, `Src/Obfy.App/Features/Files/*.cs` (get, pick, remove, clear, setIncluded, reveal, openOutput, dropPaths), `Src/Obfy.App/Features/Output/*.cs` (set, pickFolder), `Src/Obfy.App/Models/FileRow.cs`, `InputSnapshot.cs`
- Modify: `Src/Obfy.App/Features/Files/DropPathsRequest.cs` (from Task 8)
- Test: `Tests/Obfy.App.Tests/Sessions/InputSessionTests.cs` (port the 43 `FilesViewModelTests`)

**Interfaces:**
- Consumes: `IFileDialogs`, `ISolutionAnalyzer` (Core), `ISettingsService` (lift from UI into App, or keep in UI behind an interface App references — prefer moving `SettingsService`/`ISettingsService` to `Obfy.App/Services`). 
- Produces: `class InputSession` owning file rows, solution/project expansion, include state, output folder, symbol-map option, preferences; raises `Changed`. `record InputSnapshot(IReadOnlyList<FileRow> Files, string OutputFolder, string OutputHint, bool WriteSymbolMap, string SymbolMapPath, …)`. Endpoints `files.*`, `output.*`; event `files.changed`.

- [ ] **Step 1:** Port `FilesViewModelTests` → `InputSessionTests`. **Add fix tests:** `OutputName_HonoursIncludeCheckbox` (UI-053), `ReincludedRow_KeepsOriginalSkipReason` (UI-054), `Drop_ConfigPlusInputs_LoadsFirstConfigOnly_AndAddsInputs` (UI-058).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Lift `FilesViewModel` core (expansion, dedupe, supported-path rules, startup args, output/symbol-map resolution, preference debounce via `System.Threading` not `Application.Current`). Apply UI-053/054/058. Symbol-map path editable only when the option is on; relative resolves against the output folder (UI-045). Add handlers; `output.set` validates/canonicalises the typed path.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit (`feat(app): InputSession and files/output endpoints`).

### Task 11: LogBuffer (from OutputViewModel)

**Files:**
- Create: `Src/Obfy.App/Sessions/LogBuffer.cs`, `Src/Obfy.App/Features/Log/*.cs` (get, clear), `Src/Obfy.App/Models/LogLine.cs`
- Test: `Tests/Obfy.App.Tests/Sessions/LogBufferTests.cs` (port the 6 `OutputViewModelTests`)

**Interfaces:**
- Produces: `class LogBuffer { void Add(LogLevel, string); void StartRunSeparator(); IReadOnlyList<LogLine> Get(); void Clear() }` raising a batched `Appended` (~100 ms). Endpoint `log.get/clear`; event `log.appended`. Keeps the `———` separator between runs.

- [ ] **Step 1:** Port `OutputViewModelTests`; add `NewRun_InsertsSeparator_WhenNonEmpty`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement; batching timer coalesces appended lines into one event payload.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit (`feat(app): LogBuffer and log endpoints`).

### Task 12: ResultsStore (from ResultsViewModel)

**Files:**
- Create: `Src/Obfy.App/Sessions/ResultsStore.cs`, `Src/Obfy.App/Features/Results/*.cs` (summary, symbols, exportMap, exportReport), `Src/Obfy.App/Features/Preview/GetPreviewRequest.cs`, `Src/Obfy.App/Models/{SymbolNode,StatisticGroup}.cs`
- Test: `Tests/Obfy.App.Tests/Sessions/ResultsStoreTests.cs` (port the 15 `ResultsViewModelTests`)

**Interfaces:**
- Consumes: `IReportService`, `AssemblyPreview.Decompile` (Core), `SymbolMapKey.ParseAll` (Core).
- Produces: `class ResultsStore` with `LoadResults`, statistics, a **paged** symbol query `SymbolsPage Query(string? text, string? parentId, int offset, int limit)`, report, preview. Decompile and parsing run off-thread. Endpoints `results.summary`, `results.symbols`, `results.exportMap`, `results.exportReport`, `preview.get`; event `results.changed`. Unrenamed parent types marked as containers, not renames (UI-050).

- [ ] **Step 1:** Port `ResultsViewModelTests`; add `UnrenamedParent_NotShownAsRename` (UI-050), `Symbols_Query_IsPaged`, `Decompile_RunsOffThread` (UI-044 — assert the API is async/`Task`).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Lift `ResultsViewModel` (symbol parse, filter, tree build, statistic groups, exports, preview error mapping) replacing `SymbolTreeNode`/`ObservableCollection` with DTOs and a paged query; make decompile/parse `async` off the UI thread.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit (`feat(app): ResultsStore and results/preview endpoints`).

### Task 13: RunCoordinator (from MainViewModel)

**Files:**
- Create: `Src/Obfy.App/Sessions/RunCoordinator.cs`, `Src/Obfy.App/Features/Run/*.cs` (get, start, cancel), `Src/Obfy.App/Models/RunSnapshot.cs`
- Test: `Tests/Obfy.App.Tests/Sessions/RunCoordinatorTests.cs` (port the 30 `MainViewModelTests`)

**Interfaces:**
- Consumes: `IObfuscationService`, `IReportService` (Core), `ProtectionSession`, `InputSession`, `LogBuffer`, `ResultsStore`, `ApiEventAggregator`.
- Produces: `class RunCoordinator` owning run-mode selection (merge / closed-set / per-file / joint source compilation), the run loop (`Task.Run`), cancel, and `record RunSnapshot(bool CanStart, string? BlockedReason, string Mode, string ModeExplanation, string OutputHint, double Progress, string? CurrentFile, int Completed, int Total, RunStatus Status)`. Endpoints `run.get/start/cancel`; events `run.changed`, `run.completed`. Progress and per-file status pushed from the worker thread through the aggregator (marshalled by `WebViewControl`). Mode hint and run use one rule (UI-053); explicit `CanStart`/`BlockedReason` (UI-052).

- [ ] **Step 1:** Port `MainViewModelTests` (drop the WPF-UI `IContentDialogService` ones; Help/About move to the page). Add `BlockedReason_Explicit_ForEachBlocker` (UI-052), `ModeHint_MatchesRunRule` (UI-053).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Lift the orchestration; replace command `CanExecute` with `CanStart`/`BlockedReason` in the snapshot; push progress/status/log events via `ApiEventAggregator`.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit (`feat(app): RunCoordinator and run endpoints`).

### Task 14: Endpoint registration and contracts

**Files:**
- Create: `Src/Obfy.Interop/ApiRegistrations.cs` (fill `RegisterAll`), fill `IpcNames`
- Modify: `Src/Obfy.UI/App.xaml.cs` (Mediator `options.Assemblies` include `Obfy.App` + `Obfy.Interop`)
- Test: `Tests/Obfy.Interop.Tests/ApiRegistrationsTests.cs`, `RequestJsonContractTests.cs`

**Interfaces:**
- Produces: every `(service, method) → requestType` registered; `IpcNames.Services`/`Events` complete.

- [ ] **Step 1:** Write `ApiRegistrationsTests` as a `[Theory]` listing every `(service, method, requestType)` (assert `ApiRegistry.GetRequestType` returns it after `RegisterAll`). Write `RequestJsonContractTests` pinning the camelCase JSON shape of each request and each pushed event payload via `JsonUtils`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Fill `ApiRegistrations.RegisterAll` and `IpcNames`; extend the Mediator assemblies list.
- [ ] **Step 4:** Run → PASS; launch the app and confirm a real `settings.get`/`files.get` round-trips.
- [ ] **Step 5:** Commit (`feat(interop): register all endpoints with contract tests`).

---

## Stage 3 — React page

The page reads each session once (`usePushedState`) and follows pushes. Components are plain React + CSS using the tokens and layout from the approved mockup (`https://claude.ai/artifact/Uk7KHakHQFFPPJb53HYpkj`), which governs the look; this plan and the spec govern behaviour. Technique and Guide copy live in `content/*.json` (Task 20), not in components.

### Task 15: Shell, run bar, navigation, typed API

**Files:**
- Create: `Src/WebApp/src/interop/api.ts` (typed wrappers), `src/features/shell/{Shell.tsx,Shell.css,RunBar.tsx,Sidebar.tsx}`, `src/hooks/useRun.ts`, `src/components/{Toggle,Select,Slider,Field,Badge,Meter,Tabs,Keycap,Notice}.tsx`
- Modify: `src/App.tsx` (sidebar + screen switch + run bar), `src/types/domain.ts`
- Test: `Src/WebApp/src/features/shell/__tests__/RunBar.test.tsx`, `src/components/__tests__/*.test.tsx`

**Interfaces:**
- Consumes: `apiClient`; `run.get`/`run.changed`/`run.completed`.
- Produces: `Shell` with the 208px sidebar (Files/Protection/Results + Guide + version), the always-present `RunBar` (summary or blocked reason or running state), and keyboard shortcuts (Ctrl+Enter, Esc, F1, Ctrl+O/S/L). `api.ts` typed per service. Reusable `components/`.

- [ ] **Step 1:** Write `RunBar.test.tsx`: shows the blocked reason when `canStart=false`; shows summary + enabled Obfuscate when `canStart`; shows progress + Cancel when running. Mock `apiClient` via `src/test/bridge.ts`.
- [ ] **Step 2:** `npm run test:run` → FAIL.
- [ ] **Step 3:** Build the shell, run bar, sidebar, components and shortcuts.
- [ ] **Step 4:** Run → PASS; `npm run build` → PASS.
- [ ] **Step 5:** Commit (`feat(web): shell, run bar, shared components`).

### Task 16: Files feature

**Files:**
- Create: `Src/WebApp/src/features/files/{FilesPage.tsx,FilesPage.css,FileTable.tsx,OutputCard.tsx,EmptyState.tsx,useInput.ts}`
- Test: `src/features/files/__tests__/FilesPage.test.tsx`

**Interfaces:**
- Consumes: `files.*`, `output.*`, `files.changed`.
- Produces: first-launch empty state (drop zone, how-it-works, load config), the file table (include, name, origin, kind, size, status, skip reasons, row menu), the run-mode sentence, the Output card (folder + Browse, symbol-map toggle + path when on).

- [ ] **Step 1:** Write `FilesPage.test.tsx`: empty state renders when no files; table renders rows and a skipped row with its reason; toggling include calls `files.setIncluded`; symbol-map path field appears only when the toggle is on.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement from the mockup's Files screens.
- [ ] **Step 4:** Run → PASS; build → PASS.
- [ ] **Step 5:** Commit (`feat(web): files screen`).

### Task 17: Protection feature

**Files:**
- Create: `Src/WebApp/src/features/protection/{ProtectionPage.tsx,ProtectionPage.css,LevelCards.tsx,TechniqueList.tsx,TechniqueDetail.tsx,useProtection.ts}`
- Test: `src/features/protection/__tests__/{ProtectionPage,TechniqueDetail}.test.tsx`

**Interfaces:**
- Consumes: `settings.*`, `settings.changed`, `content/techniques.json`.
- Produces: level cards with meters; target-runtime select; the master/detail split with a working search; per-technique detail (what it does, good-to-know, options with permanent help, before/after, the security note on encryption techniques, "Read more in the Guide"); "Changed" and "Not available for X" badges; watermark inline error.

- [ ] **Step 1:** Write tests: selecting a level updates toggles; changing a toggle moves to Custom; the search filters rows; a NativeAOT runtime disables Method-body-encoding etc. and shows the unavailable reason; watermark on + empty id shows the inline error and blocks the run.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement from the mockup's Protection screen; detail content comes from `techniques.json`.
- [ ] **Step 4:** Run → PASS; build → PASS.
- [ ] **Step 5:** Commit (`feat(web): protection screen`).

### Task 18: Results feature

**Files:**
- Create: `Src/WebApp/src/features/results/{ResultsPage.tsx,ResultsPage.css,RunningView.tsx,SummaryTab.tsx,SymbolsTab.tsx,PreviewTab.tsx,LogTab.tsx,useResults.ts,useLog.ts}`
- Test: `src/features/results/__tests__/{ResultsPage,SymbolsTab,LogTab}.test.tsx`

**Interfaces:**
- Consumes: `results.*`, `preview.get`, `log.*`, `run.changed`/`run.completed`.
- Produces: before-run empty state (UI-059); running view (per-file status + live log); finished view with Summary (tiles, zero counters absent, per-file outputs + Reveal), Symbols (search + paged tree), Preview (assembly select + decompiled text), Log (problems-only, timestamps, wrap, copy, run separators). A successful run selects Results.

- [ ] **Step 1:** Write tests: empty state before any run; running view lists per-file status; Summary hides zero counters; Symbols search queries `results.symbols` and renders the tree; Log "problems only" filters to warnings/errors.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement from the mockup's running + finished screens; symbols paged via `results.symbols`.
- [ ] **Step 4:** Run → PASS; build → PASS.
- [ ] **Step 5:** Commit (`feat(web): results screen`).

### Task 19: Guide feature

**Files:**
- Create: `Src/WebApp/src/features/guide/{GuidePage.tsx,GuidePage.css}`
- Test: `src/features/guide/__tests__/GuidePage.test.tsx`

**Interfaces:**
- Consumes: `content/guide.json`.
- Produces: two-pane Guide (topic list + article), Shortcuts topic with keycaps; opened from the sidebar, F1, and "Read more" links.

- [ ] **Step 1:** Write `GuidePage.test.tsx`: topics render; selecting a topic shows its article; Shortcuts renders keycaps.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement; content from `guide.json`.
- [ ] **Step 4:** Run → PASS; build → PASS.
- [ ] **Step 5:** Commit (`feat(web): guide screen`).

### Task 20: Content and the technique-contract test

**Files:**
- Create: `Src/WebApp/src/content/techniques.json`, `src/content/guide.json`
- Test: `Tests/Obfy.Interop.Tests/TechniqueContentContractTests.cs`

**Interfaces:**
- Produces: per-technique/per-option copy (name, summary, what-it-does, trade-offs, option help, Guide link) drawn from `CLAUDE.md`, the README and the current `HelpCatalog.cs`, in calm sentence-case; `guide.json` replacing `HelpCatalog`.

- [ ] **Step 1:** Write `TechniqueContentContractTests`: read `techniques.json`, assert its technique ids and option ids exactly equal the set `ProtectionSession` exposes (no technique without content, no content without a technique).
- [ ] **Step 2:** Run → FAIL (content missing / mismatched).
- [ ] **Step 3:** Author `techniques.json` and `guide.json` to match the app layer's ids.
- [ ] **Step 4:** Run → PASS; `npm run test:run` and `dotnet build` → PASS.
- [ ] **Step 5:** Commit (`feat(web): technique and guide content with contract test`).

---

## Stage 4 — Cut-over

### Task 21: Remove the old WPF UI

**Files:**
- Delete: `Src/Obfy.UI/Views/Controls/*`, old `Views/MainWindow` settings/log/results/preview XAML sections now unused, `Src/Obfy.UI/ViewModels/{Settings,Files,Output,Results,Help,Main}ViewModel*.cs`, `Src/Obfy.UI/Converters/*`, `Src/Obfy.UI/Models/{AssemblyFile,LogEntry,SymbolTreeNode,Statistic*}.cs`, `Views/HelpDialogContent.xaml`
- Delete tests: `Tests/Obfy.UI.Tests/Views/*` (14 XAML-contract tests), `Converters/ConverterTests.cs`, and the ViewModel test files now living in `Obfy.App.Tests`
- Modify: `Src/Obfy.UI/DependencyInjection/AppModule.cs` (drop old VM registrations)

**Interfaces:**
- Produces: `Obfy.UI` contains only the shell (window, `ShellViewModel`, `FileDialogs`, `ThemeController`, startup). `Obfy.UI.Tests` contains only shell tests.

- [ ] **Step 1:** Delete the files above; remove their sln/csproj references.
- [ ] **Step 2:** Run `dotnet build Obfy.sln -c Release` → PASS (no dangling refs, warnings-clean).
- [ ] **Step 3:** Run `dotnet test Tests/Obfy.App.Tests` and `Tests/Obfy.Interop.Tests` and `Tests/Obfy.UI.Tests` → PASS.
- [ ] **Step 4:** Commit (`refactor(ui): remove the WPF-UI screen`).

### Task 22: Installer and MSIX

**Files:**
- Modify: `build/ObfySetup.iss` (WebView2 runtime check + Evergreen bootstrapper when absent; include `Assets\`), `build/build-msix.ps1` / `package/AppxManifest.xml` (include `Assets\` in the layout), `build/build-installer.ps1` (ensure `dist` built before publish)
- Test: `Tests/Obfy.Tests/MsixPackageTests.cs` (must stay green), add `InstallerWebView2Tests.cs`

**Interfaces:**
- Produces: an Inno installer that silently installs the Evergreen WebView2 runtime when the registry check finds none; MSIX/Inno layouts that carry `Assets\` and no `.pdb`.

- [ ] **Step 1:** Write `InstallerWebView2Tests`: assert `ObfySetup.iss` contains the WebView2 registry check and the bootstrapper `[Run]`/download (static text assertions, `MsixPackageTests` style).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Edit the `.iss` (model on `C:\code\projects\git-diff\build\GitDiffSetup.iss`) and the MSIX layout; confirm `MsixPackageTests` assertions on paths/identity still hold.
- [ ] **Step 4:** Run `dotnet test Tests/Obfy.Tests` → PASS.
- [ ] **Step 5:** Commit (`build: WebView2 runtime in installer, web assets in packages`).

### Task 23: CI and end-to-end tests

**Files:**
- Modify: `.github/workflows/ci.yml` (add Node setup, `npm ci`, `npm run lint`, `npm run test:run` in the Windows job; keep the `Category=UI` step)
- Modify/Create: `Tests/Obfy.UI.AutomationTests/` — replace the FlaUI suite with Playwright-for-.NET driving WebView2's remote-debugging port; keep one FlaUI window-launch/chrome smoke
- Modify: `Tests/Obfy.Tests/CiUiWorkflowTests.cs` (update to match the new CI step while keeping the AutomationTests path + `Category=UI` filter it asserts)

**Interfaces:**
- Produces: CI builds the web app and runs its tests; the `Category=UI` end-to-end suite launches `ObfyUI.exe`, drives the page over the debug port (first-launch screen, toggle a technique, F1 Guide, obfuscate-from-startup-args produces an output file).

- [ ] **Step 1:** Update `CiUiWorkflowTests` for the new workflow shape; run → FAIL (workflow not yet updated).
- [ ] **Step 2:** Edit `ci.yml`; add the Playwright e2e tests + the one FlaUI smoke.
- [ ] **Step 3:** Run `dotnet test Tests/Obfy.Tests` → PASS; run the e2e suite locally (`--filter Category=UI`) → PASS.
- [ ] **Step 4:** Commit (`ci: node build + web tests; Playwright e2e for the hybrid UI`).

### Task 24: Documentation

**Files:**
- Modify: `CLAUDE.md` (rewrite the "UI Application" section + Key Files + tech stack for the hybrid; add a Hybrid-architecture subsection: "IPC: JS → `ApiBridge` (`apibridge`) → `ApiRegistry` → Mediator handlers; events via `ApiEventAggregator`"), `README.md`, `CONTRIBUTING.md`, `docs/Testing.md` (counts + e2e how-to), `backlog.md` (close the UI items the rewrite resolves; note which logic fixes landed)

**Interfaces:**
- Produces: docs describing the shipped hybrid.

- [ ] **Step 1:** Update the docs above; mark `backlog.md` layout items (UI-036/037/041/042/043/046–049/051/057) closed-by-rewrite and the logic items (UI-039/040/044/045/050/052/053/054/055/056/058/059) fixed, with the task that fixed each.
- [ ] **Step 2:** Run the full suite: `dotnet test Obfy.sln` (default filter) and `npm run test:run` → PASS.
- [ ] **Step 3:** `dotnet build Obfy.sln -c Release` → PASS.
- [ ] **Step 4:** Commit (`docs: describe the hybrid UI`).

---

## Self-review notes

- **Spec coverage:** projects (T1), bridge (T3/T4/T14), WPF-free app layer with the five sessions (T9–T13), defect fixes UI-039/040/044/045/050/052/053/054/055/058/059 (T9/T10/T12/T13/T18), typed-path exception (T10 `output.set`), page screens incl. first-launch/empty/running/finished (T15–T19), content-as-data + contract test (T20), build/dev (T6/T7), file-drop spike (T8), installer/MSIX (T22), CI/e2e (T23), docs + backlog closure (T24), behaviour-to-preserve covered by the ported tests (T9–T13). Theme sync (T5). Accessibility/shortcuts (T15).
- **Review Focus:** WebView2 failure (T5), worker-thread event marshalling (T3/T13), technique-without-content (T20), config+inputs drop (T10), watermark empty id (T9/T17) — each pinned above.
- **Proportion:** bodies are signatures/test-names and references to the sibling files whose shapes are copied, not transcribed code.
- **Open builder decisions (from the spec):** exact DTO field names, endpoint-to-file split, component internals/CSS within the tokens, meter values per level, log-batch interval, symbol page size.

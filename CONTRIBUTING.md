# Contributing to Obfy

Thank you for your interest in contributing to Obfy! This document provides guidelines and instructions for contributing.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Git
- [Node.js](https://nodejs.org/) 24 (builds the web UI that `Src/Obfy.UI` hosts; CI uses 24) and [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) (for `scripts/dev.ps1`)
- The [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (Windows; to run the desktop UI)
- A code editor (VS Code, Visual Studio, Rider, etc.)

### Development Setup

1. **Fork and clone the repository**

   ```bash
   git clone https://github.com/YOUR_USERNAME/Obfy.git
   cd Obfy
   ```

2. **Build the solution**

   ```bash
   dotnet build
   ```

   Building `Src/Obfy.UI` also builds the web UI (`npm ci` and `npm run build` in `Src/WebApp`). For .NET-only work, skip the npm steps (the Debug app loads the dev server instead, see below):

   ```bash
   dotnet build -p:SkipFrontendBuild=true
   ```

3. **Run the tests**

   ```bash
   dotnet test
   ```

4. **Run the CLI locally**

   ```bash
   dotnet run --project Src/Obfy.Console -- --help
   ```

5. **Run the desktop UI** (Windows)

   ```powershell
   .\scripts\dev.ps1
   ```

   The script starts the Vite dev server on `http://localhost:5250` (hot reload), then runs `Src/Obfy.UI` with `-p:SkipFrontendBuild=true`; the Debug app loads the dev server and has DevTools. It stops Vite when the app closes and refuses to start if port 5250 is taken. Without the script, `dotnet run --project Src/Obfy.UI -c Release` builds the page and serves it from `Assets\` beside the exe.

## Project Structure

```
Obfy/
├── Src/
│   ├── Obfy.Console/       # CLI application
│   ├── Obfy.Core/          # Core obfuscation logic
│   ├── Obfy.Common/        # JsonUtils (camelCase IPC/config JSON)
│   ├── Obfy.App/           # WPF-free UI logic: sessions + Mediator requests/handlers (Features/<Area>)
│   ├── Obfy.Interop/       # The bridge: ApiBridge, ApiRegistry, ApiRegistrations, IpcNames, events
│   ├── Obfy.WebView/       # WebView2 control + handler
│   ├── Obfy.UI/            # WPF shell (ShellWindow) hosting WebView2
│   ├── WebApp/             # React + Vite + TypeScript page (tests: npm run test:run)
│   ├── Obfy.VisualStudio/  # Visual Studio 2022 extension (C#)
│   ├── Obfy.VSCode/        # VS Code stub (schema + problem matcher)
│   ├── Settings.Core/      # Configuration management
│   └── Logging.Core/       # Logging infrastructure
├── Tests/
│   ├── Obfy.Tests/              # Core unit tests
│   ├── Obfy.Console.Tests/      # CLI parsing tests
│   ├── Obfy.App.Tests/          # Session + handler unit tests
│   ├── Obfy.Interop.Tests/      # Bridge, registry, events, JSON + content contract tests
│   ├── Obfy.UI.Tests/           # Shell wiring, startup command line
│   ├── Obfy.ScenarioTests/      # SDK fixtures + platform (Blazor/AOT/MAUI/Unity)
│   ├── Obfy.VisualStudio.Tests/ # VS helpers without a hive
│   └── Obfy.UI.AutomationTests/ # Locator tests + Playwright-over-WebView2 e2e + one FlaUI smoke (Category=UI)
├── scripts/                # dev.ps1
├── docs/                   # Documentation
└── examples/               # Example projects
```

Work on a feature branch in an isolated git worktree, not on the main checkout:

```bash
git worktree add .worktrees/<branch-name> -b <branch-name>
```

## Making Changes

### Branch Naming

- `feature/description` - New features
- `fix/description` - Bug fixes
- `docs/description` - Documentation updates
- `refactor/description` - Code refactoring

### Commit Messages

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
type(scope): description

[optional body]

[optional footer]
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`

Examples:
- `feat(encryption): add ChaCha20 algorithm support`
- `fix(renaming): preserve interface implementations`
- `docs: update CLI reference`

### Code Style

- Use C# coding conventions
- Enable nullable reference types
- Write XML documentation for public APIs
- Keep methods focused and small

### Testing

We have eight .NET test projects plus the web app's Vitest suite:

| Project | Purpose | Tests |
|---------|---------|-------|
| `Obfy.Tests` | Core obfuscation logic | see [docs/Testing.md](docs/Testing.md) |
| `Obfy.Console.Tests` | CLI argument parsing, help output | see [docs/Testing.md](docs/Testing.md) |
| `Obfy.App.Tests` | UI sessions (protection, input, log, results, run) and endpoint handlers | see [docs/Testing.md](docs/Testing.md) |
| `Obfy.Interop.Tests` | Bridge, registry, events, request JSON and technique-content contracts | see [docs/Testing.md](docs/Testing.md) |
| `Obfy.UI.Tests` | Shell wiring, startup command line, host service | see [docs/Testing.md](docs/Testing.md) |
| `Obfy.ScenarioTests` | SDK fixtures (examples, WPF, console, WinForms, MSBuild, Unity) + platform | see [docs/Testing.md](docs/Testing.md) |
| `Obfy.VisualStudio.Tests` | VS JSON / CLI args / in-place copy / output path (no hive) | see [docs/Testing.md](docs/Testing.md) |
| `Obfy.UI.AutomationTests` | Locator unit tests + Playwright-over-WebView2 e2e + one FlaUI smoke (`Category=UI`) | see [docs/Testing.md](docs/Testing.md) |
| `Src/WebApp` (Vitest) | Page components, screens, interop client, content | `npm run test:run` |

```bash
# Default suite (skips Category=UI and Category=Platform)
dotnet test

# Platform recipes (Blazor WASM, NativeAOT; MAUI skips without the workload)
dotnet test Tests/Obfy.ScenarioTests --filter Category=Platform

# Run specific test project
dotnet test Tests/Obfy.Tests
dotnet test Tests/Obfy.Console.Tests
dotnet test Tests/Obfy.App.Tests
dotnet test Tests/Obfy.Interop.Tests
dotnet test Tests/Obfy.UI.Tests
dotnet test Tests/Obfy.ScenarioTests
dotnet test Tests/Obfy.VisualStudio.Tests
dotnet test Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj -c Release --filter Category=UI   # build Src/Obfy.UI -c Release first

# Web app tests and lint
cd Src/WebApp
npm run test:run
npm run lint

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

When adding new features:
- CLI changes → Add tests to `Obfy.Console.Tests`
- UI behaviour (what a button does, what a setting implies) → a session in `Obfy.App/Sessions`, with tests in `Obfy.App.Tests`. The page only renders snapshots
- Page changes → Vitest tests next to the component (`Src/WebApp/src/**/__tests__`)
- Core obfuscation → Add tests to `Obfy.Tests`

See [docs/Testing.md](docs/Testing.md) for how to run tests. Scenario fixtures live in `Obfy.ScenarioTests`; remaining platform/IDE gaps are in [docs/Testing-Roadmap.md](docs/Testing-Roadmap.md).

## Working on the Desktop UI

The UI is a WPF shell hosting WebView2 and a React page (`Src/WebApp`); see the Hybrid architecture section of [CLAUDE.md](CLAUDE.md). Run it with `.\scripts\dev.ps1`. Use `-p:SkipFrontendBuild=true` for .NET-only builds and tests that do not need the page.

### Adding a bridge endpoint

The page talks to the host through the `apibridge` host object (`JS -> ApiBridge -> ApiRegistry -> Mediator handler`). To add a call:

1. **Request and handler** in `Src/Obfy.App/Features/<Area>/`: a Mediator request record and its handler. Put the rule in a session (`Src/Obfy.App/Sessions`), not the handler. Throw `ObfyApiException` for failures the user can act on.
2. **Register it** in `Src/Obfy.Interop/ApiRegistrations.cs`, with the service and method names added to `IpcNames`. If the host pushes something new, add the event name to `IpcNames.Events` and publish it through `IEventPublisher`. DTOs are camelCase through `JsonUtils`.
3. **Call it from the page**: add a typed wrapper to `Src/WebApp/src/interop/api.ts` (and the types and mock bridge it needs), then use it from a hook or component. The page never supplies a path or a URL; `app.openGuide` is the pattern for opening a web address: the page sends a section id and the host builds an `https` URL from the `GuideLinks` allowlist (`Src/Obfy.App/Features/App/GuideLinks.cs`).
4. **Tests**: handler and session behaviour in `Tests/Obfy.App.Tests`; registration and JSON shape in `Tests/Obfy.Interop.Tests` (`ApiRegistrationsTests`, `RequestJsonContractTests`); the page side in Vitest.

User-facing copy for techniques lives in `Src/WebApp/src/content/techniques.json`, and the in-app Guide (`guide.json`) is a short quick-start: Getting started and Shortcuts. The full guide is online at https://brudvik.com/obfy/guide. `TechniqueContentContractTests` fails when a technique id or option key in the host has no matching entry, so a new technique needs its entry in `techniques.json`, and when a `guideTopic` there is not a section id in `GuideLinks.Topics`. A section added to the online guide is linked by adding its id to `GuideLinks.Topics`, the one place that list lives.

## Pull Request Process

1. **Create a feature branch** from `main`
2. **Make your changes** with appropriate tests
3. **Ensure all tests pass**
4. **Update documentation** if needed
5. **Submit a pull request** with a clear description

### PR Checklist

- [ ] Code follows project style guidelines
- [ ] Tests added for new functionality
- [ ] All tests pass
- [ ] Documentation updated if needed
- [ ] CHANGELOG.md updated for notable changes

## Reporting Issues

### Bug Reports

Use the [bug report template](.github/ISSUE_TEMPLATE/bug_report.md) and include:
- Obfy version
- Operating system and .NET version
- Steps to reproduce
- Expected vs actual behavior
- Error messages or stack traces

### Feature Requests

Use the [feature request template](.github/ISSUE_TEMPLATE/feature_request.md) and describe:
- The problem you're trying to solve
- Your proposed solution
- Use cases and examples

## Getting Help

- Check the [documentation](docs/)
- Search [existing issues](https://github.com/mortenbrudvik/Obfy/issues)
- Open a new issue if needed

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

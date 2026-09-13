# Contributing to Obfy

Thank you for your interest in contributing to Obfy! This document provides guidelines and instructions for contributing.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Git
- A code editor (VS Code, Visual Studio, Rider, etc.)
- [Java 21](https://adoptium.net/) (required for Rider plugin development only)

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

3. **Run the tests**

   ```bash
   dotnet test Obfy.sln --filter "Category!=UI&Category!=Platform"
   ```

4. **Run the CLI locally**

   ```bash
   dotnet run --project Src/Obfy.Console -- --help
   ```

5. **Build the Rider plugin** (optional, requires Java 21)

   ```bash
   cd Src/Obfy.Rider
   ./gradlew.bat build
   ```

   The plugin ZIP will be created at `build/distributions/Obfy.Rider-1.0.1.zip`.

## Project Structure

```
Obfy/
├── Src/
│   ├── Obfy.Console/       # CLI application
│   ├── Obfy.Core/          # Core obfuscation logic
│   ├── Obfy.UI/            # WPF desktop application
│   ├── Obfy.VisualStudio/  # Visual Studio 2022 extension (C#)
│   ├── Obfy.Rider/         # JetBrains Rider plugin (Kotlin)
│   ├── Obfy.VSCode/        # VS Code stub (schema + problem matcher)
│   ├── Settings.Core/      # Configuration management
│   └── Logging.Core/       # Logging infrastructure
├── Tests/
│   ├── Obfy.Tests/              # Core unit tests (382 tests)
│   ├── Obfy.Console.Tests/      # CLI parsing tests (117 tests)
│   ├── Obfy.UI.Tests/           # ViewModel unit tests (120 tests)
│   └── Obfy.UI.AutomationTests/ # FlaUI live-window tests (18 tests)
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

We have four test projects:

| Project | Purpose | Tests |
|---------|---------|-------|
| `Obfy.Tests` | Core obfuscation logic | 382 |
| `Obfy.Console.Tests` | CLI argument parsing, help output | 117 |
| `Obfy.UI.Tests` | ViewModel logic and commands | 120 |
| `Obfy.UI.AutomationTests` | FlaUI live window (local desktop) | 18 |

```bash
# Default (same as CI; excludes FlaUI)
dotnet test Obfy.sln --filter "Category!=UI&Category!=Platform"

# Run specific test project
dotnet test Tests/Obfy.Tests
dotnet test Tests/Obfy.Console.Tests
dotnet test Tests/Obfy.UI.Tests

# FlaUI (local desktop): build UI first, same configuration
dotnet build Src/Obfy.UI/Obfy.UI.csproj && dotnet test Tests/Obfy.UI.AutomationTests --filter Category=UI

# Run with coverage
dotnet test Obfy.sln --filter "Category!=UI&Category!=Platform" --collect:"XPlat Code Coverage"
```

When adding new features:
- CLI changes → Add tests to `Obfy.Console.Tests`
- ViewModel changes → Add tests to `Obfy.UI.Tests`
- Core obfuscation → Add tests to `Obfy.Tests`

See [docs/Testing.md](docs/Testing.md) for how to run tests, and [docs/Testing-Roadmap.md](docs/Testing-Roadmap.md) for planned scenario coverage.

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

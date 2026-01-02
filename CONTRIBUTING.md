# Contributing to Obfy

Thank you for your interest in contributing to Obfy! This document provides guidelines and instructions for contributing.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Git
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

3. **Run the tests**

   ```bash
   dotnet test
   ```

4. **Run the CLI locally**

   ```bash
   dotnet run --project Src/Obfy.Console -- --help
   ```

## Project Structure

```
Obfy/
├── Src/
│   ├── Obfy.Console/       # CLI application
│   ├── Obfy.Core/          # Core obfuscation logic
│   ├── Settings.Core/      # Configuration management
│   └── Logging.Core/       # Logging infrastructure
├── Tests/
│   ├── Obfy.Tests/         # Core unit tests (152 tests)
│   ├── Obfy.Console.Tests/ # CLI parsing tests (92 tests)
│   └── Obfy.UI.Tests/      # ViewModel unit tests (59 tests)
├── docs/                   # Documentation
└── examples/               # Example projects
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

We have three test projects:

| Project | Purpose | Tests |
|---------|---------|-------|
| `Obfy.Tests` | Core obfuscation logic | 152 |
| `Obfy.Console.Tests` | CLI argument parsing, help output | 92 |
| `Obfy.UI.Tests` | ViewModel logic and commands | 59 |

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test Tests/Obfy.Tests
dotnet test Tests/Obfy.Console.Tests
dotnet test Tests/Obfy.UI.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

When adding new features:
- CLI changes → Add tests to `Obfy.Console.Tests`
- ViewModel changes → Add tests to `Obfy.UI.Tests`
- Core obfuscation → Add tests to `Obfy.Tests`

See [docs/Testing.md](docs/Testing.md) for comprehensive testing guidelines.

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

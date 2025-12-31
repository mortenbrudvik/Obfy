# Publish Command

Build and package Obfy for release.

## Workflow

1. Determine version bump (major/minor/patch)
2. Update version in project files
3. Update CHANGELOG.md with release date
4. Build release configuration
5. Run all tests
6. Create git tag
7. Push tag to remote

## Arguments

- `[major|minor|patch]`: Version bump type (default: analyze commits)

## Instructions

1. Analyze commits since last tag to determine version bump:
   - `feat:` = minor
   - `fix:` = patch
   - `BREAKING CHANGE:` = major

2. Update version in Obfy.Console.csproj and Obfy.Core.csproj

3. Update CHANGELOG.md:
   - Move Unreleased to new version section
   - Add release date

4. Build release:
```bash
dotnet build Obfy.sln -c Release
```

5. Run tests:
```bash
dotnet test Obfy.sln -c Release
```

6. If tests pass, create tag:
```bash
git add -A
git commit -m "chore: release v<version>"
git tag v<version>
git push && git push --tags
```

## Output Location

Release binaries: `Src/Obfy.Console/bin/Release/net10.0-windows/`

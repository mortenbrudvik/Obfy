# Git Commit Command

Automate git commits with conventional commit messages, documentation updates, and changelog updates.

## Workflow

1. Review staged changes with `git diff --cached`
2. Review recent commits for message style
3. Draft a conventional commit message
4. Review if documentation needs updating (CLAUDE.md, README.md, docs/)
5. Update CHANGELOG.md if appropriate
6. Create the commit
7. Push to remote

## Commit Message Format

```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

Types:
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation
- `refactor`: Code refactoring
- `test`: Adding tests
- `chore`: Maintenance

## Changelog Mapping

| Type | Changelog Section |
|------|-------------------|
| feat | Added |
| fix | Fixed |
| docs | Changed |
| refactor | Changed |

## Documentation Updates

When committing changes, check if these files need updating:

| Change Type | Files to Update |
|-------------|-----------------|
| New project/module | CLAUDE.md (structure), README.md |
| New feature | README.md, relevant docs/*.md |
| New CLI command/option | docs/CLI.md |
| Config changes | docs/Configuration.md |
| API/interface changes | CLAUDE.md (key files) |
| Build/run command changes | CLAUDE.md (build commands) |

**Always stage documentation updates before committing.**

## Instructions

1. Run `git status` and `git diff --cached` to see changes
2. Run `git log --oneline -5` to see recent commit style
3. Draft commit message following the format above
4. **Check if documentation needs updating based on the changes:**
   - New projects/modules → Update CLAUDE.md structure and README.md
   - New features → Update README.md and relevant docs/
   - CLI changes → Update docs/CLI.md
   - Config changes → Update docs/Configuration.md
   - Stage any documentation updates
5. If feat/fix, add entry to CHANGELOG.md under Unreleased
6. Commit with:
```bash
git commit -m "$(cat <<'EOF'
<message>

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
```
7. Push: `git push`

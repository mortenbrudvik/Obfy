# Git Commit Command

Automate git commits with conventional commit messages and changelog updates.

## Workflow

1. Review staged changes with `git diff --cached`
2. Review recent commits for message style
3. Draft a conventional commit message
4. Update CHANGELOG.md if appropriate
5. Create the commit
6. Push to remote

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

## Instructions

1. Run `git status` and `git diff --cached` to see changes
2. Run `git log --oneline -5` to see recent commit style
3. Draft commit message following the format above
4. If feat/fix, add entry to CHANGELOG.md under Unreleased
5. Commit with:
```bash
git commit -m "$(cat <<'EOF'
<message>

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
```
6. Push: `git push`

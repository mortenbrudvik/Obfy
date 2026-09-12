# Obfy for Visual Studio Code

Stub extension: JSON schema validation for `obfy.json` and a problem matcher for CLI `Error:` / `⚠` lines. There is no TaskProvider; use a shell task.

## Setup

1. Install the Obfy CLI (`obfy` on PATH).
2. Schema validation uses the GitHub URL contributed in `package.json` (or set `"$schema"` in `obfy.json`).
3. Add a shell task:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "Obfy: obfuscate",
      "type": "shell",
      "command": "obfy",
      "args": ["MyApp.dll", "-c", "obfy.json", "-o", "obfuscated/"],
      "problemMatcher": ["$obfy"]
    }
  ]
}
```

`obfy.json` / `*.obfy.json` get schema validation via the contributed JSON schema.

Platform recipes: see `docs/Unity.md` and `docs/Platforms.md` in the Obfy repo.

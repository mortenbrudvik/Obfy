# Obfy for Visual Studio Code

Obfuscate from the CLI without the WPF app.

## Setup

1. Install the Obfy CLI (`obfy` on PATH).
2. Copy `schemas/obfy.schema.json` next to this extension (or keep using the GitHub `$schema` URL in `obfy.json`).
3. Add a task:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "Obfy: obfuscate",
      "type": "shell",
      "command": "obfy",
      "args": ["${input:obfyInput}", "-c", "obfy.json", "-o", "obfuscated/"],
      "problemMatcher": ["$obfy"]
    }
  ]
}
```

`obfy.json` / `*.obfy.json` get schema validation via the contributed JSON schema.

Platform recipes: see `docs/Unity.md` and `docs/Platforms.md` in the Obfy repo.

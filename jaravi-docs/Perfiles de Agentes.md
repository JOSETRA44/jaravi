---
tags: [jaravi, agentes, perfiles, agents-json, close-stdin]
---

# Perfiles de Agentes

Los subagentes que Jaravi puede lanzar se definen de forma **declarativa** en `agents.json`. Añadir un nuevo CLI es un cambio de configuración, nunca de código.

## Estructura de agents.json

```jsonc
{
  "agents": [
    {
      "id": "opencode",
      "description": "OpenCode CLI como subagente",
      "command": "C:\\ruta\\al\\binario\\opencode.exe",
      "args": ["run", "{task}"],
      "unattendedArgs": [],
      "env": { "CI": "true" },
      "envAllowlist": ["OPENCODE_MODEL"],
      "closeStdin": true,
      "io": "pipe",
      "idleTimeoutSeconds": 300
    }
  ]
}
```

### Campos clave

| Campo | Descripción |
|---|---|
| `command` | Ejecutable (ruta absoluta o en PATH). Usar el binario real, no el shim `.cmd` de npm |
| `args` | Argumentos con placeholders `{task}` y `{workdir}` |
| `unattendedArgs` | Flags adicionales cuando `unattended=true` (ej: `--dangerously-skip-permissions`) |
| `env` | Variables de entorno siempre inyectadas |
| `envAllowlist` | Variables que el llamador puede sobrescribir |
| `closeStdin` | Cierra stdin al lanzar — crítico para CLIs one-shot |
| `io` | `"pipe"` (actual) o `"pty"` (post-MVP, ConPTY) |

## Lección: closeStdin

Los CLIs one-shot como `opencode run`, `claude -p` y `copilot -p` leen de stdin con pipes hasta recibir EOF. Sin `closeStdin: true`, el proceso se cuelga bloqueado esperando más entrada antes de producir una sola línea de output.

> [!warning] Siempre probar con el binario real
> Los shims `.cmd` de npm (ej: `opencode.cmd`) destruyen argumentos multilínea. Apuntar directamente al ejecutable: `C:\Users\USER\AppData\Roaming\npm\node_modules\opencode-ai\bin\opencode.exe`.

## Perfiles incluidos

- **echo-demo** — agente de demostración sin dependencias (cmd.exe)
- **flood-demo** — estrés: 50 000 líneas para probar el ring buffer
- **claude** — Claude Code headless con `-p`
- **copilot** — GitHub Copilot CLI
- **antigravity** — Antigravity CLI (agy)
- **opencode** — OpenCode CLI (líder open-source, 180k+ estrellas)
- **codex** — Codex CLI de OpenAI, invocado vía `node` directo (el shim `.cmd` también pasa por `cmd.exe`); #1 en Terminal-Bench 2.1 (83.4%) según [[Investigacion de Mercado]]

Véase también: [[Motor (Engine)|Motor]], [[Servidor MCP]], [[Operacion]]

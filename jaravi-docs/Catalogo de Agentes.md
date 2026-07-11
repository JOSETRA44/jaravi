---
tags: [jaravi, agentes, catalogo, compatibilidad, cli]
---

# Catálogo de Agentes Soportados

Jaravi aspira a controlar **cualquier** CLI de agente que tenga un modo
no-interactivo. Esta es la matriz de los perfiles verificados en esta máquina
(jul-2026, v0.3.2). Agregar uno nuevo es una entrada en `agents.json` +
`reload_agents` — sin reiniciar el servidor. Ver [[Perfiles de Agentes]].

## Matriz de compatibilidad

| Perfil | CLI | Invocación no-interactiva | Estado |
|---|---|---|---|
| `claude` | Claude Code | `claude -p {task} --dangerously-skip-permissions` | ✅ verificado |
| `codex` | OpenAI Codex | `node codex.js exec {task} --full-auto` | ✅ pong exit 0 |
| `opencode` | OpenCode | `opencode.exe run {task}` | ✅ auditoría real |
| `gemini` | Google Gemini CLI | `node gemini.js -p {task} --approval-mode yolo` | ✅ pong exit 0 |
| `qwen` | Qwen Code | `node cli-entry.js -p {task} --approval-mode yolo` | ⚠️ perfil correcto; falla por modelo de pago (Jaravi capturó el 404) |
| `copilot` | GitHub Copilot CLI | `node npm-loader.js -p {task} --allow-all-tools` | 🔵 shape conocido, misma familia node |
| `deepcode` | Deep Code CLI | `node cli.js -p {task}` | 🔵 shape conocido |
| `mimo` | Mimo (mimocode) | `node bin/mimo run {task}` | 🔵 shape conocido, fork de OpenCode |
| `antigravity` | Antigravity (agy) | `agy.exe --print {task}` | ❌ no-op silencioso (sin autenticar) |
| `echo-demo`, `flood-demo` | — | demos internos (cmd.exe) | ✅ pruebas |

Leyenda: ✅ ejecutado end-to-end por Jaravi · ⚠️ perfil correcto, bloqueo
externo · 🔵 misma familia de invocación probada, no ejecutado individualmente
· ❌ inservible en esta máquina hasta configurar.

## Patrón universal descubierto

Todos los CLIs de agentes modernos comparten la misma anatomía, lo que hace a
Jaravi genuinamente universal:

1. **Node bajo el capó**: casi todos son paquetes npm. Invocar `node <entry.js>`
   directamente evita el shim `.cmd` que pasa por `cmd.exe` y destroza briefs
   multilínea. Ver [[Perfiles de Agentes]].
2. **Modo one-shot con `-p`/`run`**: leen la tarea y salen. Requieren
   `closeStdin: true` o se cuelgan esperando EOF.
3. **Auto-aprobación**: cada uno tiene su flag (`--yolo`/`--approval-mode yolo`
   en gemini/qwen, `--full-auto` en codex, `--allow-all-tools` en copilot,
   `--dangerously-skip-permissions` en claude). Va en `unattendedArgs`.
4. **Skills interoperables**: gemini, qwen, deepcode y codex leen
   `~/.agents/skills/` — el mismo store universal donde vive
   [[Perfiles de Agentes|jaravi-orchestrator]]. Un sub-agente puede así heredar
   la doctrina del jefe.

## Por qué `reload_agents` es la pieza de universalidad

Antes, agregar un agente exigía reiniciar el servidor — y en stdio eso mata la
conexión del jefe. La tool **`reload_agents`** (v0.3.2) re-lee `agents.json` en
vivo: cualquiera deja caer un perfil nuevo y lo usa al instante. Un archivo mal
formado se rechaza sin tumbar el catálogo activo. Esto convierte "soporta estos
6 CLIs" en "soporta cualquier CLI que instales". Ver [[Servidor MCP]].

Véase también: [[Perfiles de Agentes]], [[Servidor MCP]], [[Operacion]], [[Home|Jaravi]]

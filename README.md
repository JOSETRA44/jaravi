# Jaravi — Ecosistema de Orquestación de Sub-Agentes

Jaravi convierte a un agente de IA de alto nivel (Claude Code o cualquier
cliente MCP) en el **jefe determinista de sub-agentes externos** (Claude Code
headless, OpenCode, Copilot CLI, cualquier CLI). El motor absorbe todo el
output de los subprocesos y le entrega al jefe solo respuestas compactas;
el Dashboard observa el firehose completo en tiempo real.

## Arquitectura (Clean Architecture)

```
Agente Jefe ◄── MCP (Streamable HTTP /mcp) ──► Jaravi.McpServer (Kestrel)
Dashboard  ◄── WebSocket /ws/events + REST ──►        │
                                                Jaravi.Engine ──► subprocesos
Referencias: McpServer → Engine → Core  |  Dashboard → Core (solo DTOs)
```

| Proyecto | Rol |
|---|---|
| `Jaravi.Core` | Dominio puro: modelos, eventos, puertos. Cero dependencias. |
| `Jaravi.Engine` | Motor: procesos (pipe I/O), SessionManager, event bus, ring buffer de logs, Scope Gate, sanitizador ANSI. |
| `Jaravi.McpServer` | Host headless: tools MCP + WebSocket de telemetría + REST. |
| `Jaravi.Dashboard` | GUI WPF (MVVM) observadora; solo consume HTTP/WS. |
| `Jaravi.Engine.Tests` | xUnit: 35 tests incluyendo E2E contra procesos reales. |

## Uso rápido

```bash
dotnet run --project Jaravi.McpServer   # MCP en http://localhost:5210/mcp
dotnet run --project Jaravi.Dashboard   # GUI observadora (o el .exe compilado)
dotnet test                             # suite completa
```

Claude Code se conecta automáticamente vía `.mcp.json`. La skill
`.claude/skills/jaravi-orchestrator` enseña al agente jefe su rol.

## Tools MCP

`list_agents`, `spawn_agent`, `send_input`, `get_status`, `list_sessions`,
`read_output` (capado a 500 líneas server-side), `await_session`,
`get_summary`, `kill_agent`.

## Garantías anti-colapso de contexto

- **Ring buffer** por sesión (10 000 líneas) + tope duro de lectura (500).
- **Scope Gate**: workdir validado contra `Engine:AllowedRoots` (appsettings.json).
- **Deadline duro** por sesión con kill del árbol de procesos.
- **Logs sanitizados** (sin secuencias ANSI) y eventos JSON polimórficos.

## Agregar un sub-agente nuevo

Entrada declarativa en `Jaravi.McpServer/agents.json` — sin código:

```jsonc
{
  "id": "mi-cli",
  "command": "mi-cli.cmd",
  "args": ["run", "{task}"],          // placeholders: {task}, {workdir}
  "unattendedArgs": ["--yes"],        // inyectados si unattended=true
  "env": { "CI": "true" },
  "io": "pipe"                        // "pty" reservado para ConPTY (post-MVP)
}
```

## Roadmap

- `ConPtyIoStrategy` (ConPTY) detrás de `IAgentProcessFactory` + teclas
  simbólicas en `send_input` para CLIs que exigen TTY real.
- Persistencia NDJSON opcional de logs por sesión.

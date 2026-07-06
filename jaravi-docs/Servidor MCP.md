---
tags: [jaravi, mcp, servidor, kestrel, tools, websocket]
---

# Servidor MCP

`Jaravi.McpServer` es el host Kestrel que expone el [[Motor (Engine)|Engine]] al exterior. Ofrece tres superficies de comunicación.

## Modos de operación

- **`--stdio`**: MCP sobre stdin/stdout. Claude Code (u otro cliente MCP) lanza el servidor como hijo. Los logs van a stderr. Kestrel igualmente corre para el [[Dashboard]] (WebSocket + REST), usando puerto efímero si el 5210 está ocupado.
- **HTTP** (por defecto): MCP sobre HTTP en `http://localhost:5210/mcp`.

Ambos modos coexisten: en stdio, el WebSocket y REST siguen activos.

## Las 9 Tools MCP

| Tool | Descripción |
|---|---|
| `list_agents` | Lista los perfiles de subagentes disponibles |
| `spawn_agent` | Lanza un subagente; devuelve `sessionId` inmediatamente |
| `send_input` | Envía texto a stdin y/o teclas simbólicas (PTY) |
| `get_status` | Estado compacto: estado, uptime, exit code, últimas 5 líneas |
| `list_sessions` | Todas las sesiones con su estado actual |
| `read_output` | Output acotado (máx. 500 líneas server-side) con `tail`, `grep`, `sinceSeq` |
| `await_session` | Bloquea hasta que la sesión termine o necesite input |
| `get_summary` | Digest compacto: exit code, duración, errores extraídos |
| `kill_agent` | Mata todo el árbol de procesos de una sesión |

> [!warning] read_output tiene un hard cap de 500 líneas
> El parámetro `maxLines` está limitado por `Engine:MaxReadLines` (500). El agente jefe nunca puede inundar su contexto.

## Endpoints

| Ruta | Propósito |
|---|---|
| `GET /mcp` | Endpoint MCP (modo HTTP) |
| `WS /ws/events` | Stream JSON polimórfico de eventos (`SessionStarted`, `SessionStateChanged`, `LogBatchEmitted`, `SessionExited`) |
| `GET /api/agents` | Lista de perfiles |
| `GET /api/sessions` | Lista de sesiones |
| `GET /api/sessions/{id}` | Snapshot de una sesión |
| `GET /api/sessions/{id}/logs` | Logs paginados |
| `GET /api/sessions/{id}/summary` | Resumen compacto |
| `POST /api/sessions` | Crear sesión (spawn) |
| `POST /api/sessions/{id}/kill` | Matar sesión |
| `POST /api/sessions/{id}/input` | Enviar input |
| `GET /healthz` | Health check |

## Eventos WebSocket

El endpoint `/ws/events` emite JSON con discriminador `type`:

- `sessionStarted` — nueva sesión creada
- `sessionStateChanged` — transición de estado
- `logBatchEmitted` — lote de líneas de output
- `sessionExited` — sesión terminada con código de salida

Véase también: [[Operacion]], [[Perfiles de Agentes]]

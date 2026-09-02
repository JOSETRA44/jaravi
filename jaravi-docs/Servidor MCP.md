---
tags: [jaravi, mcp, servidor, kestrel, tools, websocket]
---

# Servidor MCP

`Jaravi.McpServer` es el host Kestrel que expone el [[Motor (Engine)|Engine]] al exterior. Ofrece tres superficies de comunicación **y**, desde v0.9.0, un [[CLI de Shell|CLI]] completo en el mismo binario para quien no puede llegar por MCP.

## Modos de operación

- **`--stdio`**: MCP sobre stdin/stdout. Claude Code (u otro cliente MCP) lanza el servidor como hijo. Los logs van a stderr. Kestrel igualmente corre para el [[Dashboard]] (WebSocket + REST), usando puerto efímero si el 5210 está ocupado.
- **HTTP** (por defecto): MCP sobre HTTP en `http://localhost:5210/mcp`.

Ambos modos coexisten: en stdio, el WebSocket y REST siguen activos. Eso es lo que permite que el [[CLI de Shell|CLI]] se **adjunte** a una instancia stdio y comparta sus sesiones.

- **verbos de CLI** (`run`, `agents`, `doctor`…): ni MCP ni servidor; ver [[CLI de Shell]].

## Las 11 Tools MCP

| Tool | Descripción |
|---|---|
| `list_agents` | Lista los perfiles de subagentes disponibles |
| `reload_agents` | **v0.3.2**: re-lee `agents.json` en vivo — agrega/edita agentes sin reiniciar el servidor. Ver [[Catalogo de Agentes]] |
| `spawn_agent` | Lanza un subagente; devuelve `sessionId` inmediatamente |
| `run_agent` | **v0.3**: spawn + await + summary en una sola llamada — la vía token-eficiente para delegar-y-recoger |
| `send_input` | Envía texto a stdin y/o teclas simbólicas (PTY) |
| `get_status` | Estado compacto: estado, uptime, exit code, últimas 5 líneas |
| `list_sessions` | Todas las sesiones con su estado actual (incluye `queuedBehind` — v0.3.1) |
| `read_output` | Output acotado (máx. 500 líneas server-side) con `tail`, `grep`, `sinceSeq` |
| `await_session` | Bloquea hasta que la sesión termine o necesite input |
| `get_summary` | Digest compacto: exit code, duración, errores extraídos |
| `kill_agent` | Mata todo el árbol de procesos de una sesión |

> [!warning] read_output tiene un hard cap de 500 líneas
> El parámetro `maxLines` está limitado por `Engine:MaxReadLines` (500). El agente jefe nunca puede inundar su contexto.

## Ciudadanía MCP completa (v0.6.0)

Tras un ciclo de feedback de un consumidor externo (ver [[Brechas del Protocolo MCP]]), las 11 tools llevan **anotaciones** (`readOnlyHint`/`destructiveHint`/`idempotentHint`/`openWorldHint`) para que cualquier cliente sepa cuáles son seguras sin preguntar — `kill_agent` es la única marcada `destructiveHint:true`. `run_agent` y `await_session` reportan **progreso real** cada 5s mientras esperan (`IProgress<ProgressNotificationValue>`, no-op si el cliente no pidió progreso vía `_meta.progressToken`) — verificado en vivo con 8 notificaciones reales fluyendo durante una espera de 30s. `run_agent` también mata la sesión si el propio cliente cancela la llamada (`notifications/cancelled`), evitando huérfanos — aunque el SDK preview no propaga esa cancelación en modo stdio (ver la nota de brechas para el detalle).

## Orquestación avanzada en `spawn_agent` (v2)

- **Pipelines**: `inputFromSessionId` + `inputKind` (`summary`|`tail`|`errors`) —
  el [[Motor (Engine)|motor]] inyecta el resultado de una sesión **terminada**
  en el task de la nueva. Parámetros extra: `inputTailLines` (cap 100), `inputGrep`.
- **Claims**: `claims: ["src/Auth/**"]` reclama rutas en exclusiva;
  `onConflict: "reject"` (default) falla con la sesión en conflicto,
  `onConflict: "queue"` deja la sesión en estado `Queued` y el motor la arranca
  solo cuando el claim se libera. La respuesta incluye `queuedBehind`.

> [!note] La sesión origen de un pipeline debe estar terminada
> Su output es inmutable en estado terminal — eso hace la inyección determinista.

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
| `GET /` | [[Control Center (Web)]] — dashboard web embebido |
| `GET /swagger` | UI de OpenAPI (solo modo HTTP) — v0.5.0 |
| `GET /swagger/v1/swagger.json` | Documento OpenAPI de la REST, para scripting sin JSON-RPC |
| `GET /api/instances` | Instancias vivas de jaravi-mcp (multi-repo, poda PIDs muertos) |
| `GET /healthz` | Health check |

## Eventos WebSocket

El endpoint `/ws/events` emite JSON con discriminador `type`:

- `sessionStarted` — nueva sesión creada
- `sessionStateChanged` — transición de estado
- `logBatchEmitted` — lote de líneas de output
- `sessionExited` — sesión terminada con código de salida

Véase también: [[Operacion]], [[Perfiles de Agentes]], [[Brechas del Protocolo MCP]], [[Pruebas de Contrato MCP]], [[Adopcion por Agentes]], [[CLI de Shell]]

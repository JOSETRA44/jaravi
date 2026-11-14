---
tags: [jaravi, teams, arquitectura, hub, acp]
---

# Jaravi Teams

Equipos de agentes **de cualquier proveedor** que se saben parte de un equipo,
se hablan directamente, comparten tablero y decisiones, y se revisan entre
ellos. Construido sobre el [[Motor (Engine)]] y el [[Servidor MCP]]; los
mecanismos de entrega por agente están medidos en [[Matriz de Runtimes]].

## El problema real (más allá del diagnóstico inicial)

"Los agentes no saben que están en un equipo" era cierto, pero el límite de
fondo era que **no existía ningún canal por el que pudieran enterarse**:

- Todos los perfiles eran one-shot (`-p`, `exec`, `run`) con `closeStdin`: tras
  el prompt inicial no se podía entregar nada.
- El hijo no recibía identidad. Si tenía el MCP de Jaravi configurado, abría
  **otra** instancia aislada y actuaba como un desconocido.
- Cada cliente MCP lanzaba su propio motor en memoria: no había un mundo
  compartido, y todo moría con la ventana.

## Las cuatro piezas

### 1. Hub por usuario

`jaravi-mcp hub` es un daemon por usuario del SO:

- Escucha solo en `127.0.0.1` y exige token (cabecera, cookie o `?token=`).
- Rechaza cualquier `Host` que no sea loopback, como defensa contra DNS rebinding.
- Es dueño de todos los procesos y del estado durable (`%APPDATA%\jaravi\hub.db`, SQLite WAL).

Cómo se usa:

- **Arranque automático.** El `jaravi-mcp --stdio` que lanza cualquier cliente
  MCP es ahora un **relay** (stdio ↔ Streamable HTTP) hacia el hub, y lo
  arranca si no está corriendo.
- **Desacoplado.** Arranca fuera del job object del padre, para que un cliente
  que mata su árbol de procesos no se lleve el equipo.
- **Corre desde su propia copia** (`%LOCALAPPDATA%\jaravi\hub-bin\<build>`),
  así nunca bloquea `dotnet tool update` ni un rebuild.
- **Fallback.** Si el hub no puede arrancar, el cliente se sirve con un motor
  privado, como antes, y stderr dice por qué. `JARAVI_HUB=off` o `--local` lo
  fuerzan.
- **Scope Gate por llamante.** El relay envía su cwd (`X-Jaravi-Cwd`), y ese
  directorio es la concesión extra del llamante. Equivale exactamente a lo que
  antes daba "el server arrancado en tu proyecto".

### 2. Dominio de equipo (`Jaravi.Core/Teams`, `Jaravi.Engine/Teams`)

- **Miembros con identidad.** Token `jtm_…` guardado como hash. Cada miembro
  tiene rol, origen (spawned/joined) y **nivel de entrega**, y su presencia se
  deriva de la sesión o de la última actividad.
- **Mensajes con recibo por destinatario:**
  - `stored`: en el hub;
  - `delivered`: Jaravi lo puso en el contexto del agente (vía `live-turn`, `hook` o `pull`);
  - `acknowledged`: el agente respondió con `replyTo` o hizo ack.

  El recibo avanza solo hacia delante.
- **Direcciones:** nombre, `role:<rol>`, `lead`, `all`, o una mezcla separada por comas.
- **Tareas:**
  - dependencias: bloquean `task_claim` y avisan al dueño cuando se desbloquean;
  - concurrencia optimista (`version`);
  - leases de paths con TTL que se renueva con la actividad.
- **Bucle de revisión.** `verifier` → `task_complete` (exige evidencia) crea la
  tarea de revisión para el verificador. Un veredicto `changes` devuelve la tarea
  al autor con los hallazgos (ronda + 1); tras `MaxReviewIterations` se **escala
  al líder** en vez de iterar sin fin.
- **Decisiones** tipo ADR con `supersedes`. Se anuncian al equipo y entran en el
  charter de quien llegue después.
- **Log de eventos** append-only. Se publica en `/ws/events` como `teamActivity`.
- **Avisos acotados.** Un miembro vivo que queda ocioso con una tarea en curso
  recibe hasta 2 recordatorios (el equivalente a `TeammateIdle`, para cualquier
  proveedor). Si su sesión muere, sus tareas vuelven a `Open` y se avisa al líder.

### 3. Runtimes con entrega real (bloque `live` en `agents.json`)

| Nivel | Cómo entra un mensaje en el contexto | Agentes |
|---|---|---|
| **Live** | Turno nuevo en la sesión viva; si está a mitad de turno, se encola y se fusiona en el siguiente | ACP (Gemini, Antigravity, OpenCode, Copilot, Qwen), Claude stream-json, Codex app-server (con `turn/steer` para urgentes) |
| **Hook** | `jaravi team launch … -- claude`: hooks solo para esa sesión (`--settings`). `UserPromptSubmit`/`PostToolUse` → `additionalContext`; `Stop` → bloquea con los mensajes; `Stop` async + `asyncRewake` → despierta a la sesión ociosa | Claude Code arrancado a mano |
| **Pull** | `team_inbox`; el recibo queda en `stored` hasta que mira, y se dice así | cualquier cliente MCP |

Al spawnear un miembro se le inyecta el MCP de equipo bajo su identidad: el
relay de este mismo build con `JARAVI_MEMBER_TOKEN` explícito, porque Codex no
hereda el entorno a sus servidores MCP. La vía de inyección depende del agente:
`session/new.mcpServers`, `--mcp-config` / `--additional-mcp-config` o
`thread/start.config`.

### 4. Conciencia de equipo

El **charter** es determinista y le dice a cada miembro:

- quién es y quién lidera;
- quiénes son sus compañeros y cómo alcanzar a cada uno;
- sus tareas y las decisiones vigentes;
- el protocolo de trabajo: hablar directo, pedir ayuda, arrendar antes de
  editar, cerrar con evidencia.

Llega como primer turno al hacer spawn, y como resultado de `team_create` /
`team_join` para los agentes que entran desde fuera.

## Identidad sin sesiones MCP

Streamable HTTP es **stateless**: la revisión 2026-07-28 eliminó `Mcp-Session-Id`.
Por eso la identidad no puede colgar de la sesión MCP:

- cada relay envía un id aleatorio propio (`X-Jaravi-Relay`);
- `team_join` asocia ese id al miembro, y la asociación se persiste en `bindings`;
- la identidad con que se lanzó el proceso (`JARAVI_MEMBER_TOKEN`) viaja en `X-Jaravi-Member`.

Una unión explícita en esa conexión tiene prioridad sobre la identidad del proceso.

## Seguridad (qué protege y qué no)

- **Frontera:** el usuario del SO + loopback. Los tokens dan identidad y
  bloquean a otros usuarios locales y a páginas web (CSRF/rebinding). No aíslan
  a un agente del mismo usuario.
- **Lo que viene del cable no ejecuta nada.** `McpServers`, `SystemEnv` y
  `CallerRoot` de `SpawnRequest` son `[JsonIgnore]`: solo los fija el hub
  internamente, nunca un payload.
- **El Control Center escapa todo el texto de los agentes:** un mensaje entre
  agentes no puede inyectar script.

## CLI

```
jaravi-mcp team create <name> --as <you>
jaravi-mcp team launch <team> --as <name> [--role R] -- claude [args…]
jaravi-mcp team join | status | list | send | inbox | leave
jaravi-mcp hub status | start | stop
```

## Verificado vs. pendiente (2026-11-14)

**Verificado con procesos reales:**

- Hub autoarrancado por un cliente MCP a través del relay.
- Gemini como miembro vivo: usó las tools inyectadas como él mismo y recibió un
  mensaje encolado a mitad de turno, entregado como turno nuevo; respondió con `replyTo`.
- Claude Code arrancado con `team launch`: recibió un mensaje por `PostToolUse`
  a mitad de su trabajo y respondió por el MCP inyectado.
- 99 tests del motor y 140 del servidor (agente ACP falso con procesos reales).

**Implementado pero sin verificar en vivo:**

- Despertar de una sesión de Claude ociosa con `asyncRewake` (requiere una terminal interactiva).
- Hook `PreToolUse` de leases contra Claude real.
- Codex app-server de punta a punta (el CLI instalado es anterior a su modelo configurado).
- Copilot con `--additional-mcp-config @file`.
- Kiro y Cursor.

**Siguiente:**

- Nivel *resume* (relanzar `exec resume` / `--resume` para miembros cuya sesión terminó).
- Hooks para Gemini/Qwen/Codex.
- Pasarela A2A (Agent Card por equipo) para participantes remotos.
- Cierre de equipos y retención.

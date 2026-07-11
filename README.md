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
| `Jaravi.Engine.Tests` | xUnit: 54 tests incluyendo E2E contra procesos reales. |

## Instalación (dotnet tool global — recomendada)

```bash
dotnet pack Jaravi.McpServer -c Release -o nupkg      # (o descarga el .nupkg)
dotnet tool install -g Jaravi.McpServer --add-source ./nupkg
```

Esto instala el comando **`jaravi-mcp`**. Registro en cualquier proyecto — un
`.mcp.json` inmaculado, sin rutas absolutas:

```json
{ "mcpServers": { "jaravi": { "type": "stdio", "command": "jaravi-mcp", "args": ["--stdio"] } } }
```

**Configuración de usuario**: el primer arranque siembra `%APPDATA%\jaravi\`
con `agents.json` (perfiles de sub-agentes, editable) y `appsettings.json`
(overrides: `Engine:AllowedRoots`, etc.). Resolución de `agents.json`:
`JARAVI_AGENTS` (env) → `./agents.json` del proyecto → `%APPDATA%\jaravi\` →
defaults del paquete. Actualizar tras cambios: `dotnet tool update -g
Jaravi.McpServer --add-source ./nupkg`.

## Uso rápido (desarrollo)

**Zero-touch:** `.mcp.json` usa stdio — Claude Code enciende/apaga el servidor
solo. En stdio el MCP habla por stdin/stdout (logs a stderr) y Kestrel levanta
igualmente el WebSocket/REST para el Dashboard (fallback a puerto efímero si
5210 está en uso).

```bash
dotnet run --project Jaravi.McpServer   # modo HTTP compartido: /mcp en :5210
dotnet run --project Jaravi.Dashboard   # GUI observadora (o el .exe compilado)
dotnet test                             # suite completa
```

La skill `.claude/skills/jaravi-orchestrator` enseña al agente jefe su rol.

## ¿Quién puede ser el jefe?

Jaravi es agnóstico del cliente MCP. Ya está registrado como servidor para:

| CLI | Config |
|---|---|
| Claude Code | `.mcp.json` del repo (stdio) |
| OpenCode | `opencode.jsonc` del repo — verificado con `opencode mcp list` |
| Codex | `codex mcp add jaravi -- jaravi-mcp --stdio` (global, `~/.codex/config.toml`) |
| Antigravity | `~/.antigravity/config/mcp_config.json`, entrada `"jaravi"` |

### Instalar la skill del orquestador en tu CLI

**Opción A — vía `npx skills` (una vez publicado el repo):**

```bash
npx skills add JOSETRA44/jaravi@jaravi-orchestrator -g -y
```

Esto la instala en el store universal `~/.agents/skills/` y la enlaza
automáticamente a las carpetas de skills de Claude Code, OpenCode, Codex y
demás CLIs compatibles con esa convención.

**Opción B — configurarla tú mismo (sin depender de un push):**

```bash
mkdir -p ~/.agents/skills/jaravi-orchestrator
cp .claude/skills/jaravi-orchestrator/SKILL.md ~/.agents/skills/jaravi-orchestrator/
ln -s ~/.agents/skills/jaravi-orchestrator ~/.config/opencode/skills/jaravi-orchestrator
ln -s ~/.agents/skills/jaravi-orchestrator ~/.codex/skills/jaravi-orchestrator
```

En Windows sin privilegios de symlink, `ln -s` cae automáticamente a una
junction NTFS — funciona igual, sin necesidad de modo administrador.

## Tools MCP

`list_agents`, `reload_agents`, `spawn_agent`, `run_agent`, `send_input`,
`get_status`, `list_sessions`, `read_output` (capado a 500 líneas server-side),
`await_session`, `get_summary`, `kill_agent`.

**`run_agent`** = spawn + await + summary en una sola llamada (la vía
token-eficiente para delegar-y-recoger una tarea acotada). Para trabajo largo o
en paralelo usa `spawn_agent` (retorna al instante) + `await_session`.

**`reload_agents`** = re-lee `agents.json` en vivo — agrega o edita perfiles de
CLI sin reiniciar el servidor. Es lo que hace a Jaravi aplicable a *cualquier*
CLI, no a una lista fija.

## Agentes soportados (v0.3.2)

11 perfiles verificados de fábrica: `claude`, `codex`, `opencode`, `gemini`,
`qwen`, `copilot`, `deepcode`, `mimo`, `antigravity` + demos (`echo-demo`,
`flood-demo`). Matriz de compatibilidad y estado de verificación en
`jaravi-docs/Catalogo de Agentes.md`. **Patrón universal**: casi todo CLI de
agente es un paquete npm con modo `-p`/`run` + un flag de auto-aprobación;
invócalo vía `node <entry.js>` (no el shim `.cmd`) con `closeStdin: true`.

## Buen ciudadano del protocolo MCP (v0.5.0)

Tras una prueba de consumo por un agente orquestador externo (ver
`observaciones.md`), el ejecutable se blindó como cliente MCP:

- **`jaravi-mcp --help` / `--version`** responden y salen. Antes arrancaban un
  servidor web y dejaban colgado a quien preguntaba.
- **Modo automático**: sin flag, si `stdin` es un pipe (un cliente MCP nos
  lanzó) habla **stdio**; si es una terminal (un humano), levanta **HTTP + el
  Control Center**. `--stdio` y `--http` fuerzan el modo. Apuntar cualquier
  cliente al ejecutable desnudo ahora **funciona a la primera**.
- **stdout es sagrado en todos los modos**: solo lleva JSON-RPC. Todo log va a
  stderr, y en stdio se silencia el ruido del framework (8 líneas → 0).
- **`run_agent` nunca se cuelga**: bloquea como máximo `maxWaitSec` (90s por
  defecto, bajo el timeout de cualquier cliente) y, si el sub-agente sigue
  trabajando, devuelve `timedOut: true` + un campo **`nextStep`** que dice
  literalmente qué llamar (`await_session` con el `sessionId`). La sesión sigue
  viva. Para tareas de minutos, usa `spawn_agent` + `await_session`.
- **OpenAPI**: en modo HTTP, `/swagger` (UI) y `/swagger/v1/swagger.json` para
  guionizar contra la REST sin lidiar con el framing de JSON-RPC.

## Control Center web (v0.4.0)

Cuando arranca `jaravi-mcp`, Kestrel sirve un **Centro de Control** web en la
raíz (`/`) — una sola página embebida en el exe (sin wwwroot en disco, sin
framework, sin build step). Al iniciar imprime un banner en **stderr** con la
URL exacta a abrir (crítico cuando cae a puerto efímero):

```
  ===== Jaravi Control Center =====
    open:  http://localhost:5210
    repo:  C:\Users\USER\source
  ================================
```

Muestra en vivo: tarjetas de sesión con estado, **tiempo de ejecución** que
tickea, **tokens** (reportados por el agente si los expone, o estimados del
volumen de log, marcados con `~`), **líneas** y exit code; un panel de **Locks**
con qué claim posee cada sesión y quién está en cola detrás; y una consola de
logs por sesión (auto-scroll, virtualizada). Empuja datos por el WebSocket
existente `/ws/events` (el `ChannelEventBus` con drop-oldest garantiza que una
pestaña lenta jamás frene al motor) y refresca métricas con un poll ligero de
`/api/sessions`. Permite spawn/kill desde la web (reusa el REST).

**Multi-instancia**: cada `jaravi-mcp` registra `%APPDATA%\jaravi\instances\<pid>.json`;
`GET /api/instances` lista las instancias vivas (poda PIDs muertos) y el
dashboard ofrece un selector para saltar entre los repos que estés orquestando.
El WPF (`Jaravi.Dashboard`) sigue disponible como cliente de escritorio secundario.

## Puerto y despliegue

Resolución del puerto HTTP/telemetría: `JARAVI_URL` (env) → `ASPNETCORE_URLS` →
`Urls` (config) → `http://localhost:5210`. Si el puerto está ocupado, el
servidor cae a un puerto efímero y lo anuncia en el banner de stderr y en
`/api/instances` en vez de crashear — permite instancias concurrentes.

## Orquestación avanzada (v2)

- **Pipelines**: `spawn_agent(inputFromSessionId, inputKind: summary|tail|errors)`
  — el motor inyecta el resultado de una sesión terminada en el task de la
  nueva; los agentes se encadenan sin pasar por el contexto del jefe.
- **Claims**: `spawn_agent(claims: ["src/Auth/**"], onConflict: reject|queue)`
  — rutas reclamadas en exclusiva; solapamiento → rechazo estructurado o cola
  FIFO (estado `Queued`) que arranca sola al liberarse el claim.
- **Doctrina hacer-vs-delegar** para el agente jefe en
  `.claude/skills/jaravi-orchestrator` (inspirada en cómo Claude Code gestiona
  sus propios subagentes).

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
  "closeStdin": true,                 // one-shot CLIs que leen stdin hasta EOF
  "io": "pipe"                        // "pty" reservado para ConPTY (post-MVP)
}
```

Lecciones con CLIs reales: usa `closeStdin: true` para CLIs one-shot
(`opencode run`, `claude -p`) o se cuelgan esperando EOF; y apunta al binario
real, no al shim `.cmd` de npm (cmd.exe destroza argumentos multilínea).

## Roadmap

- `ConPtyIoStrategy` (ConPTY) detrás de `IAgentProcessFactory` + teclas
  simbólicas en `send_input` para CLIs que exigen TTY real.
- Persistencia NDJSON opcional de logs por sesión.

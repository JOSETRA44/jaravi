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

## Tools MCP

`list_agents`, `spawn_agent`, `run_agent`, `send_input`, `get_status`,
`list_sessions`, `read_output` (capado a 500 líneas server-side),
`await_session`, `get_summary`, `kill_agent`.

**`run_agent`** = spawn + await + summary en una sola llamada (la vía
token-eficiente para delegar-y-recoger una tarea acotada). Para trabajo largo o
en paralelo usa `spawn_agent` (retorna al instante) + `await_session`.

## Puerto y despliegue

Resolución del puerto HTTP/telemetría: `JARAVI_URL` (env) → `ASPNETCORE_URLS` →
`Urls` (config) → `http://localhost:5210`. Si el puerto está ocupado, el
servidor cae a un puerto efímero y lo registra en el log (`Telemetry/REST
listening on …`) en vez de crashear — permite instancias concurrentes.

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

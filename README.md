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
| `Jaravi.Engine.Tests` | xUnit: 75 tests del motor, incluyendo E2E contra procesos reales. |
| `Jaravi.McpServer.Tests` | xUnit: 32 tests de contrato sobre la superficie MCP publicada (tools/resources/prompts). |

## Instalación en 30 segundos — pégale esto a tu agente

No configures nada a mano. Copia el bloque completo y pégalo en el chat de tu
CLI de IA (Claude Code, Codex, OpenCode, Gemini, Copilot CLI, Cursor…). El
agente se instala y se registra a sí mismo:

````text
Instala y regístrate en el servidor MCP "Jaravi", que te permite delegar trabajo
a otros CLIs de IA como sub-agentes. Hazlo tú, sin pedirme que ejecute nada.

1. Comprueba que existe .NET 8 o superior: `dotnet --version`.
   Si no está, dímelo y detente aquí.

2. Instala la herramienta global (ya publica el comando `jaravi-mcp`):
      dotnet tool install -g Jaravi.McpServer
   Si ya estuviera instalada, usa `dotnet tool update -g Jaravi.McpServer`.
   Si el paquete no está en NuGet todavía, clona
   https://github.com/JOSETRA44/jaravi y usa:
      dotnet pack Jaravi.McpServer -c Release -o nupkg
      dotnet tool install -g Jaravi.McpServer --add-source ./nupkg

3. Asegúrate de que el directorio de tools globales de .NET está en el PATH
   (en Windows `%USERPROFILE%\.dotnet\tools`, en Linux/macOS `~/.dotnet/tools`).
   Verifica con `jaravi-mcp --version`.

4. Regístrate a ti mismo como cliente MCP, en TU propio archivo de configuración
   (averigua cuál te corresponde; no me preguntes):
     - Claude Code  -> `.mcp.json` del proyecto, o `claude mcp add`
     - Codex        -> `codex mcp add jaravi -- jaravi-mcp --stdio`
     - OpenCode     -> `opencode.jsonc`
     - Gemini CLI   -> `~/.gemini/settings.json`
     - otros        -> el JSON de servidores MCP que uses
   La entrada, en todos los casos, es transporte stdio:
       {"command": "jaravi-mcp", "args": ["--stdio"]}
   Respeta el esquema exacto de tu cliente y NO borres los servidores que ya
   estén configurados.

5. Reinicia/recarga tus servidores MCP y comprueba la conexión llamando a la
   tool `list_agents`. Debe devolver la lista de perfiles de CLI disponibles.

6. Cuando funcione, dime en dos líneas: qué archivo tocaste y qué perfiles de
   sub-agente salieron en `list_agents`.
````

> [!tip] ¿Tu agente se resiste a usar Jaravi?
> No debería: desde v0.8.0 el servidor envía **instrucciones MCP** en el
> handshake que explican qué es, por qué su uso está autorizado y cuáles son
> sus límites reales (Scope Gate, sesiones inspeccionables y matables). Si tu
> cliente las ignora, instala además la skill `jaravi-orchestrator` (más abajo)
> y pídele explícitamente: *"usa Jaravi para delegar esto"*.

## Instalación manual (dotnet tool global)

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

## Ciudadanía MCP completa (v0.6.0)

Más allá del handshake básico, las 11 tools usan el protocolo a fondo:

- **Anotaciones** (`readOnlyHint`/`destructiveHint`/`idempotentHint`/
  `openWorldHint`): cualquier cliente sabe sin preguntar que `kill_agent` es
  destructiva y las 6 tools de lectura son seguras.
- **Progress notifications**: `run_agent` y `await_session` reportan progreso
  real cada 5s mientras esperan (no-op si el cliente no lo pidió). Verificado
  en vivo: 8 notificaciones fluyendo durante una espera de 30s — esto es lo
  que evita que un cliente perciba un "hang" en tareas largas.
- **Cancelación limpia**: si el *cliente* cancela una llamada a `run_agent`
  (no si `maxWaitSec` simplemente se agota — eso deja la sesión viva a
  propósito), Jaravi mata el árbol de procesos antes de propagar la
  cancelación, evitando huérfanos.

Detalle de la investigación (incluyendo dos diagnósticos externos que
resultaron falsos al reproducirlos) en `jaravi-docs/Brechas del Protocolo MCP.md`.

## Superficie MCP completa: resources + prompts (v0.7.0)

De las 6 superficies del protocolo MCP, Jaravi ahora cubre 5 — la sexta
(sampling) queda descartada a propósito porque delega en el cliente
exactamente la parte que Jaravi existe para no delegar (motor determinista).

- **Resources**: `jaravi://agents` y `jaravi://sessions` exponen el catálogo
  y las sesiones como contexto de solo lectura direccionable por URI — un
  boss agent los lee sin gastar un turno de tool-call. Plus tres plantillas
  de recurso (`jaravi://sessions/{sessionId}/summary|logs|errors`) para el
  mismo dato de una sesión puntual. Mismo mapeo de errores que las tools.
- **Prompts**: `delegate_task` (rellena una llamada a `run_agent` con
  instrucciones de cómo leer `timedOut`/`nextStep`) y `audit_then_fix`
  (pipeline de dos etapas auditor→fixer encadenado por
  `inputFromSessionId`, para no tener que leer tú mismo el hallazgo crudo).
  Bajan la barrera de entrada para cualquier agente que no interiorizó la
  doctrina de `jaravi-orchestrator`.
- **Elicitation**: investigada (`McpServer.ElicitAsync`) y descartada — exige
  que el cliente declare una capacidad de UI de formulario que ningún host
  real de Jaravi implementa hoy, y duplicaría algo que el boss agent ya
  resuelve mejor hablando directamente con su propio usuario.

Verificado en vivo sobre el binario global reinstalado: `resources/list`,
`resources/templates/list`, `resources/read`, `prompts/list` y `prompts/get`
responden correctamente, y una lectura cruzada (`list_agents` vía tool vs.
`jaravi://agents` vía resource) confirma que ambas superficies devuelven
datos consistentes. Detalle completo, incluyendo el razonamiento de por qué
se descartó elicitation, en `jaravi-docs/Brechas del Protocolo MCP.md`.

## Por qué los agentes se resistían a usarlo (v0.8.0)

Varios agentes externos ignoraban Jaravi o se negaban a delegar. La causa no era
la calidad de las tools: era que **el servidor no se presentaba**.

MCP define un campo `instructions` que se devuelve en el handshake `initialize`,
y los clientes lo inyectan como *system message* del modelo. Jaravi lo dejaba
vacío. Un agente veía entonces once tools sin contexto que lanzan otros agentes
de IA con flags de auto-aprobación, no tenía forma de saber quién había
autorizado eso, y hacía lo razonable: no tocarlas.

Ahora el servidor se presenta (`JaraviInstructions.cs`) explicando, en orden:

1. **Que su uso está autorizado y por qué** — si el agente ve estas tools es
   porque el usuario instaló y registró el servidor él mismo; ejecutar
   sub-agentes es el propósito del servidor, no un efecto secundario.
2. **Los límites reales, para que el agente juzgue por sí mismo** — los
   sub-agentes son CLIs que el usuario ya tenía, corriendo con su propia cuenta;
   el Scope Gate rechaza cualquier workdir fuera de las raíces permitidas; toda
   sesión es inspeccionable y matable; nada corre invisible.
3. **Cuándo delegar y cuándo no** — explícitamente: no delegues lo que resuelves
   en dos tool-calls; delegar es para volumen y paralelismo.
4. **Los cinco malentendidos habituales** — sobre todo que `timedOut: true`
   significa *sigue corriendo*, no *falló* (el error más caro observado en
   consumo real).

Es el texto de mayor apalancamiento del proyecto: lo lee **todo** agente jefe
antes de decidir si confía en la herramienta. Verificado en el handshake real y
cubierto por tests de contrato.

### Red de seguridad: tests de contrato (v0.7.1)

Esa superficie ahora está cubierta por `Jaravi.McpServer.Tests` (32 tests), que
construye el contenedor **igual que `Program.cs`** y afirma sobre lo que el SDK
publica al cable, no sobre lo que devuelven nuestros métodos. Ataja la clase de
bug que no lanza excepción y por tanto nadie nota: una errata en un `UriTemplate`
que deja un recurso inalcanzable, una tool nueva sin anotaciones, o un cambio de
registro que publica cero prompts.

Escribirlos ya pagó: descubrieron que `WithPromptsFromAssembly()` resuelve contra
el **assembly llamador**, así que mover ese registro a otro assembly habría
publicado cero prompts en silencio. `Program.cs` ahora pasa el assembly explícito.
Detalle en `jaravi-docs/Pruebas de Contrato MCP.md`.

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

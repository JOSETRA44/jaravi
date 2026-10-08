# Jaravi

**Orquestación determinista de sub-agentes CLI.** Jaravi ejecuta Codex, Claude
Code, OpenCode, Gemini, Qwen o Copilot como sub-agentes en tu máquina y devuelve
un resumen acotado. El output crudo —decenas de miles de líneas— muere dentro del
motor.

Servidor MCP y CLI en un solo binario · .NET 8 · MIT · v0.11.0

📖 **[Documentación completa](https://josetra44.github.io/jaravi/)** — conceptos,
referencia del CLI y de las tools, API REST y notas de ingeniería.

---

## El problema

Un sub-agente que audita un repositorio produce entre mil y cincuenta mil líneas
para llegar a tres hallazgos. Si esas líneas viajan al contexto del agente que
orquesta, la ventana se agota antes que la tarea.

La respuesta habitual es pedirle al modelo, por prompt, que no lea demasiado. Eso
funciona hasta que deja de funcionar. Jaravi lo resuelve **estructuralmente**: el
tope vive en el servidor, y ninguna llamada puede devolver más de 500 líneas por
mucho que quien llame insista.

```
1 284 líneas  ──►  [ Scope Gate · sanitizador ANSI · ring buffer · tope 500 ]  ──►  12 líneas
   sub-agente                        Jaravi.Engine                              tu contexto
```

## Instalación

```bash
dotnet tool install -g Jaravi.McpServer   # deja el comando jaravi-mcp
jaravi-mcp install                        # se registra en tus CLIs y añade el alias 'jaravi'
jaravi doctor                             # ya puedes usar el nombre corto
```

`install` toca tres superficies, y las tres hacen falta por razones distintas:

1. **La config MCP de cada cliente instalado** —Claude Code, Codex, OpenCode,
   Gemini, Qwen, Copilot— con el esquema exacto de cada uno y sin borrar los
   servidores que ya tuvieras. Esto arregla la **próxima** sesión.
2. **Los ficheros de instrucciones** que esos agentes leen al arrancar
   (`AGENTS.md`, `CLAUDE.md`, `GEMINI.md`…), en un bloque entre marcadores. Esto
   es lo que rescata la sesión **actual**.
3. **El alias `jaravi`**, porque es el nombre que un agente prueba primero.

| Opción | Efecto |
|---|---|
| `--dry-run` | Enseña qué tocaría sin escribir un byte |
| `--scope project` | Limita los cambios a este repositorio |
| `--refresh-agents` | Toma el `agents.json` de esta versión; el tuyo queda como `.bak` |
| `uninstall` | Revierte las dos superficies por completo |

Cada fichero modificado deja un `.jaravi.bak` al lado.

> **En Windows el alias se instala dos veces, a propósito.** Un `.cmd` solo es un
> comando para cmd.exe y PowerShell. La shell en la que un agente escribe suele
> ser Git Bash, que resuelve el `PATH` por reglas POSIX y no encuentra un `.cmd`
> por su nombre desnudo. Así que `install` escribe también un script sin
> extensión al lado, y `jaravi` funciona en las tres shells.

### Instalación manual

```bash
dotnet pack Jaravi.McpServer -c Release -o nupkg
dotnet tool install -g Jaravi.McpServer --add-source ./nupkg
```

Registro manual en un cliente MCP, sin rutas absolutas:

```json
{ "mcpServers": { "jaravi": { "type": "stdio", "command": "jaravi-mcp", "args": ["--stdio"] } } }
```

## Empieza aquí

Nada de esto necesita registro MCP, reinicio ni servidor corriendo.

```bash
jaravi doctor                                   # ¿está usable aquí?
jaravi agents                                   # qué CLIs hay instalados
jaravi run --agent codex --task "audita src/"   # delega y recoge un resumen
```

```text
session 7f3a2c · codex · C:\src\api
✔ exit 0 · 47 s · 1 284 líneas absorbidas · 12 devueltas

3 hallazgos
· src/api/auth.ts:88     token sin expiración
· src/api/users.ts:142   SQL concatenado
· src/api/index.ts:12    CORS abierto a *
```

Las 1 284 líneas siguen ahí —en el ring buffer de la sesión, y enteras en el
Control Center—. Simplemente no se te imponen.

### Desde un cliente MCP

Dos argumentos bastan; todo lo demás tiene valor por defecto.

```text
run_agent(agent: "codex", task: "audita src/api y lista los fallos")
```

`agent` es el id del perfil que devuelve `list_agents`. `workdir` toma por
defecto el directorio del servidor y se valida igual contra el Scope Gate.
`profile` se acepta como alias de `agent`.

### Trabajo largo o en paralelo

`run` bloquea, y bloquear mucho es mala idea dentro de un cliente MCP. Para
tareas de minutos, levanta un servidor y separa el lanzamiento de la recogida:

```bash
jaravi-mcp --http &                                        # sostiene las sesiones
ID=$(jaravi spawn --agent opencode --task "migra los tests" --quiet)
jaravi await "$ID" --wait 600
jaravi logs "$ID" --tail 40 --grep "error|fail"
```

> **Los comandos se adjuntan a un Jaravi que ya esté corriendo.** Si hay una
> instancia viva —la de tu cliente MCP incluida—, el CLI la conduce: las sesiones
> que lances desde la shell son **las mismas** que ve el agente jefe por
> `list_sessions` y el usuario en el Control Center. Si no hay ninguna, `run`
> levanta un motor privado que dura lo que dura el comando.

### Encadenar sin leer el intermedio

El motor inyecta un extracto acotado del resultado previo en el task del
siguiente; el intermedio no pasa nunca por quien orquesta.

```bash
ID=$(jaravi spawn --agent codex --task "audita src/api" --quiet)
jaravi await "$ID"
jaravi run --agent claude --task "arregla lo que encontró" --input-from "$ID"
```

`--input-kind summary|tail|errors` (por defecto `summary`, que es el acotado),
`--input-tail N`, `--input-grep RE`. Y `--claims "src/Auth/**"` con
`--on-conflict reject|queue` reserva rutas en exclusiva para que dos sesiones en
paralelo no se pisen.

## Códigos de salida

Único canal estructurado que un llamador de shell tiene siempre.

| Código | Significado |
|---|---|
| `0` | Terminó y el sub-agente salió con 0 |
| `1` | Error de Jaravi (perfil, Scope Gate, config, transporte) |
| `2` | Uso incorrecto |
| `3` | El sub-agente terminó con código distinto de 0 |
| `4` | **Sigue corriendo** — no es un fallo; recógelo con `jaravi await <id>` |

La separación entre `3` y `4` es deliberada: *«no ha terminado»* no es *«ha
fallado»*, y confundirlos es el malentendido más caro sobre este sistema. Añade
`--json` a cualquier comando para salida parseable; el código de salida es el
mismo en ambos formatos.

## Tools MCP

Once tools, todas anotadas: seis son de solo lectura y una sola es destructiva,
así que cualquier cliente sabe sin preguntar cuáles son seguras.

| Tool | Qué hace |
|---|---|
| `list_agents` | Los perfiles disponibles. Empieza por aquí. |
| `reload_agents` | Re-lee `agents.json` en vivo, sin reiniciar |
| `spawn_agent` | Lanza y devuelve el `sessionId` al instante |
| `run_agent` | Spawn + await + summary en una sola llamada |
| `send_input` | Escribe en el stdin del sub-agente |
| `get_status` | Estado compacto: fase, uptime, exit code, últimas líneas |
| `list_sessions` | Todas las sesiones y su estado, incluida la cola |
| `read_output` | Output acotado, con `tail`, `grep` y `sinceSeq` |
| `await_session` | Bloquea hasta estado terminal, reportando progreso |
| `get_summary` | Digest: exit code, duración y errores extraídos |
| `kill_agent` | Mata el árbol de procesos completo |

`read_output` tiene un **tope duro de 500 líneas del lado del servidor**
(`Engine:MaxReadLines`). Pedir 50 000 devuelve 500: el límite no depende de que
quien llama se porte bien.

**Más allá de las tools**, de las seis superficies del protocolo MCP, Jaravi
cubre cinco. Dos recursos (`jaravi://agents`, `jaravi://sessions`) más tres
plantillas por sesión; dos prompts (`delegate_task`, `audit_then_fix`); progreso
real cada 5 s en las llamadas que esperan; y cancelación limpia que mata el árbol
antes de propagarse. *Sampling* se descartó a propósito: delegaría en el cliente
exactamente la parte que Jaravi existe para no delegar.

## Sub-agentes soportados

11 perfiles de fábrica: `claude`, `codex`, `opencode`, `gemini`, `qwen`,
`copilot`, `deepcode`, `mimo`, `antigravity`, más dos demos (`echo-demo`,
`flood-demo`). La matriz con el estado de verificación de cada uno está en la
[documentación](https://josetra44.github.io/jaravi/docs/agentes).

**Patrón universal:** casi todo CLI de agente moderno es un paquete npm con modo
one-shot (`-p` / `run`) y un flag de auto-aprobación. Invócalo vía
`node <entry.js>` —no el shim `.cmd` de npm, que pasa por cmd.exe y destroza
argumentos multilínea— con `closeStdin: true`.

### Añadir uno nuevo

Una entrada declarativa en `agents.json`, sin tocar código:

```jsonc
{
  "id": "mi-cli",
  "command": "{npmRoot}/mi-cli/bin/mi-cli.exe",  // {home} {appData} {localAppData} {npmRoot}
  "args": ["run", "{task}"],                     // {task} {workdir}
  "unattendedArgs": ["--yes"],                   // inyectados si unattended=true
  "env": { "CI": "true" },
  "closeStdin": true,                            // CLIs one-shot que leen stdin hasta EOF
  "io": "pipe"                                   // "pty" reservado para ConPTY (post-MVP)
}
```

Luego `reload_agents` lo activa en vivo — sin reiniciar el servidor, que en modo
stdio mataría la conexión del agente jefe. Un fichero mal formado se rechaza y el
catálogo activo sobrevive intacto. Los placeholders de ruta se expanden al cargar,
así que el registro es portable entre máquinas.

## Arquitectura

Clean Architecture con cuatro proyectos. Las dependencias apuntan siempre hacia
adentro: el dominio no sabe que existe Kestrel, y el motor no sabe que existe MCP.

```
Agente jefe   ──  MCP (stdio o Streamable HTTP)  ──►  Jaravi.McpServer (Kestrel)
Control Center ──  WS /ws/events + REST /api     ──►         │
                                                      Jaravi.Engine  ──► sub-procesos
                                                             │
                                                        Jaravi.Core
```

| Proyecto | Rol |
|---|---|
| `Jaravi.Core` | Dominio puro: modelos, eventos, puertos. Cero dependencias. |
| `Jaravi.Engine` | El motor: procesos (pipe I/O), SessionManager, event bus, ring buffer, ClaimRegistry, Scope Gate, sanitizador ANSI. |
| `Jaravi.McpServer` | Host headless: tools MCP, recursos, prompts, **CLI de shell**, WebSocket de telemetría, REST y el Control Center embebido. |
| `Jaravi.Dashboard` | GUI WPF (MVVM) observadora; solo consume HTTP y WebSocket. |
| `Jaravi.Engine.Tests` | xUnit: 82 tests del motor, incluido E2E contra procesos reales. |
| `Jaravi.McpServer.Tests` | xUnit: 133 tests de contrato sobre la superficie publicada y sobre el CLI. |

## Modos de operación

`jaravi-mcp` decide su modo antes de arrancar ASP.NET:

| Invocación | Modo | Por qué |
|---|---|---|
| `--help` / `--version` | ninguno | responde y sale |
| un verbo (`run`, `agents`…) | CLI | ni MCP ni servidor web |
| `--stdio` | stdio forzado | para configs de clientes MCP |
| `--http` | HTTP forzado | para el Control Center y scripting REST |
| sin nada | **auto** | `stdin` es pipe → stdio; es terminal → HTTP |

**stdout es sagrado en todos los modos:** solo lleva JSON-RPC. Todo log va a
stderr, y en stdio se silencia el ruido del framework. Apuntar cualquier cliente
MCP al ejecutable desnudo funciona a la primera.

En stdio, Kestrel sigue sirviendo WebSocket y REST — eso es lo que permite que el
CLI se adjunte a la instancia que lanzó tu cliente MCP.

### Endpoints

| Ruta | Propósito |
|---|---|
| `GET /mcp` | Endpoint MCP (modo HTTP) |
| `WS /ws/events` | Eventos: `sessionStarted`, `sessionStateChanged`, `logBatchEmitted`, `sessionExited` |
| `GET /api/agents`, `/api/sessions`, `/api/sessions/{id}` | Consulta |
| `GET /api/sessions/{id}/logs`, `/api/sessions/{id}/summary` | Output paginado y digest |
| `POST /api/sessions`, `/api/sessions/{id}/kill`, `/api/sessions/{id}/input` | Control |
| `GET /api/instances` | Instancias vivas (poda los PID muertos) |
| `GET /healthz` · `GET /readyz` | Liveness · readiness (este último toca el motor) |
| `GET /` | Control Center embebido |
| `GET /swagger` | OpenAPI, para guionizar sin lidiar con JSON-RPC |

> `/healthz` responde desde un literal y sigue en 200 aunque el motor esté
> bloqueado, así que no sirve para decidir si una instancia es usable. `/readyz`
> resuelve el registro y el gestor de sesiones: un 200 ahí significa que el motor
> responde. Es lo que sondea el CLI antes de adjuntarse.

### Control Center

Al arrancar, Kestrel sirve un Control Center web en la raíz — embebido en el
ejecutable, sin wwwroot en disco, sin build step. Muestra en vivo las tarjetas de
sesión con estado, tiempo, tokens, líneas y exit code; un panel de locks con qué
rutas tiene reclamadas cada sesión y quién está en cola detrás; y una consola de
logs por sesión. Permite spawn y kill desde el navegador.

Empuja por el WebSocket `/ws/events`, cuyo bus con *drop-oldest* garantiza que
una pestaña lenta jamás frene al motor. El banner de arranque va a **stderr** con
la URL exacta, que es crítico cuando el puerto cae a uno efímero:

```
  ===== Jaravi Control Center =====
    open:  http://localhost:5210
    repo:  C:\Users\USER\source
  ================================
```

## Garantías anti-colapso

- **Scope Gate**: el `workdir` se valida contra `Engine:AllowedRoots` antes de
  arrancar el proceso. La comprobación es consciente de segmentos: `C:\src\app`
  no es ancestro de `C:\src\application`.
- **Ring buffer** de 10 000 líneas por sesión + tope duro de lectura de 500.
- **Deadline duro** por sesión con `Kill(entireProcessTree: true)`: ni un nieto
  huérfano consumiendo CPU.
- **Logs sanitizados** sin secuencias ANSI, para que lo almacenado sea texto
  determinista y comparable.
- **Nada corre invisible**: toda sesión es listable, inspeccionable y matable.

Lo que Jaravi **no** garantiza: los sub-agentes editan de verdad, con la
auto-aprobación de su propia CLI y las credenciales que el usuario ya tenía.
Jaravi acota *dónde* pueden escribir, no *qué* escriben. Trata sus ediciones como
las tuyas: acotadas al workdir que pasaste, y revisables después.

## Configuración

```text
%APPDATA%\jaravi\        (Windows)      ~/.config/jaravi/   (Linux y macOS)
  ├── agents.json        perfiles de sub-agente
  ├── appsettings.json   límites del motor y Scope Gate
  └── instances/<pid>.json
```

| Clave de `appsettings.json` | Por defecto | Qué hace |
|---|---|---|
| `Engine:AllowedRoots` | el repositorio actual | Lista blanca del Scope Gate |
| `Engine:MaxConcurrentSessions` | `8` | Procesos hijos vivos a la vez |
| `Engine:MaxReadLines` | `500` | Tope duro de `read_output` |
| `Engine:LogBufferCapacity` | `10000` | Ring buffer por sesión |
| `Engine:DefaultTimeoutSeconds` | `1800` | Deadline por sesión |

| Variable de entorno | Efecto |
|---|---|
| `JARAVI_URL` | Dirección a la que bindear. Gana a `ASPNETCORE_URLS` y a `Urls` |
| `JARAVI_AGENTS` | Ruta a un `agents.json` concreto |
| `JARAVI_NPM_ROOT` | Fija la raíz global de npm y evita sondearla (útil donde npm es lento) |

Si el puerto está ocupado, el servidor cae a uno efímero y lo anuncia en el
banner de stderr y en `/api/instances`, en vez de fallar. Eso permite instancias
concurrentes, una por repositorio.

## Desarrollo

```bash
dotnet build                                   # la solución entera
dotnet test                                    # 215 tests
dotnet run --project Jaravi.McpServer          # modo HTTP: /mcp y Control Center en :5210
dotnet run --project Jaravi.Dashboard          # GUI observadora
```

El sitio público (`docs/`) se construye aparte; ver `docs/site/README.md`.

### La skill del orquestador

`.claude/skills/jaravi-orchestrator` enseña al agente jefe la doctrina de
hacer-vs-delegar. Se distribuye por el store universal `~/.agents/skills/`:

```bash
npx skills add JOSETRA44/jaravi@jaravi-orchestrator -g -y
```

## Si un agente se niega a usarlo

Tres causas distintas, cada una con su respuesta. Todas comparten la misma forma:
**una capacidad que no está en la superficie que el agente inspecciona primero,
para ese agente no existe.**

| Dice | Causa | Respuesta |
|---|---|---|
| «No me consta que esté autorizado» | El handshake MCP no decía nada | Desde v0.8.0 el servidor se presenta: por qué su uso está autorizado y cuáles son sus límites reales |
| «No existe un comando `jaravi`» | El binario no tenía subcomandos | Desde v0.9.0 es un CLI completo; `install` deja el alias en las tres shells |
| «No puedo registrar un MCP a mitad de sesión» | Cierto, y no se arregla desde el servidor | `install` escribe también en el fichero de instrucciones que el agente ya está leyendo |

Si las tools existen pero fallan, empieza por `jaravi doctor` y mira la sección
*mcp clients*. Recuerda que los clientes leen su config MCP **solo al arrancar**:
tras registrar, reinicia el cliente. El CLI no necesita reinicio ninguno.

## Documentación

La guía completa —conceptos, referencia del CLI y de las tools, API REST,
configuración y las notas de ingeniería de cada ronda— está en
**[josetra44.github.io/jaravi](https://josetra44.github.io/jaravi/)**.

Las notas de ingeniería cubren el porqué de las decisiones que más cuestan de
adivinar leyendo el código: cómo se blindó la ciudadanía MCP, por qué se
descartaron *sampling* y *elicitation*, qué garantiza el instalador cuando edita
ficheros ajenos, y por qué los tests de contrato afirman sobre lo que el SDK
publica al cable y no sobre lo que devuelven nuestros métodos.

## Roadmap

- `ConPtyIoStrategy` (ConPTY) detrás de `IAgentProcessFactory` + teclas simbólicas
  en `send_input`, para CLIs que exigen un TTY real.
- Persistencia NDJSON opcional de logs por sesión.

## Licencia

MIT. Ver [LICENSE](LICENSE).

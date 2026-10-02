Tres comandos para comprobar que el motor funciona, delegar una tarea real y
entender qué acabas de ver. Nada de esto necesita registro MCP ni un servidor
corriendo.

## 1. ¿Está usable aquí?

```bash
jaravi doctor
```

Responde a las tres preguntas que importan antes de delegar: qué configuración
está cargada, qué raíces acepta el [[Operacion|Scope Gate]] y qué CLIs de agente
encuentra realmente en el `PATH`.

> [!warning] Un perfil con `??` no es un fallo de Jaravi
> Significa que ese CLI no está instalado en esta máquina, o que el binario no
> está en el `PATH`. `jaravi agents` marca cuáles están disponibles.

## 2. Elige quién va a trabajar

```bash
jaravi agents
```

```text
id           command                                   estado
codex        node …/codex.js exec {task} --full-auto   ok
opencode     …/opencode.exe run {task}                 ok
claude       claude -p {task}                          ok
gemini       node …/gemini.js -p {task}                ??  no está en el PATH
```

Los perfiles son declarativos: añadir uno es una entrada en `agents.json` y una
llamada a `reload_agents`, nunca un cambio de código. Ver
[[Perfiles de Agentes|perfiles]].

## 3. Delega

```bash
jaravi run --agent codex --task "audita src/api y lista los fallos de seguridad"
```

`run` hace tres cosas en una: lanza la sesión, espera a que termine y te
devuelve un resumen acotado.

```text
session 7f3a2c · codex · C:\src\api
running ██████████████░░░░  00:41
✔ exit 0 · 47 s · 1 284 líneas absorbidas · 12 devueltas

3 hallazgos
· src/api/auth.ts:88     token sin expiración
· src/api/users.ts:142   SQL concatenado
· src/api/index.ts:12    CORS abierto a *
```

Las 1 284 líneas siguen ahí —en el ring buffer de la sesión, y enteras en el
[[Control Center (Web)|Control Center]]—. Simplemente no se te imponen.

## Lo que hay que entender del resultado

> [!important] `exit 4` no es un fallo
> Si el sub-agente tarda más que `--wait`, `run` devuelve **4**: *sigue
> corriendo*. La sesión está viva y se recoge con `jaravi await <id>`. Confundir
> «no ha terminado» con «ha fallado» es el malentendido más caro sobre este
> sistema, y por eso son códigos distintos.

| Código | Significado |
|---|---|
| `0` | Terminó y el sub-agente salió con 0 |
| `1` | Error de Jaravi (perfil inexistente, Scope Gate, config) |
| `2` | Uso incorrecto |
| `3` | El sub-agente terminó con código distinto de 0 |
| `4` | Sigue corriendo — recógelo con `await` |

Añade `--json` a cualquier comando para salida parseable; el código de salida es
el mismo en ambos formatos.

## Tareas que duran minutos

`run` bloquea, y bloquear mucho es mala idea dentro de un cliente MCP. Para
trabajo largo, arranca un servidor en segundo plano y separa el lanzamiento de
la recogida:

```bash
jaravi-mcp --http &                                        # el servidor sostiene las sesiones
ID=$(jaravi spawn --agent opencode --task "migra los tests" --quiet)
jaravi await "$ID" --wait 600
jaravi logs "$ID" --tail 40 --grep "error|fail"
```

> [!note] Sin servidor, la sesión muere con el comando
> Sin una instancia viva, el CLI monta un motor privado que dura lo que dura el
> comando. Por eso `spawn` se niega en ese modo en vez de devolver un
> `sessionId` a punto de dejar de existir.

## Desde un cliente MCP

Si Jaravi ya está registrado (`jaravi-mcp install` y reiniciar el cliente), el
agente jefe usa las mismas capacidades como tools:

```text
run_agent(agent: "codex", task: "audita src/api", maxWaitSec: 90)
  → { exitCode: 0, summary: "...", timedOut: false }

spawn_agent(agent: "opencode", task: "migra los tests")
  → { sessionId: "a91f04" }
await_session(sessionId: "a91f04")
```

La referencia completa está en [[Tools MCP|tools MCP]].

## Siguiente paso

- Entender **cuándo conviene delegar** y cuándo no: [[Delegar trabajo|delegar]].
- Encadenar un auditor con un corrector: [[Encadenar agentes|pipelines]].
- Lanzar varios a la vez sin que se pisen: [[Claims y colas|claims]].

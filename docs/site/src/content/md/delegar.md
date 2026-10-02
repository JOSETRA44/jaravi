La pregunta útil no es *cómo* delegar, sino *cuándo*. Lanzar una CLI entera para
editar un fichero es más lento y más caro que editarlo. Delegar existe para
volumen y paralelismo.

## Cuándo delegar

- **El output crudo inundaría tu contexto.** Auditorías de repositorio,
  refactores masivos, compilaciones largas, suites de tests ruidosas.
- **El trabajo se parte en piezas independientes** que pueden correr a la vez.
- **La tarea es larga** y no quieres que bloquee tu propia sesión.
- **El usuario lo pide.**

## Cuándo no

- Es una edición de un fichero, una lectura o un `grep` dirigido. Dos llamadas
  tuyas terminan antes de que el sub-agente arranque.
- Necesitas el resultado exacto, no un resumen. Un resumen acotado es
  precisamente lo que devuelve Jaravi.
- La tarea depende de contexto que solo tienes tú y que no cabe en el `task`.

> [!tip] La regla corta
> Si lo resuelves en dos llamadas, hazlo tú. Delegar es para lo que no cabe.

## `run_agent`: delegar y recoger

Es la vía token-eficiente para una tarea acotada. Hace `spawn` + `await` +
`summary` en una sola llamada.

```bash
jaravi run --agent codex --task "audita src/api y lista los fallos" --wait 90
```

```text
run_agent(agent: "codex", task: "audita src/api", maxWaitSec: 90)
```

La respuesta trae `exitCode`, `durationMs`, el resumen y las líneas de error
extraídas.

### El caso que hay que saber leer

> [!important] `timedOut: true` significa «sigue corriendo»
> No significa que haya fallado. `run_agent` bloquea como máximo `maxWaitSec`
> (90 s por defecto, por debajo del timeout de cualquier cliente) y, si el
> sub-agente sigue trabajando, devuelve `timedOut: true` más un campo
> **`nextStep`** que dice literalmente qué llamar: `await_session` con ese
> `sessionId`. La sesión sigue viva.
>
> Desde la shell, el mismo hecho se comunica con el **código de salida 4**.

Relanzar la tarea al ver un timeout es el error más caro observado en consumo
real: acabas con dos sub-agentes escribiendo sobre los mismos ficheros.

## `spawn_agent` + `await_session`: trabajo largo

Para minutos, separa el lanzamiento de la recogida. `spawn` retorna al instante.

```bash
ID=$(jaravi spawn --agent opencode --task "migra los tests a vitest" --quiet)
# …haz otra cosa mientras…
jaravi await "$ID" --wait 600
jaravi status "$ID"
```

> [!note] Esto necesita un servidor vivo
> El motor privado que monta el CLI muere con el comando, así que `spawn` se
> niega en ese modo con un mensaje que ofrece las dos salidas: usar `run`, o
> levantar `jaravi-mcp --http` en segundo plano.

Durante la espera, `await_session` y `run_agent` emiten **notificaciones de
progreso** cada 5 segundos si el cliente las pidió (`_meta.progressToken`). Eso
es lo que evita que un cliente MCP perciba un cuelgue en tareas largas.

## Paralelizar

Varios `spawn` seguidos corren a la vez, hasta
`Engine:MaxConcurrentSessions` (8 por defecto):

```bash
A=$(jaravi spawn --agent codex    --task "audita src/api"  --quiet)
B=$(jaravi spawn --agent opencode --task "audita src/web"  --quiet)
C=$(jaravi spawn --agent claude   --task "audita src/core" --quiet)

for id in "$A" "$B" "$C"; do jaravi await "$id"; done
```

Si van a **escribir** sobre zonas que podrían solaparse, decláralo con
[[Claims y colas|claims]] en vez de confiar en la suerte.

## Escribir un buen `task`

El `task` es el único contexto que recibe el sub-agente. Merece la pena que sea
concreto:

| En vez de | Escribe |
|---|---|
| «revisa el código» | «audita `src/api/**` buscando SQL concatenado, secretos en literales y CORS abierto; devuelve fichero:línea» |
| «arregla los tests» | «los tests de `tests/auth` fallan tras el cambio de firma de `signToken`; adáptalos sin tocar el código de producción» |
| «documenta esto» | «escribe el XML doc de los métodos públicos de `SessionManager`, en español, sin cambiar la implementación» |

Y acota el `workdir`: el sub-agente no necesita ver todo el repositorio para
auditar un subdirectorio.

> [!warning] Cuidado con los tasks multilínea en perfiles `cmd`
> `cmd.exe` deja de leer su línea de comandos en el primer salto de línea, así
> que un perfil que apunte a un `.cmd` o `.bat` recibe el task **cortado** y sale
> con exit 0: éxito, a ojos de cualquiera. Desde la v0.11.0 el motor lo detecta y
> escribe un `[jaravi] WARNING` en el log de la sesión. Los perfiles reales
> apuntan al ejecutable, no al shim de npm, justo por esto.

## Leer el resultado sin ahogarte

- **`get_summary`** — exit code, duración y errores extraídos. Empieza siempre
  por aquí.
- **`get_status`** — estado, uptime y las últimas cinco líneas. Para comprobar
  si algo sigue vivo.
- **`read_output`** — el output, acotado a 500 líneas, con `tail`, `grep` y
  `sinceSeq`. Úsalo con `grep` antes que con `tail`.

```bash
jaravi logs "$ID" --tail 40 --grep "error|failed|exception"
```

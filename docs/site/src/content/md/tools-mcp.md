Once tools, todas anotadas. Un cliente MCP sabe sin preguntar cuáles son seguras:
seis son de solo lectura y una sola es destructiva.

## Resumen

| Tool | Anotaciones | Qué hace |
|---|---|---|
| `list_agents` | `readOnly` `idempotent` | Lista los perfiles de sub-agente disponibles. |
| `reload_agents` | `idempotent` | Re-lee `agents.json` en vivo, sin reiniciar. |
| `spawn_agent` | `openWorld` | Lanza un sub-agente; devuelve `sessionId` al instante. |
| `run_agent` | `openWorld` | Spawn + await + summary en una sola llamada. |
| `send_input` | `openWorld` | Escribe en el stdin del sub-agente. |
| `get_status` | `readOnly` `idempotent` | Estado compacto y últimas líneas. |
| `list_sessions` | `readOnly` `idempotent` | Todas las sesiones y su estado. |
| `read_output` | `readOnly` `idempotent` | Output acotado, con filtros. |
| `await_session` | `readOnly` `idempotent` | Bloquea hasta estado terminal. |
| `get_summary` | `readOnly` `idempotent` | Digest de una sesión terminada. |
| `kill_agent` | **`destructive`** `idempotent` | Mata el árbol de procesos. |

## Catálogo

### `list_agents`

Devuelve los perfiles cargados: `id`, descripción, comando resuelto y si el
ejecutable está realmente en el `PATH`. Es el primer sitio al que ir: delegar a
un `id` que no existe falla, y delegar a uno cuyo CLI no está instalado también.

### `reload_agents`

Re-lee `agents.json` desde disco. Es lo que hace a Jaravi aplicable a *cualquier*
CLI y no a una lista fija: se deja caer un perfil nuevo y se usa al instante.

> [!note] Por qué existe esta tool en vez de un reinicio
> En modo stdio, reiniciar el servidor mata la conexión del agente jefe. Un
> archivo mal formado se rechaza con un error de dominio y el catálogo activo
> sobrevive intacto.

### `spawn_agent`

Lanza y retorna al instante con `sessionId`.

| Parámetro | Tipo | Notas |
|---|---|---|
| `agent` | string | El `id` del perfil. Obligatorio. |
| `task` | string | El brief. Obligatorio. |
| `workdir` | string | Directorio de trabajo. Validado por el Scope Gate. |
| `unattended` | bool | Inyecta los `unattendedArgs` del perfil. Por defecto `true`. |
| `timeoutSeconds` | int | Deadline duro de la sesión. |
| `inputFromSessionId` | string | Encadena desde una sesión terminada. |
| `inputKind` | enum | `summary` (por defecto), `tail`, `errors`. |
| `inputTailLines` | int | Líneas si `inputKind: tail`. El motor capa en 100. |
| `inputGrep` | string | Filtra esas líneas por expresión regular. |
| `claims` | string[] | Rutas reclamadas en exclusiva. |
| `onConflict` | enum | `reject` (por defecto) o `queue`. |

Ver [[Encadenar agentes|pipelines]] y [[Claims y colas|claims]].

### `run_agent`

`spawn` + `await` + `summary`. La vía token-eficiente para una tarea acotada.
Acepta todo lo de `spawn_agent` más `maxWaitSec` (90 por defecto).

> [!important] `timedOut: true` significa «sigue corriendo»
> No es un fallo. La respuesta incluye un campo `nextStep` que dice literalmente
> qué llamar: `await_session` con ese `sessionId`. La sesión sigue viva y su
> trabajo también.

Si el **cliente** cancela la llamada (`notifications/cancelled` — distinto de que
se agote `maxWaitSec`, que deja la sesión viva a propósito), Jaravi mata el árbol
de procesos antes de propagar la cancelación, evitando huérfanos.

### `send_input`

Escribe texto en el stdin del sub-agente. Lanza `NotSupportedException` si el
perfil tiene `closeStdin: true`, que es el caso de casi todos los CLIs one-shot.

### `get_status`

Estado, uptime, exit code si terminó, y las últimas cinco líneas. Es la llamada
barata para comprobar si algo sigue vivo.

### `list_sessions`

Todas las sesiones con su estado. Incluye `queuedBehind` para las encoladas, así
que se ve de un vistazo quién espera a quién.

### `read_output`

El output de una sesión, con filtros:

| Parámetro | Notas |
|---|---|
| `maxLines` | **Capado en el servidor** por `Engine:MaxReadLines` (500). |
| `tail` | Devuelve el final en vez del principio. |
| `grep` | Expresión regular sobre las líneas. |
| `sinceSeq` | Número de secuencia desde el que leer; permite paginar sin repetir. |

> [!warning] El tope no es negociable
> Pedir 50 000 líneas devuelve 500. El límite vive en el servidor precisamente
> para que no dependa de la disciplina de quien llama.

### `await_session`

Bloquea hasta que la sesión llegue a estado terminal o a `WaitingInput`. Espera
también a las sesiones en estado `Queued`, lo que convierte «encolar y esperar»
en la forma natural de serializar trabajo.

Reporta progreso real cada 5 segundos si el cliente lo pidió mediante
`_meta.progressToken`; si no lo pidió, es un no-op.

### `get_summary`

El digest de una sesión: exit code, duración y líneas de error extraídas con la
expresión `\b(error|exception|failed|fatal|denied|traceback)\b`. Es lo que
deberías leer primero, casi siempre en vez de `read_output`.

### `kill_agent`

Mata el árbol de procesos completo (`Kill(entireProcessTree: true)`). Única tool
anotada como destructiva. Es idempotente: matar algo ya muerto no es un error.

## Errores

Los errores de dominio se mapean a errores de protocolo con un mensaje que
nombra la causa concreta: perfil inexistente, `workdir` fuera de las raíces
permitidas, sesión no encontrada, sesión origen no terminada, conflicto de
claims. Esa correspondencia está fijada por
[[Pruebas de Contrato MCP|tests de contrato]] sobre lo que el SDK publica al
cable, no sobre lo que devuelven los métodos.

## Lo que hay más allá de las tools

MCP tiene seis superficies. Jaravi cubre cinco: tools, **recursos**, **prompts**,
progreso y cancelación. La sexta, *sampling*, se descartó a propósito porque
delegaría en el cliente exactamente la parte que Jaravi existe para no delegar.
Ver [[Recursos y prompts|recursos y prompts]] y
[[Brechas del Protocolo MCP|brechas del protocolo]].

Dos sub-agentes editando los mismos ficheros a la vez producen un resultado que
no es de ninguno de los dos. Los **claims** permiten lanzarlos en paralelo
declarando qué escribe cada uno.

## Reclamar rutas

```bash
jaravi spawn --agent codex --task "refactoriza la autenticación" \
  --claims "src/Auth/**"
```

```text
spawn_agent(agent: "codex", task: "…", claims: ["src/Auth/**"])
```

Mientras esa sesión viva, esas rutas son suyas. Los claims se liberan solos al
llegar la sesión a estado terminal —completada, fallida o matada—.

## Qué pasa ante un conflicto

`onConflict` decide, y su valor por defecto es el conservador:

| Valor | Comportamiento |
|---|---|
| `reject` *(por defecto)* | La llamada falla con un error estructurado que **nombra la sesión poseedora**. |
| `queue` | La sesión queda en estado `Queued`; el motor la arranca sola cuando el claim se libera. |

```bash
jaravi spawn --agent claude --task "documenta la autenticación" \
  --claims "src/Auth/**" --on-conflict queue
```

```text
session 3c4d91 · queued  (queuedBehind: 1a2b04)
```

La respuesta incluye `queuedBehind`, y `list_sessions` lo muestra también: en
todo momento se sabe quién espera a quién.

## Cómo se detecta el solapamiento

`ClaimRegistry` normaliza cada glob a su **raíz sin comodines** y considera que
dos claims chocan si una raíz es prefijo de la otra.

| Claim A | Claim B | ¿Chocan? |
|---|---|---|
| `src/Auth/**` | `src/Auth/Tokens/*.cs` | Sí — B está dentro de A |
| `src/Auth/**` | `src/Api/**` | No |
| `src/Auth/**` | `src/**` | Sí — A está dentro de B |

> [!note] Detección conservadora a propósito
> Comparar raíces es menos preciso que evaluar los globs completos: puede
> declarar un conflicto donde en la práctica no lo habría. Se prefiere así. El
> coste de un falso conflicto es una espera; el de un falso «no hay conflicto»
> son dos agentes pisándose los ficheros.

## `Queued` no es un estado terminal

> [!tip] Encolar y esperar es la forma de serializar
> `await_session` espera también a las sesiones encoladas. Eso significa que
> encolar con `--on-conflict queue` y luego esperar es todo lo que hace falta
> para serializar escrituras, sin lógica extra por parte de quien orquesta.

La máquina de estados completa es:

```text
Created → Starting → Running ⇄ WaitingInput → Completed | Failed | Killed
                 ↑
              Queued
```

Cuando cualquier sesión llega a estado terminal, el `SessionManager` recorre la
cola en orden FIFO y arranca las que ya no chocan con nadie.

## Un patrón de uso

Tres auditorías en paralelo —solo leen, no reclaman nada— y después una
corrección serializada por zona:

```bash
# Fase 1: leer en paralelo, sin claims
A=$(jaravi spawn --agent codex    --task "audita src/api"  --quiet)
B=$(jaravi spawn --agent opencode --task "audita src/web"  --quiet)
for id in "$A" "$B"; do jaravi await "$id"; done

# Fase 2: escribir, cada uno en lo suyo, encolando si se cruzan
jaravi spawn --agent claude --task "corrige la api" --input-from "$A" \
  --claims "src/api/**" --on-conflict queue
jaravi spawn --agent claude --task "corrige la web" --input-from "$B" \
  --claims "src/web/**" --on-conflict queue
```

Combinado con [[Encadenar agentes|pipelines]], el resultado es un grafo de
trabajo que el motor coordina solo.

## Límite de concurrencia

Independientemente de los claims, `Engine:MaxConcurrentSessions` (8 por defecto)
limita cuántos procesos hijos viven a la vez. Lo que excede ese cupo entra en la
misma cola FIFO —no se rechaza—. Ver [[Configuración|configuración]].

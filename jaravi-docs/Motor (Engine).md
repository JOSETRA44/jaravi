---
tags: [jaravi, motor, engine, session-manager, procesos]
---

# Motor (Engine)

`Jaravi.Engine` es el cerebro de Jaravi. Implementa los puertos definidos en [[Arquitectura|Jaravi.Core]] y orquesta el ciclo de vida completo de las sesiones de subagentes.

## Componentes principales

### SessionManager
Núcleo del motor: lanza sesiones, bombea output, aplica watchdog de timeout y detecta bloqueos en entrada.

- `SpawnAsync` — valida el perfil, comprueba [[Operacion|ScopeGate]], arranca el proceso y lanza una tarea de supervisión asíncrona.
- `SendInputAsync` — escribe a stdin del subproceso; lanza `NotSupportedException` si el perfil tiene `closeStdin: true`.
- `AwaitSessionAsync` — punto de sincronización determinista: bloquea hasta estado terminal o `WaitingInput`.
- `GetSummary` — digest compacto con líneas de error extraídas vía regex `\b(error|exception|failed|fatal|denied|traceback)\b`.
- Máquina de estados: `Created → Starting → Running ⇄ WaitingInput → Completed | Failed | Killed`.

### PipeProcessFactory
Estrategia pipe-based ([[Operacion|stdio]]). Redirige stdin/stdout/stderr del hijo. Las CLIs detectan la ausencia de TTY y entran en modo no interactivo.

- `closeStdin` cierra stdin inmediatamente al lanzar — esencial para CLIs one-shot que leen piped stdin hasta EOF.
- `KillTree()` mata todo el árbol de procesos.

### ChannelEventBus
Pub/sub asíncrono in-process con `BoundedChannelFullMode.DropOldest`. Cada suscriptor (Dashboard, WebSocket) tiene un buffer de 4096 eventos. Un suscriptor lento nunca back-pressurea al motor.

### RingBufferLogStore
Buffer circular de 10 000 líneas por sesión. Lecturas siempre acotadas por `MaxReadLines` (500 por defecto). Soporta filtros `Tail`, `Grep` (regex) y `SinceSeq` para paginación.

### AnsiSanitizer
Limpia secuencias de escape ANSI/VT y caracteres de control. El resultado es texto limpio y determinista para el agente jefe.

### ScopeGate
Valida que el `workdir` esté dentro de `AllowedRoots`. Previene que un prompt malicioso ejecute subagentes fuera de los directorios autorizados.

> [!note] EngineOptions se configura en `appsettings.json`
> ```json
> { "Engine": { "AllowedRoots": ["..."], "MaxConcurrentSessions": 8,
>   "MaxReadLines": 500, "LogBufferCapacity": 10000 } }
> ```


## ClaimRegistry y cola de sesiones (v2)

`ClaimRegistry` implementa los bloqueos de rutas: cada claim (glob) se
normaliza a su raíz sin comodines y dos claims chocan si una raíz es prefijo
de la otra (detección conservadora). Los claims se sostienen mientras la
sesión corre y se liberan al llegar a estado terminal.

- Conflicto + `reject` → `ClaimConflictException` con la sesión poseedora.
- Conflicto (o sin slot libre) + `queue` → estado `Queued`; al terminar
  cualquier sesión, el `SessionManager` recorre la cola FIFO y arranca las que
  ya no chocan.

## Pipelines de sesiones (v2)

`SpawnRequest.InputFrom` referencia una sesión **terminal**: el motor renderiza
su resultado (`summary`, `tail` acotado a 100 o `errors`) como bloque
determinista y lo concatena al task antes del `PromptTemplate` del perfil.
Así un auditor alimenta a un corrector sin que el jefe copie nada.

> [!tip] El estado Queued no es terminal
> `await_session` espera también a las sesiones encoladas: encolar y esperar
> es la forma de serializar escrituras sin lógica extra en el jefe.

Véase también: [[Servidor MCP]], [[Perfiles de Agentes]]

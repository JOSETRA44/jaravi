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

Véase también: [[Servidor MCP]], [[Perfiles de Agentes]]

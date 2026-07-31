---
tags: [jaravi, mcp, investigacion, protocolo, brechas]
---

# Brechas del Protocolo MCP

MCP tiene 6 superficies: **tools**, resources, prompts, progress, cancellation,
elicitation. Antes de la ronda v0.6.0, [[Servidor MCP|Jaravi]] solo usaba
**una** — tools. Esta nota documenta la investigación de qué faltaba y qué se
aplicó, más un feedback real de consumo externo que la disparó.

## El disparador: un consumidor externo real

Un agente orquestador (Antigravity) intentó usar `jaravi-mcp` y reportó 4
bloqueos en `observaciones.md`. **Lección metodológica clave: 2 de los 4
diagnósticos eran falsos** — siempre reproducir antes de arreglar:

| Diagnóstico reportado | Realidad verificada |
|---|---|
| "stdio contamina stdout" | ❌ Falso — `--stdio` ya era JSON puro. El bug real: el modo **por defecto** (sin flag) arrancaba HTTP y logueaba a stdout |
| "opencode se cuelga esperando stdin" | ❌ Falso — con stdin cerrado responde en 17s exit 0. El bug real: `run_agent` bloqueaba hasta 600s, sobre el timeout de cualquier cliente |
| `--help` cuelga | ✅ Cierto |
| Ruido en stderr / sin Swagger | ✅ Ciertos |

Arreglos de esa ronda (v0.5.0): `CommandLine.cs` con `--help`/`--version` que
responden antes de arrancar el host, autodetección de modo (`stdin` pipe →
stdio, TTY → HTTP), stdout sagrado en todos los modos, `run_agent.maxWaitSec`
600→90 con campo `nextStep` accionable, y Swagger/OpenAPI. Ver [[Operacion]].

## Brechas de protocolo investigadas (v0.6.0)

Se comparó la superficie usada por Jaravi contra el SDK oficial
(`ModelContextProtocol.Core`, inspeccionando su XML de documentación) y contra
el spec MCP. Tres brechas de alto impacto, tres aplicadas:

### 1. Anotaciones de tools — aplicado
Sin `readOnlyHint`/`destructiveHint`/`idempotentHint`/`openWorldHint`, un
cliente no puede distinguir `kill_agent` (destructivo) de `list_agents`
(solo lectura) — pide permiso para todo, o para nada. Las 11 tools quedaron
anotadas; verificado en `tools/list` que `kill_agent` sale con
`"destructiveHint":true` y las 6 de lectura con `"readOnlyHint":true`.

### 2. Progress notifications — aplicado
El SDK auto-inyecta `IProgress<ProgressNotificationValue>` en cualquier tool
(no-op si el cliente no pidió progreso vía `_meta.progressToken` en la
llamada). `run_agent` y `await_session` ahora trocean su espera en slices de
5s y reportan progreso entre cada uno. **Verificado en vivo**: 8 notificaciones
reales `notifications/progress` durante una espera de 30s, con mensaje de
estado y conteo de líneas. Esto es exactamente lo que habría evitado que
Antigravity percibiera un "hang" — muchos clientes MCP resetean su propio
timeout al recibir progreso.

### 3. Cancelación no debe dejar huérfanos — aplicado, con caveat honesto
`run_agent` ahora captura `OperationCanceledException` cuando el *cliente*
cancela la llamada (no cuando `maxWaitSec` simplemente se agota — eso es un
camino distinto y deliberado que deja la sesión viva) y mata el árbol de
procesos antes de propagar la cancelación.

> [!warning] No confirmado end-to-end en stdio con el SDK preview
> El formato del mensaje de cancelación (`notifications/cancelled`,
> `CancelledNotificationParams.RequestId`) coincide exactamente con lo que
> documenta el propio SDK, que además afirma explícitamente que los parámetros
> `CancellationToken` "respetan" esa notificación. Pero en 4 rondas de prueba
> en vivo (tras resolver varios bugs de mi propio arnés de PowerShell:
> `StreamReader.Peek()` bloqueante, `Task.Run` con overload ambiguo, lecturas
> concurrentes sobre el mismo stream), la sesión de prueba siguió `Running`
> 15s después de cancelar — sin señal de que el `ct` del handler se cancelara.
> Es un paquete **preview** (`2.0.0-preview.1`); lo más probable es que la
> documentación XML ya describa el spec pero el wiring en runtime para
> transporte stdio aún no esté completo. El código de Jaravi es correcto y
> queda listo para cuando el SDK madure (y protege ya el camino HTTP, donde
> `RequestAborted` de ASP.NET es un mecanismo maduro e independiente de MCP).

### Descubrimiento colateral: el SDK despacha peticiones en paralelo
Verificado en la investigación: un `list_sessions` respondió en <1s mientras
un `run_agent` de hasta 90s seguía pendiente en la misma conexión stdio — el
transporte NO serializa peticiones. Esto es una propiedad valiosa y ya
correcta del stack, no algo que Jaravi tuviera que construir.

## Brechas de protocolo cerradas (v0.7.0)

Las dos brechas restantes de valor real quedaron aplicadas y verificadas en
vivo por JSON-RPC crudo sobre stdio (no solo "compila").

### 4. Resources — aplicado
`JaraviResources.cs` expone el catálogo de agentes y las sesiones como
recursos direccionables por URI (`jaravi://agents`, `jaravi://sessions`) más
tres plantillas de recurso (`jaravi://sessions/{sessionId}/summary|logs|errors`)
que el SDK resuelve por matching de `UriTemplate` contra el parámetro del
método. Es progressive disclosure real: un boss agent (o la lógica de carga
de contexto del propio cliente) puede leer el catálogo o la lista de sesiones
sin gastar un turno de tool-call. Los errores de recurso pasan por el mismo
`McpGuard` que las tools, así que `sessions/nope123/summary` responde con el
mismo `McpException` limpio que tendría `get_summary("nope123")` — una sola
fuente de verdad para el mapeo de errores.

**Verificado en vivo**: `resources/list` devuelve los 2 recursos directos,
`resources/templates/list` devuelve las 3 plantillas, `resources/read` sobre
`jaravi://agents` devuelve los 11 perfiles como JSON, y el caso negativo
(sessionId inexistente) da `McpException` código -32603 en vez de una
excepción sin manejar.

### 5. Prompts — aplicado
`JaraviPrompts.cs` expone dos plantillas de orquestación reutilizables vía
`[McpServerPromptType]`/`[McpServerPrompt]`:

- `delegate_task` — rellena una llamada a `run_agent` a partir de
  `profile`/`workdir`/`objective`/`constraints`, e incluye instrucciones sobre
  cómo leer el resultado (`timedOut` → seguir `nextStep`, no asumir fallo;
  `state:"Failed"` → revisar `errorLines` antes de reintentar).
- `audit_then_fix` — pipeline de dos etapas (auditor de solo lectura → fixer
  encadenado por `inputFromSessionId`) que documenta el patrón de "no leas tú
  mismo el hallazgo crudo, deja que el motor se lo pase al siguiente agente".

Un catálogo de tools por sí solo obliga a cada boss agent a redescubrir de
forma independiente la secuencia correcta de llamadas; los prompts entregan
esa secuencia ya rellenada, que es lo que realmente baja la barrera de
entrada para un agente que no interiorizó la doctrina de
[[Perfiles de Agentes|jaravi-orchestrator]]. Muchos clientes MCP
además exponen los prompts como entradas de menú tipo slash-command.

`JaraviPrompts` es una clase estática (no tiene estado ni dependencias) —
`WithPrompts<T>()` no compila para tipos estáticos (`CS0718`, C# no permite
un tipo estático como argumento genérico), así que el registro usa
`WithPromptsFromAssembly()`, que descubre el tipo por reflexión sobre el
atributo en vez de instanciarlo.

**Verificado en vivo**: `prompts/list` devuelve ambos con su descripción;
`prompts/get delegate_task` con argumentos reales renderiza el `ChatMessage`
ya relleno, listo para que el boss agent lo use tal cual.

## Brechas evaluadas y descartadas (con motivo)

- **Sampling** — que el servidor le pida al cliente que invoque su propio LLM.
  No aplica al caso de uso de Jaravi: el motor es determinista a propósito
  (ver [[Arquitectura]]), y Sampling delega precisamente la
  parte que Jaravi existe para NO delegar.

- **Elicitation** — descartado tras investigar la API real del SDK
  (`McpServer.ElicitAsync(ElicitRequestParams, ct)` y su overload genérico
  `ElicitAsync<T>(message, options, ct)`, inyectados vía el parámetro especial
  `McpServer`). El mecanismo existe y funciona a nivel de protocolo, pero dos
  razones lo descartan para Jaravi, no solo "no se exploró":
  1. **Depende de una capacidad de cliente que casi ningún host de Jaravi
     declara.** `ElicitAsync` requiere que el cliente anuncie
     `ClientCapabilities.Elicitation` (una UI de formulario/URL para pedirle
     algo a un humano) y lanza `InvalidOperationException` si no la declara.
     Los clientes reales de `jaravi-mcp` son boss agents (Claude Code, codex,
     opencode) corriendo mayormente en modo headless por stdio — ninguno de
     los probados en este proyecto implementa esa capacidad. Usarlo
     obligaría a envolver cada llamada en un try/catch de fallback,
     añadiendo complejidad permanente por una capacidad ausente en la
     práctica.
  2. **Contradice el modelo de orquestación de Jaravi.** El boss agent —no
     `jaravi-mcp`— es la capa que ya habla con el humano. Si un boss agent
     quiere confirmación antes de un `kill_agent`, puede pedírsela a su
     propio usuario en su propio turno de chat y solo entonces llamar a la
     tool; eso ya funciona hoy, en cualquier cliente, sin depender de que el
     host implemente formularios de elicitation. Meter una confirmación
     bloqueante dentro del motor determinista de Jaravi para replicar algo
     que la capa de arriba ya resuelve mejor no es una brecha, es la
     dirección equivocada.

  Mismo criterio que con Sampling: la superficie de protocolo de Jaravi se
  mantiene acotada a lo que mejora determinísticamente la orquestación
  agente-a-agente (tools, resources, prompts, progress, cancellation) y
  excluye lo que depende de comportamiento de cliente opcional y raramente
  implementado.

## Red de seguridad posterior (v0.7.1)

Toda esta superficie —anotaciones, URIs de recursos, plantillas de prompt— pasó
a estar cubierta por tests automáticos en `Jaravi.McpServer.Tests`, que afirman
sobre lo que el SDK publica al cable en vez de sobre nuestros métodos. Antes solo
existían arneses de PowerShell de un solo uso. Ver [[Pruebas de Contrato MCP]]
(incluye el hallazgo de que `WithPromptsFromAssembly()` resuelve contra el
assembly llamador, una fragilidad latente que el primer test destapó).

Véase también: [[Servidor MCP]], [[Operacion]], [[Catalogo de Agentes]], [[Pruebas de Contrato MCP]], [[Home|Jaravi]]

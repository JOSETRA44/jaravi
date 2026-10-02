Además de las once tools, Jaravi publica **recursos** —contexto de solo lectura
direccionable por URI— y **prompts**, que son plantillas de llamada. Un agente
jefe lee un recurso sin gastar un turno de tool-call.

## Recursos

| URI | Contenido |
|---|---|
| `jaravi://agents` | El catálogo de perfiles, igual que `list_agents`. |
| `jaravi://sessions` | Todas las sesiones y su estado, igual que `list_sessions`. |

Y tres **plantillas de recurso**, para el mismo dato de una sesión concreta:

| Plantilla | Contenido |
|---|---|
| `jaravi://sessions/{sessionId}/summary` | El digest de esa sesión. |
| `jaravi://sessions/{sessionId}/logs` | Su output, con el mismo tope de 500 líneas. |
| `jaravi://sessions/{sessionId}/errors` | Solo las líneas de error extraídas. |

> [!note] Misma información, distinto coste
> Una lectura cruzada verificada en vivo confirma que `list_agents` (tool) y
> `jaravi://agents` (recurso) devuelven datos consistentes. La diferencia no es
> el dato: es que un recurso puede inyectarse como contexto sin que el modelo
> tenga que decidir llamar a nada.

El mapeo de errores es el mismo que en las tools: una sesión inexistente da un
error de protocolo que la nombra, no un recurso vacío.

## Prompts

Dos plantillas, pensadas para bajar la barrera de entrada de un agente que no ha
interiorizado la doctrina de orquestación.

### `delegate_task`

Rellena una llamada a `run_agent` con instrucciones explícitas de cómo leer la
respuesta —en particular, qué hacer cuando `timedOut` es `true` y qué significa
`nextStep`—.

Es el antídoto al malentendido más caro del sistema: ver un timeout, concluir que
la tarea fracasó y relanzarla mientras la primera sigue trabajando.

### `audit_then_fix`

Un pipeline de dos etapas, auditor → corrector, encadenado por
`inputFromSessionId`. La plantilla deja hecha la parte que la gente olvida:
esperar a que la primera sesión llegue a estado terminal antes de encadenar, y
usar `inputKind: summary` en vez del output completo.

Ver [[Encadenar agentes|pipelines]].

## Las superficies que faltan

MCP define seis superficies. Jaravi usa cinco.

| Superficie | Estado |
|---|---|
| Tools | 11, todas anotadas. |
| Resources | 2 recursos + 3 plantillas. |
| Prompts | 2. |
| Progress | `run_agent` y `await_session` reportan cada 5 s. |
| Cancellation | El cliente cancela → se mata el árbol antes de propagar. |
| Sampling | **Descartada a propósito.** |

> [!important] Por qué no hay sampling
> *Sampling* permite al servidor pedirle al cliente que ejecute una inferencia.
> Usarlo delegaría en el cliente exactamente la parte que Jaravi existe para no
> delegar: la decisión determinista de qué se ejecuta y con qué límites. Un motor
> que le pide al modelo que decida deja de ser un motor.

**Elicitation** —pedirle datos al usuario mediante un formulario— también se
investigó y se descartó: exige que el cliente declare una capacidad de UI que
ningún host real de Jaravi implementa hoy, y duplicaría algo que el agente jefe
resuelve mejor hablando directamente con su propio usuario.

El razonamiento completo, incluyendo dos diagnósticos externos que resultaron
falsos al reproducirlos, está en
[[Brechas del Protocolo MCP|brechas del protocolo]].

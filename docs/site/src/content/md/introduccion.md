Jaravi convierte a un agente de IA de alto nivel —Claude Code, Codex, OpenCode o
cualquier cliente MCP— en el **jefe determinista de sub-agentes externos**. El
motor absorbe todo el output de los subprocesos y le entrega al jefe únicamente
respuestas compactas.

> [!abstract] En una línea
> Delegar trabajo a otra CLI de IA sin que su output te destruya el contexto.

## El problema

Un sub-agente que audita un repositorio produce entre mil y cincuenta mil líneas
para llegar a tres hallazgos. Si esas líneas viajan al contexto del agente que
orquesta, la ventana se agota antes que la tarea: el jefe deja de recordar qué
estaba haciendo, repite trabajo, o directamente pierde el hilo.

La respuesta habitual es pedirle al modelo, por prompt, que no lea demasiado.
Eso funciona hasta que deja de funcionar. Jaravi lo resuelve **estructuralmente**:
el tope está en el servidor, y ninguna llamada puede devolver más de 500 líneas
por mucho que quien llame insista.

## Cómo lo resuelve

1. **Absorbe.** El motor lanza el sub-agente como proceso hijo con I/O por pipes
   y se queda con todo su output en un ring buffer de 10 000 líneas por sesión.
2. **Limpia.** El `AnsiSanitizer` elimina escapes ANSI, spinners y control de
   cursor, de modo que lo almacenado es texto determinista y comparable.
3. **Resume.** `get_summary` devuelve exit code, duración y las líneas de error
   extraídas. Eso es lo que ve el jefe.
4. **Deja el resto disponible.** Nada se pierde: el output completo sigue
   consultable con `read_output` —acotado— y visible entero en el
   [[Control Center (Web)|Control Center]].

## Dos formas de llegar

Jaravi **no es solo un servidor MCP**. El mismo binario es un CLI completo, y esa
duplicidad no es un capricho: ningún cliente MCP relee su configuración a mitad
de sesión, así que un agente que descubre Jaravi ahora mismo no puede registrarlo
—pero sí puede ejecutarlo.

```bash
jaravi doctor                                   # ¿está usable aquí?
jaravi agents                                   # perfiles disponibles
jaravi run --agent codex --task "audita src/"   # delega y devuelve un resumen
```

Con un servidor vivo (`jaravi-mcp --http` en segundo plano) hay además ciclo
completo: `spawn` → `await` → `status` / `logs` / `sessions` / `kill`.

> [!important] Las sesiones son las mismas
> Si hay una instancia de Jaravi corriendo —incluida la que lanzó tu cliente MCP—
> el CLI se adjunta a ella. Lo que lances desde la shell es exactamente lo que ve
> el agente jefe por `list_sessions` y lo que aparece en el Control Center.

## Qué hay dentro

| Proyecto | Rol |
|---|---|
| `Jaravi.Core` | Dominio puro: modelos, eventos y puertos. Cero dependencias. |
| `Jaravi.Engine` | El motor: procesos, sesiones, bus de eventos, ring buffer, Scope Gate. |
| `Jaravi.McpServer` | Host headless: tools MCP, CLI de shell, telemetría y REST. |
| `Jaravi.Dashboard` | GUI WPF observadora; solo consume HTTP y WebSocket. |

La [[Arquitectura|arquitectura completa]] detalla cómo encajan y por qué las
dependencias apuntan siempre hacia el núcleo.

## Por dónde seguir

- **Nunca lo has instalado** → [[Instalación|instalación]], y luego
  [[Inicio rápido|inicio rápido]].
- **Tu agente se niega a usarlo** → [[Que tu agente lo use|desbloquéalo]].
- **Vienes a buscar un dato concreto** → [[CLI de Shell|referencia del CLI]] o
  [[Servidor MCP|referencia de tools]].
- **Quieres entender el diseño** → [[Arquitectura|arquitectura]] y
  [[Motor (Engine)|motor]].

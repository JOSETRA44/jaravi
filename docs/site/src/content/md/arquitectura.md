Jaravi sigue **Clean Architecture** con cuatro proyectos ensamblados por capas.
Las dependencias apuntan siempre hacia adentro: el dominio no sabe que existe
Kestrel, y el motor no sabe que existe MCP.

<figure class="archdiag">
<div class="archdiag__layer archdiag__layer--clients">
<p class="archdiag__label">Clientes · fuera del proceso</p>
<div class="archdiag__row">
<span class="archnode">Agente jefe<small>Claude Code · Codex · OpenCode · Gemini · Qwen · Copilot</small></span>
<span class="archnode">Control Center<small>navegador, servido por el propio binario</small></span>
<span class="archnode">Jaravi.Dashboard<small>WPF MVVM, cliente de escritorio opcional</small></span>
</div>
</div>

<p class="archdiag__wire">MCP (stdio o Streamable HTTP) · WebSocket <code>/ws/events</code> · REST <code>/api</code></p>

<div class="archdiag__layer archdiag__layer--host">
<p class="archdiag__label">Jaravi.McpServer · host Kestrel</p>
<div class="archdiag__row">
<span class="archnode archnode--sub">11 tools MCP</span>
<span class="archnode archnode--sub">2 recursos + 3 plantillas</span>
<span class="archnode archnode--sub">2 prompts</span>
<span class="archnode archnode--sub">CLI de shell</span>
<span class="archnode archnode--sub">REST + OpenAPI</span>
</div>
</div>

<p class="archdiag__wire">llama a</p>

<div class="archdiag__layer archdiag__layer--engine">
<p class="archdiag__label">Jaravi.Engine · el motor</p>
<div class="archdiag__row">
<span class="archnode archnode--sub">SessionManager</span>
<span class="archnode archnode--sub">RingBufferLogStore</span>
<span class="archnode archnode--sub">ChannelEventBus</span>
<span class="archnode archnode--sub">ClaimRegistry</span>
<span class="archnode archnode--sub">ScopeGate</span>
<span class="archnode archnode--sub">AnsiSanitizer</span>
</div>
</div>

<p class="archdiag__wire">implementa los puertos de</p>

<div class="archdiag__layer archdiag__layer--core">
<p class="archdiag__label">Jaravi.Core · dominio puro, cero dependencias</p>
<div class="archdiag__row">
<span class="archnode archnode--sub">Modelos</span>
<span class="archnode archnode--sub">Eventos</span>
<span class="archnode archnode--sub">Puertos</span>
</div>
</div>

<p class="archdiag__wire archdiag__wire--out">el motor lanza, por pipe I/O</p>

<div class="archdiag__layer archdiag__layer--sub">
<p class="archdiag__label">Sub-agentes · procesos hijos</p>
<div class="archdiag__row">
<span class="archnode archnode--proc">opencode</span>
<span class="archnode archnode--proc">codex</span>
<span class="archnode archnode--proc">claude</span>
<span class="archnode archnode--proc">gemini</span>
<span class="archnode archnode--proc">copilot</span>
<span class="archnode archnode--proc">…</span>
</div>
</div>

<figcaption>Las flechas van siempre hacia abajo. Nada del núcleo conoce a quien lo invoca.</figcaption>
</figure>

## Los cuatro proyectos

| Proyecto | Rol |
|---|---|
| `Jaravi.Core` | Dominio puro: modelos (`SessionState`, `LogEntry`, `SpawnRequest`), eventos polimórficos y puertos (`ISessionManager`, `ILogStore`, `IEventBus`, `IAgentRegistry`). Cero dependencias externas. |
| `Jaravi.Engine` | Implementa esos puertos. Procesos con I/O por pipes, `SessionManager`, bus de eventos, ring buffer de logs, registro de claims, Scope Gate y sanitizador ANSI. |
| `Jaravi.McpServer` | Host headless: tools MCP, recursos, prompts, **CLI de shell**, WebSocket de telemetría, REST y el Control Center embebido. |
| `Jaravi.Dashboard` | GUI WPF (MVVM) observadora. Solo consume HTTP y WebSocket; depende únicamente de `Jaravi.Core` por los DTOs. |

## Principios

1. **Dominio puro.** `Jaravi.Core` no referencia nada. Es lo que permite que el
   mismo motor viva dentro de un servidor Kestrel o dentro de un comando de
   shell de un solo uso sin cambiar una línea.
2. **El [[Motor (Engine)|motor]] implementa los puertos del núcleo.** Ahí está la
   lógica de sesiones, el bus de eventos, el
   [[Perfiles de Agentes|registro de agentes]] y el [[Operacion|Scope Gate]].
3. **El [[Servidor MCP]] solo expone.** Traduce entre el protocolo y el motor
   —incluido el mapeo de errores de dominio a errores de protocolo— y no toma
   ninguna decisión propia sobre sesiones.
4. **Los clientes observan.** Tanto el [[Control Center (Web)|Control Center]]
   como el Dashboard consumen `/ws/events` y `/api`. Ninguno puede saltarse el
   servidor para hablar con el motor.

> [!note] Dos implementaciones del mismo cliente
> El CLI habla con `IJaraviClient`, que tiene dos implementaciones:
> `RestJaraviClient` conduce una instancia viva por REST, e
> `InProcessJaraviClient` monta un motor privado dentro del propio comando.
> Esa abstracción es lo que hace que las sesiones lanzadas desde la shell sean
> **las mismas** que ve el agente jefe. Ver [[CLI de Shell]].

## Garantías anti-colapso

- Ring buffer de 10 000 líneas por sesión, con tope duro de lectura de 500.
- [[Operacion|Scope Gate]]: `workdir` validado contra `Engine:AllowedRoots`.
- Deadline duro por sesión con `Kill(entireProcessTree: true)`.
- Logs sanitizados sin secuencias ANSI mediante `AnsiSanitizer`.

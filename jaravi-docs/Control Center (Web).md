---
tags: [jaravi, dashboard, web, telemetria, kestrel, control-center]
---

# Control Center (Web)

Interfaz de observabilidad **web** servida por el propio [[Servidor MCP|jaravi-mcp]]
(v0.4.0). Cuando un orquestador empieza a controlar sub-agentes, el usuario abre
un `localhost` y ve en vivo qué hacen, cuánto tardan, qué archivos bloquean y qué
"piensan" — sin instalar nada. Reemplaza como interfaz por defecto al
[[Dashboard]] WPF (que queda como cliente de escritorio secundario).

> [!tip] Cero fricción
> Es una sola página HTML+CSS+JS **embebida como recurso del ensamblado** y
> servida en `/`. Sin wwwroot en disco, sin framework, sin build step — funciona
> desde el immutable tool store de la `dotnet tool`.

## Cómo se descubre la URL

Al arrancar, Kestrel imprime un banner en **stderr** (stdout es la tubería MCP)
con la URL exacta a abrir — indispensable cuando el puerto 5210 está ocupado y
cae a uno efímero. Ver [[Operacion]] para la cadena de resolución de puerto.

```
  ===== Jaravi Control Center =====
    open:  http://localhost:5210
    repo:  C:\Users\USER\source
  ================================
```

## Qué muestra (telemetría en vivo)

| Señal | Fuente | Detalle |
|---|---|---|
| Sub-agentes trabajando | eventos `SessionStarted/StateChanged/Exited` | tarjetas con badge de estado coloreado |
| Tiempo de ejecución | `SessionSnapshot.DurationSeconds` + `StartedAt` | tickea en vivo en el cliente, congela al terminar |
| Tokens | `TokenMeter` en el [[Motor (Engine)|motor]] | reportados por el agente (`codex`, gemini…) o estimados del volumen de log (marcados `~`) |
| Locks (claims) | `SessionSnapshot.Claims` + `queuedBehindSessionId` | qué sesión posee qué glob y quién está encolado detrás |
| "Pensamiento" | `LogBatchEmitted` | consola de log por sesión, auto-scroll, virtualizada (cap ~2000 líneas) |

## Arquitectura del transporte

- **WebSocket `/ws/events`** (el mismo que usa el [[Dashboard]] WPF) para lo
  instantáneo: cambios de estado y líneas de log. El `ChannelEventBus` con
  `DropOldest` garantiza que una pestaña lenta **jamás frene al motor** — la
  propiedad de escalabilidad ya estaba construida.
- **Poll ligero de `GET /api/sessions` cada 2 s** para la fila de métricas
  (tokens, duración, claims). Evita añadir tipos de evento al contrato de
  `Jaravi.Core` y es robusto ante reconexión.
- Se descartó SignalR (paquete pesado para reimplementar lo que ya funciona) y
  SSE (el WS ya existe y es bidireccional a futuro).

## Multi-instancia (varios repos a la vez)

Cada `jaravi-mcp` registra `%APPDATA%\jaravi\instances\<pid>.json` con
`{pid, url, repoRoot, startedAt}` y lo borra al apagarse. `GET /api/instances`
lista las vivas y **poda las de PIDs muertos**. El dashboard muestra qué repo
controla la instancia actual y ofrece un selector para saltar entre las hermanas.

Véase también: [[Dashboard]], [[Servidor MCP]], [[Operacion]], [[Motor (Engine)]], [[Home|Jaravi]]

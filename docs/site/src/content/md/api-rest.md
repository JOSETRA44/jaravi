Kestrel no solo sirve el endpoint MCP. Expone también una REST completa y un
WebSocket de telemetría, y ambos siguen activos **incluso en modo stdio** —que es
lo que permite que el CLI se adjunte a la instancia que lanzó tu cliente MCP—.

## Endpoints

| Ruta | Propósito |
|---|---|
| `GET /mcp` | Endpoint MCP (Streamable HTTP, solo en modo HTTP). |
| `WS /ws/events` | Stream de eventos JSON polimórfico. |
| `GET /api/agents` | Lista de perfiles. |
| `GET /api/sessions` | Lista de sesiones. |
| `POST /api/sessions` | Crear sesión (spawn). |
| `GET /api/sessions/{id}` | Snapshot de una sesión. |
| `GET /api/sessions/{id}/logs` | Logs paginados. |
| `GET /api/sessions/{id}/summary` | Resumen compacto. |
| `POST /api/sessions/{id}/kill` | Matar la sesión. |
| `POST /api/sessions/{id}/input` | Enviar input a stdin. |
| `GET /api/instances` | Instancias vivas de `jaravi-mcp` (poda los PID muertos). |
| `GET /` | [[Control Center (Web)|Control Center]] embebido. |
| `GET /swagger` | UI de OpenAPI (solo en modo HTTP). |
| `GET /swagger/v1/swagger.json` | Documento OpenAPI. |
| `GET /healthz` | Health check. |

> [!tip] Guionizar sin lidiar con JSON-RPC
> El documento OpenAPI existe justamente para eso: automatizar contra Jaravi
> desde un script sin implementar el framing de JSON-RPC ni negociar un
> handshake MCP.

## Eventos del WebSocket

`/ws/events` emite JSON con un discriminador `type`:

| `type` | Cuándo |
|---|---|
| `sessionStarted` | Se creó una sesión nueva. |
| `sessionStateChanged` | Transición en la máquina de estados. |
| `logBatchEmitted` | Un lote de líneas de output. |
| `sessionExited` | La sesión terminó, con su exit code. |

```json
{ "type": "logBatchEmitted", "sessionId": "7f3a2c",
  "lines": [{ "seq": 412, "stream": "stdout", "text": "…" }] }
```

> [!note] Un suscriptor lento no frena al motor
> El `ChannelEventBus` da a cada suscriptor un buffer de 4 096 eventos con
> `BoundedChannelFullMode.DropOldest`. Una pestaña de navegador que no consume a
> tiempo pierde eventos —recuperables por REST— antes que aplicar back-pressure
> a los procesos hijos.

## Registro de instancias

Cada `jaravi-mcp` registra `%APPDATA%\jaravi\instances\<pid>.json` al arrancar.
`GET /api/instances` devuelve las vivas, y de paso poda las entradas cuyo proceso
ya no existe.

> [!warning] El registro miente, y por eso se sondea
> Windows reutiliza PIDs: una entrada de hace semanas puede parecer viva porque
> otro proceso heredó su número. Antes de adjuntarse, el CLI hace `GET /healthz`
> con 2 s de timeout y pasa a la siguiente si no responde.

Esto es también lo que sostiene el selector multi-instancia del Control Center:
con varios proyectos abiertos, puedes saltar entre los repos que estés
orquestando.

## Cómo elige el CLI a qué instancia adjuntarse

El factory prefiere la instancia cuyo `repoRoot` **contiene** el `workdir`, no la
más reciente. Con varios proyectos abiertos, esa es la que tiene el Scope Gate
correcto y el dashboard que estás mirando.

La comprobación es consciente de segmentos: `C:\src\app` no es ancestro de
`C:\src\application`.

Para forzarlo:

```bash
jaravi run --url http://localhost:5211 --agent codex --task "…"   # una instancia concreta
jaravi run --no-attach --agent codex --task "…"                   # motor privado, sin adjuntarse
```

## Puerto

La resolución es, en orden: `JARAVI_URL` (variable de entorno) →
`ASPNETCORE_URLS` → `Urls` en la configuración → `http://localhost:5210`.

Si el puerto está ocupado, el servidor **cae a un puerto efímero** y lo anuncia
—en el banner de stderr y en `/api/instances`— en vez de fallar. Es lo que
permite tener varias instancias concurrentes, una por repositorio.

```text
  ===== Jaravi Control Center =====
    open:  http://localhost:5210
    repo:  C:\Users\USER\source
  ================================
```

Ese banner va a **stderr**, no a stdout, porque en stdio stdout solo puede llevar
JSON-RPC. Ver [[Operacion|modos de operación]].

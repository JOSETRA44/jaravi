---
tags: [jaravi, operacion, stdio, http, despliegue, scope-gate]
---

# Operación

Jaravi soporta dos modos de operación: **zero-touch stdio** y **HTTP compartido**.

## Modo stdio (zero-touch)

El archivo `.mcp.json` del proyecto apunta al ejecutable compilado con `--stdio`:

```bash
dotnet build Jaravi.McpServer
# .mcp.json ya referencia Jaravi.McpServer/bin/Debug/net8.0/Jaravi.McpServer.exe
```

En este modo:
- El agente jefe (Claude Code) lanza Jaravi como proceso hijo.
- El MCP habla por stdin/stdout (JSON-RPC).
- Todos los logs del servidor van a stderr.
- Kestrel igualmente levanta WebSocket `/ws/events` y REST `/api` para el [[Dashboard]].
- Si el puerto 5210 ya está ocupado, Kestrel elige un puerto efímero automáticamente.

## Modo HTTP (compartido)

```bash
dotnet run --project Jaravi.McpServer
```

- MCP en `http://localhost:5210/mcp` (Streamable HTTP).
- Múltiples agentes jefe pueden compartir el mismo servidor.
- El [[Dashboard]] y el MCP coexisten en el mismo proceso.

## Scope Gate: seguridad de directorios

El `workdir` de cada subagente se valida contra `Engine:AllowedRoots` en `appsettings.json`:

```json
"Engine": {
  "AllowedRoots": ["C:\\Users\\USER\\source"]
}
```

El [[Motor (Engine)|ScopeGate]] rechaza cualquier `workdir` fuera de las raíces autorizadas con error 403. Esto previene que un prompt malicioso ejecute subagentes en directorios arbitrarios.

## Comandos de uso diario

```bash
dotnet build Jaravi.McpServer           # Compilar
dotnet run --project Jaravi.McpServer   # Modo HTTP
dotnet run --project Jaravi.Dashboard   # GUI observadora
dotnet test                             # Suite completa (35+ tests)
```

> [!tip] El Dashboard funciona en ambos modos
> Tanto en stdio como en HTTP, Kestrel siempre expone WS/REST. El Dashboard se conecta a `http://localhost:5210` sin importar el modo del servidor.

## Mecanismos de protección

1. **Ring buffer**: 10 000 líneas por sesión, lectura máxima 500 líneas.
2. **Deadline duro**: timeout configurable por sesión (30 min por defecto); al excederse, `Kill(entireProcessTree: true)`.
3. **Watchdog de idle**: detecta sesiones sin output y las marca como `WaitingInput`.
4. **Logs sanitizados**: el `AnsiSanitizer` elimina escapes ANSI antes de almacenar.

Véase también: [[Servidor MCP]], [[Perfiles de Agentes]], [[Motor (Engine)]]

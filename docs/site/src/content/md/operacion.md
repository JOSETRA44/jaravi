Jaravi soporta dos modos de operación: **zero-touch stdio** y **HTTP compartido**.

## Selección de modo (v0.5.0)

`jaravi-mcp` decide su modo antes de arrancar ASP.NET:

| Invocación | Modo | Por qué |
|---|---|---|
| `--help` / `--version` | ninguno | responde y sale (antes arrancaba un web server y colgaba al que preguntaba) |
| `--stdio` | stdio forzado | para configs de clientes MCP |
| `--http` | HTTP forzado | para el [[Control Center (Web)]] y scripting REST |
| sin flag | **auto** | `stdin` es pipe → stdio (nos lanzó un cliente MCP); `stdin` es terminal → HTTP (nos lanzó un humano) |

> [!important] stdout es sagrado
> En todos los modos, `stdout` solo lleva JSON-RPC. Todo log va a `stderr`, y en
> stdio se silencia el ruido del framework. Así, apuntar cualquier cliente MCP al
> ejecutable desnudo funciona — antes, sin el flag, los logs de ASP.NET iban a
> stdout y rompían el parser del cliente.

## Modo stdio (zero-touch)

`.mcp.json` (y el equivalente de cada CLI, ver abajo) apunta al comando global
**`jaravi-mcp`** (instalado como `dotnet tool`, no una ruta al build local):

```json
{ "mcpServers": { "jaravi": { "type": "stdio", "command": "jaravi-mcp", "args": ["--stdio"] } } }
```

En este modo:
- El agente jefe lanza Jaravi como proceso hijo — sin intervención humana.
- El MCP habla por stdin/stdout (JSON-RPC).
- Todos los logs del servidor van a stderr.
- Kestrel igualmente levanta WebSocket `/ws/events` y REST `/api` para el [[Dashboard]].
- Si el puerto 5210 ya está ocupado, Kestrel elige un puerto efímero automáticamente
  (resolución: `JARAVI_URL` env → `ASPNETCORE_URLS` → config `Urls` → `:5210`).

## Cualquier CLI puede ser el jefe

Jaravi es agnóstico del cliente MCP. Registrado y verificado en esta máquina:

| CLI | Mecanismo | Estado |
|---|---|---|
| Claude Code | `.mcp.json` del repo | ✅ en uso desde el inicio del proyecto |
| OpenCode | `opencode.jsonc` del repo | ✅ verificado con `opencode mcp list` |
| Codex | `codex mcp add jaravi -- jaravi-mcp --stdio` | ✅ verificado con `codex mcp list` |
| Antigravity | `~/.antigravity/config/mcp_config.json` | ✅ entrada agregada (formato validado) |

La skill [[Perfiles de Agentes|jaravi-orchestrator]] (la doctrina de cómo actuar
de jefe) se distribuye vía el store universal `~/.agents/skills/`, enlazado a
las carpetas de skills de OpenCode y Codex — instalable con
`npx skills add JOSETRA44/jaravi@jaravi-orchestrator` una vez publicado el repo.

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
dotnet test                             # Suite completa (54 tests)
```

> [!tip] El Dashboard funciona en ambos modos
> Tanto en stdio como en HTTP, Kestrel siempre expone WS/REST. El Dashboard se conecta a `http://localhost:5210` sin importar el modo del servidor.

## Mecanismos de protección

1. **Ring buffer**: 10 000 líneas por sesión, lectura máxima 500 líneas.
2. **Deadline duro**: timeout configurable por sesión (30 min por defecto); al excederse, `Kill(entireProcessTree: true)`.
3. **Watchdog de idle**: detecta sesiones sin output y las marca como `WaitingInput`.
4. **Logs sanitizados**: el `AnsiSanitizer` elimina escapes ANSI antes de almacenar.

Véase también: [[Servidor MCP]], [[Perfiles de Agentes]], [[Motor (Engine)]]

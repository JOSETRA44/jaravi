# Observaciones y Feedback: Jaravi MCP Server

Como "agente consumidor" que ha intentado acoplarse y utilizar `jaravi-mcp` para delegar tareas a agentes externos, aquí detallo el feedback técnico, deficiencias y puntos de fricción encontrados al usar el producto en un entorno real.

## 1. Contaminación del Canal Stdio (Crítico para MCP)
El protocolo MCP sobre `stdio` exige de manera estricta que **únicamente** se impriman mensajes JSON-RPC válidos por la salida estándar (`stdout`). 
- **Problema:** Al invocar `jaravi-mcp` sin argumentos, el sistema levanta el servidor web por defecto y escupe logs de ASP.NET Core (`info: Jaravi.McpServer[0]...`) directamente en `stdout`. Esto rompe instantáneamente el parser JSON de cualquier cliente MCP estándar que intente conectarse.
- **Mitigación Actual:** Es **obligatorio** usar el flag `--stdio` para que funcione, pero incluso entonces, la configuración requiere que los clientes sepan inyectar ese flag (como noté que hiciste en `.mcp.json`). Si un usuario de Claude Desktop o de Antigravity intenta apuntar al ejecutable nativo sin el flag, fallará silenciosamente.

## 2. Invocación de Sub-Agentes y "Hanging" (Bloqueos)
Al ejecutar el endpoint MCP `tools/call` invocando a `run_agent` con el perfil `opencode` (`"unattended": true` por defecto), la llamada queda bloqueada (hanging) y no retorna el JSON-RPC esperado dentro de tiempos razonables (esperé >15s y tuve que forzar el cierre).
- **Diagnóstico:** Es altamente probable que `opencode` (u otros agentes subyacentes) estén intentando leer de `stdin` (pidiendo confirmación o detectando falta de TTY) y se queden esperando eternamente. 
- **Sugerencia:** El motor `Jaravi.Engine` debería tener un mecanismo de *fail-fast* más agresivo o inyectar forzosamente un cierre de `stdin` (`closeStdin: true`) a nivel de proceso si detecta que la ejecución `unattended` está atascada esperando TTY, de lo contrario, el agente jefe (yo) se queda bloqueado esperando el resumen.

## 3. Ausencia de Documentación REST / Swagger
El servidor indica en el log: `Telemetry/REST listening on http://127.0.0.1:XXXX`.
- **Problema:** Intenté consultar `http://127.0.0.1:XXXX/swagger/v1/swagger.json` para interactuar con la API REST y evadir las complicaciones de stdio, pero el endpoint no devuelve nada. 
- **Sugerencia:** Si existe una API REST, exponer Swagger/OpenAPI embebido facilitaría inmensamente el debugging para desarrolladores y permitiría crear scripts de integración más rápidos sin lidiar con el framing de JSON-RPC sobre stdio.

## 4. Ruido en STDERR
Incluso al ejecutar con `--stdio`, Jaravi sigue emitiendo los logs de ciclo de vida de ASP.NET Core (`Application started...`, `Hosting environment...`) a través de `stderr`.
- Si bien la especificación MCP *permite* escribir logs al `stderr`, el exceso de verbosidad del framework subyacente (.NET) puede inundar los logs del cliente padre. Sería ideal suprimir los logs de nivel `Info` del framework cuando se corre en modo `--stdio`.

## Conclusión del Experimento
El producto tiene una arquitectura potentísima (el Control Center en HTML es un gran valor añadido), pero **el acoplamiento inicial como cliente puro es friccionante**. El principal foco a corregir debería ser blindar la robustez del subproceso (evitar que se quede colgado esperando input interactivo) y asegurar que el ejecutable sea un "ciudadano ejemplar" del protocolo MCP (JSON puro en stdout, silencio o errores reales en stderr).

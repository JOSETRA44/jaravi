---
tags: [jaravi, teams, runtimes, investigacion]
---

# Matriz de Runtimes (fase 0 de [[Jaravi Teams]])

Pregunta que responde: **¿por qué vía puede Jaravi entregar un mensaje a un agente que ya
está trabajando, de forma que llegue a su contexto y no solo a una base de datos?**

Medido el 2026-11-14 en esta máquina con procesos reales: un cliente ACP de prueba, un
probe `claude -p --input-format stream-json` y un probe `codex app-server`. Todos inyectan
un servidor MCP que registra su arranque y su entorno, y piden dos turnos en la misma
sesión. El segundo turno es justo lo que necesita un mensaje de equipo.

| Agente | Versión | Protocolo vivo | MCP inyectado conectó | Llamó a la tool | 2º turno en la misma sesión | Hereda env al MCP | Notas |
|---|---|---|---|---|---|---|---|
| Antigravity (agy) | 1.5.8 | ACP `agy acp` | sí | sí | sí (8 s) | sí | `sessionCapabilities`: resume, load, close |
| Gemini CLI | 0.47.0 | ACP `--acp` | sí | sí | sí (29 s) | sí | `loadSession` |
| Claude Code | 2.1.325 | stream-json (`-p --input-format stream-json`) | sí (`--mcp-config`) | sí | sí (3 s) | sí | `--append-system-prompt` lleva el charter |
| Copilot CLI | 1.0.109 | ACP `--acp` | **no**: ignora `mcpServers` de `session/new` | — | sí (1.5 s) | — | inyectar con `--additional-mcp-config` al lanzar |
| OpenCode | 1.18.34 | ACP `opencode acp` | sí | — | — | sí | el turno falló por la API key de OpenCode caducada (credencial del usuario) |
| Qwen Code | 0.27.2 | ACP `--acp` | sí | — | — | sí | el turno falló porque el modelo configurado ya no es gratuito (config del usuario) |
| Codex CLI | npm local | `codex app-server` (JSON-RPC propio) | sí (`thread/start.config.mcp_servers`) | — | — | **no** | el turno falló: el modelo configurado exige actualizar el CLI. Tiene `turn/steer`, que inyecta a mitad de turno |
| Kiro CLI | — | ACP según docs, `serve` WebSocket | sin probar | | | | |
| Cursor | no instalado | ACP según docs | sin probar | | | | |

## Conclusiones de diseño

1. **Entrega real = un turno nuevo en una sesión viva.** ACP, Claude stream-json y Codex
   app-server lo permiten. Es la vía principal (tier A), no un caso raro.
2. **La identidad se inyecta, no se hereda.** Codex filtra el env de sus servidores MCP.
   La identidad del miembro viaja siempre en la config del MCP inyectado: env explícito
   o argumentos. La herencia queda como mejora oportunista.
3. **La vía de inyección del MCP varía por agente** (`session/new`, flag de arranque, config
   del thread). Por eso es un dato del perfil, no lógica cableada.
4. Los fallos de OpenCode, Qwen y Codex fueron de **credenciales o modelo del usuario**, no
   del mecanismo. Jaravi debe distinguir "el runtime no soporta X" de "el agente falló al
   ejecutar" y decirlo.

## Reproducir

Los probes estaban en el scratchpad de la sesión: `acp-probe.mjs`, `claude-stream-probe.mjs`,
`codex-probe.mjs` y `probe-mcp.mjs`. Los tests de integración del motor recogen su esencia
como fixtures.

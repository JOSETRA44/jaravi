---
tags: [jaravi, arquitectura, clean-architecture, diagrama]
---

# Arquitectura

Jaravi sigue **Clean Architecture** con cuatro proyectos ensamblados jerárquicamente. Las dependencias apuntan siempre hacia adentro del núcleo.

```mermaid
flowchart TB
    Boss["Agente Jefe<br/>(Claude Code, OpenCode, Codex o Antigravity)"]
    Dashboard["Jaravi.Dashboard<br/>(WPF MVVM)"]
    McpServer["Jaravi.McpServer<br/>(Kestrel)"]
    Engine["Jaravi.Engine<br/>(SessionManager)"]
    Core["Jaravi.Core<br/>(Modelos / Puertos)"]
    Sub["Sub-agentes CLI<br/>(opencode, codex, claude, copilot, antigravity…)"]

    Boss -- MCP: stdio o Streamable HTTP --> McpServer
    Dashboard -- WS /ws/events + REST --> McpServer
    McpServer --> Engine
    Engine --> Core
    Dashboard --> Core
    Engine -- pipe I/O --> Sub
```

## Principios

1. **Dominio puro** — `Jaravi.Core` no tiene dependencias externas. Define modelos (`SessionState`, `LogEntry`, `SpawnRequest`), eventos polimórficos y puertos (`ISessionManager`, `ILogStore`, `IEventBus`, `IAgentRegistry`).
2. **[[Motor (Engine)|Motor]]** implementa los puertos del Core. Contiene la lógica de sesiones, el bus de eventos, el [[Perfiles de Agentes|registro de agentes]], el ring buffer de logs y el [[Operacion|Scope Gate]].
3. **[[Servidor MCP]]** expone el motor mediante Kestrel. Ofrece 10 tools MCP, un endpoint WebSocket `/ws/events` para telemetría en vivo y una API REST `/api` para control y consulta. Es agnóstico de cliente: cualquier CLI que hable MCP puede conectarse y volverse el jefe — ver [[Operacion#Cualquier CLI puede ser el jefe|quién puede serlo]].
4. **[[Dashboard]]** consume HTTP y WebSocket. Solo depende de `Jaravi.Core` (DTOs compartidos). Usa MVVM con CommunityToolkit.Mvvm.

## Garantías anti-colapso

- Ring buffer de 10 000 líneas por sesión + tope duro de lectura de 500 líneas.
- [[Operacion|Scope Gate]]: workdir validado contra `Engine:AllowedRoots`.
- Deadline duro por sesión con `Kill(entireProcessTree: true)`.
- Logs sanitizados sin secuencias ANSI mediante `AnsiSanitizer`.

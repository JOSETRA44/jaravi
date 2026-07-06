---
tags: [jaravi, indice, documentacion, orquestacion]
---

# Jaravi — Ecosistema de Orquestación de Sub-Agentes

Jaravi convierte un agente de IA de alto nivel (Claude Code, OpenCode, Copilot CLI) en el **jefe determinista** de subagentes externos. El motor absorbe todo el output de los subprocesos y entrega al jefe solo respuestas compactas, mientras que el [[Dashboard]] observa el *firehose* completo en tiempo real.

> [!tip] Filosofía
> El agente jefe nunca ve el output crudo de los subprocesos. Así se evita el colapso de contexto.

## Proyectos de la solución

| Proyecto | Rol |
|---|---|
| `Jaravi.Core` | Dominio puro: modelos, eventos, puertos. Sin dependencias externas. |
| `Jaravi.Engine` | [[Motor (Engine)|Motor]]: procesos, sesiones, event bus, ring buffer, Scope Gate, sanitizador ANSI. |
| `Jaravi.McpServer` | [[Servidor MCP]]: host Kestrel con 9 tools MCP + WebSocket + REST. |
| `Jaravi.Dashboard` | [[Dashboard]] GUI WPF (MVVM) observadora vía HTTP/WebSocket. |
| `Jaravi.Engine.Tests` | xUnit: 36 tests incluyendo E2E contra procesos reales. |

## Navegación rápida

- [[Motor (Engine)|Motor]] — SessionManager, PipeProcessFactory, ChannelEventBus, ScopeGate
- [[Servidor MCP]] — Tools MCP, modos stdio y HTTP, agents.json
- [[Dashboard]] — GUI WPF, MVVM, consumo de API REST/WS
- [[Perfiles de Agentes]] — agents.json declarativo, lección closeStdin
- [[Operacion]] — Zero-touch stdio vs HTTP, despliegue
- [[Arquitectura]] — Diagrama y principios de Clean Architecture

> [!warning] Repositorio
> Código fuente en `C:\Users\USER\source\APPS-C++\consola\jaravi`

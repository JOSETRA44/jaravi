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
| `Jaravi.McpServer` | [[Servidor MCP]]: host Kestrel con 11 tools MCP (incl. `run_agent`, `reload_agents`) + WebSocket + REST. |
| `Jaravi.Dashboard` | [[Dashboard]] GUI WPF (MVVM) observadora vía HTTP/WebSocket. |
| `Jaravi.Engine.Tests` | xUnit: 75 tests del motor, incluyendo E2E contra procesos reales. |
| `Jaravi.McpServer.Tests` | xUnit: 32 [[Pruebas de Contrato MCP|tests de contrato]] sobre la superficie MCP publicada. |

## Navegación rápida — arquitectura y motor

- [[Motor (Engine)|Motor]] — SessionManager, PipeProcessFactory, ChannelEventBus, ScopeGate, ClaimRegistry
- [[Servidor MCP]] — Tools MCP, modos stdio y HTTP, agents.json
- [[Brechas del Protocolo MCP]] — investigación de qué le faltaba a Jaravi como ciudadano MCP, y qué se aplicó
- [[Pruebas de Contrato MCP]] — por qué la superficie MCP se prueba al nivel del cable, no del método
- [[Adopcion por Agentes]] — por qué los agentes se negaban a usar Jaravi, y el prompt de auto-instalación
- [[Control Center (Web)]] — dashboard web servido por Kestrel (interfaz por defecto)
- [[Dashboard]] — GUI WPF de escritorio (cliente secundario)
- [[Perfiles de Agentes]] — agents.json declarativo, lección closeStdin, hot-reload
- [[Catalogo de Agentes]] — matriz de 11 CLIs soportados con estado de verificación
- [[Operacion]] — Zero-touch stdio vs HTTP, despliegue
- [[Arquitectura]] — Diagrama y principios de Clean Architecture

## Navegación rápida — estrategia de negocio

- [[Investigacion de Mercado]] — tamaño de mercado, adopción de MCP, el problema que Jaravi resuelve
- [[Modelo de Negocio]] — propuesta open-core, tiers Community/Enterprise
- [[Jaravi Paper (LaTeX)]] — puente al documento formal (`docs/jaravi.tex`)

> [!warning] Repositorio
> Código fuente en `C:\Users\USER\source\APPS-C++\consola\jaravi`

---
tags: [jaravi, negocio, mercado, investigacion, 2026]
---

# Investigación de Mercado (2026)

Resumen de la investigación de mercado que fundamenta la tesis de [[Home|Jaravi]]: la orquestación multiagente tiene un problema de **complejidad, contexto y costo** que un motor determinista resuelve estructuralmente, no por prompting.

## El problema que valida a Jaravi

> [!danger] 60% de los pilotos fracasan
> El 60% de los pilotos de IA multiagente no llegan a producción. La causa más citada: **complejidad de orquestación** (Deloitte, 2025).

> [!warning] Colapso de contexto silencioso
> En sesiones largas, cuando el contexto se acerca al límite, el modelo **descarta instrucciones anteriores sin avisar** — incluyendo restricciones críticas. Con 4+ trabajadores, el contexto excede la ventana con frecuencia (FlowHunt, 2026).

> [!warning] Explosión de costos
> Herramientas agénticas: \$200–\$2,000+ por ingeniero/mes. Flujos que cuestan \$0.50 en pruebas pueden escalar a \$50,000/mes en producción, porque el orquestador hace múltiples llamadas LLM solo para descomponer y agregar tareas (KGT Solutions, 2026). Anthropic estima un **sobrecosto de tokens de ~15×** en arquitecturas multiagente orquestadas por LLM.

> [!note] La brecha de escalamiento
> 80% de las empresas que empiezan con un agente planean orquestar varios en 2 años — pero **menos del 10%** lo ha logrado (Gartner, vía Kanerika 2026).

## Tamaño de mercado

| Segmento | Actual | Proyección |
|---|---|---|
| Agentes de IA | \$5.25 mil M (2024) | \$52.62 mil M (2030, CAGR 46.3%) |
| Agentes autónomos | \$8.5 mil M (2026) | \$35 mil M (2030) |
| Asistentes de código IA | \$12.8 mil M (2026) | \$30.1 mil M (2032, CAGR 27%) |
| Servicios open-source | \$66.84 mil M (2026) | — |

Gartner: 40% de empresas integrará agentes de IA hacia fines de 2026; CIOs esperan que el 75% del trabajo de TI involucre personas aumentadas con IA para 2030.

## MCP como estándar de facto

El protocolo que [[Servidor MCP|Jaravi.McpServer]] habla nativamente ya es infraestructura mainstream, no un experimento:

- **41%** de organizaciones de software en producción limitada o amplia con servidores MCP.
- **9,652** servidores únicos registrados oficialmente al 24-may-2026 (28,959 versiones).
- **97 millones** de descargas/mes de los SDKs TypeScript/Python (mar-2026).
- Dic-2025: Anthropic donó MCP a la **Agentic AI Foundation** (Linux Foundation, cofundada con Block y OpenAI) — estándar neutral de proveedor.
- Modelo de dos capas consolidado: **MCP** (integración vertical con herramientas) + **A2A** de Google (coordinación horizontal entre agentes).
- La extensión **Enterprise-Managed Authorization (EMA)** ya está estable, adoptada por Asana, Atlassian, Canva, Figma, Linear, Supabase — el tipo de gobierno que un tier Enterprise de Jaravi resolvería.

## El ecosistema de CLIs que Jaravi orquesta

| Agente | Posición de mercado (jul-2026) | Perfil en Jaravi |
|---|---|---|
| OpenCode | Líder open-source: 180,312 estrellas (MIT) | `opencode` |
| Codex CLI (OpenAI) | #1 en Terminal-Bench 2.1, 83.4% | `codex` |
| Claude Code | 46% satisfacción (JetBrains, abr-2026) | `claude` |
| GitHub Copilot | 29% uso laboral, 4.7M suscriptores pagos | `copilot` |
| Antigravity | — | `antigravity` |

Ver [[Perfiles de Agentes]] para el detalle declarativo de cada perfil.

## Fuentes

Viston.tech · Deloitte · Kanerika · KGT Solutions · FlowHunt · Digital Applied · InfoQ · Toloka AI · Zylos Research · Morphllm · Ideaplan · GetMonetizely (todas 2026). Cita completa en [[Jaravi Paper (LaTeX)]].

Véase también: [[Modelo de Negocio]], [[Arquitectura]]

---
tags: [jaravi, negocio, open-core, estrategia]
---

# Modelo de Negocio: Open-Core

Propuesta de monetización para [[Home|Jaravi]], fundamentada en [[Investigacion de Mercado|la investigación de mercado 2026]] y en el patrón validado de herramientas de desarrollador.

> [!tip] Por qué open-core
> El patrón dominante en dev tools 2026: núcleo libre que genera confianza, comunidad y distribución (ej. HashiCorp/Terraform); capas comerciales para lo que las empresas exigen y los desarrolladores individuales no necesitan. Según OpenLogic (2023), **67% de las empresas** que adoptan un producto open-core terminan pagando por el nivel superior.

## Propuesta de tiers

| | Community (MIT, gratis) | Enterprise |
|---|---|---|
| Motor y tools MCP | Completo | Completo |
| Perfiles de agentes | `agents.json` local | Catálogo gestionado + plantillas verificadas |
| Dashboard | WPF de escritorio | Hosted, multi-usuario, multi-tenant |
| Gobierno | [[Motor (Engine)#ScopeGate y cola de sesiones (v2)|Scope Gate]], claims locales | SSO/EMA, RBAC, políticas centralizadas |
| Auditoría | Eventos en memoria | Logs persistentes, exportación, retención |
| Soporte | Comunidad | SLA, cuenta dedicada |

> [!note] No es especulativo
> La extensión **Enterprise-Managed Authorization (EMA)** de MCP ya está en estado estable y adoptada por Asana, Atlassian, Canva, Figma, Linear y Supabase. Es exactamente el tipo de necesidad de gobierno que el tier Enterprise resolvería para el motor de orquestación — el mercado ya está pidiendo esto.

## Por qué Jaravi puede cobrar por gobierno, no por el motor

La tesis central (ver [[Arquitectura]]) es que el motor de orquestación es determinista, no un agente de IA — así que **no hay costo marginal de tokens que Jaravi absorba o revenda**. El valor comercial no está en la inferencia (eso lo paga el usuario directamente a su proveedor de LLM), sino en:

1. **Observabilidad multi-usuario** (Dashboard hosted).
2. **Gobierno centralizado** (SSO/EMA, políticas de Scope Gate y claims a nivel organización).
3. **Auditoría persistente** (retención y exportación de logs de sesión, hoy solo en memoria).
4. **Catálogo verificado de perfiles** (curar y mantener `agents.json` para CLIs enterprise).

## Roadmap comercial

1. **Ahora**: Community MIT, v0.3 — validar adopción vía `dotnet tool install -g`.
2. **Corto plazo**: métricas de costo/uso por sesión y por perfil (base para pricing por valor).
3. **Mediano plazo**: Dashboard hosted multi-usuario + integración EMA → primer producto de pago.
4. **Largo plazo**: catálogo público de perfiles verificados por la comunidad (efecto de red).

Véase también: [[Investigacion de Mercado]], [[Jaravi Paper (LaTeX)]]

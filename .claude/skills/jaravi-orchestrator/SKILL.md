---
name: jaravi-orchestrator
description: >-
  Convierte al agente en el JEFE de sub-agentes externos (Claude Code, OpenCode,
  Copilot CLI…) a través de las tools MCP de Jaravi. Usa esta skill SIEMPRE que
  el usuario pida delegar trabajo a otros agentes, ejecutar tareas en paralelo
  con sub-agentes, orquestar CLIs de IA, o mencione Jaravi, spawn_agent o
  sesiones de sub-agentes — incluso si no dice la palabra "orquestar". También
  cuando una tarea grande se beneficiaría de dividirse entre varios agentes
  trabajando a la vez.
---

# Jaravi Orchestrator — el rol de jefe

Eres el **orquestador**, no el ejecutor. Tu trabajo es descomponer el objetivo,
delegar a sub-agentes vía las tools MCP de Jaravi (`spawn_agent`, `await_session`,
`get_summary`…), supervisar con presupuesto mínimo de contexto y consolidar
resultados. El código lo escriben los sub-agentes; tú diriges.

## Por qué existe Jaravi

Orquestar CLIs directamente en tu terminal rompe tu contexto: un sub-agente
puede emitir 50 000 líneas y colapsar tu sesión. Jaravi absorbe todo ese output
en su motor y solo te entrega respuestas compactas y deterministas. La GUI
(Jaravi.Dashboard) ya muestra el firehose completo al humano en tiempo real —
**no necesitas leer logs crudos jamás**; pedirlos solo destruye tu propia
ventana de contexto.

## Prerrequisito

Ninguno: `.mcp.json` usa transporte **stdio** apuntando al ejecutable compilado
con `--stdio`, así que Claude Code enciende y apaga el servidor solo (zero-touch).
En modo stdio, Kestrel también levanta el WebSocket/REST para el Dashboard
(fallback a puerto efímero si 5210 está ocupado). Si cambiaste el código del
servidor, recompila (`dotnet build Jaravi.McpServer`) para que el exe esté fresco.
El modo HTTP (`dotnet run --project Jaravi.McpServer`, endpoint
`http://localhost:5210/mcp`) sigue disponible para un motor compartido de larga vida.

## Higiene de contexto (las reglas que te mantienen vivo)

1. **Resumen antes que logs**: tras terminar una sesión, lee `get_summary`
   (exit code, duración, líneas de error extraídas, cola). En el 90 % de los
   casos es todo lo que necesitas.
2. **`read_output` siempre quirúrgico**: úsalo solo cuando el summary no basta,
   y siempre con `grep` (regex) y/o `tail` pequeños. El servidor capa a 500
   líneas por llamada, pero tu presupuesto real debería ser 20–50.
3. **Nunca pagines el output completo.** Si sientes la tentación de leerlo
   todo, delega el análisis: lanza otro sub-agente cuya tarea sea leer ese
   resultado y devolverte una conclusión.
4. **`await_session` es tu punto de sincronización**, no el polling. Bloquea
   hasta terminal o `waitingInput` con timeout. No hagas bucles de `get_status`.

## Flujo de orquestación

1. **Descompón** el objetivo en tareas independientes y delegables.
2. **Elige perfil** con `list_agents` (echo-demo y flood-demo son de prueba).
3. **Lanza** con `spawn_agent` usando un `brief` estructurado (no texto libre):
   `objective`, `context`, `constraints`, `deliverables`, `forbidden`.
   El motor lo renderiza como prompt determinista y bien formado.
   - `workdir` es obligatorio y debe estar dentro de las raíces permitidas
     (Scope Gate del motor; configurable en `appsettings.json → Engine:AllowedRoots`).
   - `unattended` es `true` por defecto: inyecta los flags no-interactivos del
     perfil. Déjalo así salvo que necesites confirmaciones humanas.
   - Pon `timeoutSec` realista: es un deadline duro con kill del árbol de
     procesos — tu red de seguridad contra sub-agentes colgados.
4. **Paraleliza**: lanza varias sesiones y luego `await_session` de cada una.
   El límite de concurrencia del motor te protege de sobrecargar la máquina.
5. **Sincroniza** con `await_session(sessionId, timeoutSec)`:
   - `completed` → lee `get_summary` y continúa.
   - `failed` → `get_summary` trae las líneas de error extraídas; si necesitas
     más, un `read_output` con `grep` dirigido al síntoma.
   - `waitingInput` → el sub-agente está bloqueado esperando entrada; responde
     con `send_input` o mátalo si es un prompt inesperado.
   - `timedOut: true` → sigue vivo; decide entre esperar otro ciclo, `send_input`
     o `kill_agent`.
6. **Termina limpio**: mata con `kill_agent` toda sesión que ya no aporte.
   Las sesiones vivas consumen slots de concurrencia.

## Ejemplo

```
spawn_agent(
  profile: "claude",
  workdir: "C:\\Users\\USER\\source\\mi-proyecto",
  brief: {
    objective: "Arreglar los tests que fallan en el módulo auth",
    context: "El suite se ejecuta con `dotnet test`; fallan 3 tests de JWT",
    constraints: ["no cambiar la API pública", "no tocar la BD"],
    deliverables: ["tests en verde", "resumen de la causa raíz"],
    forbidden: ["hacer commit", "tocar archivos fuera de src/Auth"]
  },
  timeoutSec: 900,
  labels: ["fix-auth"]
)
→ await_session(id, 300) → get_summary(id) → decidir siguiente paso
```

## Agregar un nuevo tipo de sub-agente

No se escribe código: agrega una entrada en `Jaravi.McpServer/agents.json`
(command, args con `{task}`/`{workdir}`, `unattendedArgs`, `env`) y recompila/
reinicia el servidor. Verifícalo con `list_agents` y una sesión de humo antes
de delegarle trabajo real. Dos lecciones aprendidas con CLIs reales:

- **`closeStdin: true` para CLIs one-shot** (`opencode run`, `claude -p`…):
  leen stdin hasta EOF cuando está en pipe y se cuelgan para siempre si queda
  abierto. Con este flag el motor les entrega el EOF al arrancar
  (`send_input` queda deshabilitado para esa sesión).
- **Apunta al binario real, no al shim `.cmd` de npm**: los shims pasan por
  `cmd.exe`, que destroza argumentos multilínea como los briefs renderizados.

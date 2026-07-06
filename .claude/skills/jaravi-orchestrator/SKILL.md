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

Ninguno: `.mcp.json` usa transporte **stdio** con el comando global `jaravi-mcp`
(dotnet tool), así que Claude Code enciende y apaga el servidor solo (zero-touch).
En modo stdio, Kestrel también levanta el WebSocket/REST para el Dashboard
(fallback a puerto efímero si 5210 está ocupado). Config editable del usuario en
`%APPDATA%\jaravi\` (`agents.json`, `appsettings.json`). Si cambiaste el código
del servidor: `dotnet pack Jaravi.McpServer -c Release -o nupkg` y
`dotnet tool update -g Jaravi.McpServer --add-source ./nupkg`.
El modo HTTP (`dotnet run --project Jaravi.McpServer`, endpoint
`http://localhost:5210/mcp`) sigue disponible para un motor compartido de larga vida.

## Doctrina: hacer vs delegar (tú manejas el presupuesto de contexto)

Es la misma decisión que toma Claude Code con sus propios subagentes: delegar
cuesta un arranque frío + redactar un brief; hacerlo tú cuesta contexto de tu
ventana. Decide así:

- **Hazlo tú mismo** cuando la tarea es corta y dirigida (una edición puntual,
  una lectura, una decisión de diseño), cuando requiere el contexto completo de
  tu conversación, o cuando redactar el brief costaría más que el trabajo.
- **Delega** (`spawn_agent`) cuando la tarea generaría output masivo en tu
  terminal (builds, suites de tests, escaneos, auditorías), cuando es
  paralelizable en trozos independientes, o cuando es autocontenida: si un
  brief basta para que un agente frío la haga, no la hagas tú.
- **Regla de oro del arranque frío**: el sub-agente NO vio tu conversación.
  El brief debe ser autosuficiente — rutas absolutas, comandos exactos,
  criterios de éxito. Si tu brief necesita "como te dije antes", está mal.

## Pipelines: encadena agentes sin tocar el intermedio

Cuando el paso N necesita el resultado del paso N−1, NO copies tú el output
(eso quema tu contexto): usa `spawn_agent(inputFromSessionId: <id>)` y el motor
inyecta el resultado de la sesión terminal previa directamente en el task del
nuevo agente.

- `inputKind`: `summary` (digest, default), `tail` (+`inputTailLines`, cap 100,
  +`inputGrep` opcional) o `errors` (solo líneas de error extraídas).
- La sesión origen debe estar **terminada** (su output ya es inmutable); si no,
  el spawn falla con error claro — haz `await_session` primero.
- Patrones: auditor → corrector (`errors`), generador → validador (`tail`),
  investigador → implementador (`summary`).

## Claims: paraleliza sin colisiones de archivos

Antes de lanzar dos sesiones que puedan **escribir** en la misma zona, declara
`claims` (globs relativos al workdir, p. ej. `["src/Auth/**"]`). El motor
detecta solapamiento por raíz de ruta y aplica tu política:

- `onConflict: "queue"` → la sesión queda `Queued` y arranca sola cuando el
  claim se libera (serialización automática; `await_session` la espera igual).
- `onConflict: "reject"` (default) → el spawn falla indicando qué sesión tiene
  el claim, y tú replanificas.
- Sin claims = sin restricción: solo protege lo que declares. Sesiones de solo
  lectura no necesitan claims.

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

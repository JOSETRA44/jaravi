---
tags: [jaravi, mcp, testing, contrato, calidad]
---

# Pruebas de Contrato MCP

Hasta v0.7.0 los 75 tests de Jaravi vivían todos en `Jaravi.Engine.Tests` —
probaban el **motor**. La superficie MCP (lo que un agente externo realmente
consume: nombres de tools, anotaciones, URIs de recursos, plantillas de prompt)
solo se verificaba con arneses de PowerShell escritos a mano, ejecutados una vez
y descartados. Es decir: **la capa con más probabilidad de romperse en silencio
era la única sin red de seguridad automática**.

`Jaravi.McpServer.Tests` (v0.7.1) cierra eso con 32 tests.

## Por qué "contrato" y no "unidad"

El bug que importa aquí no es "el método devuelve mal el JSON". Es más sutil:

- Una errata en un `UriTemplate` (`{sessionID}` contra un parámetro `sessionId`)
  **compila, registra y nunca matchea** — el recurso queda inalcanzable.
- Agregar la tool #12 sin anotaciones no rompe nada… hasta que un cliente pide
  permiso para todo o para nada.
- Cambiar cómo se registran los prompts puede publicar **cero** prompts sin un
  solo error.

Ninguno de esos lanza una excepción. Por eso los tests no invocan solo los
métodos: construyen el `ServiceCollection` **igual que `Program.cs`** y afirman
sobre las proyecciones que el SDK publica al cable (`ProtocolTool`,
`ProtocolResource`, `ProtocolResourceTemplate`, `ProtocolPrompt`). Se prueba lo
que el agente externo ve, no lo que nosotros creemos haber escrito.

```csharp
services.AddMcpServer()
    .WithTools<JaraviTools>()
    .WithResources<JaraviResources>()
    .WithPromptsFromAssembly(typeof(JaraviPrompts).Assembly);
```

Invariantes que quedan fijados (no listas cerradas, sino reglas):
toda tool declara anotaciones y descripción · `kill_agent` es destructiva y las
6 de lectura no lo son · todo recurso declara mimeType y descripción · **los
`{placeholders}` de cada `UriTemplate` coinciden exactamente con los nombres de
parámetro del método que los recibe** · ambos prompts se descubren · todo
argumento de prompt lleva descripción y marca correcta de requerido.

## Hallazgo real: `WithPromptsFromAssembly()` resuelve contra el *llamador*

Al escribir el test, `prompts/list` salió **vacío**. No era un fallo del test:
la sobrecarga sin argumentos usa `Assembly.GetCallingAssembly()`. Desde
`Program.cs` funciona por casualidad (el llamador *es* `Jaravi.McpServer`);
desde el assembly de tests devuelve cero.

El acoplamiento implícito es una trampa real en producción, no solo en el test:
mover ese registro a un helper en otro assembly publicaría cero prompts sin
error alguno. Se corrigió en `Program.cs` pasando el assembly explícitamente
(`typeof(JaraviPrompts).Assembly`) y verificando en vivo que la lista sigue
publicando ambos prompts. **Un test que aún no existía encontró una fragilidad
latente en el código de producción** — el argumento más fuerte para tenerlo.

## Decisiones de diseño de los tests

- **Fakes a mano, no librería de mocking.** La superficie MCP solo lee unos
  pocos métodos, y un fake que lanza `SessionNotFoundException` ante un id
  desconocido reproduce el contrato real del motor — que es justamente lo que se
  quiere fijar. Un mock configurado test a test probaría el mock.
- **`McpGuard` es `internal`** (es un detalle de cómo los errores del motor se
  vuelven errores de protocolo), pero su mapeo es exactamente lo que hay que
  fijar → `InternalsVisibleTo` en el `.csproj`. Incluye el caso negativo
  deliberado: una `InvalidOperationException` **no** se traduce, porque es un
  bug de Jaravi y disfrazarlo de error de protocolo escondería defectos reales.
- **Los prompts se prueban por contenido semántico**, no por texto exacto: que
  mencionen `timedOut`/`nextStep`/`errorLines` (el malentendido más común de un
  agente externo) y que `audit_then_fix` diga explícitamente que no se lea el
  hallazgo crudo. Son la única superficie cuyo consumidor es un *modelo*, así
  que nada aguas abajo lanzaría jamás ante una malformada — solo orquestaría
  peor.

## Trampas de C# encontradas

- **CS0121** — `McpGuard.Run(() => throw new ...)`: una lambda que solo lanza no
  tiene tipo de retorno inferible, así que matchea tanto `Func<T>` como
  `Func<Task<T>>` y la llamada es ambigua. Se resuelve tipando el delegado
  (`Func<string> notFound = ...`), lo que además deja explícito qué sobrecarga
  prueba cada test.
- La aserción ingenua "no quedan llaves sin renderizar" es **falsa**: la
  plantilla `delegate_task` contiene un `brief: { … }` literal a propósito. Lo
  que no debe sobrevivir es un *placeholder* (nombre de parámetro entre llaves),
  que es lo que la regex del test verifica.

Véase también: [[Servidor MCP]], [[Brechas del Protocolo MCP]], [[Arquitectura]], [[Home|Jaravi]]

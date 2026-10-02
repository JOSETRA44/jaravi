> [!abstract] En una línea
> Jaravi se registra solo en todos los CLIs de IA de la máquina, y además escribe
> en el fichero de instrucciones que esos agentes ya están leyendo — porque lo
> primero arregla la sesión siguiente y solo lo segundo arregla la actual.

## El patrón que se repitió tres veces

Un agente externo reportó, correctamente, que no podía usar Jaravi. Es la tercera
vez que aparece la misma forma, la que está en [[Adopcion por Agentes]]: **una
capacidad que no está en la superficie que el agente inspecciona primero, para él
no existe.**

| Versión | Superficie que el agente miraba | Qué encontraba |
|---|---|---|
| v0.8.0 | Campo `instructions` del handshake MCP | Vacío |
| v0.9.0 | `--help` | Ningún comando — se añadió el [[CLI de Shell\|CLI]] |
| v0.10.0 | **La instalación misma** | Un bloque de prosa en el README |

Hasta ahora el único camino de autoconfiguración era un texto que un humano tenía
que encontrar y pegarle al agente, pidiéndole que *adivinara* cuál era su propio
fichero de config, que respetara un esquema que no había visto nunca y que no
borrara los servidores ajenos. Eso no es un instalador: es un examen. Los agentes
lo suspendían y reportaban, con razón, que no podían.

## Dos superficies, dos problemas distintos

Ninguno de los seis clientes relee su config MCP a mitad de sesión — comprobado en
Claude Code, Codex, OpenCode, Gemini, Qwen y Copilot. Es un límite del cliente y no
se arregla desde el servidor. De ahí que `install` escriba en dos sitios:

1. **El registro MCP del cliente.** Sirve para la **próxima** sesión.
2. **El fichero de instrucciones** (`AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `QWEN.md`,
   `.github/copilot-instructions.md`), en un bloque entre marcadores
   `<!-- jaravi:begin -->` / `<!-- jaravi:end -->`. Sirve para la sesión **actual**:
   ahí el agente lee que existe un CLI que no necesita registro alguno.

Escribir solo lo primero es lo que dejaba al agente atascado justo cuando preguntaba.

## Dónde escribe, por cliente

| Cliente | Registro MCP (ámbito usuario) | Clave | Forma de la entrada |
|---|---|---|---|
| Claude Code | `~/.claude.json` | `mcpServers` | `type: stdio` + `command` + `args` |
| Codex | `~/.codex/config.toml` | `[mcp_servers.jaravi]` | TOML, se **anexa** |
| OpenCode | `~/.config/opencode/opencode.json` | `mcp` | `type: local`, `command` como **array** |
| Gemini CLI | `~/.gemini/settings.json` | `mcpServers` | `command` + `args` |
| Qwen Code | `~/.qwen/settings.json` | `mcpServers` | `command` + `args` |
| Copilot CLI | `~/.copilot/mcp-config.json` | `mcpServers` | `type: local` + `tools: ["*"]` |

> [!warning] La forma importa más que el sitio
> Un esquema equivocado no da error: el cliente arranca, ignora la entrada en
> silencio y el agente concluye que Jaravi no funciona. Cada forma de esta tabla
> sale de la documentación oficial del cliente y está fijada por tests.

Añadir un cliente es una fila en `ClientCatalog.All`, nunca una rama en el
instalador — el mismo principio declarativo que [[Perfiles de Agentes|agents.json]].

## Garantías

Todo esto edita ficheros de otros. Lo único aceptable es negarse; perder
configuración ajena no lo es.

- **Nunca borra lo que no vino a tocar.** Las claves desconocidas se preservan
  íntegras — `~/.claude.json` es sobre todo *no* config MCP.
- **JSON inválido se rechaza, no se sobrescribe.** Reescribir un fichero que no se
  pudo parsear lo sustituiría por uno que solo contiene nuestra entrada.
- **Idempotente.** Si la entrada ya está bien, no se escribe ni un byte, así que
  reejecutar no reformatea ni elimina comentarios.
- **Reversible.** `uninstall` deshace las dos superficies, y todo fichero tocado
  deja un `.jaravi.bak` al lado.
- **`--dry-run`** enseña exactamente qué haría, incluido qué clientes habría que
  reiniciar, sin escribir nada.

> [!note] Comentarios en JSON
> `opencode.jsonc` se comenta por convención. System.Text.Json los lee pero no sabe
> reescribirlos, así que si el fichero tenía comentarios el informe lo dice y el
> original queda en el `.jaravi.bak`.

## El alias `jaravi`

La queja literal del reporte externo fue que *«no existe un comando `jaravi`»*. Un
paquete .NET solo admite un `ToolCommandName` ([dotnet/sdk#10014]), así que el alias
es un script de dos líneas junto al binario real en el directorio de tools, que ya
está en el PATH. Los dos nombres funcionan y nada de lo que ya diga `jaravi-mcp` se
rompe. Nunca sobrescribe un `jaravi` que no sea nuestro.

[dotnet/sdk#10014]: https://github.com/dotnet/sdk/issues/10014

## La trampa del arranque desnudo

`jaravi-mcp` sin argumentos elegía modo stdio cuando stdin estaba redirigido — que
es **exactamente** cómo la herramienta de shell de un agente ejecuta comandos. El
primer contacto de cualquier agente con el binario era un cuelgue hasta el timeout,
sin decir nada.

Ahora, cuando el modo se *infiere* (no cuando se pide con `--stdio`), se escribe una
línea a stderr antes de bloquear. Un cliente MCP real ignora stderr; el llamante que
nunca fue un cliente MCP se entera al instante.

## Portabilidad de `agents.json`

`install` puede triunfar y dejar `jaravi agents` mostrando `??` en casi todo si el
registro está atado a una máquina. Los perfiles llevaban rutas absolutas del portátil
donde se escribieron, así que en cualquier otro sitio apuntaban a nada.

Ahora se expanden al cargar: `{npmRoot}`, `{home}`, `{appData}`, `{localAppData}`.
Los de spawn (`{task}`, `{workdir}`) siguen intactos. La expansión ocurre una sola
vez en la carga, de modo que la sonda de PATH de `agents`/`doctor` y el spawn nunca
pueden discrepar sobre cuál es el comando real.

## Diagnóstico

`jaravi-mcp doctor` añade la sección que un agente necesita para configurarse solo:
qué clientes MCP hay instalados en la máquina, dónde guarda cada uno su registro, y
si Jaravi ya figura en él.

```
mcp clients on this machine
  ok    claude     NOT registered C:\Users\USER\.claude.json
  ok    codex      registered     C:\Users\USER\.codex\config.toml
  ...
  5 installed client(s) do not have Jaravi registered. Fix all of them with: jaravi-mcp install
```

## Ver también

- [[Adopcion por Agentes]] — por qué los agentes se negaban, y las instrucciones MCP
- [[CLI de Shell]] — los verbos, adjuntarse vs. motor privado, códigos de salida
- [[Catalogo de Agentes]] — los CLIs que Jaravi sabe lanzar como sub-agentes
- [[Servidor MCP]] — modos stdio y HTTP

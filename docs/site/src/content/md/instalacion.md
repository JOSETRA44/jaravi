Jaravi se distribuye como **herramienta global de .NET**. El paquete instala el
comando `jaravi-mcp`; `jaravi-mcp install` se encarga del resto.

## Requisitos

- **.NET 8 SDK** o superior. El paquete declara `RollForward: Major`, así que
  funciona en runtimes más nuevos sin recompilar.
- Al menos **una CLI de agente instalada** (OpenCode, Codex, Claude Code, Gemini,
  Qwen o Copilot). Jaravi no trae modelos: conduce los que ya tienes.

## Instalación en 30 segundos

```bash
dotnet tool install -g Jaravi.McpServer   # deja el comando jaravi-mcp
jaravi-mcp install                        # se registra solo en tus CLIs de IA
jaravi-mcp doctor                         # comprueba que quedó usable
```

> [!tip] El alias corto
> `install` deja también un alias `jaravi`, porque es el nombre que un agente
> prueba primero. Un paquete .NET solo admite un `ToolCommandName`, así que el
> alias es un script de dos líneas junto al binario real, en un directorio que ya
> está en el `PATH`. Nunca sobrescribe un `jaravi` que no sea nuestro.

## Instalación manual desde el código

Si NuGet aún no sirve la versión que quieres, o trabajas sobre el repositorio:

```bash
git clone https://github.com/JOSETRA44/jaravi
cd jaravi
dotnet pack Jaravi.McpServer -c Release -o nupkg
dotnet tool install -g Jaravi.McpServer --add-source ./nupkg
```

Para desarrollar sin instalar nada:

```bash
dotnet run --project Jaravi.McpServer          # modo HTTP: /mcp y Control Center en :5210
dotnet run --project Jaravi.Dashboard          # GUI de escritorio (opcional)
dotnet test                                    # la suite completa
```

## Qué hace exactamente `install`

Toca tres superficies, y las tres hacen falta por razones distintas:

1. **La config MCP de cada cliente instalado** —Claude Code, Codex, OpenCode,
   Gemini, Qwen, Copilot— con el esquema exacto de cada uno y sin borrar los
   servidores que ya tuvieras. Esto arregla la **próxima** sesión.
2. **Los ficheros de instrucciones** que esos agentes leen al arrancar
   (`AGENTS.md`, `CLAUDE.md`, `GEMINI.md`…), dentro de un bloque delimitado por
   marcadores. Esto es lo que rescata la sesión **actual**.
3. **El alias `jaravi`**.

| Opción | Efecto |
|---|---|
| `--dry-run` | Enseña qué tocaría, sin escribir un byte. |
| `--scope project` | Limita los cambios a este repositorio. |
| `--refresh-agents` | Toma el `agents.json` de esta versión y guarda el tuyo como `.bak`. |
| `uninstall` | Revierte las dos superficies por completo. |

Cada fichero modificado deja un `.jaravi.bak` al lado. El detalle de las
garantías —qué nunca se borra, qué se rechaza en vez de sobrescribir— está en
[[Autoconfiguracion|autoconfiguración]].

## Registro manual en un cliente MCP

Si prefieres hacerlo tú, la entrada es la misma en casi todos:

```json
{ "mcpServers": { "jaravi": { "type": "stdio", "command": "jaravi-mcp", "args": ["--stdio"] } } }
```

> [!warning] La forma importa más que el sitio
> Un esquema equivocado no da error: el cliente arranca, ignora la entrada en
> silencio y el agente concluye que Jaravi no funciona. OpenCode, por ejemplo,
> espera `type: local` y `command` como **array**. La tabla exacta por cliente
> está en [[Autoconfiguracion|autoconfiguración]].

## Verificar que quedó bien

```bash
jaravi-mcp doctor
```

`doctor` informa de la configuración cargada, las raíces del Scope Gate, las
instancias vivas, qué CLIs encuentra en el `PATH` y —lo más útil para un agente—
qué clientes MCP hay en la máquina y si Jaravi ya figura en cada uno.

```text
mcp clients on this machine
  ok    claude     NOT registered C:\Users\USER\.claude.json
  ok    codex      registered     C:\Users\USER\.codex\config.toml
  ...
  5 installed client(s) do not have Jaravi registered.
  Fix all of them with: jaravi-mcp install
```

## Primer arranque

El primer arranque siembra `%APPDATA%\jaravi\` (o `~/.config/jaravi/`) con:

- **`agents.json`** — los perfiles de sub-agente, editable. Ver
  [[Perfiles de Agentes|perfiles]].
- **`appsettings.json`** — overrides del motor, incluido `Engine:AllowedRoots`,
  que es la lista blanca del Scope Gate. Ver [[Configuración|configuración]].

Con eso ya puedes pasar a [[Inicio rápido|tu primera delegación]].

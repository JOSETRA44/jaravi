Toda la configuración vive en dos ficheros y tres variables de entorno. El primer
arranque los siembra; a partir de ahí son tuyos.

## Dónde están

```text
%APPDATA%\jaravi\            (Windows)
~/.config/jaravi/            (Linux y macOS)
  ├── agents.json            perfiles de sub-agente
  ├── appsettings.json       límites del motor y Scope Gate
  └── instances/<pid>.json   registro de instancias vivas
```

Resolución de `agents.json`, en orden: `JARAVI_AGENTS` (variable de entorno) →
`./agents.json` del directorio actual → el de configuración de usuario → el que
viene dentro del paquete.

## `appsettings.json`

```json
{
  "Engine": {
    "AllowedRoots": ["C:\\Users\\USER\\source"],
    "MaxConcurrentSessions": 8,
    "MaxReadLines": 500,
    "LogBufferCapacity": 10000,
    "DefaultTimeoutSeconds": 1800
  },
  "Urls": "http://localhost:5210"
}
```

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Engine:AllowedRoots` | el repositorio actual | Lista blanca del [[Garantías y límites\|Scope Gate]]. Un `workdir` fuera se rechaza. |
| `Engine:MaxConcurrentSessions` | `8` | Procesos hijos vivos a la vez. Lo que excede se encola. |
| `Engine:MaxReadLines` | `500` | Tope duro de `read_output`. Cap del servidor, no sugerencia. |
| `Engine:LogBufferCapacity` | `10000` | Tamaño del ring buffer por sesión. |
| `Engine:DefaultTimeoutSeconds` | `1800` | Deadline por sesión si no se pasa uno. |
| `Urls` | `http://localhost:5210` | Dirección de Kestrel. |

> [!important] `AllowedRoots` es la única defensa real
> Es lo que impide que un prompt inyectado en un fichero del repositorio saque a
> un sub-agente de tus carpetas. Ponerlo demasiado ancho (`C:\`) equivale a
> desactivarlo.

## Variables de entorno

| Variable | Efecto |
|---|---|
| `JARAVI_URL` | Dirección a la que bindear. Gana a `ASPNETCORE_URLS` y a `Urls`. |
| `JARAVI_AGENTS` | Ruta a un `agents.json` concreto. |
| `ASPNETCORE_URLS` | Respetada como alternativa estándar de ASP.NET. |

Si el puerto está ocupado, Kestrel cae a un puerto efímero y lo anuncia en el
banner de stderr y en `/api/instances`, en vez de fallar al arrancar.

## `agents.json`

Los perfiles de sub-agente. La estructura completa y los campos están en
[[Perfiles de Agentes|perfiles]]; aquí van solo los placeholders, que es lo que
más se consulta:

| Placeholder | Se expande a | Cuándo |
|---|---|---|
| `{task}` | El brief de la tarea | Al lanzar |
| `{workdir}` | El directorio de trabajo | Al lanzar |
| `{npmRoot}` | La raíz de módulos globales de npm | Al cargar el fichero |
| `{home}` | El directorio del usuario | Al cargar |
| `{appData}` | `%APPDATA%` o equivalente | Al cargar |
| `{localAppData}` | `%LOCALAPPDATA%` o equivalente | Al cargar |

> [!note] Por qué unos se expanden al cargar y otros al lanzar
> Los de ruta se resuelven una sola vez, en la carga, para que la sonda de `PATH`
> que hacen `agents` y `doctor` y el spawn real **nunca puedan discrepar** sobre
> cuál es el comando. Los de tarea dependen de la llamada, así que se resuelven
> entonces.

Esta expansión es lo que hace portable el registro: los perfiles ya no llevan
rutas absolutas del portátil donde se escribieron.

## Recargar sin reiniciar

```bash
jaravi agents                       # ver qué hay cargado
```

```text
reload_agents                       # re-leer agents.json en vivo
```

Un fichero mal formado se rechaza con un error que nombra el archivo y la forma
esperada; el catálogo activo sobrevive intacto.

## Actualizar los perfiles que trae la versión nueva

Tu `agents.json` se siembra una vez y **no se vuelve a tocar**, así que las
correcciones de perfiles publicadas desde entonces se quedan sin usar. Para
tomarlas:

```bash
jaravi-mcp install --refresh-agents   # el tuyo queda como .jaravi.bak
```

`jaravi-mcp doctor` avisa cuando conviene hacerlo.

## Configuración por proyecto

```bash
jaravi-mcp install --scope project
```

Limita el registro y el bloque de instrucciones a este repositorio: escribe el
`.mcp.json` (u `opencode.jsonc`) local y el `AGENTS.md` del proyecto, sin tocar
nada del ámbito de usuario.

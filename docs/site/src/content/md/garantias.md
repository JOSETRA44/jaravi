Delegar significa que otro proceso escribe en tu disco con tus credenciales. Lo
que hace eso aceptable no son las instrucciones que le des al modelo, sino los
límites que el motor aplica pase lo que pase.

## Scope Gate

Todo `workdir` se valida contra `Engine:AllowedRoots` **antes** de arrancar el
proceso. Un `workdir` fuera de esas raíces se rechaza con un error de dominio,
no con una advertencia.

```json
{
  "Engine": {
    "AllowedRoots": ["C:\\Users\\USER\\source"]
  }
}
```

Esto es lo que impide que un prompt inyectado en un fichero del repositorio
convenza a un sub-agente de trabajar en `C:\Windows`. La comprobación es
consciente de segmentos: `C:\src\app` **no** es ancestro de `C:\src\application`.

## Ring buffer y tope de lectura

Cada sesión tiene un buffer circular de **10 000 líneas**. Cuando se llena, lo
más viejo cae; el motor nunca crece sin límite ni aplica back-pressure al
sub-agente.

Encima de eso hay un segundo límite, que es el que protege tu contexto:

> [!important] El tope de lectura es del servidor
> `read_output` acepta un `maxLines`, pero está capado por `Engine:MaxReadLines`
> (500 por defecto). Un agente jefe que pida 50 000 líneas recibe 500. El límite
> no depende de que quien llama se porte bien.

`Tail`, `Grep` (expresión regular) y `SinceSeq` permiten paginar dentro de ese
tope sin releer lo mismo.

## Deadline duro

Cada sesión lleva su plazo (`--timeout`, 1 800 s por defecto). Al agotarse, el
motor ejecuta `Kill(entireProcessTree: true)`: no queda un nieto huérfano
consumiendo CPU en segundo plano porque el padre haya muerto.

Hay además un **watchdog de inactividad** que detecta sesiones sin output y las
marca como `WaitingInput` en vez de dejarlas colgadas en `Running` para siempre.

## Sanitización de output

El `AnsiSanitizer` elimina secuencias de escape ANSI/VT y caracteres de control
antes de almacenar. Dos motivos:

1. **Determinismo.** El mismo trabajo produce el mismo texto, comparable entre
   ejecuciones.
2. **Coste.** Los spinners y las barras de progreso de un CLI interactivo son
   miles de repeticiones de la misma línea con códigos de cursor. Guardarlas
   llenaría el ring buffer de ruido.

## Nada corre invisible

Toda sesión es listable (`list_sessions`), inspeccionable (`get_status`,
`read_output`) y matable (`kill_agent`) —por MCP, por REST y desde el
[[Control Center (Web)|Control Center]]—. No hay modo silencioso.

`kill_agent` es además la **única** tool anotada como `destructiveHint: true`, de
modo que cualquier cliente MCP sabe sin preguntar cuáles de las once son seguras.

## Aislamiento entre sesiones

- **Claims**: una sesión puede reclamar rutas en exclusiva. Un solapamiento se
  rechaza con la sesión culpable identificada, o se encola. Ver
  [[Claims y colas|claims]].
- **Concurrencia**: `Engine:MaxConcurrentSessions` (8 por defecto) limita cuántos
  procesos hijos viven a la vez. Lo que excede se encola, no se rechaza.
- **Bus de eventos**: `ChannelEventBus` usa `BoundedChannelFullMode.DropOldest`
  con 4 096 eventos por suscriptor. Una pestaña del navegador lenta pierde
  eventos antes que frenar al motor.

## Lo que Jaravi *no* garantiza

Vale la pena decirlo explícitamente:

> [!warning] Los sub-agentes editan de verdad
> Corren con la auto-aprobación de su propia CLI, porque su razón de ser es
> trabajar sin supervisión. Trata sus ediciones como tratarías las tuyas:
> acotadas al `workdir` que pasaste, y revisables después. Jaravi acota **dónde**
> pueden escribir, no **qué** escriben.

- No aísla por contenedor ni por usuario: el sub-agente tiene las credenciales
  que ya tenía el usuario.
- No revierte cambios. Usa control de versiones, como con cualquier edición.
- No inspecciona el prompt en busca de intenciones. Esa no es una defensa que
  pueda sostenerse; la que sí se sostiene es el Scope Gate.

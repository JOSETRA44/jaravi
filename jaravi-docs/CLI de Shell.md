---
tags: [jaravi, cli, shell, adopcion, mcp]
---

# CLI de Shell

Hasta v0.9.0, `jaravi-mcp` solo entendía `--help`, `--version`, `--stdio` y
`--http`. No existía **ni un solo subcomando que hiciera algo**. Un agente con
acceso a shell pero sin Jaravi registrado en su cliente MCP no tenía ninguna vía
para delegar.

## El diagnóstico: dos afirmaciones ciertas y una conclusión evitable

Un agente externo reportó exactamente esto:

1. *"`jaravi-mcp` es un servidor stdio de MCP, no un CLI; no existe un comando
   `jaravi` invocable desde la consola."* — **Cierto, y era culpa de Jaravi.**
   Ejecutarlo desde una shell levantaba un servidor HTTP que bloquea para
   siempre. No había nada más.
2. *"Un servidor MCP no se puede registrar en una sesión ya en ejecución."* —
   **Cierto, y no es corregible desde el servidor.** Los clientes leen su config
   MCP al arrancar. Es comportamiento del cliente.

Lo único evitable era la conclusión *"por tanto no puedo usar Jaravi"*: toda
instancia — **incluidas las de modo `--stdio`** — mantiene Kestrel arriba y sirve
la [[Servidor MCP|REST API]] en `:5210`. El camino existía. Pero nada en el
`--help`, ni en las instrucciones, ni en el README lo decía, así que el agente no
podía saberlo. Un camino que nadie documenta no es un camino.

> [!important] La lección
> No basta con que una capacidad exista. Si la superficie que el agente
> inspecciona primero (`--help`) no la menciona, la capacidad no existe para él.

## La corrección: nueve verbos, no un parche

`jaravi-mcp <verbo>` cubre todo lo que hacen las tools MCP: `agents`, `run`,
`spawn`, `sessions`, `status`, `logs`, `await`, `kill` y `doctor`.

`run --agent <id> --task "..."` es el que resuelve el caso reportado: **no
necesita registro MCP, ni reinicio, ni servidor**.

> [!note] Dos verbos más en v0.10.0
> `install` y `uninstall` completan la lista. No delegan trabajo: registran
> Jaravi en los CLIs de IA de la máquina y escriben en los ficheros de
> instrucciones que esos agentes leen al arrancar. Como `doctor`, corren
> **antes** de montar ningún motor — las herramientas que existen para
> arreglar una instalación rota no pueden exigir una instalación sana. Ver
> [[Autoconfiguracion]].

### Adjuntarse o correr en privado

La decisión de diseño que sostiene el resto ([[Arquitectura|IJaraviClient]] con
dos implementaciones):

- **`RestJaraviClient`** — si hay una instancia viva, el CLI la conduce por REST.
  Las sesiones lanzadas desde la shell son **las mismas** que ve el agente jefe
  por `list_sessions` y el usuario en el [[Control Center (Web)]]. Un solo mundo.
- **`InProcessJaraviClient`** — si no hay ninguna, monta un motor privado dentro
  del propio comando, con la misma receta que el servidor (`JaraviConfig`).

El factory prefiere la instancia cuyo `repoRoot` **contiene** el workdir, no la
más reciente: con varios proyectos abiertos, esa es la que tiene el Scope Gate
correcto y el dashboard que el usuario está mirando. La comprobación es
consciente de segmentos — `C:\src\app` no es ancestro de `C:\src\application`.

> [!warning] El registro de instancias miente, y por eso se sondea
> Windows reutiliza PIDs: una entrada de hace semanas puede parecer viva porque
> otro proceso heredó su número. `TryAttachAsync` hace `GET /healthz` con 2s de
> timeout antes de adjuntarse, y pasa a la siguiente si no responde.

El motor privado **muere con el comando**. Por eso `spawn` se niega en ese modo
con un mensaje que explica el porqué y ofrece las dos salidas, y por eso `run`,
si expira el `--wait` sin servidor, **mata la sesión y lo dice**: devolver un
`sessionId` que está a punto de dejar de existir sería mentir.

## Códigos de salida

Único canal estructurado que un llamador de shell tiene siempre.

| Código | Significado |
|---|---|
| `0` | Terminó; el sub-agente salió con 0 |
| `1` | Error de Jaravi (perfil, Scope Gate, config) |
| `2` | Uso incorrecto |
| `3` | El sub-agente terminó con código ≠ 0 |
| `4` | **Sigue corriendo** — recógelo con `await` |

La separación entre `3` y `4` es deliberada: *"no ha terminado"* no es
*"ha fallado"*, y confundirlos es el malentendido más caro sobre este sistema
(ver [[Adopcion por Agentes]]).

## Bugs que solo aparecieron al ejecutarlo

Los cuatro compilaban sin un aviso:

1. **`ILogger<T>` sin registrar.** El `WebApplicationBuilder` lo aportaba gratis;
   un `ServiceCollection` pelado no. Todo `run` standalone reventaba.
2. **`--no-attach run` arrancaba un servidor web.** El verbo se leía de `argv[0]`.
   La corrección — *el verbo es el primer positional, tras consumir las opciones*
   — es la única que sobrevive a `--task run`, donde ese `run` es un valor.
3. **Un verbo mal escrito arrancaba un servidor web en silencio**, indistinguible
   de un cuelgue. Ahora cualquier palabra suelta que no sea un verbo da error de uso.
4. **`--json` devolvía siempre 0**, incluso con el sub-agente fallando con 7 —
   rompía el contrato de códigos justo en el modo que un script usa. `Json()` ya
   no decide el código; lo decide el comando, una sola vez, para ambos formatos.

De paso, un `agents.json` mal formado escupía un stack trace y salía con 127;
ahora es un error de dominio que nombra el archivo y la forma esperada.

## Cobertura

22 [[Pruebas de Contrato MCP|tests]] nuevos (57 en el proyecto, 132 en total)
fijan el parseo de argumentos, el enrutado de verbos, la cobertura de rutas
consciente de segmentos y la sonda de PATH. Cada uno corresponde a un fallo real
de los de arriba.

Véase también: [[Servidor MCP]], [[Adopcion por Agentes]], [[Operacion]], [[Home|Jaravi]]

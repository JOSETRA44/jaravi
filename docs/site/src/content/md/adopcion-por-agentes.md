Jaravi puede estar impecablemente construido y aun así **no usarse**. Durante
varias rondas, agentes externos conectados al servidor lo ignoraban o se negaban
a delegar. La causa no estaba en las tools.

> [!success] Continuación: v0.10.0
> El mismo patrón volvió una tercera vez, ahora en la instalación: la única
> autoconfiguración era prosa que un humano tenía que pegar. Se resolvió con
> `jaravi-mcp install`, que escribe el registro MCP **y** el fichero de
> instrucciones que el agente ya está leyendo. Ver [[Autoconfiguracion]].

## El diagnóstico: el servidor no se presentaba

MCP define un campo `instructions` que el servidor devuelve en el handshake
`initialize`. Los clientes lo inyectan como **system message** del modelo — la
documentación del SDK lo dice literalmente: *"Client applications typically use
these instructions as system messages"*.

Jaravi lo dejaba vacío. `AddMcpServer()` se llamaba sin opciones y la palabra
`Instructions` no aparecía en todo el proyecto.

La comprobación que lo cerró fue observacional: en una misma sesión de Claude
Code, otros servidores MCP conectados (bcrp, scopus, Supabase, figma…) inyectaban
su bloque de instrucciones en el contexto del agente. **Jaravi no aparecía por
ningún lado.** No es que el agente desconfiara de Jaravi: es que Jaravi nunca le
dijo nada.

Puesto en el lugar del agente: ve once tools sin contexto que lanzan **otros
agentes de IA con flags de auto-aprobación**, sin ninguna indicación de quién
autorizó eso ni qué lo limita. Negarse no es un fallo del agente — es la
conducta correcta ante una herramienta que no se explica.

## La corrección: `JaraviInstructions.cs`

Un solo `const string` cableado en `AddMcpServer(options => ...)`. Es el texto de
mayor apalancamiento del proyecto: lo lee **todo** agente jefe antes de decidir
si confía en la herramienta, y se paga en contexto en **cada** sesión. Denso a
propósito, y sin repetir lo que ya dicen las descripciones de las tools (guía
explícita del SDK).

Estructura, en orden deliberado:

1. **Legitimidad primero.** Si el agente ve estas tools es porque el usuario
   instaló y registró el servidor él mismo; ese registro *es* la autorización.
   Ejecutar sub-agentes es el propósito del servidor, no un efecto colateral.
2. **Los límites reales, para que el agente juzgue solo.** Los sub-agentes son
   CLIs que el usuario ya tenía, con su propia cuenta y credenciales; el
   [[Motor (Engine)|Scope Gate]] rechaza cualquier workdir fuera de las raíces
   permitidas; toda sesión es inspeccionable y matable; nada corre invisible.
   Se dice explícitamente que los sub-agentes corren con auto-aprobación — no
   ocultarlo es lo que hace creíble el resto.
3. **Cuándo NO delegar.** "No delegues lo que resuelves en dos tool-calls."
   Sin este límite se cambia un problema de contexto por uno de latencia y
   coste, y el agente aprende a desconfiar del consejo.
4. **Los cinco malentendidos habituales**, encabezados por el más caro visto en
   consumo real: `timedOut: true` significa *sigue corriendo*, no *falló*.

> [!important] El matiz que importa
> El texto **no** le dice al agente que suspenda su criterio. Dice que la
> cautela normal sigue aplicando, y que lo único que no debe hacer es negarse
> *por creer que delegar es en sí inseguro o no autorizado*. Presionar a un
> agente para que ignore su juicio produciría exactamente el tipo de herramienta
> en la que no se debe confiar.

## Segundo frente: el prompt de auto-instalación

La otra mitad del problema era de instalación, no de ejecución: el usuario tenía
que editar a mano el JSON de MCP de cada cliente. El README ahora abre con un
bloque copy-paste que el usuario pega en su agente para que **se instale y se
registre a sí mismo** — la convención que ya usan la mayoría de los MCP.

Cubre `dotnet tool install`, la comprobación del PATH, el registro en el archivo
que corresponda a cada cliente (Claude Code, Codex, OpenCode, Gemini…) sin
pisar los servidores existentes, y una verificación final llamando a
`list_agents`.

## Cobertura

Tres [[Pruebas de Contrato MCP|tests de contrato]] fijan lo esencial: que las
instrucciones existen (regresión directa de la causa raíz), que responden a las
preguntas que hacían fallar la adopción (autorización, Scope Gate, `timedOut`,
cuándo no delegar), y que **no crecen sin control** — se cobran en cada sesión,
así que tienen un techo de tamaño.

Verificado en vivo sobre el binario global 0.8.0: el `initialize` devuelve 3273
caracteres de instrucciones con todas las afirmaciones clave presentes.

## Tercer frente: los que no podían llegar (v0.9.0)

Cerrado el problema de *querer* usarlo, quedaba el de *poder*. Un agente externo
reportó que `jaravi-mcp` no era invocable como CLI y que no podía registrar un
servidor MCP a mitad de sesión. Ambas cosas eran ciertas, y la primera era culpa
de Jaravi: no existía ningún subcomando.

La respuesta no fue documentar mejor, sino **dar el camino que faltaba** — un CLI
completo en el mismo binario, que no necesita registro ni reinicio. El detalle
está en [[CLI de Shell]].

> [!note] Un patrón que se repite
> Las tres rondas de adopción fallaron por lo mismo: **Jaravi no se explicaba en
> la superficie que el agente inspecciona primero.** Primero fue el handshake
> `initialize` sin `instructions`; después el `--help` sin comandos. La capacidad
> existía en ambos casos.

Véase también: [[Servidor MCP]], [[Brechas del Protocolo MCP]], [[Pruebas de Contrato MCP]], [[Perfiles de Agentes]], [[CLI de Shell]], [[Home|Jaravi]]

Un auditor encuentra tres fallos; un corrector los arregla. Entre los dos hay un
resultado intermedio que **no tiene por qué pasar por ti**. Los pipelines son la
forma de encadenarlos sin leer lo del medio.

## La idea

`spawn_agent` y `run_agent` aceptan `inputFromSessionId`: una referencia a una
sesión **ya terminada**. El motor renderiza el resultado de esa sesión como un
bloque determinista y lo concatena al `task` de la nueva, antes de aplicar el
`promptTemplate` del perfil.

```bash
ID=$(jaravi spawn --agent codex --task "audita src/api" --quiet)
jaravi await "$ID"

jaravi run --agent claude \
  --task "arregla lo que encontró la auditoría" \
  --input-from "$ID" \
  --input-kind summary
```

Lo que recibe el segundo agente es su `task` más el extracto. Lo que recibes tú
es el resultado final.

## Los tres tipos de entrada

| `--input-kind` | Qué inyecta | Cuándo |
|---|---|---|
| `summary` *(por defecto)* | El digest: exit code, duración y errores extraídos. | Casi siempre. Es lo acotado. |
| `errors` | Solo las líneas que casan con el extractor de errores. | Cuando el corrector solo necesita los fallos. |
| `tail` | Las últimas N líneas, con tope del motor en 100. | Cuando el resultado útil es el final del output (un informe impreso al terminar). |

> [!important] Por qué `summary` es el valor por defecto
> Poner `tail` por defecto reintroduciría exactamente la manguera que Jaravi
> existe para evitar, solo que una capa más abajo. Si eliges `tail`, elígelo a
> sabiendas.

Se puede afinar más:

```bash
jaravi run --agent claude --task "arregla los fallos de seguridad" \
  --input-from "$ID" --input-kind tail --input-tail 60 --input-grep "security|CWE"
```

`--input-tail` está capado a 100 líneas por el motor, y `--input-grep` es una
expresión regular que filtra esas líneas.

## Por qué la sesión origen debe estar terminada

> [!note] Estado terminal, output inmutable
> El output de una sesión en estado terminal ya no cambia. Eso hace que la
> inyección sea determinista: la misma sesión origen produce siempre el mismo
> extracto. Referenciar una sesión viva daría un resultado distinto según el
> momento exacto de la lectura.

Si la sesión no ha terminado, la llamada se rechaza con un error de dominio que
lo dice. La solución es `await_session` antes de encadenar.

## Desde MCP

```text
spawn_agent(
  agent: "claude",
  task: "arregla lo que encontró la auditoría",
  inputFromSessionId: "7f3a2c",
  inputKind: "summary"
)
```

Hay además un **prompt** MCP, `audit_then_fix`, que rellena las dos etapas de
este pipeline con las instrucciones correctas para leer `timedOut`/`nextStep`.
Ver [[Recursos y prompts|recursos y prompts]].

## Cadenas más largas

Nada impide encadenar tres o cuatro etapas: cada `spawn` puede referenciar la
sesión anterior.

```bash
AUDIT=$(jaravi spawn --agent codex    --task "audita src/"          --quiet)
jaravi await "$AUDIT"

FIX=$(jaravi spawn   --agent claude   --task "aplica las correcciones" \
                     --input-from "$AUDIT" --quiet)
jaravi await "$FIX"

jaravi run --agent opencode --task "escribe tests que cubran lo corregido" \
  --input-from "$FIX" --input-kind summary
```

Ninguno de los resultados intermedios llega a tu contexto. Todos siguen
disponibles en el [[Control Center (Web)|Control Center]] si quieres mirarlos.

> [!warning] Un extracto es multilínea
> Y un perfil que apunte a un `.cmd` recibirá el task cortado en el primer salto
> de línea, saliendo con exit 0 como si todo hubiera ido bien. El motor lo
> detecta y avisa en el log, pero la corrección real es apuntar al ejecutable.
> Ver [[Perfiles de Agentes|perfiles]].

## Cuándo no encadenar

- Cuando necesitas **decidir** entre las dos etapas. Un pipeline es una tubería,
  no un árbol: si vas a mirar el resultado para elegir qué hacer, léelo con
  `get_summary` y lanza la segunda etapa tú.
- Cuando la segunda etapa necesita el output **completo**. En ese caso el
  problema es de diseño de la tarea, no de la tubería.

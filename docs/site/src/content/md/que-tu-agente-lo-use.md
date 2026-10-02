Puede pasar —ha pasado varias veces— que Jaravi esté instalado, registrado y
funcionando, y aun así el agente se niegue a usarlo. Casi siempre es una de tres
causas, y las tres tienen respuesta.

## «No me consta que esté autorizado»

**Era cierto hasta la v0.8.0.** El protocolo MCP define un campo `instructions`
que se devuelve en el handshake `initialize`, y los clientes lo inyectan como
*system message* del modelo. Jaravi lo dejaba vacío.

Un agente veía entonces once tools sin contexto que lanzan otros agentes de IA
con flags de auto-aprobación, no tenía forma de saber quién había autorizado eso,
y hacía lo razonable: no tocarlas.

Ahora el servidor se presenta, explicando en orden:

1. **Que su uso está autorizado y por qué** — si el agente ve estas tools es
   porque el usuario instaló y registró el servidor él mismo.
2. **Los límites reales**, para que el agente juzgue por sí mismo: los
   sub-agentes son CLIs que el usuario ya tenía, corriendo con su propia cuenta;
   el Scope Gate rechaza cualquier `workdir` fuera de las raíces permitidas;
   toda sesión es inspeccionable y matable.
3. **Cuándo delegar y cuándo no** — explícitamente: no delegues lo que resuelves
   en dos llamadas.
4. **Los cinco malentendidos habituales**, empezando por que `timedOut: true`
   significa *sigue corriendo*, no *falló*.

> [!tip] Si tu cliente no muestra las instructions
> Actualiza a v0.8.0 o superior y **reinicia el cliente**. El handshake ocurre
> una sola vez, al arrancar.

## «No existe ningún comando `jaravi`»

**Era cierto hasta la v0.9.0.** El ejecutable solo entendía `--help`,
`--version`, `--stdio` y `--http`: no había un solo subcomando que hiciera algo.

Ahora el binario es un CLI completo con once verbos, y `jaravi-mcp install` deja
además el alias corto `jaravi`. Esto es lo que un agente con acceso a shell
puede ejecutar sin registrar nada:

```bash
jaravi agents
jaravi run --agent codex --task "audita src/"
```

## «No puedo registrar un MCP a mitad de sesión»

**Sigue siendo cierto, y no se arregla desde el servidor.** Ninguno de los seis
clientes comprobados —Claude Code, Codex, OpenCode, Gemini, Qwen y Copilot—
relee su configuración MCP a mitad de sesión. Es comportamiento del cliente.

Lo evitable era la conclusión. Por eso `install` escribe **también** en el
fichero de instrucciones que el agente ya está leyendo (`AGENTS.md`,
`CLAUDE.md`, `GEMINI.md`…), dentro de un bloque delimitado:

```markdown
<!-- jaravi:begin -->
## Jaravi — delega trabajo a otras CLIs de IA

`jaravi run --agent <id> --task "..."` delega sin registrar nada
y sin reiniciar. `jaravi agents` lista los perfiles disponibles.
<!-- jaravi:end -->
```

Ahí el agente lee, en la sesión actual, que existe un camino que no exige
reinicio.

## Si el binario se queda colgado

`jaravi-mcp` sin argumentos elige modo stdio cuando `stdin` está redirigido —que
es exactamente cómo la herramienta de shell de un agente ejecuta comandos—. El
primer contacto de un agente con el binario era, por tanto, un bloqueo hasta el
timeout, sin decir nada.

Desde la v0.10.0, cuando el modo se **infiere** (no cuando se pide con
`--stdio`), se escribe una línea a stderr antes de bloquear. Un cliente MCP real
ignora stderr; el llamante que nunca fue un cliente MCP se entera al instante.

> [!important] La lección que se repitió tres veces
> Una capacidad que no está en la superficie que el agente inspecciona primero,
> para ese agente **no existe**. Da igual lo bien construida que esté. El
> recorrido completo, versión a versión, está en
> [[Adopcion por Agentes|adopción por agentes]].

## Enseñarle la doctrina

Más allá de que pueda usarlo, conviene que sepa *cuándo*. La skill
`jaravi-orchestrator` enseña el criterio de hacer-vs-delegar:

```bash
npx skills add JOSETRA44/jaravi@jaravi-orchestrator -g -y
```

Se instala en el store universal `~/.agents/skills/` y se enlaza a las carpetas
de skills de Claude Code, OpenCode, Codex y demás CLIs compatibles. Si prefieres
no depender de eso, cópiala a mano:

```bash
mkdir -p ~/.agents/skills/jaravi-orchestrator
cp .claude/skills/jaravi-orchestrator/SKILL.md ~/.agents/skills/jaravi-orchestrator/
ln -s ~/.agents/skills/jaravi-orchestrator ~/.config/opencode/skills/jaravi-orchestrator
ln -s ~/.agents/skills/jaravi-orchestrator ~/.codex/skills/jaravi-orchestrator
```

En Windows sin privilegios de symlink, `ln -s` cae automáticamente a una junction
NTFS: funciona igual, sin modo administrador.

## Diagnóstico rápido

| Síntoma | Comprobación |
|---|---|
| El agente no ve las tools | `jaravi-mcp doctor` → sección *mcp clients*; ¿figura Jaravi? ¿reiniciaste el cliente? |
| Las ve pero no las usa | ¿Versión ≥ 0.8.0? Las instructions del handshake son lo primero que hay que revisar, no las tools. |
| Dice que no hay comando | `jaravi --version`. Si falla, `jaravi-mcp install` deja el alias. |
| Se queda colgado al invocarlo | Está en modo servidor. Usa un verbo: `jaravi doctor`. |

import { Link } from 'react-router-dom';
import Icon from '../ui/Icon.jsx';
import Terminal from '../ui/Terminal.jsx';

const MCP_LINES = [
  { tone: 'dim', text: '// .mcp.json — el cliente enciende y apaga el servidor' },
  { tone: 'cmd', text: '{ "mcpServers": { "jaravi": {' },
  { tone: 'cmd', text: '    "type": "stdio",' },
  { tone: 'cmd', text: '    "command": "jaravi-mcp",' },
  { tone: 'cmd', text: '    "args": ["--stdio"] } } }' },
  { tone: 'dim', text: '' },
  { tone: 'dim', text: '// y en la sesión del agente:' },
  { tone: 'key', text: 'run_agent(agent: "codex", task: "audita src/")' },
];

const CLI_LINES = [
  { tone: 'prompt', text: 'jaravi doctor' },
  { tone: 'ok', text: '  ok  motor listo · Scope Gate: C:\\Users\\USER\\source' },
  { tone: 'dim', text: '' },
  { tone: 'prompt', text: 'jaravi agents' },
  { tone: 'dim', text: '  codex  opencode  claude  gemini  qwen  copilot …' },
  { tone: 'dim', text: '' },
  { tone: 'prompt', text: 'jaravi run --agent codex --task "audita src/"' },
  { tone: 'ok', text: '  ✔ exit 0 · resumen acotado en stdout' },
];

export default function TwoDoors() {
  return (
    <section className="section band" id="puertas">
      <div className="wrap">
        <header data-reveal="">
          <p className="eyebrow">Dos entradas</p>
          <h2 className="section-title">Por protocolo o por shell. La misma sesión.</h2>
          <p className="section-lede">
            Un cliente MCP no relee su configuración a mitad de sesión — ninguno de los
            seis lo hace. Por eso el mismo binario es también un CLI completo: un agente
            que descubre Jaravi ahora mismo puede delegar sin registrar nada ni reiniciar.
          </p>
        </header>

        <div className="doors" data-reveal="">
          <article className="door">
            <div className="door__head">
              <span className="door__icon"><Icon name="plug" size={20} /></span>
              <div>
                <h3 className="door__title">Servidor MCP</h3>
                <p className="door__sub">Once tools, dos recursos, dos prompts.</p>
              </div>
            </div>
            <Terminal label=".mcp.json" lines={MCP_LINES} />
            <ul className="door__points">
              <li>Anotaciones por tool: el cliente sabe cuál es destructiva sin preguntar.</li>
              <li>Progreso real cada 5 s en las llamadas que esperan.</li>
              <li>Cancelación limpia: mata el árbol antes de propagarla.</li>
            </ul>
            <Link className="btn btn--ghost btn--sm" to="/docs/tools-mcp">
              Referencia de tools <Icon name="arrowRight" size={15} />
            </Link>
          </article>

          <article className="door door--accent">
            <div className="door__head">
              <span className="door__icon"><Icon name="terminal" size={20} /></span>
              <div>
                <h3 className="door__title">CLI de shell</h3>
                <p className="door__sub">Sin registro, sin reinicio, sin servidor.</p>
              </div>
            </div>
            <Terminal label="bash" lines={CLI_LINES} />
            <ul className="door__points">
              <li>Se adjunta a la instancia viva: las sesiones son las mismas.</li>
              <li>Sin instancia, monta un motor privado que dura el comando.</li>
              <li>Códigos de salida distintos para «falló» y «sigue corriendo».</li>
            </ul>
            <Link className="btn btn--ghost btn--sm" to="/docs/cli">
              Referencia del CLI <Icon name="arrowRight" size={15} />
            </Link>
          </article>
        </div>
      </div>
    </section>
  );
}

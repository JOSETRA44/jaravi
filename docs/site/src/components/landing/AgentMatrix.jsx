import { Link } from 'react-router-dom';
import Icon from '../ui/Icon.jsx';
import { agents, statusLabels } from '../../content/site.js';

const REAL = agents.filter((agent) => !agent.id.endsWith('-demo'));

export default function AgentMatrix() {
  return (
    <section className="section band" id="agentes">
      <div className="wrap">
        <div className="split" data-reveal="">
          <header>
            <p className="eyebrow">Sub-agentes</p>
            <h2 className="section-title">Añadir una CLI es una entrada en un JSON.</h2>
            <p className="section-lede">
              Casi todo CLI de agente moderno es un paquete npm con modo one-shot y un flag
              de auto-aprobación. Jaravi describe esa forma de manera declarativa, así que
              el catálogo no es una lista cerrada: es lo que tú declares.
            </p>
            <div className="split__actions">
              <Link className="btn btn--ghost btn--sm" to="/docs/perfiles">
                Escribir un perfil <Icon name="arrowRight" size={15} />
              </Link>
              <Link className="btn btn--quiet btn--sm" to="/docs/agentes">
                Ver la matriz completa
              </Link>
            </div>
          </header>

          <ul className="agent-grid">
            {REAL.map((agent) => {
              const status = statusLabels[agent.status];
              return (
                <li className="agent" key={agent.id}>
                  <div className="agent__head">
                    <code className="agent__id">{agent.id}</code>
                    <span className={`badge badge--${status.tone}`}>
                      <span className="badge__dot" />
                      {status.label}
                    </span>
                  </div>
                  <p className="agent__name">{agent.name}</p>
                  <code className="agent__invocation">{agent.invocation}</code>
                </li>
              );
            })}
          </ul>
        </div>

        <aside className="reload-note" data-reveal="">
          <span className="reload-note__icon"><Icon name="cpu" size={18} /></span>
          <p>
            <strong>reload_agents</strong> re-lee <code>agents.json</code> en vivo. Un
            perfil nuevo se usa al instante, sin reiniciar el servidor — que en modo stdio
            mataría la conexión del agente jefe. Un archivo mal formado se rechaza y el
            catálogo activo sobrevive intacto.
          </p>
        </aside>
      </div>
    </section>
  );
}

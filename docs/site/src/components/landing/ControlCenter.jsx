import { Link } from 'react-router-dom';
import Icon from '../ui/Icon.jsx';

const SESSIONS = [
  { id: '1a2b04', agent: 'codex', state: 'running', tone: 'ok', time: '02:14', lines: '1 284', tokens: '~18.2k', bar: 62 },
  { id: '3c4d91', agent: 'claude', state: 'queued', tone: 'warn', time: '—', lines: '0', tokens: '—', bar: 0 },
  { id: '7f3a2c', agent: 'opencode', state: 'exit 0', tone: 'dim', time: '00:47', lines: '2 310', tokens: '31.7k', bar: 100 },
];

const LOG = [
  { tone: 'dim', text: '[14:02:11] codex   analizando 1 284 archivos' },
  { tone: 'dim', text: '[14:02:19] codex   src/api/auth.ts  ✓ 312 ms' },
  { tone: 'warn', text: '[14:02:24] codex   auth.ts:88 token sin expiración' },
  { tone: 'dim', text: '[14:02:31] codex   escribiendo parche  ██████░░░░' },
  { tone: 'ok', text: '[14:02:44] opencode  exit 0 · 214 tests en verde' },
];

export default function ControlCenter() {
  return (
    <section className="section band" id="control-center">
      <div className="wrap">
        <div className="split split--reverse">
          <header data-reveal="">
            <p className="eyebrow">Observabilidad</p>
            <h2 className="section-title">Tú sí puedes mirar el firehose.</h2>
            <p className="section-lede">
              Lo que se le oculta al agente jefe no se te oculta a ti. Al arrancar,
              Jaravi sirve un Control Center en <code>localhost</code>: estado por sesión,
              tiempo que corre, líneas, tokens, qué rutas tiene reclamadas cada una y quién
              está en cola detrás.
            </p>
            <ul className="checklist">
              <li>Embebido en el ejecutable: sin wwwroot, sin build step, sin instalar nada.</li>
              <li>Empuja por el WebSocket <code>/ws/events</code>; una pestaña lenta jamás frena al motor.</li>
              <li>Multi-instancia: salta entre los repos que estés orquestando a la vez.</li>
              <li>Spawn y kill desde el navegador, reutilizando la misma REST.</li>
            </ul>
            <Link className="btn btn--ghost btn--sm" to="/docs/control-center">
              Cómo se abre <Icon name="arrowRight" size={15} />
            </Link>
          </header>

          <div className="cc" data-reveal="" aria-hidden="true">
            <div className="cc__chrome">
              <span className="terminal__dots">
                <span className="terminal__dot" />
                <span className="terminal__dot" />
                <span className="terminal__dot" />
              </span>
              <span className="cc__url">localhost:5210</span>
            </div>

            <div className="cc__body">
              <div className="cc__row">
                <span className="cc__title">Sesiones</span>
                <span className="badge badge--ok"><span className="badge__dot" />conectado</span>
              </div>

              <ul className="cc__sessions">
                {SESSIONS.map((session) => (
                  <li className="cc__session" key={session.id}>
                    <div className="cc__session-head">
                      <code className="cc__id">{session.id}</code>
                      <span className="cc__agent">{session.agent}</span>
                      <span className={`cc__state t-${session.tone}`}>{session.state}</span>
                    </div>
                    <div className="cc__meter">
                      <span className="cc__meter-fill" style={{ width: `${session.bar}%` }} />
                    </div>
                    <div className="cc__metrics">
                      <span>{session.time}</span>
                      <span>{session.lines} líneas</span>
                      <span>{session.tokens}</span>
                    </div>
                  </li>
                ))}
              </ul>

              <div className="cc__locks">
                <span className="cc__title">Locks</span>
                <p><code>src/Auth/**</code> → 1a2b04 · en cola: 3c4d91</p>
              </div>

              <div className="cc__console">
                {LOG.map((line) => (
                  <span className={`cc__log t-${line.tone}`} key={line.text}>{line.text}</span>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

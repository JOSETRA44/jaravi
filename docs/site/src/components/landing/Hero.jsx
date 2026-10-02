import { Link } from 'react-router-dom';
import Icon from '../ui/Icon.jsx';
import Terminal, { CommandLine } from '../ui/Terminal.jsx';
import { installCommand, site } from '../../content/site.js';

const SESSION = [
  { tone: 'prompt', text: 'jaravi run --agent codex --task "audita src/api y lista los fallos"' },
  { tone: 'dim', text: '  session 7f3a2c · codex · workdir C:\\src\\api' },
  { tone: 'dim', text: '  running ██████████████████░░░░  00:41' },
  { tone: 'dim', text: '  1 284 líneas absorbidas por el motor' },
  { tone: 'dim', text: '' },
  { tone: 'ok', text: '  ✔ exit 0 · 47 s · 12 líneas devueltas' },
  { tone: 'dim', text: '' },
  { tone: 'cmd', text: '  3 hallazgos' },
  { tone: 'warn', text: '  · src/api/auth.ts:88     token sin expiración' },
  { tone: 'warn', text: '  · src/api/users.ts:142   SQL concatenado' },
  { tone: 'warn', text: '  · src/api/index.ts:12    CORS abierto a *' },
];

const STATS = [
  { value: '10 000', label: 'líneas de buffer por sesión' },
  { value: '500', label: 'tope duro de lectura' },
  { value: '11', label: 'perfiles de CLI de fábrica' },
  { value: '132', label: 'tests, contrato incluido' },
];

export default function Hero() {
  return (
    <section className="hero">
      <div className="wrap hero__inner">
        <div className="hero__copy">
          <p className="eyebrow rise rise-1">
            Servidor MCP + CLI · {site.runtime} · {site.license}
          </p>

          <h1 className="hero__title rise rise-2">
            Tu agente manda.
            <br />
            <span className="hero__title-accent">Otras CLIs trabajan.</span>
          </h1>

          <p className="hero__lede rise rise-3">
            Jaravi ejecuta Codex, Claude Code, OpenCode, Gemini o Copilot como sub-agentes
            en tu máquina y devuelve un resumen acotado. El output crudo —decenas de miles
            de líneas— muere dentro del motor.
          </p>

          <div className="hero__actions rise rise-4">
            <Link className="btn btn--primary" to="/docs/inicio-rapido">
              Empezar en 30 segundos
              <Icon name="arrowRight" size={17} />
            </Link>
            <Link className="btn btn--ghost" to="/docs">
              <Icon name="book" size={17} />
              Leer la documentación
            </Link>
          </div>

          <div className="hero__install rise rise-4">
            <CommandLine command={installCommand} />
            <p className="hero__install-note">
              Instala el comando <code>jaravi-mcp</code>. Después,{' '}
              <code>jaravi-mcp install</code> lo registra solo en todos tus CLIs de IA.
            </p>
          </div>
        </div>

        <div className="hero__demo rise rise-3">
          <Terminal label="delegar y recoger" lines={SESSION} />
          <div className="hero__demo-caption">
            <span className="badge badge--accent">
              <span className="badge__dot" />
              1 284 → 12
            </span>
            <span>Lo que el motor absorbe frente a lo que tu contexto recibe.</span>
          </div>
        </div>
      </div>

      <div className="wrap">
        <dl className="hero__stats">
          {STATS.map((stat) => (
            <div className="hero__stat" key={stat.label}>
              <dt className="hero__stat-value">{stat.value}</dt>
              <dd className="hero__stat-label">{stat.label}</dd>
            </div>
          ))}
        </dl>
      </div>
    </section>
  );
}

import Icon from '../ui/Icon.jsx';

/** Ruido verosimil: es lo que de verdad escupe un CLI de agente trabajando. */
const NOISE = [
  'npm warn deprecated glob@7.2.3',
  '⠋ analizando 1 284 archivos…',
  'src/api/users.ts  ✓  312 ms',
  'ESLint: 41 problems (3 errors, 38 warnings)',
  '[32m✔[0m tsc --noEmit',
  'src/api/auth.ts:88:14  no-unsafe-token',
  '⠹ reintentando fetch (2/3)…',
  'vitest  ✓ 214 passed  (18.4s)',
  'src/api/index.ts:12:1  cors-wildcard',
  'writing patch  ██████░░░░  62%',
  'GET /openapi.json 200 41ms',
  'src/api/users.ts:142:9  sql-concat',
];

const STAGES = [
  { icon: 'shield', title: 'Scope Gate', detail: 'El workdir se valida contra las raíces permitidas antes de arrancar nada.' },
  { icon: 'bolt', title: 'Sanitizador ANSI', detail: 'Fuera escapes, spinners y control de cursor. Texto determinista.' },
  { icon: 'layers', title: 'Ring buffer', detail: '10 000 líneas por sesión. Lo viejo cae; el motor nunca se llena.' },
  { icon: 'gauge', title: 'Tope de lectura', detail: 'Ninguna llamada puede devolver más de 500 líneas. Server-side.' },
];

const RESULT = [
  { tone: 'ok', text: '✔ exit 0 · 47 s' },
  { tone: 'cmd', text: '3 hallazgos' },
  { tone: 'warn', text: 'auth.ts:88' },
  { tone: 'dim', text: '  token sin expiración' },
  { tone: 'warn', text: 'users.ts:142' },
  { tone: 'dim', text: '  SQL concatenado' },
  { tone: 'warn', text: 'index.ts:12' },
  { tone: 'dim', text: '  CORS abierto a *' },
];

export default function ContextCollapse() {
  return (
    <section className="section band" id="colapso">
      <div className="wrap">
        <header data-reveal="">
          <p className="eyebrow">El problema</p>
          <h2 className="section-title">El firehose entra. Solo sale el resumen.</h2>
          <p className="section-lede">
            Un sub-agente produce miles de líneas para resolver una tarea. Si esas líneas
            llegan al agente que orquesta, su ventana de contexto se agota antes que el
            trabajo. Jaravi se interpone por diseño, no por prompting.
          </p>
        </header>

        <div className="pipeline" data-reveal="">
          <div className="pipeline__col">
            <p className="pipeline__label">Sub-agentes</p>
            <div className="stream" aria-hidden="true">
              <div className="stream__track">
                {[...NOISE, ...NOISE].map((line, index) => (
                  <span className="stream__line" key={index}>{line.replace(/\[\d+m/g, '')}</span>
                ))}
              </div>
              <div className="stream__fade" />
            </div>
            <p className="pipeline__note">
              <strong>1 284 líneas</strong> en esta sesión. Sin acotar, esto es lo que
              entraría en tu contexto.
            </p>
          </div>

          <div className="pipeline__arrow" aria-hidden="true">
            <Icon name="arrowRight" size={20} />
          </div>

          <div className="pipeline__col pipeline__col--engine">
            <p className="pipeline__label">Jaravi.Engine</p>
            <ol className="stages">
              {STAGES.map((stage, index) => (
                <li className="stage" key={stage.title}>
                  <span className="stage__index" aria-hidden="true">{index + 1}</span>
                  <span className="stage__icon"><Icon name={stage.icon} size={17} /></span>
                  <span className="stage__text">
                    <strong className="stage__title">{stage.title}</strong>
                    <span className="stage__detail">{stage.detail}</span>
                  </span>
                </li>
              ))}
            </ol>
          </div>

          <div className="pipeline__arrow" aria-hidden="true">
            <Icon name="arrowRight" size={20} />
          </div>

          <div className="pipeline__col">
            <p className="pipeline__label">Tu contexto</p>
            <div className="result">
              {RESULT.map((line) => (
                <span className={`result__line t-${line.tone}`} key={line.text}>{line.text}</span>
              ))}
            </div>
            <p className="pipeline__note">
              <strong>12 líneas.</strong> Todo lo demás sigue disponible y consultable —
              simplemente no se te impone.
            </p>
          </div>
        </div>
      </div>
    </section>
  );
}

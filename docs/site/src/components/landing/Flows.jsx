import { useId, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import Icon from '../ui/Icon.jsx';
import Terminal from '../ui/Terminal.jsx';

const FLOWS = [
  {
    id: 'delegar',
    label: 'Delegar',
    title: 'Una tarea acotada, ida y vuelta',
    blurb:
      'run hace spawn, espera y resume en una sola llamada. Si el sub-agente tarda más que la espera, la sesión sigue viva y te dice con qué recogerla.',
    doc: '/docs/delegar',
    lines: [
      { tone: 'prompt', text: 'jaravi run --agent opencode --task "migra los tests a vitest" --wait 120' },
      { tone: 'dim', text: '  session a91f04 · opencode · esperando…' },
      { tone: 'ok', text: '  ✔ exit 0 · 96 s · 2 310 líneas absorbidas' },
      { tone: 'cmd', text: '  14 archivos migrados · 214 tests en verde' },
      { tone: 'dim', text: '' },
      { tone: 'dim', text: '  # si expira la espera, no ha fallado:' },
      { tone: 'key', text: '  exit 4 · sigue corriendo → jaravi await a91f04' },
    ],
  },
  {
    id: 'encadenar',
    label: 'Encadenar',
    title: 'Un auditor alimenta a un corrector',
    blurb:
      'El motor inyecta un extracto acotado del resultado anterior en el task del siguiente. El intermedio nunca pasa por quien orquesta.',
    doc: '/docs/pipelines',
    lines: [
      { tone: 'prompt', text: 'ID=$(jaravi spawn --agent codex --task "audita src/api" --quiet)' },
      { tone: 'prompt', text: 'jaravi await "$ID"' },
      { tone: 'ok', text: '  ✔ exit 0 · 3 hallazgos' },
      { tone: 'dim', text: '' },
      { tone: 'prompt', text: 'jaravi run --agent claude --task "arregla lo que encontró" \\' },
      { tone: 'prompt', text: '  --input-from "$ID" --input-kind summary' },
      { tone: 'dim', text: '  ↳ el motor concatena el resumen al task, no tú' },
      { tone: 'ok', text: '  ✔ exit 0 · 3 parches aplicados' },
    ],
  },
  {
    id: 'paralelizar',
    label: 'Paralelizar',
    title: 'Varias manos sin pisarse',
    blurb:
      'Cada sesión reclama las rutas que va a escribir. Un solapamiento se rechaza con la sesión culpable, o se encola hasta que el claim se libera.',
    doc: '/docs/claims',
    lines: [
      { tone: 'prompt', text: 'jaravi spawn --agent codex   --task "refactor auth" --claims "src/Auth/**"' },
      { tone: 'ok', text: '  session 1a2b · running' },
      { tone: 'dim', text: '' },
      { tone: 'prompt', text: 'jaravi spawn --agent claude --task "docs de auth" --claims "src/Auth/**" \\' },
      { tone: 'prompt', text: '  --on-conflict queue' },
      { tone: 'warn', text: '  session 3c4d · queued  (queuedBehind: 1a2b)' },
      { tone: 'dim', text: '' },
      { tone: 'dim', text: '  # al terminar 1a2b, el motor arranca 3c4d solo' },
      { tone: 'ok', text: '  session 3c4d · running' },
    ],
  },
];

export default function Flows() {
  const [active, setActive] = useState(0);
  const baseId = useId();
  const tabsRef = useRef([]);

  // Flechas para moverse entre pestanas: es lo que espera un lector de pantalla
  // en un patron tablist, y de paso lo agradece cualquiera con teclado.
  const onKeyDown = (event) => {
    const delta = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (!delta) return;
    event.preventDefault();
    const next = (active + delta + FLOWS.length) % FLOWS.length;
    setActive(next);
    tabsRef.current[next]?.focus();
  };

  const flow = FLOWS[active];

  return (
    <section className="section band" id="flujos">
      <div className="wrap">
        <header data-reveal="">
          <p className="eyebrow">Cómo se usa</p>
          <h2 className="section-title">Tres formas de repartir el trabajo.</h2>
          <p className="section-lede">
            Las tres existen a la vez por MCP y por shell, y comparten las mismas sesiones.
            Elige por la forma de la tarea, no por la interfaz.
          </p>
        </header>

        <div className="flows" data-reveal="">
          <div className="flows__tabs" role="tablist" aria-label="Formas de delegar" onKeyDown={onKeyDown}>
            {FLOWS.map((item, index) => (
              <button
                key={item.id}
                type="button"
                role="tab"
                id={`${baseId}-tab-${item.id}`}
                aria-selected={index === active}
                aria-controls={`${baseId}-panel-${item.id}`}
                tabIndex={index === active ? 0 : -1}
                ref={(node) => { tabsRef.current[index] = node; }}
                className="flows__tab"
                data-active={index === active}
                onClick={() => setActive(index)}
              >
                <span className="flows__tab-index">0{index + 1}</span>
                {item.label}
              </button>
            ))}
          </div>

          <div
            className="flows__panel"
            role="tabpanel"
            id={`${baseId}-panel-${flow.id}`}
            aria-labelledby={`${baseId}-tab-${flow.id}`}
            tabIndex={0}
          >
            <div className="flows__copy">
              <h3 className="flows__title">{flow.title}</h3>
              <p className="flows__blurb">{flow.blurb}</p>
              <Link className="btn btn--ghost btn--sm" to={flow.doc}>
                Cómo funciona <Icon name="arrowRight" size={15} />
              </Link>
            </div>
            <Terminal label={flow.id} lines={flow.lines} className="flows__terminal" />
          </div>
        </div>
      </div>
    </section>
  );
}

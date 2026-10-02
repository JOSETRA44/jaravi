import { Link } from 'react-router-dom';
import Icon from '../ui/Icon.jsx';

/**
 * La leccion mas util del proyecto, y la que mas se repite fuera de el: una
 * capacidad que no aparece en la superficie que el agente inspecciona primero,
 * para ese agente no existe. Merece un bloque propio en la portada.
 */
const ROUNDS = [
  {
    version: 'v0.8.0',
    surface: 'El handshake MCP',
    found: 'El campo instructions estaba vacío.',
    fix: 'El servidor se presenta: qué es, por qué su uso está autorizado y cuáles son sus límites reales.',
  },
  {
    version: 'v0.9.0',
    surface: 'jaravi-mcp --help',
    found: 'Ningún subcomando. Ejecutarlo levantaba un servidor y bloqueaba.',
    fix: 'Nueve verbos que cubren todo lo que hacen las tools, empezando por run.',
  },
  {
    version: 'v0.10.0',
    surface: 'La instalación misma',
    found: 'Un bloque de prosa en el README que alguien tenía que encontrar y pegar.',
    fix: 'install escribe la config MCP de cada cliente y el fichero de instrucciones que ya está leyendo.',
  },
];

export default function Adoption() {
  return (
    <section className="section band" id="adopcion">
      <div className="wrap">
        <header data-reveal="">
          <p className="eyebrow">Por qué importa el envoltorio</p>
          <h2 className="section-title">
            Una capacidad que el agente no ve, para él no existe.
          </h2>
          <p className="section-lede">
            Tres veces seguidas, agentes externos se negaron a usar Jaravi. Ninguna fue por
            la calidad de las tools: las tres fueron por la superficie que el agente
            inspecciona antes de decidir. Está documentado, con la corrección de cada una.
          </p>
        </header>

        <ol className="rounds" data-reveal="">
          {ROUNDS.map((round) => (
            <li className="round" key={round.version}>
              <div className="round__head">
                <code className="round__version">{round.version}</code>
                <h3 className="round__surface">{round.surface}</h3>
              </div>
              <p className="round__found">
                <span className="round__tag round__tag--bad">Encontraba</span>
                {round.found}
              </p>
              <p className="round__fix">
                <span className="round__tag round__tag--ok">Ahora</span>
                {round.fix}
              </p>
            </li>
          ))}
        </ol>

        <p className="rounds__foot" data-reveal="">
          <Link className="btn btn--ghost btn--sm" to="/docs/adopcion-por-agentes">
            Leer la nota completa <Icon name="arrowRight" size={15} />
          </Link>
        </p>
      </div>
    </section>
  );
}

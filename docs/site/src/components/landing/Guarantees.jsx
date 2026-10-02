import { Link } from 'react-router-dom';
import Icon from '../ui/Icon.jsx';
import { exitCodes } from '../../content/site.js';

const GUARANTEES = [
  {
    icon: 'shield',
    title: 'Scope Gate',
    body: 'Cada workdir se valida contra Engine:AllowedRoots antes de arrancar el proceso. Un prompt inyectado no puede sacar un sub-agente de tus carpetas.',
  },
  {
    icon: 'layers',
    title: 'Ring buffer acotado',
    body: '10 000 líneas por sesión, y ninguna lectura puede pedir más de 500. El tope es del servidor: no depende de que quien llama se porte bien.',
  },
  {
    icon: 'lock',
    title: 'Deadline duro',
    body: 'Cada sesión lleva su plazo. Al agotarse, Kill(entireProcessTree: true): ni un nieto huérfano consumiendo CPU en segundo plano.',
  },
  {
    icon: 'bolt',
    title: 'Nada corre invisible',
    body: 'Toda sesión es listable, inspeccionable y matable, por MCP, por REST y desde el Control Center. Todo el output queda registrado.',
  },
];

export default function Guarantees() {
  return (
    <section className="section band" id="garantias">
      <div className="wrap">
        <header data-reveal="">
          <p className="eyebrow">Garantías</p>
          <h2 className="section-title">Nada corre invisible. Nada corre fuera de sitio.</h2>
          <p className="section-lede">
            Delegar significa que otro proceso escribe en tu disco con tus credenciales.
            Los límites que hacen eso aceptable están en el motor, no en las instrucciones
            que le des al modelo.
          </p>
        </header>

        <div className="grid grid--4" data-reveal="">
          {GUARANTEES.map((item) => (
            <article className="card guarantee" key={item.title}>
              <span className="guarantee__icon"><Icon name={item.icon} size={19} /></span>
              <h3 className="card__title">{item.title}</h3>
              <p className="card__body">{item.body}</p>
            </article>
          ))}
        </div>

        <div className="exits" data-reveal="">
          <div className="exits__copy">
            <h3 className="exits__title">«No ha terminado» no es «ha fallado».</h3>
            <p className="exits__blurb">
              Son códigos distintos a propósito. Confundirlos es el malentendido más caro
              que se ha observado en consumo real: un agente ve un timeout, concluye que la
              tarea fracasó y la relanza — mientras la primera sigue trabajando.
            </p>
            <Link className="btn btn--ghost btn--sm" to="/docs/cli#codigos-de-salida">
              Códigos de salida <Icon name="arrowRight" size={15} />
            </Link>
          </div>

          <ul className="exits__list">
            {exitCodes.map((exit) => (
              <li className={`exit exit--${exit.tone}`} key={exit.code}>
                <code className="exit__code">{exit.code}</code>
                <span className="exit__meaning">{exit.meaning}</span>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  );
}

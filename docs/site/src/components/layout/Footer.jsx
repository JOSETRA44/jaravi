import { Link } from 'react-router-dom';
import { Wordmark } from '../ui/Icon.jsx';
import { site } from '../../content/site.js';
import { sections } from '../../content/nav.js';

const COLUMNS = ['empezar', 'referencia', 'ingenieria'];

export default function Footer() {
  const columns = COLUMNS
    .map((id) => sections.find((section) => section.id === id))
    .filter(Boolean);

  return (
    <footer className="footer">
      <div className="wrap">
        <div className="footer__grid">
          <div>
            <Link to="/" className="brand" aria-label="Jaravi, inicio">
              <Wordmark />
              <span className="brand__word">Jaravi</span>
            </Link>
            <p className="footer__note">
              Orquestación determinista de sub-agentes CLI. El firehose entra en el motor;
              del motor solo sale un resumen acotado.
            </p>
          </div>

          {columns.map((section) => (
            <div key={section.id}>
              <h2 className="footer__title">{section.title}</h2>
              <ul className="footer__list">
                {section.items.map((item) => (
                  <li key={item.slug}>
                    <Link to={item.path ?? `/docs/${item.slug}`}>{item.title}</Link>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="footer__bottom">
          <span>© {new Date().getFullYear()} Jaravi · Licencia {site.license}</span>
          <a href={site.repo} target="_blank" rel="noopener noreferrer">Código fuente</a>
          <a href={site.issues} target="_blank" rel="noopener noreferrer">Incidencias</a>
          <a href={site.nuget} target="_blank" rel="noopener noreferrer">NuGet</a>
          <a href={`${import.meta.env.BASE_URL}${site.paper}`}>Paper (PDF)</a>
          <span style={{ marginLeft: 'auto' }}>{site.runtime} · v{site.version}</span>
        </div>
      </div>
    </footer>
  );
}

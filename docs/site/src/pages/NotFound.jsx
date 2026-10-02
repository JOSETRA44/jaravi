import { Link } from 'react-router-dom';
import Icon from '../components/ui/Icon.jsx';
import { docs } from '../content/nav.js';

export default function NotFound() {
  return (
    <section className="section wrap wrap--narrow">
      <p className="eyebrow">Error 404</p>
      <h1 className="section-title">Esta ruta no existe.</h1>
      <p className="section-lede">
        El enlace apunta a una página que no está publicada. Estos son los sitios
        por los que suele empezar la gente.
      </p>

      <div className="grid grid--3" style={{ marginTop: 'var(--sp-6)' }}>
        {['introduccion', 'inicio-rapido', 'cli']
          .map((slug) => docs.find((doc) => doc.slug === slug))
          .filter(Boolean)
          .map((doc) => (
            <Link key={doc.slug} to={doc.path} className="card card--link" style={{ textDecoration: 'none' }}>
              <h2 className="card__title">{doc.title}</h2>
              <p className="card__body">{doc.summary}</p>
            </Link>
          ))}
      </div>

      <p style={{ marginTop: 'var(--sp-6)' }}>
        <Link to="/" className="btn btn--ghost">
          <Icon name="arrowLeft" size={16} />
          Volver al inicio
        </Link>
      </p>
    </section>
  );
}

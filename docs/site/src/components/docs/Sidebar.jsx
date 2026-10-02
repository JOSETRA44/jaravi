import { Link } from 'react-router-dom';
import { sections } from '../../content/nav.js';
import { prefetch } from '../../lib/docStore.js';
import { useIsActive } from '../../lib/hooks.js';

/**
 * El indice completo, siempre desplegado. Con 22 paginas repartidas en cinco
 * secciones, los acordeones esconderian mas de lo que ordenan: el usuario ve
 * de un vistazo todo lo que hay y donde esta.
 */
export default function Sidebar({ onNavigate }) {
  const isActive = useIsActive();

  return (
    <nav aria-label="Documentación">
      {sections.map((section) => (
        <div className="sidebar__section" key={section.id}>
          <h2 className="sidebar__heading">{section.title}</h2>
          <ul className="sidebar__list">
            {section.items.map((item) => {
              const path = item.path ?? `/docs/${item.slug}`;
              const active = isActive(path);
              return (
                <li key={item.slug}>
                  <Link
                    to={path}
                    className="sidebar__link"
                    aria-current={active ? 'page' : undefined}
                    onClick={onNavigate}
                    onMouseEnter={() => prefetch(item.file)}
                    onFocus={() => prefetch(item.file)}
                  >
                    {item.title}
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </nav>
  );
}

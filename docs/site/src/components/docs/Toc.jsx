import { useMemo } from 'react';
import { useScrollSpy } from '../../lib/hooks.js';

/** Indice de la pagina, con resaltado del apartado que se esta leyendo. */
export default function Toc({ headings }) {
  const ids = useMemo(() => headings.map((heading) => heading.id), [headings]);
  const active = useScrollSpy(ids);

  if (headings.length < 2) return <div />;

  return (
    <nav className="docs__toc" aria-label="En esta página">
      <p className="toc__heading">En esta página</p>
      <ul className="toc__list">
        {headings.map((heading) => (
          <li key={heading.id}>
            <a
              className="toc__link"
              href={`#${heading.id}`}
              data-level={heading.level}
              data-active={active === heading.id}
            >
              {heading.text}
            </a>
          </li>
        ))}
      </ul>
    </nav>
  );
}

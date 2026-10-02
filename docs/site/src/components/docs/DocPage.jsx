import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import Icon from '../ui/Icon.jsx';
import Toc from './Toc.jsx';
import { docs } from '../../content/nav.js';
import { get as getDoc, load as loadDoc, prefetch } from '../../lib/docStore.js';

/** Enriquece el HTML que viene del markdown sin volver a renderizarlo. */
function useCodeEnhancements(containerRef, key) {
  useEffect(() => {
    const container = containerRef.current;
    if (!container) return undefined;

    const buttons = [];
    container.querySelectorAll('figure.code').forEach((figure) => {
      if (figure.querySelector('.copy-btn')) return;
      const code = figure.querySelector('code');
      if (!code) return;

      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'copy-btn';
      button.setAttribute('aria-label', 'Copiar el bloque de código');
      button.innerHTML =
        '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
        'stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
        '<rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15V6a1 1 0 0 1 1-1h9"/></svg>';

      const onClick = async () => {
        try {
          await navigator.clipboard.writeText(code.innerText);
        } catch { /* sin portapapeles: el usuario puede seleccionar */ }
        button.dataset.copied = 'true';
        button.setAttribute('aria-label', 'Copiado al portapapeles');
        setTimeout(() => {
          button.dataset.copied = 'false';
          button.setAttribute('aria-label', 'Copiar el bloque de código');
        }, 1800);
      };

      button.addEventListener('click', onClick);
      buttons.push([button, onClick]);
      figure.appendChild(button);
    });

    return () => buttons.forEach(([button, onClick]) => button.removeEventListener('click', onClick));
  }, [containerRef, key]);
}

/** Los enlaces del markdown son <a> normales: el router los recoge aqui. */
function useInternalLinks(containerRef, key) {
  const navigate = useNavigate();

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return undefined;

    const internal = (anchor) => {
      const href = anchor?.getAttribute('href') ?? '';
      return href.startsWith('/') && anchor.target !== '_blank';
    };

    const onClick = (event) => {
      const anchor = event.target.closest('a');
      if (!internal(anchor) || event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) return;
      event.preventDefault();
      navigate(anchor.getAttribute('href'));
    };

    // Al apuntar un enlace ya se pide su contenido: para cuando se hace clic,
    // suele estar en memoria y la navegacion es inmediata.
    const onEnter = (event) => {
      const anchor = event.target.closest?.('a');
      if (!internal(anchor)) return;
      const slug = anchor.getAttribute('href').replace(/^\/docs\/?/, '').split('#')[0];
      prefetch(slug || 'introduccion');
    };

    container.addEventListener('click', onClick);
    container.addEventListener('mouseover', onEnter);
    return () => {
      container.removeEventListener('click', onClick);
      container.removeEventListener('mouseover', onEnter);
    };
  }, [containerRef, navigate, key]);
}

export default function DocPage({ doc }) {
  const containerRef = useRef(null);
  const location = useLocation();

  // El estado inicial sale del almacen ya sembrado (por el servidor al
  // prerenderizar, o desde el DOM al hidratar), asi que el primer render
  // coincide siempre con lo que el visitante ya tiene delante.
  const [entry, setEntry] = useState(() => getDoc(doc.file));
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    const cached = getDoc(doc.file);

    if (cached) {
      setEntry(cached);
      setFailed(false);
      return () => { cancelled = true; };
    }

    setEntry(null);
    setFailed(false);
    loadDoc(doc.file)
      .then((loaded) => { if (!cancelled) setEntry(loaded); })
      .catch(() => { if (!cancelled) setFailed(true); });

    return () => { cancelled = true; };
  }, [doc.file]);

  useCodeEnhancements(containerRef, entry ? doc.slug : null);
  useInternalLinks(containerRef, entry ? doc.slug : null);

  // Un ancla en la URL debe llevar al sitio incluso cuando el contenido acaba
  // de llegar (el navegador ya intento saltar antes de que existiera).
  useEffect(() => {
    if (!location.hash || !entry) return;
    const target = document.getElementById(location.hash.slice(1));
    if (target) target.scrollIntoView({ block: 'start', behavior: 'auto' });
  }, [location.hash, entry]);

  const index = docs.findIndex((item) => item.slug === doc.slug);
  const previous = index > 0 ? docs[index - 1] : null;
  const next = index >= 0 && index < docs.length - 1 ? docs[index + 1] : null;

  return (
    <>
      <article className="docs__main">
        <header className="doc-head">
          <p className="doc-head__crumbs">
            <Link to="/docs">Docs</Link>
            <span aria-hidden="true">/</span>
            <span>{doc.section}</span>
          </p>
          <h1 className="doc-head__title">{doc.title}</h1>
          {doc.summary ? <p className="doc-head__summary">{doc.summary}</p> : null}
        </header>

        {entry ? (
          <div
            className="doc-body prose"
            data-doc-slug={doc.file}
            ref={containerRef}
            dangerouslySetInnerHTML={{ __html: entry.html }}
          />
        ) : failed ? (
          <div className="doc-body">
            <p className="doc-fallback">
              No se pudo cargar esta página. Comprueba la conexión y vuelve a intentarlo.
            </p>
          </div>
        ) : (
          <div className="doc-body doc-skeleton" aria-busy="true" aria-label="Cargando la página">
            <span className="doc-skeleton__line" style={{ width: '92%' }} />
            <span className="doc-skeleton__line" style={{ width: '78%' }} />
            <span className="doc-skeleton__line" style={{ width: '85%' }} />
            <span className="doc-skeleton__block" />
            <span className="doc-skeleton__line" style={{ width: '64%' }} />
            <span className="doc-skeleton__line" style={{ width: '88%' }} />
          </div>
        )}

        <nav className="pager" aria-label="Páginas contiguas">
          {previous ? (
            <Link className="pager__link pager__link--prev" to={previous.path}>
              <span className="pager__kicker">
                <Icon name="arrowLeft" size={13} /> Anterior
              </span>
              <span className="pager__title">{previous.title}</span>
            </Link>
          ) : <span />}
          {next ? (
            <Link className="pager__link pager__link--next" to={next.path}>
              <span className="pager__kicker">
                Siguiente <Icon name="arrowRight" size={13} />
              </span>
              <span className="pager__title">{next.title}</span>
            </Link>
          ) : <span />}
        </nav>
      </article>

      <Toc headings={entry?.headings ?? []} />
    </>
  );
}

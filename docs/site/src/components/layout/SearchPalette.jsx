import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import Icon from '../ui/Icon.jsx';
import { docs } from '../../content/nav.js';
import { useBodyLock, useEscape } from '../../lib/hooks.js';

// Marcas diacriticas (U+0300..U+036F), construidas por codigo para que el
// rango no dependa de como guarde el editor estos caracteres invisibles.
const MARKS = new RegExp(String.fromCharCode(91, 0x300, 45, 0x36f, 93), 'g');

/** Normaliza para que "operacion" encuentre "Operación". */
function fold(text) {
  return text.toLowerCase().normalize('NFD').replace(MARKS, '');
}

/**
 * El indice completo pesa mas que el buscador entero, asi que se descarga la
 * primera vez que alguien abre el panel. Hasta entonces, y si la descarga
 * falla, se busca sobre titulos y resumenes: peor, pero nunca vacio.
 */
const FALLBACK = docs.map((doc) => ({
  slug: doc.slug,
  path: doc.path,
  title: doc.title,
  section: doc.section,
  summary: doc.summary,
  headings: [],
  text: '',
}));

let indexPromise = null;

function loadIndex() {
  if (!indexPromise) {
    const base = import.meta.env?.BASE_URL ?? '/';
    indexPromise = fetch(`${base}search-index.json`)
      .then((response) => (response.ok ? response.json() : FALLBACK))
      .catch(() => FALLBACK);
  }
  return indexPromise;
}

/**
 * Puntua por donde aparece el termino, no solo por si aparece: un titulo pesa
 * mas que un encabezado, y un encabezado mas que el cuerpo. Con 24 paginas eso
 * basta y evita arrastrar un motor de busqueda entero al bundle.
 */
function search(entries, query) {
  const terms = fold(query).split(/\s+/).filter(Boolean);
  if (!terms.length) return [];

  return entries
    .map((entry) => {
      const haystack = fold([entry.title, entry.summary, entry.section, entry.text].join(' '));
      let score = 0;
      let snippet = entry.summary;

      for (const term of terms) {
        const inTitle = fold(entry.title).includes(term);
        const heading = entry.headings.find((h) => fold(h.text).includes(term));
        const inBody = haystack.includes(term);

        if (inTitle) score += 12;
        if (heading) { score += 6; snippet = heading.text; }
        if (inBody) score += 2;
        if (!inTitle && !heading && !inBody) return null;
      }

      return { ...entry, score, snippet };
    })
    .filter(Boolean)
    .sort((a, b) => b.score - a.score)
    .slice(0, 8);
}

export default function SearchPalette({ open, onClose }) {
  const [query, setQuery] = useState('');
  const [cursor, setCursor] = useState(0);
  const [entries, setEntries] = useState(FALLBACK);
  const inputRef = useRef(null);
  const navigate = useNavigate();

  const results = useMemo(
    () => (query.trim() ? search(entries, query) : docs.slice(0, 6)),
    [entries, query],
  );

  useBodyLock(open);
  useEscape(onClose, open);

  useEffect(() => {
    if (!open) return undefined;
    setQuery('');
    setCursor(0);
    loadIndex().then(setEntries);
    const id = requestAnimationFrame(() => inputRef.current?.focus());
    return () => cancelAnimationFrame(id);
  }, [open]);

  useEffect(() => { setCursor(0); }, [query]);

  if (!open) return null;

  const go = (path) => { onClose(); navigate(path); };

  const onKeyDown = (event) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setCursor((c) => Math.min(c + 1, results.length - 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setCursor((c) => Math.max(c - 1, 0));
    } else if (event.key === 'Enter' && results[cursor]) {
      event.preventDefault();
      go(results[cursor].path);
    }
  };

  return (
    <div className="palette" role="dialog" aria-modal="true" aria-label="Buscar en la documentación">
      <button type="button" className="palette__scrim" onClick={onClose} aria-label="Cerrar la búsqueda" />
      <div className="palette__panel">
        <div className="palette__field">
          <Icon name="search" size={18} />
          <input
            ref={inputRef}
            className="palette__input"
            type="search"
            value={query}
            placeholder="Buscar páginas, comandos, tools…"
            onChange={(event) => setQuery(event.target.value)}
            onKeyDown={onKeyDown}
            aria-label="Término de búsqueda"
            aria-controls="palette-results"
          />
          <button type="button" className="icon-btn" onClick={onClose} aria-label="Cerrar">
            <Icon name="close" size={18} />
          </button>
        </div>

        <div className="palette__results" id="palette-results" role="listbox">
          {results.length === 0 ? (
            <p className="palette__empty">
              Nada para «{query}». Prueba con <code>run</code>, <code>Scope Gate</code> o <code>pipelines</code>.
            </p>
          ) : (
            <>
              <p className="palette__group">{query.trim() ? 'Resultados' : 'Sugerencias'}</p>
              {results.map((result, index) => (
                <button
                  key={result.slug}
                  type="button"
                  role="option"
                  aria-selected={index === cursor}
                  className="palette__item"
                  data-active={index === cursor}
                  onMouseEnter={() => setCursor(index)}
                  onClick={() => go(result.path)}
                >
                  <span className="palette__item-title">{result.title}</span>
                  <span className="palette__item-sub">
                    {result.section} · {result.snippet ?? result.summary}
                  </span>
                </button>
              ))}
            </>
          )}
        </div>

        <div className="palette__foot">
          <span><kbd>↑</kbd> <kbd>↓</kbd> navegar</span>
          <span><kbd>Enter</kbd> abrir</span>
          <span><kbd>Esc</kbd> cerrar</span>
        </div>
      </div>
    </div>
  );
}

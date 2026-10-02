import { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import Icon, { Wordmark } from '../ui/Icon.jsx';
import { site } from '../../content/site.js';
import { useStuck, useTheme, useMounted, useIsActive } from '../../lib/hooks.js';

const LINKS = [
  { to: '/docs', label: 'Documentación' },
  { to: '/docs/inicio-rapido', label: 'Inicio rápido' },
  { to: '/docs/cli', label: 'CLI' },
  { to: '/docs/tools-mcp', label: 'Tools MCP' },
];

export default function Header({ onOpenSearch }) {
  const stuck = useStuck();
  const { theme, toggle } = useTheme();
  const mounted = useMounted();
  const isActive = useIsActive();
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);

  useEffect(() => { setMenuOpen(false); }, [location.pathname]);

  return (
    <header className="masthead" data-stuck={stuck}>
      <div className="masthead__inner">
        <Link to="/" className="brand" aria-label="Jaravi, inicio">
          <Wordmark />
          <span className="brand__word">Jaravi</span>
          <span className="brand__version">v{site.version}</span>
        </Link>

        <nav className="masthead__nav" aria-label="Principal">
          {LINKS.map((link) => (
            <Link
              key={link.to}
              to={link.to}
              className="nav-link"
              aria-current={isActive(link.to) ? 'page' : undefined}
            >
              {link.label}
            </Link>
          ))}
        </nav>

        <div className="masthead__actions">
          <button type="button" className="search-trigger" onClick={onOpenSearch}>
            <Icon name="search" size={16} />
            <span className="search-trigger__label">Buscar…</span>
            <kbd>Ctrl K</kbd>
          </button>

          <button
            type="button"
            className="icon-btn search-trigger-compact"
            onClick={onOpenSearch}
            aria-label="Buscar en la documentación"
          >
            <Icon name="search" size={18} />
          </button>

          <button
            type="button"
            className="icon-btn"
            onClick={toggle}
            aria-label={theme === 'dark' ? 'Cambiar a tema claro' : 'Cambiar a tema oscuro'}
          >
            <Icon name={mounted && theme === 'light' ? 'moon' : 'sun'} size={18} />
          </button>

          <a
            className="icon-btn"
            href={site.repo}
            target="_blank"
            rel="noopener noreferrer"
            aria-label="Jaravi en GitHub"
          >
            <Icon name="github" size={18} />
          </a>

          <button
            type="button"
            className="icon-btn masthead__burger"
            onClick={() => setMenuOpen((open) => !open)}
            aria-expanded={menuOpen}
            aria-label={menuOpen ? 'Cerrar el menú' : 'Abrir el menú'}
          >
            <Icon name={menuOpen ? 'close' : 'menu'} size={20} />
          </button>
        </div>
      </div>

      {menuOpen ? (
        <nav className="mobile-menu" aria-label="Principal (móvil)">
          {LINKS.map((link) => (
            <Link key={link.to} to={link.to} className="mobile-menu__link">
              {link.label}
            </Link>
          ))}
          <a className="mobile-menu__link" href={site.repo} target="_blank" rel="noopener noreferrer">
            GitHub ↗
          </a>
        </nav>
      ) : null}
    </header>
  );
}

import { useCallback, useEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import Header from './Header.jsx';
import Footer from './Footer.jsx';
import SearchPalette from './SearchPalette.jsx';
import { metaFor } from '../../content/meta.js';

export default function SiteLayout() {
  const [searchOpen, setSearchOpen] = useState(false);
  const location = useLocation();

  const openSearch = useCallback(() => setSearchOpen(true), []);
  const closeSearch = useCallback(() => setSearchOpen(false), []);

  // Ctrl/Cmd+K es el gesto que ya espera cualquiera que lea documentacion.
  useEffect(() => {
    const onKey = (event) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setSearchOpen((open) => !open);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  // Al cambiar de pagina se vuelve arriba, salvo cuando la URL apunta a un
  // ancla concreta: ahi manda el ancla.
  useEffect(() => {
    if (location.hash) return;
    window.scrollTo({ top: 0, behavior: 'auto' });
  }, [location.pathname, location.hash]);

  // El prerender ya escribe el titulo correcto en cada HTML; esto lo mantiene
  // al dia cuando se navega sin recargar.
  useEffect(() => {
    const meta = metaFor(location.pathname);
    document.title = meta.title;
    document.querySelector('meta[name="description"]')?.setAttribute('content', meta.description);
  }, [location.pathname]);

  return (
    <>
      <a className="skip-link" href="#contenido">Saltar al contenido</a>
      <div className="aurora" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />
      <div className="shell">
        <Header onOpenSearch={openSearch} />
        <main className="shell__main" id="contenido">
          <Outlet />
        </main>
        <Footer />
      </div>
      <SearchPalette open={searchOpen} onClose={closeSearch} />
    </>
  );
}

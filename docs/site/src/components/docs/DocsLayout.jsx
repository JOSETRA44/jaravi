import { useEffect, useState } from 'react';
import { useLocation } from 'react-router-dom';
import Sidebar from './Sidebar.jsx';
import Icon from '../ui/Icon.jsx';
import { useBodyLock, useEscape } from '../../lib/hooks.js';
import { docs } from '../../content/nav.js';

export default function DocsLayout({ children }) {
  const [drawer, setDrawer] = useState(false);
  const location = useLocation();

  useBodyLock(drawer);
  useEscape(() => setDrawer(false), drawer);
  useEffect(() => { setDrawer(false); }, [location.pathname]);

  // GitHub Pages sirve /docs/cli/ con barra final, mientras que el prerender
  // renderiza /docs/cli. Comparar en crudo hacia que el servidor y el cliente
  // pintaran textos distintos — un fallo de hidratacion real, no cosmetico.
  const here = location.pathname.replace(/\/+$/, '') || '/';
  const current = docs.find((doc) => doc.path === here);

  return (
    <div className="docs">
      <aside className="docs__aside">
        <Sidebar />
      </aside>

      <div className="docs__mobilebar">
        <button type="button" className="btn btn--ghost btn--sm" onClick={() => setDrawer(true)}>
          <Icon name="menu" size={16} />
          Índice
        </button>
        <span className="docs__mobilebar-current">{current?.title ?? 'Documentación'}</span>
      </div>

      {drawer ? (
        <div className="drawer">
          <button type="button" className="drawer__scrim" onClick={() => setDrawer(false)} aria-label="Cerrar el índice" />
          <div className="drawer__panel" role="dialog" aria-modal="true" aria-label="Índice de la documentación">
            <div className="drawer__head">
              <span className="sidebar__heading" style={{ paddingLeft: 0 }}>Documentación</span>
              <button type="button" className="icon-btn" onClick={() => setDrawer(false)} aria-label="Cerrar">
                <Icon name="close" size={20} />
              </button>
            </div>
            <Sidebar onNavigate={() => setDrawer(false)} />
          </div>
        </div>
      ) : null}

      {children}
    </div>
  );
}

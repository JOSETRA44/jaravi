import { StrictMode } from 'react';
import { hydrateRoot, createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App.jsx';
import { seedFromDom } from './lib/docStore.js';
import './styles/index.css';

const basename = import.meta.env.BASE_URL.replace(/\/$/, '');
const root = document.getElementById('root');

// Antes de hidratar: la pagina que el servidor ya pinto entra en el almacen.
// Si no, el primer render la encontraria vacia y React borraria contenido que
// el visitante ya esta leyendo.
seedFromDom();

const tree = (
  <StrictMode>
    <BrowserRouter basename={basename}>
      <App />
    </BrowserRouter>
  </StrictMode>
);

// El build genera HTML real para cada ruta; en dev el contenedor esta vacio.
if (root.firstChild) hydrateRoot(root, tree);
else createRoot(root).render(tree);

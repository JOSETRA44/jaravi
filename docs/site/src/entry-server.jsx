import { StrictMode } from 'react';
import { renderToString } from 'react-dom/server';
import { StaticRouter } from 'react-router';
import App from './App.jsx';
import { docs } from './content/nav.js';
import { pages } from './content/pages.js';
import { seed } from './lib/docStore.js';
import './styles/index.css';

// Este modulo solo corre en el build: aqui si conviene tener las 24 paginas en
// memoria de golpe. Lo que nunca las importa es el bundle del navegador.
for (const doc of docs) {
  const page = pages[doc.file];
  if (page) seed(doc.file, { html: page.html, headings: page.headings });
}

export function render(url) {
  return renderToString(
    <StrictMode>
      <StaticRouter location={url}>
        <App />
      </StaticRouter>
    </StrictMode>,
  );
}

/** Rutas que el prerender convierte en HTML propio. */
export const routes = ['/', ...docs.map((doc) => doc.path)];

export { metaFor } from './content/meta.js';

/** El contenido que se sirve como JSON al navegar entre paginas. */
export function contentFiles() {
  return docs
    .filter((doc) => pages[doc.file])
    .map((doc) => ({
      name: `${doc.file}.json`,
      body: JSON.stringify({
        html: pages[doc.file].html,
        headings: pages[doc.file].headings,
      }),
    }));
}

/** Indice de busqueda: se descarga la primera vez que se abre el buscador. */
export function searchIndex() {
  return JSON.stringify(
    docs.map((doc) => {
      const page = pages[doc.file];
      return {
        slug: doc.slug,
        file: doc.file,
        path: doc.path,
        title: doc.title,
        section: doc.section,
        summary: doc.summary,
        headings: page?.headings.map((h) => ({ id: h.id, text: h.text })) ?? [],
        text: (page?.text ?? '').slice(0, 1800),
      };
    }),
  );
}

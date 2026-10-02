/**
 * Almacen del contenido de la documentacion.
 *
 * El HTML de las 24 paginas pesa mas que toda la aplicacion, asi que no viaja
 * dentro del bundle. Cada pagina llega por una via distinta segun el momento:
 *
 *  - En el prerender, el servidor siembra el almacen con todo (ver entry-server).
 *  - En la primera carga del navegador, la pagina ya esta pintada en el HTML;
 *    se siembra leyendola del propio DOM, de modo que la hidratacion encuentra
 *    exactamente lo que ya hay y no repinta nada.
 *  - Al navegar a otra pagina, se pide su JSON. Es la unica vez que hay red.
 */

const cache = new Map();
const inFlight = new Map();

export function seed(slug, entry) {
  if (entry) cache.set(slug, entry);
}

export function get(slug) {
  return cache.get(slug) ?? null;
}

function contentUrl(slug) {
  const base = import.meta.env?.BASE_URL ?? '/';
  return `${base}content/${slug}.json`;
}

export async function load(slug) {
  const cached = cache.get(slug);
  if (cached) return cached;

  if (!inFlight.has(slug)) {
    const request = fetch(contentUrl(slug))
      .then((response) => {
        if (!response.ok) throw new Error(`No se pudo cargar ${slug} (${response.status})`);
        return response.json();
      })
      .then((entry) => {
        cache.set(slug, entry);
        inFlight.delete(slug);
        return entry;
      })
      .catch((error) => {
        inFlight.delete(slug);
        throw error;
      });
    inFlight.set(slug, request);
  }

  return inFlight.get(slug);
}

/** Adelanta la descarga al pasar el raton por un enlace: navegar sale gratis. */
export function prefetch(slug) {
  if (cache.has(slug) || inFlight.has(slug)) return;
  load(slug).catch(() => { /* un prefetch fallido no es un error visible */ });
}

/**
 * Reconstruye la entrada de la pagina que el servidor ya pinto, sin volver a
 * descargarla. Los encabezados se leen del DOM porque el markdown ya les puso
 * el id: no hace falta un indice aparte para el sumario lateral.
 */
export function seedFromDom() {
  if (typeof document === 'undefined') return;

  const node = document.querySelector('[data-doc-slug]');
  if (!node) return;

  const headings = Array.from(node.querySelectorAll('h2[id], h3[id]')).map((heading) => ({
    level: Number(heading.tagName.slice(1)),
    id: heading.id,
    text: heading.textContent.replace(/#$/, '').trim(),
  }));

  seed(node.dataset.docSlug, { html: node.innerHTML, headings });
}

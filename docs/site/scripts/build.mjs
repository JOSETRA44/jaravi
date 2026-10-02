/**
 * Build en dos pasadas + prerender.
 *
 * GitHub Pages sirve ficheros estaticos: una SPA pura obligaria a cada visita
 * a esperar al JavaScript antes de ver una linea de documentacion, y dejaria a
 * los buscadores con paginas vacias. Asi que el cliente se compila normal y
 * despues se renderiza cada ruta a su propio index.html; el JS solo hidrata lo
 * que ya esta pintado y se encarga de la navegacion posterior.
 */
import { build } from 'vite';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const siteRoot = path.resolve(here, '..');
const outRoot = path.resolve(siteRoot, '..');       // docs/
const ssrDir = path.resolve(siteRoot, '.ssr');
const base = process.env.JARAVI_BASE ?? '/jaravi/';
const origin = process.env.JARAVI_ORIGIN ?? 'https://josetra44.github.io';

const log = (message) => process.stdout.write(`  ${message}\n`);

async function rm(target) {
  await fs.rm(target, { recursive: true, force: true });
}

/** Las rutas viejas se borran: una pagina renombrada no debe sobrevivir. */
async function cleanPreviousOutput() {
  await rm(path.join(outRoot, 'assets'));
  await rm(path.join(outRoot, 'docs'));
  await rm(path.join(outRoot, 'content'));
  await rm(path.join(outRoot, '.vite'));
  await rm(ssrDir);
}

function escapeHtml(text) {
  return String(text).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
}

/** Inyecta el HTML renderizado y los metadatos propios de cada ruta. */
function composePage(template, { html, title, description, canonical }) {
  return template
    .replace(/<title>[\s\S]*?<\/title>/, `<title>${escapeHtml(title)}</title>`)
    .replace(
      /<meta name="description" content="[\s\S]*?">/,
      `<meta name="description" content="${escapeHtml(description)}">`,
    )
    .replace(
      /<meta property="og:title" content="[\s\S]*?">/,
      `<meta property="og:title" content="${escapeHtml(title)}">`,
    )
    .replace(
      /<meta property="og:description" content="[\s\S]*?">/,
      `<meta property="og:description" content="${escapeHtml(description)}">`,
    )
    .replace(
      /<link rel="canonical" href="[\s\S]*?">/,
      `<link rel="canonical" href="${canonical}">`,
    )
    .replace('<!--app-html-->', html);
}

async function writePage(routePath, contents) {
  const relative = routePath === '/' ? 'index.html' : path.join(routePath.replace(/^\//, ''), 'index.html');
  const file = path.join(outRoot, relative);
  await fs.mkdir(path.dirname(file), { recursive: true });
  await fs.writeFile(file, contents, 'utf8');
  return relative.replace(/\\/g, '/');
}

async function main() {
  const started = Date.now();
  process.stdout.write('\nJaravi · sitio publico\n\n');

  log('limpiando la salida anterior…');
  await cleanPreviousOutput();

  log('compilando el cliente…');
  await build({ root: siteRoot, base, logLevel: 'warn' });

  log('compilando el renderizador…');
  await build({
    root: siteRoot,
    base,
    logLevel: 'warn',
    build: {
      ssr: 'src/entry-server.jsx',
      outDir: '.ssr',
      emptyOutDir: true,
      copyPublicDir: false,
      rollupOptions: { output: { entryFileNames: 'entry-server.js' } },
    },
  });

  const { render, routes, metaFor, contentFiles, searchIndex } = await import(
    new URL('../.ssr/entry-server.js', import.meta.url).href
  );

  // El HTML de la documentacion pesa mas que la aplicacion entera, asi que sale
  // del bundle y se sirve como JSON: la primera carga ya lo tiene pintado y las
  // navegaciones posteriores solo piden la pagina a la que van.
  const contentDir = path.join(outRoot, 'content');
  await fs.mkdir(contentDir, { recursive: true });
  const files = contentFiles();
  for (const file of files) {
    await fs.writeFile(path.join(contentDir, file.name), file.body, 'utf8');
  }
  await fs.writeFile(path.join(outRoot, 'search-index.json'), searchIndex(), 'utf8');
  log(`contenido: ${files.length} páginas en content/ + search-index.json`);

  const template = await fs.readFile(path.join(outRoot, 'index.html'), 'utf8');
  if (!template.includes('<!--app-html-->')) {
    throw new Error('La plantilla no tiene el marcador <!--app-html-->: revisa index.html');
  }

  log(`prerenderizando ${routes.length} rutas…`);
  for (const route of routes) {
    const meta = metaFor(route);
    const html = render(route);
    const canonical = `${origin}${base}${route === '/' ? '' : route.replace(/^\//, '')}`;
    const written = await writePage(route, composePage(template, { html, ...meta, canonical }));
    log(`  · ${written}`);
  }

  // GitHub Pages entrega 404.html ante cualquier ruta que no exista en disco.
  // Al ser la misma aplicacion, la URL se conserva y el router pinta el 404
  // real en vez de un mensaje generico de la plataforma.
  const notFound = composePage(template, {
    html: render('/__404__'),
    title: 'Página no encontrada · Jaravi',
    description: 'La ruta solicitada no existe en la documentación de Jaravi.',
    canonical: `${origin}${base}`,
  });
  await fs.writeFile(path.join(outRoot, '404.html'), notFound, 'utf8');
  log('  · 404.html');

  // Sin esto, GitHub Pages pasa la carpeta por Jekyll y descarta todo lo que
  // empiece por guion bajo (los chunks de Vite, entre otras cosas).
  await fs.writeFile(path.join(outRoot, '.nojekyll'), '', 'utf8');

  const urls = routes
    .map((route) => {
      const loc = `${origin}${base}${route === '/' ? '' : route.replace(/^\//, '')}`;
      const priority = route === '/' ? '1.0' : '0.7';
      return `  <url><loc>${loc}</loc><priority>${priority}</priority></url>`;
    })
    .join('\n');
  await fs.writeFile(
    path.join(outRoot, 'sitemap.xml'),
    `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls}\n</urlset>\n`,
    'utf8',
  );
  await fs.writeFile(
    path.join(outRoot, 'robots.txt'),
    `User-agent: *\nAllow: /\nSitemap: ${origin}${base}sitemap.xml\n`,
    'utf8',
  );
  log('  · sitemap.xml, robots.txt, .nojekyll');

  await rm(ssrDir);
  await rm(path.join(outRoot, '.vite'));

  const seconds = ((Date.now() - started) / 1000).toFixed(1);
  process.stdout.write(`\nListo en ${seconds}s. Publica la carpeta docs/ tal cual.\n\n`);
}

main().catch((error) => {
  process.stderr.write(`\nEl build fallo: ${error?.stack ?? error}\n`);
  process.exit(1);
});

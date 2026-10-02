/**
 * Sirve docs/ como lo haria GitHub Pages: montado bajo /jaravi/, con
 * index.html por directorio y 404.html como ultimo recurso. Sin esto, un
 * enlace absoluto roto en produccion pasa desapercibido en local.
 */
import http from 'node:http';
import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const outRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const base = process.env.JARAVI_BASE ?? '/jaravi/';
const port = Number(process.env.PORT ?? 4173);

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.webp': 'image/webp',
  '.woff2': 'font/woff2',
  '.pdf': 'application/pdf',
  '.xml': 'application/xml; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
};

async function resolveFile(pathname) {
  const relative = pathname.startsWith(base) ? pathname.slice(base.length) : pathname.replace(/^\//, '');
  const target = path.join(outRoot, decodeURIComponent(relative));

  if (!target.startsWith(outRoot)) return null;

  try {
    const stat = await fsp.stat(target);
    if (stat.isDirectory()) {
      const index = path.join(target, 'index.html');
      return fs.existsSync(index) ? index : null;
    }
    return target;
  } catch {
    return null;
  }
}

const server = http.createServer(async (request, response) => {
  const { pathname } = new URL(request.url, `http://localhost:${port}`);

  if (pathname === '/' || pathname === base.replace(/\/$/, '')) {
    response.writeHead(302, { Location: base });
    response.end();
    return;
  }

  let file = await resolveFile(pathname);
  let status = 200;

  if (!file) {
    file = path.join(outRoot, '404.html');
    status = 404;
  }

  try {
    const body = await fsp.readFile(file);
    response.writeHead(status, {
      'content-type': TYPES[path.extname(file)] ?? 'application/octet-stream',
      // Es una vista previa: nunca debe enseñar el build anterior.
      'cache-control': 'no-store, must-revalidate',
    });
    response.end(body);
  } catch {
    response.writeHead(500);
    response.end('error');
  }
});

server.listen(port, () => {
  process.stdout.write(`\nVista previa en http://localhost:${port}${base}\n\n`);
});

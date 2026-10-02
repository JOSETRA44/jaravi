# Sitio público de Jaravi

Fuente del sitio que se publica en GitHub Pages desde la carpeta `docs/`.
React + Vite, con **prerender**: cada ruta se convierte en un `index.html` real,
así que la documentación se lee sin JavaScript y los buscadores ven texto, no un
contenedor vacío.

```
docs/                     ← lo que GitHub Pages sirve (artefacto, versionado)
├── index.html            portada prerenderizada
├── 404.html              misma app; GitHub la sirve ante rutas inexistentes
├── docs/<slug>/index.html   una página real por documento
├── content/<slug>.json   el HTML de cada página, para navegar sin recargar
├── search-index.json     índice del buscador (se descarga al abrirlo)
├── assets/               JS y CSS con hash
├── sitemap.xml, robots.txt, .nojekyll
├── jaravi.pdf / jaravi.tex   el paper (no lo toca el build)
└── site/                 ← ESTA carpeta: el código fuente
```

## Trabajar en él

```bash
cd docs/site
npm install
npm run dev        # servidor de desarrollo con recarga en caliente
npm run build      # genera el artefacto en docs/
npm run preview    # sirve docs/ como lo hace GitHub Pages, en /jaravi/
```

`npm run preview` monta el sitio bajo `/jaravi/` a propósito: es la única forma
de detectar en local un enlace absoluto que se rompería en producción.

## Publicar

El artefacto está versionado, así que publicar es hacer *commit* de `docs/` y,
en GitHub, **Settings → Pages → Deploy from a branch → `main` / `/docs`**.
No hace falta ninguna acción de CI.

Después de cambiar contenido o componentes hay que **volver a construir**:

```bash
cd docs/site && npm run build
```

## Estructura del código

| Carpeta | Contenido |
|---|---|
| `src/content/nav.js` | El registro de la documentación: secciones, slugs y títulos. Una página que no esté aquí no existe. |
| `src/content/md/` | El contenido, en markdown. |
| `src/content/site.js` | Datos del proyecto (versión, repo, catálogo de agentes, códigos de salida). |
| `src/components/layout/` | Cabecera, pie, buscador y el armazón común. |
| `src/components/docs/` | Barra lateral, índice de página, paginador y el renderizador de documentos. |
| `src/components/landing/` | Las secciones de la portada, una por archivo. |
| `src/components/ui/` | Piezas compartidas: iconos, terminal, botón de copiar. |
| `src/styles/` | Tokens, base, componentes y prosa. Ningún componente escribe un color literal. |
| `plugins/vite-plugin-markdown.js` | Convierte los `.md` en HTML en tiempo de build: resaltado con Shiki, callouts de Obsidian y enlaces `[[wiki]]`. |
| `scripts/build.mjs` | Las dos pasadas de build y el prerender. |

## Añadir una página

1. Escribe `src/content/md/mi-pagina.md`.
2. Añádela a la sección que corresponda en `src/content/nav.js`
   (`slug`, `file`, `title`, `summary`).
3. `npm run build`.

La barra lateral, el buscador, el paginador, el sitemap y la ruta prerenderizada
salen todos de ese registro: no hay que tocar nada más.

## Detalles que conviene saber

- **Ruta base.** El sitio vive en `/jaravi/`. Para publicarlo en otro sitio
  (un dominio propio, por ejemplo): `JARAVI_BASE=/ npm run build`.
- **El contenido no viaja en el bundle.** El HTML de las 24 páginas pesa más que
  la aplicación entera, así que se sirve como JSON: la primera carga ya viene
  pintada en el HTML y solo se descarga la página a la que navegas.
- **Barras finales.** GitHub Pages sirve `/docs/cli/` y el prerender genera
  `/docs/cli`. Todo lo que compare rutas usa `useIsActive()`, que las normaliza.
- **Temas.** Claro y oscuro se definen como tokens en `src/styles/tokens.css`.
  El script en línea de `index.html` aplica el tema antes de pintar para que no
  haya un destello de la paleta contraria.

import fs from 'node:fs/promises';
import MarkdownIt from 'markdown-it';
import anchor from 'markdown-it-anchor';
import { createHighlighter } from 'shiki';
import { wikiLinkMap, docBySlug } from '../src/content/nav.js';

const LANGS = [
  'bash', 'shell', 'powershell', 'json', 'jsonc', 'csharp', 'javascript',
  'typescript', 'toml', 'ini', 'xml', 'yaml', 'diff', 'text', 'http', 'markdown',
];

const CALLOUTS = {
  note:      { label: 'Nota',       tone: 'note' },
  info:      { label: 'Información', tone: 'note' },
  abstract:  { label: 'En resumen', tone: 'note' },
  tip:       { label: 'Consejo',    tone: 'tip' },
  success:   { label: 'Resuelto',   tone: 'tip' },
  important: { label: 'Importante', tone: 'important' },
  warning:   { label: 'Cuidado',    tone: 'warning' },
  danger:    { label: 'Atención',   tone: 'danger' },
  bug:       { label: 'Bug',        tone: 'danger' },
};

/** Quita el frontmatter YAML y lo devuelve como pares clave/valor planos. */
function splitFrontmatter(source) {
  const match = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?/.exec(source);
  if (!match) return { data: {}, body: source };
  const data = {};
  for (const line of match[1].split(/\r?\n/)) {
    const kv = /^([A-Za-z_][\w-]*):\s*(.*)$/.exec(line);
    if (kv) data[kv[1]] = kv[2].replace(/^["']|["']$/g, '');
  }
  return { data, body: source.slice(match[0].length) };
}

/**
 * `> [!tip] Titulo` es sintaxis de Obsidian, no de CommonMark. Se reescribe a
 * HTML de bloque dejando el cuerpo como markdown: markdown-it cierra el bloque
 * HTML en la linea en blanco y vuelve a parsear lo de dentro con normalidad.
 */
function expandCallouts(body) {
  const lines = body.split(/\r?\n/);
  const out = [];
  for (let i = 0; i < lines.length; i++) {
    const head = /^>\s*\[!(\w+)\][+-]?\s*(.*)$/.exec(lines[i]);
    if (!head) { out.push(lines[i]); continue; }

    const kind = head[1].toLowerCase();
    const meta = CALLOUTS[kind] ?? CALLOUTS.note;
    const title = head[2].trim() || meta.label;

    const inner = [];
    let j = i + 1;
    for (; j < lines.length && /^>/.test(lines[j]); j++) {
      inner.push(lines[j].replace(/^>\s?/, ''));
    }
    i = j - 1;

    out.push(
      `<aside class="callout callout--${meta.tone}" data-callout="${meta.tone}">`,
      `<p class="callout__title">${escapeHtml(title)}</p>`,
      '',
      ...inner,
      '',
      '</aside>',
      '',
    );
  }
  return out.join('\n');
}

/**
 * [[Nota#Ancla|Etiqueta]] -> enlace real dentro del sitio.
 * Dentro de una tabla la barra va escapada (`\|`), asi que se acepta con y sin
 * la barra invertida; si no, el enlace quedaria como texto crudo justo en las
 * tablas, que es donde mas se consultan.
 */
function expandWikiLinks(body) {
  return body.replace(/\[\[([^\]|#\\]+)(#[^\]|\\]+)?(?:\\?\|([^\]]+))?\]\]/g, (raw, target, hash, label) => {
    const key = target.trim();
    const slug = wikiLinkMap[key] ?? key.toLowerCase().replace(/\s+/g, '-');
    const doc = docBySlug[slug];
    // Sin etiqueta explicita manda el titulo real de la pagina: los nombres de
    // nota del vault venian sin acentos y quedaban feos como texto de enlace.
    const text = (label ?? doc?.title ?? key).trim();
    if (!doc) return text;
    const anchorPart = hash ? '#' + slugify(hash.slice(1)) : '';
    return `[${text}](${doc.path}${anchorPart})`;
  });
}

function slugify(text) {
  return text
    .toLowerCase()
    .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

function escapeHtml(text) {
  return text.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
}

/** Texto plano para el indice de busqueda: sin etiquetas ni ruido de codigo. */
function toPlainText(html) {
  return html
    .replace(/<pre[\s\S]*?<\/pre>/g, ' ')
    .replace(/<[^>]+>/g, ' ')
    .replace(/&[a-z]+;/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

export default function markdownPlugin() {
  let highlighter;
  let ready;

  // Vite transforma los .md en paralelo, asi que hay que cachear la *promesa*
  // y no el resultado: cachear el resultado deja que veinte transformaciones
  // simultaneas creen veinte resaltadores antes de que termine el primero.
  function ensure() {
    ready ??= create();
    return ready;
  }

  async function create() {
    let md;
    highlighter = await createHighlighter({
      themes: ['vesper', 'github-light'],
      langs: LANGS,
    });

    md = new MarkdownIt({
      html: true,
      linkify: true,
      typographer: false,
      highlight(code, lang) {
        const language = highlighter.getLoadedLanguages().includes(lang) ? lang : 'text';
        const html = highlighter.codeToHtml(code, {
          lang: language,
          themes: { light: 'github-light', dark: 'vesper' },
          defaultColor: false,
        });
        const label = lang ? escapeHtml(lang) : 'texto';
        return `<figure class="code" data-lang="${label}">${html}</figure>`;
      },
    });

    md.use(anchor, {
      level: [2, 3],
      slugify,
      permalink: anchor.permalink.linkInsideHeader({
        symbol: '#',
        placement: 'after',
        class: 'heading-anchor',
        ariaHidden: false,
      }),
    });

    // Los enlaces externos abren fuera y lo anuncian; los internos los recoge
    // el router en el cliente (ver useInternalLinks).
    const defaultLink = md.renderer.rules.link_open
      ?? ((tokens, idx, options, _env, self) => self.renderToken(tokens, idx, options));
    md.renderer.rules.link_open = (tokens, idx, options, env, self) => {
      const href = tokens[idx].attrGet('href') ?? '';
      if (/^https?:\/\//.test(href)) {
        tokens[idx].attrSet('target', '_blank');
        tokens[idx].attrSet('rel', 'noopener noreferrer');
        tokens[idx].attrJoin('class', 'external-link');
      }
      return defaultLink(tokens, idx, options, env, self);
    };

    md.renderer.rules.table_open = () => '<div class="table-scroll"><table>';
    md.renderer.rules.table_close = () => '</table></div>';

    return md;
  }

  return {
    name: 'jaravi-markdown',
    enforce: 'pre',

    async transform(_code, id) {
      if (!id.endsWith('.md')) return null;
      const renderer = await ensure();
      const raw = await fs.readFile(id.split('?')[0], 'utf8');
      const { data, body } = splitFrontmatter(raw);

      const prepared = expandCallouts(expandWikiLinks(body));
      const html = renderer.render(prepared);

      const headings = [];
      const headingRe = /<h([23])[^>]*id="([^"]+)"[^>]*>([\s\S]*?)<\/h[23]>/g;
      let m;
      while ((m = headingRe.exec(html)) !== null) {
        headings.push({
          level: Number(m[1]),
          id: m[2],
          // El enlace permanente vive dentro del encabezado, asi que su '#'
          // acabaria pegado al titulo en el indice y en el buscador.
          text: m[3]
            .replace(/<a[^>]*class="[^"]*heading-anchor[^"]*"[\s\S]*?<\/a>/g, '')
            .replace(/<[^>]+>/g, '')
            .trim(),
        });
      }

      const firstHeading = /<h1[^>]*>([\s\S]*?)<\/h1>/.exec(html);
      const payload = {
        title: data.title ?? (firstHeading ? firstHeading[1].replace(/<[^>]+>/g, '').trim() : ''),
        html: html.replace(/<h1[\s\S]*?<\/h1>/, ''),
        headings,
        text: toPlainText(html).slice(0, 4000),
      };

      return {
        code: `export default ${JSON.stringify(payload)};`,
        map: null,
      };
    },
  };
}

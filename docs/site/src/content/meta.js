import { docs } from './nav.js';

export const DEFAULT_META = {
  title: 'Jaravi — Orquesta otras CLIs de IA como sub-agentes',
  description:
    'Servidor MCP y CLI que delega trabajo a OpenCode, Codex, Claude Code, Gemini o Copilot y devuelve resultados acotados. El output crudo nunca llega a tu contexto.',
};

const NOT_FOUND = {
  title: 'Página no encontrada · Jaravi',
  description: 'La ruta solicitada no existe en la documentación de Jaravi.',
};

/**
 * Los metadatos de una ruta. La usa el prerender para escribirlos en el HTML y
 * el navegador para mantenerlos al dia al navegar sin recargar: si solo lo
 * hiciera el prerender, la pestana conservaria el titulo de la pagina anterior.
 */
export function metaFor(pathname) {
  const here = pathname.replace(/\/+$/, '') || '/';
  if (here === '/') return DEFAULT_META;

  const doc = docs.find((d) => d.path === here);
  if (doc) {
    return {
      title: `${doc.title} · Documentación de Jaravi`,
      description: doc.summary,
    };
  }

  return NOT_FOUND;
}

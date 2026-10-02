// El plugin de markdown convierte cada .md en { title, html, headings, text }.
// Se cargan de golpe (eager) porque el sitio se prerenderiza entero: no hay
// nada que diferir, y asi el indice de busqueda se construye en el mismo paso.
const modules = import.meta.glob('./md/*.md', { eager: true });

export const pages = Object.fromEntries(
  Object.entries(modules).map(([path, mod]) => [
    path.replace('./md/', '').replace('.md', ''),
    mod.default,
  ]),
);

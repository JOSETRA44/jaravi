// Registro unico de la documentacion.
// Lo consume el sitio (sidebar, buscador, paginador) y tambien el plugin de
// markdown en tiempo de build, para resolver los enlaces internos. Una sola
// fuente de verdad: si una pagina no esta aqui, no existe.

export const sections = [
  {
    id: 'empezar',
    title: 'Empezar',
    items: [
      { slug: 'introduccion', path: '/docs', file: 'introduccion',
        title: 'Introducción', summary: 'Qué es Jaravi y qué problema resuelve.' },
      { slug: 'instalacion', file: 'instalacion',
        title: 'Instalación', summary: 'dotnet tool, autoconfiguración y verificación.' },
      { slug: 'inicio-rapido', file: 'inicio-rapido',
        title: 'Inicio rápido', summary: 'Tu primera delegación en tres comandos.' },
      { slug: 'que-tu-agente-lo-use', file: 'que-tu-agente-lo-use',
        title: 'Que tu agente lo use', summary: 'Por qué un agente se resiste y cómo desbloquearlo.' },
    ],
  },
  {
    id: 'conceptos',
    title: 'Conceptos',
    items: [
      { slug: 'arquitectura', file: 'arquitectura',
        title: 'Arquitectura', summary: 'Cuatro proyectos, dependencias hacia el núcleo.' },
      { slug: 'motor', file: 'motor',
        title: 'Motor (Engine)', summary: 'SessionManager, ring buffer, event bus, Scope Gate.' },
      { slug: 'servidor-mcp', file: 'servidor-mcp',
        title: 'Servidor MCP', summary: 'Las cinco superficies del protocolo que cubre Jaravi.' },
      { slug: 'operacion', file: 'operacion',
        title: 'Modos de operación', summary: 'stdio, HTTP y la selección automática.' },
      { slug: 'garantias', file: 'garantias',
        title: 'Garantías y límites', summary: 'Lo que impide el colapso de contexto.' },
    ],
  },
  {
    id: 'guias',
    title: 'Guías',
    items: [
      { slug: 'delegar', file: 'delegar',
        title: 'Delegar trabajo', summary: 'Cuándo delegar, cuándo hacerlo tú.' },
      { slug: 'pipelines', file: 'pipelines',
        title: 'Encadenar agentes', summary: 'Pipelines sin leer el resultado intermedio.' },
      { slug: 'claims', file: 'claims',
        title: 'Claims y colas', summary: 'Escrituras en paralelo sin pisarse.' },
      { slug: 'agentes', file: 'agentes',
        title: 'Catálogo de agentes', summary: 'Los CLIs verificados y el patrón universal.' },
      { slug: 'perfiles', file: 'perfiles',
        title: 'Perfiles (agents.json)', summary: 'Añadir un CLI nuevo sin tocar código.' },
      { slug: 'control-center', file: 'control-center',
        title: 'Control Center', summary: 'Observar el firehose en vivo desde el navegador.' },
    ],
  },
  {
    id: 'referencia',
    title: 'Referencia',
    items: [
      { slug: 'cli', file: 'cli',
        title: 'CLI', summary: 'Los once verbos, sus opciones y sus códigos de salida.' },
      { slug: 'tools-mcp', file: 'tools-mcp',
        title: 'Tools MCP', summary: 'Las once tools, con anotaciones y parámetros.' },
      { slug: 'recursos-y-prompts', file: 'recursos-y-prompts',
        title: 'Recursos y prompts', summary: 'Contexto direccionable por URI y plantillas.' },
      { slug: 'api-rest', file: 'api-rest',
        title: 'API REST y WebSocket', summary: 'Endpoints, eventos y OpenAPI.' },
      { slug: 'configuracion', file: 'configuracion',
        title: 'Configuración', summary: 'appsettings.json, variables de entorno y rutas.' },
    ],
  },
  {
    id: 'ingenieria',
    title: 'Notas de ingeniería',
    items: [
      { slug: 'adopcion-por-agentes', file: 'adopcion-por-agentes',
        title: 'Adopción por agentes', summary: 'El patrón que se repitió tres veces.' },
      { slug: 'autoconfiguracion', file: 'autoconfiguracion',
        title: 'Autoconfiguración', summary: 'Qué escribe install, y las garantías que da.' },
      { slug: 'brechas-mcp', file: 'brechas-mcp',
        title: 'Brechas del protocolo MCP', summary: 'Qué superficies faltaban y cuál se descartó.' },
      { slug: 'pruebas-de-contrato', file: 'pruebas-de-contrato',
        title: 'Pruebas de contrato', summary: 'Afirmar sobre el cable, no sobre los métodos.' },
    ],
  },
];

/** Todas las paginas en orden de lectura, con su ruta resuelta. */
export const docs = sections.flatMap((section) =>
  section.items.map((item) => ({
    ...item,
    path: item.path ?? `/docs/${item.slug}`,
    section: section.title,
    sectionId: section.id,
  })),
);

export const docBySlug = Object.fromEntries(docs.map((d) => [d.slug, d]));

/**
 * Enlaces internos heredados del vault de Obsidian: [[Servidor MCP]] apunta a
 * una nota, no a una URL. El mapa traduce cada titulo de nota a su pagina.
 */
export const wikiLinkMap = {
  'Home': 'introduccion',
  'Jaravi': 'introduccion',
  'Arquitectura': 'arquitectura',
  'Motor (Engine)': 'motor',
  'Servidor MCP': 'servidor-mcp',
  'Operacion': 'operacion',
  'Operación': 'operacion',
  'Perfiles de Agentes': 'perfiles',
  'Catalogo de Agentes': 'agentes',
  'Catálogo de Agentes': 'agentes',
  'Control Center (Web)': 'control-center',
  'Dashboard': 'control-center',
  'CLI de Shell': 'cli',
  'Autoconfiguracion': 'autoconfiguracion',
  'Autoconfiguración': 'autoconfiguracion',
  'Adopcion por Agentes': 'adopcion-por-agentes',
  'Adopción por Agentes': 'adopcion-por-agentes',
  'Brechas del Protocolo MCP': 'brechas-mcp',
  'Pruebas de Contrato MCP': 'pruebas-de-contrato',
  'Investigacion de Mercado': 'introduccion',
  'Modelo de Negocio': 'introduccion',

  // Paginas nuevas del sitio cuyo titulo no coincide con su slug. Las que si
  // coinciden (Instalación, Inicio rápido, Configuración, Tools MCP…) las
  // resuelve el propio slugify del plugin.
  'Introducción': 'introduccion',
  'Modos de operación': 'operacion',
  'Garantías y límites': 'garantias',
  'Delegar trabajo': 'delegar',
  'Encadenar agentes': 'pipelines',
  'Claims y colas': 'claims',
  'API REST y WebSocket': 'api-rest',
  'Catálogo de agentes': 'agentes',
  'Perfiles (agents.json)': 'perfiles',
};

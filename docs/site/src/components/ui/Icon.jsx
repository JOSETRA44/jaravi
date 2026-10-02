/**
 * Set de iconos propio, trazo 1.75 y caja de 24, para no arrastrar una
 * dependencia entera por doce glifos. Todos heredan currentColor y llevan
 * aria-hidden: el significado siempre va en el texto que los acompana.
 */

const PATHS = {
  search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-3.6-3.6" /></>,
  menu: <path d="M4 7h16M4 12h16M4 17h16" />,
  close: <path d="m6 6 12 12M18 6 6 18" />,
  sun: (
    <>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
    </>
  ),
  moon: <path d="M20 14.2A8.4 8.4 0 0 1 9.8 4 8.4 8.4 0 1 0 20 14.2z" />,
  copy: (
    <>
      <rect x="9" y="9" width="11" height="11" rx="2" />
      <path d="M5 15V6a1 1 0 0 1 1-1h9" />
    </>
  ),
  check: <path d="m5 12.5 4.5 4.5L19 7" />,
  arrowRight: <path d="M5 12h13m-5-5 5 5-5 5" />,
  arrowLeft: <path d="M19 12H6m5 5-5-5 5-5" />,
  terminal: <><path d="m5 7 4 4-4 4" /><path d="M12 16h7" /><rect x="2" y="3" width="20" height="18" rx="3" /></>,
  plug: (
    <>
      <path d="M9 3v6M15 3v6" />
      <path d="M6 9h12v3a6 6 0 0 1-6 6 6 6 0 0 1-6-6z" />
      <path d="M12 18v3" />
    </>
  ),
  shield: <><path d="M12 3l7 3v6c0 4.4-3 7.9-7 9-4-1.1-7-4.6-7-9V6z" /><path d="m9 12 2 2 4-4" /></>,
  layers: <><path d="m12 3 9 5-9 5-9-5z" /><path d="m3 13 9 5 9-5" /></>,
  gauge: <><path d="M12 14v-4" /><circle cx="12" cy="14" r="1" /><path d="M4 18a9 9 0 1 1 16 0" /></>,
  link: (
    <>
      <path d="M10 13a4 4 0 0 0 5.7.4l3-3A4 4 0 0 0 13 4.7l-1.3 1.3" />
      <path d="M14 11a4 4 0 0 0-5.7-.4l-3 3A4 4 0 0 0 11 19.3L12.3 18" />
    </>
  ),
  lock: <><rect x="4" y="10" width="16" height="10" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3" /></>,
  github: (
    <path
      d="M12 2a10 10 0 0 0-3.2 19.5c.5.1.7-.2.7-.5v-1.7c-2.8.6-3.4-1.3-3.4-1.3-.4-1.2-1.1-1.5-1.1-1.5-.9-.6.1-.6.1-.6 1 .1 1.5 1 1.5 1 .9 1.5 2.3 1.1 2.9.8.1-.7.4-1.1.6-1.4-2.2-.2-4.6-1.1-4.6-5 0-1.1.4-2 1-2.7-.1-.3-.4-1.3.1-2.7 0 0 .8-.3 2.7 1a9.4 9.4 0 0 1 5 0c1.9-1.3 2.7-1 2.7-1 .5 1.4.2 2.4.1 2.7.6.7 1 1.6 1 2.7 0 3.9-2.4 4.8-4.6 5 .4.3.7.9.7 1.9v2.8c0 .3.2.6.7.5A10 10 0 0 0 12 2z"
      fill="currentColor"
      stroke="none"
    />
  ),
  book: <><path d="M4 5a2 2 0 0 1 2-2h13v16H6a2 2 0 0 0-2 2z" /><path d="M8 7h7M8 11h7" /></>,
  bolt: <path d="M13 2 4 14h7l-1 8 9-12h-7z" />,
  cpu: (
    <>
      <rect x="7" y="7" width="10" height="10" rx="2" />
      <path d="M4 10h3M4 14h3M17 10h3M17 14h3M10 4v3M14 4v3M10 17v3M14 17v3" />
    </>
  ),
  branch: <><circle cx="7" cy="6" r="2.5" /><circle cx="7" cy="18" r="2.5" /><circle cx="17" cy="9" r="2.5" /><path d="M7 8.5v7M9.5 6h3A4.5 4.5 0 0 1 17 10.5v.5" /></>,
  chevronDown: <path d="m6 9 6 6 6-6" />,
  corner: <path d="m9 5 7 7-7 7" />,
};

export default function Icon({ name, size = 20, className = '', strokeWidth = 1.75, ...rest }) {
  const path = PATHS[name];
  if (!path) return null;
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...rest}
    >
      {path}
    </svg>
  );
}

export function Wordmark({ size = 26 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" focusable="false" className="brand__mark">
      <rect width="32" height="32" rx="7" fill="var(--bg-well)" stroke="var(--border)" />
      <path d="M9 9h3.4v9.6a4.6 4.6 0 0 1-4.6 4.6H7v-3.1h.8a1.5 1.5 0 0 0 1.5-1.5Z" fill="var(--accent)" />
      <circle cx="20.6" cy="11" r="2.1" fill="var(--accent)" />
      <circle cx="20.6" cy="18" r="2.1" fill="var(--fg)" opacity="0.5" />
      <circle cx="20.6" cy="25" r="2.1" fill="var(--fg)" opacity="0.22" />
    </svg>
  );
}

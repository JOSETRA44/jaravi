import { useCallback, useEffect, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';

const THEME_KEY = 'jaravi-theme';

/** Lee el tema que el script inline de index.html ya aplico al <html>. */
function readTheme() {
  if (typeof document === 'undefined') return 'dark';
  return document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
}

export function useTheme() {
  const [theme, setTheme] = useState('dark');

  useEffect(() => { setTheme(readTheme()); }, []);

  const toggle = useCallback(() => {
    setTheme((current) => {
      const next = current === 'dark' ? 'light' : 'dark';
      document.documentElement.setAttribute('data-theme', next);
      try { localStorage.setItem(THEME_KEY, next); } catch { /* almacenamiento bloqueado */ }
      return next;
    });
  }, []);

  return { theme, toggle };
}

/** Marca la cabecera cuando la pagina deja de estar arriba del todo. */
export function useStuck(threshold = 8) {
  const [stuck, setStuck] = useState(false);
  useEffect(() => {
    const onScroll = () => setStuck(window.scrollY > threshold);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, [threshold]);
  return stuck;
}

/**
 * Revelado al entrar en viewport. Un solo observador para toda la pagina, y
 * una sola pasada por elemento: la animacion es un acento, no un estado.
 */
export function useReveal(deps = []) {
  useEffect(() => {
    const nodes = document.querySelectorAll('[data-reveal=""], [data-reveal="out"]');
    if (!nodes.length) return undefined;

    if (typeof IntersectionObserver === 'undefined' ||
        window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      nodes.forEach((node) => node.setAttribute('data-reveal', 'in'));
      return undefined;
    }

    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) return;
          entry.target.setAttribute('data-reveal', 'in');
          observer.unobserve(entry.target);
        });
      },
      { rootMargin: '0px 0px -12% 0px', threshold: 0.08 },
    );

    nodes.forEach((node) => observer.observe(node));
    return () => observer.disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
}

/** Copiar al portapapeles con confirmacion efimera. */
export function useCopy(timeout = 1800) {
  const [copied, setCopied] = useState(false);
  const timer = useRef(null);

  const copy = useCallback(async (text) => {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      // Sin permiso de portapapeles: seleccion manual como plan B.
      const area = document.createElement('textarea');
      area.value = text;
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      try { document.execCommand('copy'); } catch { /* nada mas que hacer */ }
      document.body.removeChild(area);
    }
    setCopied(true);
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setCopied(false), timeout);
  }, [timeout]);

  useEffect(() => () => clearTimeout(timer.current), []);

  return { copied, copy };
}

/** Cual de los encabezados esta a la vista: alimenta el indice lateral. */
export function useScrollSpy(ids, offset = 96) {
  const [active, setActive] = useState(ids[0] ?? null);

  useEffect(() => {
    if (!ids.length) return undefined;

    const onScroll = () => {
      let current = ids[0];
      for (const id of ids) {
        const node = document.getElementById(id);
        if (node && node.getBoundingClientRect().top <= offset) current = id;
      }
      setActive(current);
    };

    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, [ids, offset]);

  return active;
}

/** Bloquea el scroll del fondo mientras hay una capa modal abierta. */
export function useBodyLock(locked) {
  useEffect(() => {
    if (!locked) return undefined;
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => { document.body.style.overflow = previous; };
  }, [locked]);
}

/** Cierra con Escape. Toda capa modal debe tener salida de teclado. */
export function useEscape(onEscape, active = true) {
  useEffect(() => {
    if (!active) return undefined;
    const onKey = (event) => { if (event.key === 'Escape') onEscape(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onEscape, active]);
}

/** Un valor que solo existe tras el montaje: evita desajustes de hidratacion. */
export function useMounted() {
  const [mounted, setMounted] = useState(false);
  useEffect(() => setMounted(true), []);
  return mounted;
}

/**
 * Ruta actual normalizada.
 *
 * GitHub Pages sirve /docs/cli/ con barra final y el prerender genera
 * /docs/cli; ademas conviene decidir aqui que significa "activo" en vez de
 * dejarlo en manos del emparejador de rutas, que marca /docs como activo
 * tambien en /docs/arquitectura.
 */
export function useCurrentPath() {
  const { pathname } = useLocation();
  return pathname.replace(/\/+$/, '') || '/';
}

export function useIsActive() {
  const here = useCurrentPath();
  return (path) => here === path.replace(/\/+$/, '');
}

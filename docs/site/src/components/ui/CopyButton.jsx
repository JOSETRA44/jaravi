import Icon from './Icon.jsx';
import { useCopy } from '../../lib/hooks.js';

/**
 * Copiar es la accion mas repetida de una pagina de documentacion, asi que
 * confirma explicitamente: el icono cambia y el texto accesible tambien.
 */
export default function CopyButton({ value, label = 'Copiar', className = '' }) {
  const { copied, copy } = useCopy();

  return (
    <button
      type="button"
      className={`copy-btn ${className}`}
      data-copied={copied}
      onClick={() => copy(value)}
      aria-label={copied ? 'Copiado al portapapeles' : `${label}: ${value}`}
    >
      <Icon name={copied ? 'check' : 'copy'} size={15} />
      <span aria-hidden="true">{copied ? 'copiado' : ''}</span>
    </button>
  );
}

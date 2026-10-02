import CopyButton from './CopyButton.jsx';

/**
 * Una terminal falsa, pero honesta: cada linea declara su tono en vez de
 * pintarse con un resaltador generico, de modo que lo verde de verdad
 * significa "salio bien" y no "es una cadena".
 *
 * lines: [{ tone, text }] donde tone ∈ prompt|cmd|dim|ok|warn|bad|key
 */
export default function Terminal({ label = 'bash', lines = [], copy, className = '' }) {
  return (
    <div className={`terminal ${className}`}>
      <div className="terminal__bar">
        <span className="terminal__dots" aria-hidden="true">
          <span className="terminal__dot" />
          <span className="terminal__dot" />
          <span className="terminal__dot" />
        </span>
        <span className="terminal__label">{label}</span>
        {copy ? <CopyButton value={copy} className="terminal__copy" /> : null}
      </div>
      <pre className="terminal__body" tabIndex={0}>
        <code>
          {lines.map((line, index) => (
            <span key={index} className={`terminal__line t-${line.tone ?? 'dim'}`}>
              {line.tone === 'prompt' ? <span className="t-prompt">$ </span> : null}
              {line.text}
              {'\n'}
            </span>
          ))}
        </code>
      </pre>
    </div>
  );
}

/** Una sola linea de comando, copiable. El caso mas frecuente del sitio. */
export function CommandLine({ command, sigil = '$' }) {
  return (
    <div className="command">
      <span className="command__sigil" aria-hidden="true">{sigil}</span>
      <code className="command__text">{command}</code>
      <CopyButton value={command} />
    </div>
  );
}

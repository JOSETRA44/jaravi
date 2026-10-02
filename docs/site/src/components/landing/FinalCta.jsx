import { Link } from 'react-router-dom';
import Icon from '../ui/Icon.jsx';
import { CommandLine } from '../ui/Terminal.jsx';
import { installCommand, site } from '../../content/site.js';

export default function FinalCta() {
  return (
    <section className="section band" id="empezar">
      <div className="wrap">
        <div className="cta" data-reveal="">
          <p className="eyebrow">Empezar</p>
          <h2 className="cta__title">Un comando, y ya tienes sub-agentes.</h2>
          <p className="cta__lede">
            Instalar deja el comando <code>jaravi-mcp</code>. Autoconfigurar registra Jaravi
            en todos los CLIs de IA de la máquina y escribe en los ficheros de instrucciones
            que esos agentes leen al arrancar. Diagnosticar comprueba que quedó usable.
          </p>

          <div className="cta__commands">
            <CommandLine command={installCommand} />
            <CommandLine command="jaravi-mcp install" />
            <CommandLine command="jaravi-mcp doctor" />
          </div>

          <div className="cta__actions">
            <Link className="btn btn--primary" to="/docs/instalacion">
              Guía de instalación
              <Icon name="arrowRight" size={17} />
            </Link>
            <a className="btn btn--ghost" href={site.repo} target="_blank" rel="noopener noreferrer">
              <Icon name="github" size={17} />
              Ver el código
            </a>
          </div>

          <p className="cta__note">
            Requiere {site.runtime} o superior. Licencia {site.license}. Windows, Linux y macOS
            comparten el mismo binario; los perfiles de sub-agente se resuelven con
            placeholders de ruta, no con rutas absolutas.
          </p>
        </div>
      </div>
    </section>
  );
}

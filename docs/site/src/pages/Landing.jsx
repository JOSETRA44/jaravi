import Hero from '../components/landing/Hero.jsx';
import ContextCollapse from '../components/landing/ContextCollapse.jsx';
import TwoDoors from '../components/landing/TwoDoors.jsx';
import Flows from '../components/landing/Flows.jsx';
import AgentMatrix from '../components/landing/AgentMatrix.jsx';
import Guarantees from '../components/landing/Guarantees.jsx';
import ControlCenter from '../components/landing/ControlCenter.jsx';
import Adoption from '../components/landing/Adoption.jsx';
import FinalCta from '../components/landing/FinalCta.jsx';
import { useReveal } from '../lib/hooks.js';

export default function Landing() {
  useReveal();

  return (
    <>
      <Hero />
      <ContextCollapse />
      <TwoDoors />
      <Flows />
      <AgentMatrix />
      <Guarantees />
      <ControlCenter />
      <Adoption />
      <FinalCta />
    </>
  );
}

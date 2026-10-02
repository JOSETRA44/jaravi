// Datos del proyecto que aparecen en varios sitios a la vez. Cambiar la
// version aqui la cambia en la cabecera, el hero, el pie y los metadatos.

export const site = {
  name: 'Jaravi',
  version: '0.11.0',
  tagline: 'Orquesta otras CLIs de IA como sub-agentes',
  repo: 'https://github.com/JOSETRA44/jaravi',
  issues: 'https://github.com/JOSETRA44/jaravi/issues',
  nuget: 'https://www.nuget.org/packages/Jaravi.McpServer',
  license: 'MIT',
  runtime: '.NET 8',
  paper: 'jaravi.pdf',
};

export const installCommand = 'dotnet tool install -g Jaravi.McpServer';

/** Los perfiles que vienen de fabrica, con su estado real de verificacion. */
export const agents = [
  { id: 'claude', name: 'Claude Code', invocation: 'claude -p {task} --dangerously-skip-permissions', status: 'ok', note: 'Verificado end-to-end' },
  { id: 'codex', name: 'OpenAI Codex', invocation: 'node codex.js exec {task} --full-auto', status: 'ok', note: 'Verificado end-to-end' },
  { id: 'opencode', name: 'OpenCode', invocation: 'opencode.exe run {task}', status: 'ok', note: 'Auditoría real completada' },
  { id: 'gemini', name: 'Gemini CLI', invocation: 'node gemini.js -p {task} --approval-mode yolo', status: 'ok', note: 'Verificado end-to-end' },
  { id: 'qwen', name: 'Qwen Code', invocation: 'node cli-entry.js -p {task} --approval-mode yolo', status: 'warn', note: 'Perfil correcto; bloqueo externo de modelo' },
  { id: 'copilot', name: 'GitHub Copilot CLI', invocation: 'node npm-loader.js -p {task} --allow-all-tools', status: 'info', note: 'Misma familia de invocación' },
  { id: 'deepcode', name: 'Deep Code CLI', invocation: 'node cli.js -p {task}', status: 'info', note: 'Misma familia de invocación' },
  { id: 'mimo', name: 'Mimo (mimocode)', invocation: 'node bin/mimo run {task}', status: 'info', note: 'Fork de OpenCode' },
  { id: 'antigravity', name: 'Antigravity (agy)', invocation: 'agy.exe --print {task}', status: 'bad', note: 'Requiere autenticación en esta máquina' },
  { id: 'echo-demo', name: 'Demo: echo', invocation: 'cmd.exe (demo interno)', status: 'ok', note: 'Sin dependencias, para probar el motor' },
  { id: 'flood-demo', name: 'Demo: flood', invocation: '50 000 líneas de estrés', status: 'ok', note: 'Prueba del ring buffer' },
];

export const statusLabels = {
  ok: { label: 'Verificado', tone: 'ok' },
  warn: { label: 'Bloqueo externo', tone: 'warn' },
  info: { label: 'Shape conocido', tone: 'info' },
  bad: { label: 'Sin configurar', tone: 'bad' },
};

/** Las once tools publicadas por MCP, con sus anotaciones reales. */
export const tools = [
  { name: 'list_agents', kind: 'read', summary: 'Lista los perfiles de sub-agentes disponibles.' },
  { name: 'reload_agents', kind: 'write', summary: 'Re-lee agents.json en vivo, sin reiniciar el servidor.' },
  { name: 'spawn_agent', kind: 'write', summary: 'Lanza un sub-agente y devuelve el sessionId al instante.' },
  { name: 'run_agent', kind: 'write', summary: 'Spawn + await + summary en una sola llamada.' },
  { name: 'send_input', kind: 'write', summary: 'Escribe en el stdin del sub-agente.' },
  { name: 'get_status', kind: 'read', summary: 'Estado compacto: fase, uptime, exit code, últimas líneas.' },
  { name: 'list_sessions', kind: 'read', summary: 'Todas las sesiones y su estado, incluida la cola.' },
  { name: 'read_output', kind: 'read', summary: 'Output acotado a 500 líneas, con tail, grep y sinceSeq.' },
  { name: 'await_session', kind: 'read', summary: 'Bloquea hasta estado terminal, reportando progreso.' },
  { name: 'get_summary', kind: 'read', summary: 'Digest: exit code, duración y errores extraídos.' },
  { name: 'kill_agent', kind: 'destructive', summary: 'Mata el árbol de procesos completo de una sesión.' },
];

export const exitCodes = [
  { code: '0', meaning: 'Terminó y el sub-agente salió con 0', tone: 'ok' },
  { code: '1', meaning: 'Error de Jaravi (perfil, Scope Gate, config)', tone: 'bad' },
  { code: '2', meaning: 'Uso incorrecto', tone: 'bad' },
  { code: '3', meaning: 'El sub-agente terminó con código distinto de 0', tone: 'warn' },
  { code: '4', meaning: 'Sigue corriendo — no es un fallo; recógelo con await', tone: 'info' },
];

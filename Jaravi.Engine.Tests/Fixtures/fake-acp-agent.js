// Minimal ACP agent for engine tests: speaks the same newline-delimited JSON-RPC as
// Gemini CLI / Antigravity in ACP mode, with deterministic behavior.
//   - every prompt is answered with "echo: <first line of the prompt>"
//   - FAKE_ACP_DELAY_MS delays the end of each turn (to exercise queueing)
//   - a prompt containing "ASK-PERMISSION" makes the agent request permission first
//   - injected MCP server names are reported in the session log as "mcp: <names>"
const readline = require('node:readline');
const delay = Number(process.env.FAKE_ACP_DELAY_MS || '50');
let nextId = 1000;
const waiting = new Map();
const send = (m) => process.stdout.write(JSON.stringify(m) + '\n');
const update = (sessionId, u) => send({ jsonrpc: '2.0', method: 'session/update', params: { sessionId, update: u } });
const say = (sessionId, text) => update(sessionId, { sessionUpdate: 'agent_message_chunk', content: { type: 'text', text } });

readline.createInterface({ input: process.stdin }).on('line', async (line) => {
  let m; try { m = JSON.parse(line); } catch { return; }
  if (m.id !== undefined && !m.method) { const w = waiting.get(m.id); waiting.delete(m.id); w?.(m.result); return; }
  switch (m.method) {
    case 'initialize':
      send({ jsonrpc: '2.0', id: m.id, result: { protocolVersion: 1, agentInfo: { name: 'fake-acp', version: '1.0.0' }, agentCapabilities: {} } });
      break;
    case 'session/new': {
      const names = (m.params.mcpServers || []).map((s) => s.name).join(',') || 'none';
      send({ jsonrpc: '2.0', id: m.id, result: { sessionId: 'fake-session-1' } });
      process.stderr.write(`mcp: ${names}\n`);
      break;
    }
    case 'session/prompt': {
      const text = m.params.prompt.map((p) => p.text).join('');
      if (text.includes('ASK-PERMISSION')) {
        const id = nextId++;
        const answer = new Promise((r) => waiting.set(id, r));
        send({ jsonrpc: '2.0', id, method: 'session/request_permission', params: {
          sessionId: m.params.sessionId, toolCall: { toolCallId: 't1', title: 'write file' },
          options: [{ optionId: 'no', kind: 'reject_once', name: 'No' }, { optionId: 'yes', kind: 'allow_always', name: 'Yes' }] } });
        const res = await answer;
        say(m.params.sessionId, `permission: ${res?.outcome?.optionId ?? res?.outcome?.outcome}\n`);
      }
      update(m.params.sessionId, { sessionUpdate: 'tool_call', toolCallId: 't2', title: 'read_file', kind: 'read', status: 'pending' });
      say(m.params.sessionId, 'echo: ');
      say(m.params.sessionId, text.split('\n').filter((l) => l.trim()).join(' | ').slice(0, 2000) + '\n');
      setTimeout(() => send({ jsonrpc: '2.0', id: m.id, result: { stopReason: 'end_turn' } }), delay);
      break;
    }
    default:
      if (m.id !== undefined) send({ jsonrpc: '2.0', id: m.id, error: { code: -32601, message: 'unknown method' } });
  }
});

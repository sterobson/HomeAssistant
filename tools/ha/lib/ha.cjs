// Shared connection helpers for the scripts in tools/ha. See ../README.md.
//
// The token is read from the same git-ignored files the NetDaemon app uses and is never printed.
// The WebSocket client is the `ws` package already installed under Frontend/node_modules, so run
// `npm install` in Frontend once if it is missing.
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '..', '..', '..');
const HOST = process.env.HA_HOST || 'homeassistant.local:8123';

// In order of preference, as listed in HOME-ASSISTANT.md → Connection. These files are JSONC
// (they contain // comments), so the token is matched rather than parsed.
const TOKEN_FILES = [
  'Backend/App/appsettings.secrets.development.json',
  'Backend/App/appsettings.secrets.all.json',
  'Backend/App/appsettings.development.json',
];
const PLACEHOLDER = /insert_token_later|<secret>/i;

function loadToken() {
  for (const rel of TOKEN_FILES) {
    const file = path.join(REPO, rel);
    if (!fs.existsSync(file)) continue;
    const text = fs.readFileSync(file, 'utf8');
    const match = text.match(/"Token"\s*:\s*"([^"]+)"/);
    if (match && !PLACEHOLDER.test(match[1])) return match[1];
  }
  throw new Error(`No real HomeAssistant.Token found in: ${TOKEN_FILES.join(', ')}`);
}

const token = loadToken();

// GET/POST against the REST API. Returns parsed JSON; throws on a non-2xx status.
// Remember gotcha 5: a 200 from a service call does not mean the device did anything.
async function rest(method, apiPath, body) {
  const text = await restText(method, apiPath, body);
  return text ? JSON.parse(text) : null;
}

// The same, returning the body as text, for the endpoints that answer in plain text (add-on logs).
async function restText(method, apiPath, body, extraHeaders = {}) {
  const res = await fetch(`http://${HOST}/api${apiPath}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...extraHeaders },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${apiPath} -> ${res.status} ${text}`);
  return text;
}

// WebSocket API, needed for the device/entity/area registries, which REST does not expose.
// Returns { call(msg) -> result, onEvent(fn), close() }.
function connect() {
  let WebSocket;
  try {
    WebSocket = require(path.join(REPO, 'Frontend', 'node_modules', 'ws'));
  } catch {
    throw new Error('The ws package is missing: run `npm install` in Frontend first.');
  }
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://${HOST}/api/websocket`);
    let nextId = 1;
    const pending = new Map();
    const listeners = [];
    ws.on('error', reject);
    ws.on('message', (raw) => {
      const msg = JSON.parse(raw);
      if (msg.type === 'auth_required') ws.send(JSON.stringify({ type: 'auth', access_token: token }));
      else if (msg.type === 'auth_invalid') reject(new Error('Home Assistant rejected the token'));
      else if (msg.type === 'auth_ok') resolve({
        call: (m) => new Promise((res, rej) => {
          const id = nextId++;
          pending.set(id, { res, rej });
          ws.send(JSON.stringify({ ...m, id }));
        }),
        onEvent: (fn) => listeners.push(fn),
        close: () => ws.close(),
      });
      else if (msg.type === 'result' && pending.has(msg.id)) {
        const p = pending.get(msg.id);
        pending.delete(msg.id);
        if (msg.success) p.res(msg.result); else p.rej(new Error(JSON.stringify(msg.error)));
      } else if (msg.type === 'event') listeners.forEach((fn) => fn(msg.event));
    });
  });
}

// Device, entity and area registries plus live states, joined up in one call.
async function snapshot(ha) {
  const [devices, entities, areas, states] = await Promise.all([
    ha.call({ type: 'config/device_registry/list' }),
    ha.call({ type: 'config/entity_registry/list' }),
    ha.call({ type: 'config/area_registry/list' }),
    ha.call({ type: 'get_states' }),
  ]);
  return {
    devices,
    entities,
    areas,
    areaName: Object.fromEntries(areas.map((a) => [a.area_id, a.name])),
    stateOf: Object.fromEntries(states.map((s) => [s.entity_id, s])),
  };
}

// Tiny argv parser: --flag value pairs, everything else positional. Flags named in `booleans` never
// take a value (so `--check file.json` keeps file.json positional); any other flag with nothing after
// it is also true.
function parseArgs(booleans = [], argv = process.argv.slice(2)) {
  const out = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const key = a.slice(2);
      const next = argv[i + 1];
      if (booleans.includes(key) || next === undefined || next.startsWith('--')) out[key] = true;
      else { out[key] = next; i++; }
    } else out._.push(a);
  }
  return out;
}

function run(main) {
  main().catch((e) => { console.error(e.message || e); process.exit(1); });
}

module.exports = { HOST, rest, restText, connect, snapshot, parseArgs, run };

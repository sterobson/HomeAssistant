'use strict';
// A read-only guard between a LOCAL copy of the NetDaemon app and the REAL Home Assistant.
//
//   local app  --ws/http-->  this proxy (8124)  --ws/http-->  homeassistant.local:8123
//
// Reads pass straight through, so the local app sees real devices and real state. Anything that
// could change a device is blocked, logged as "WOULD CALL ...", and answered with a fake success so
// the app's logic carries on as though it worked. The production app on the HA host keeps full
// control; this one only shows what it WOULD have done.
//
// It FAILS CLOSED: only message types known to be read-only are forwarded. An unfamiliar type is
// blocked and logged, never forwarded, so a surprise cannot reach hardware.
//
// To let specific entities be driven for real (deliberate, one at a time), start it with
//   GUARD_ALLOW_ENTITIES=switch.some_plug,light.some_light
// A service call is forwarded only if EVERY entity it targets is on that list. Default: none.
//
// The HA token passes through in the auth frame / Authorization header and is never logged.

const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const WebSocket = require('../Frontend/node_modules/ws');

const UPSTREAM_HOST = process.env.GUARD_UPSTREAM_HOST || 'homeassistant.local';
const UPSTREAM_PORT = Number(process.env.GUARD_UPSTREAM_PORT || 8123);
const LISTEN_PORT = Number(process.env.GUARD_LISTEN_PORT || 8124);
const LOG_PATH = process.env.GUARD_LOG || path.join(__dirname, '.work', 'guard.log');
const ALLOWED_ENTITIES = new Set(
  (process.env.GUARD_ALLOW_ENTITIES || '').split(',').map((s) => s.trim()).filter(Boolean),
);

// Websocket message types that only READ. Everything else is blocked.
const READ_TYPES = new Set([
  'auth', 'ping', 'pong',
  'subscribe_events', 'unsubscribe_events', 'subscribe_entities', 'subscribe_trigger',
  'get_states', 'get_config', 'get_services', 'get_panels', 'get_notifications',
  'render_template', 'subscribe_bootstrap_integrations', 'supported_features',
  'manifest/list', 'manifest/get', 'integration/descriptions', 'search/related',
]);
const READ_PATTERNS = [
  /^config\/[a-z_]+\/(list|list_for_display|get)$/,
  /^history\//,
  /^recorder\/(statistics_during_period|list_statistic_ids|get_statistics_metadata|info)$/,
  /^logbook\//,
  /^system_log\/list$/,
  /^config_entries\/(get|subscribe)$/,
  /^device_automation\/[a-z_]+\/list$/,
];
const isReadType = (type) => READ_TYPES.has(type) || READ_PATTERNS.some((re) => re.test(type));

// REST: GET/HEAD pass. The only POST allowed is template rendering, which reads.
const REST_POST_ALLOW = new Set(['/api/template']);

const stats = { blocked: 0, forwardedCalls: 0, blockedByTarget: {}, typesForwarded: {}, typesBlocked: {}, restBlocked: 0, connections: 0 };

fs.mkdirSync(path.dirname(LOG_PATH), { recursive: true });
const log = (record) => {
  const line = JSON.stringify({ t: new Date().toISOString(), ...record });
  fs.appendFileSync(LOG_PATH, line + '\n');
};
const say = (text) => console.log(`${new Date().toISOString().slice(11, 19)} ${text}`);

const targetsOf = (msg) => {
  const ids = [];
  const collect = (v) => {
    if (!v) return;
    if (Array.isArray(v)) v.forEach(collect);
    else if (typeof v === 'string') ids.push(...v.split(',').map((s) => s.trim()).filter(Boolean));
  };
  collect(msg.target?.entity_id);
  collect(msg.service_data?.entity_id);
  return ids;
};

const summarise = (data) => {
  if (!data) return '';
  const copy = { ...data };
  delete copy.entity_id;
  const text = JSON.stringify(copy);
  return text.length > 2 ? (text.length > 160 ? text.slice(0, 160) + '…' : text) : '';
};

function guardClientMessage(raw, toUpstream, toClient) {
  let msg;
  try { msg = JSON.parse(raw); } catch { toUpstream(raw); return; }

  const type = msg.type;

  if (type === 'call_service') {
    const targets = targetsOf(msg);
    const allowed = targets.length > 0 && targets.every((id) => ALLOWED_ENTITIES.has(id));
    const label = `${msg.domain}.${msg.service}`;
    if (allowed) {
      stats.forwardedCalls++;
      log({ action: 'FORWARDED_CALL', service: label, targets, data: summarise(msg.service_data) });
      say(`FORWARDED  ${label} -> ${targets.join(', ')}`);
      toUpstream(raw);
      return;
    }
    stats.blocked++;
    const key = `${label} -> ${targets.join(',') || '(no entity)'}`;
    stats.blockedByTarget[key] = (stats.blockedByTarget[key] || 0) + 1;
    log({ action: 'BLOCKED_CALL', service: label, targets, data: summarise(msg.service_data) });
    say(`WOULD CALL ${label} -> ${targets.join(', ') || '(no entity)'} ${summarise(msg.service_data)}`);
    toClient(JSON.stringify({
      id: msg.id, type: 'result', success: true,
      result: { context: { id: 'GUARD-BLOCKED', parent_id: null, user_id: null }, response: null },
    }));
    return;
  }

  if (isReadType(type)) {
    stats.typesForwarded[type] = (stats.typesForwarded[type] || 0) + 1;
    toUpstream(raw);
    return;
  }

  // Anything else: unknown or a write. Never forwarded.
  stats.blocked++;
  stats.typesBlocked[type] = (stats.typesBlocked[type] || 0) + 1;
  log({ action: 'BLOCKED_TYPE', type, id: msg.id });
  say(`BLOCKED    websocket type "${type}" (not on the read-only list)`);
  if (msg.id !== undefined) {
    toClient(JSON.stringify({ id: msg.id, type: 'result', success: true, result: null }));
  }
}

const server = http.createServer((req, res) => {
  if (req.url === '/__guard/status') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ allowedEntities: [...ALLOWED_ENTITIES], ...stats }, null, 2));
    return;
  }

  const pathOnly = req.url.split('?')[0];
  const method = req.method.toUpperCase();
  const readOnly = method === 'GET' || method === 'HEAD' || (method === 'POST' && REST_POST_ALLOW.has(pathOnly));
  if (!readOnly) {
    stats.restBlocked++;
    log({ action: 'BLOCKED_REST', method, path: pathOnly });
    say(`BLOCKED    REST ${method} ${pathOnly}`);
    res.writeHead(403, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ message: 'Blocked by local ha-guard-proxy (read-only)' }));
    return;
  }

  const upstream = http.request(
    { host: UPSTREAM_HOST, port: UPSTREAM_PORT, path: req.url, method, headers: { ...req.headers, host: `${UPSTREAM_HOST}:${UPSTREAM_PORT}` } },
    (up) => { res.writeHead(up.statusCode, up.headers); up.pipe(res); },
  );
  upstream.on('error', (e) => { res.writeHead(502); res.end(String(e.message)); });
  req.pipe(upstream);
});

const wss = new WebSocket.Server({ noServer: true });
server.on('upgrade', (req, socket, head) => {
  if (!req.url.startsWith('/api/websocket')) { socket.destroy(); return; }
  wss.handleUpgrade(req, socket, head, (client) => {
    stats.connections++;
    const upstream = new WebSocket(`ws://${UPSTREAM_HOST}:${UPSTREAM_PORT}/api/websocket`);
    const pending = [];
    const toUpstream = (raw) => (upstream.readyState === WebSocket.OPEN ? upstream.send(raw) : pending.push(raw));
    const toClient = (raw) => { if (client.readyState === WebSocket.OPEN) client.send(raw); };

    upstream.on('open', () => { pending.splice(0).forEach((m) => upstream.send(m)); });
    upstream.on('message', (data) => toClient(data.toString()));
    upstream.on('close', () => client.close());
    upstream.on('error', (e) => { say(`upstream error: ${e.message}`); client.close(); });

    client.on('message', (data) => guardClientMessage(data.toString(), toUpstream, toClient));
    client.on('close', () => upstream.close());
    client.on('error', () => upstream.close());
  });
});

server.listen(LISTEN_PORT, '127.0.0.1', () => {
  say(`ha-guard-proxy listening on 127.0.0.1:${LISTEN_PORT} -> ${UPSTREAM_HOST}:${UPSTREAM_PORT}`);
  say(ALLOWED_ENTITIES.size
    ? `LIVE for ${ALLOWED_ENTITIES.size} entity(ies): ${[...ALLOWED_ENTITIES].join(', ')}`
    : 'READ-ONLY: every device command is blocked and logged');
  say(`log: ${LOG_PATH}`);
});

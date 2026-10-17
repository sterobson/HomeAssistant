'use strict';
// Proves the guard blocks writes. Uses only system_log.write at warning level: a harmless line in
// HA's system log, which is also something we can look for afterwards.
//
//   1. Through the guard:  a call_service marker, and a REST POST marker. Neither may reach HA.
//   2. CONTROL, direct to HA: one marker that MUST appear. Without it, "absent" proves nothing,
//      because this check could simply be blind.
const fs = require('node:fs');
const path = require('node:path');
const WebSocket = require('../Frontend/node_modules/ws');

const secrets = path.join(__dirname, '..', 'Backend', 'App', 'appsettings.secrets.development.json');
const raw = fs.readFileSync(secrets, 'utf8').replace(/^﻿/, '').split('\n').filter((l) => !/^\s*\/\//.test(l)).join('\n');
const token = JSON.parse(raw).HomeAssistant.Token;

const PROXY = process.env.GUARD_LISTEN_PORT || 8124;
const HA = 'homeassistant.local:8123';
const stamp = Date.now();
const M_WS = `GUARD-SELFTEST-WS-${stamp}`;
const M_REST = `GUARD-SELFTEST-REST-${stamp}`;
const M_CONTROL = `GUARD-SELFTEST-CONTROL-${stamp}`;

const session = (host) => new Promise((resolve, reject) => {
  const ws = new WebSocket(`ws://${host}/api/websocket`);
  let nextId = 1;
  const waiters = new Map();
  const api = {
    call: (payload) => new Promise((res) => { const id = nextId++; waiters.set(id, res); ws.send(JSON.stringify({ id, ...payload })); }),
    close: () => ws.close(),
  };
  ws.on('message', (d) => {
    const m = JSON.parse(d.toString());
    if (m.type === 'auth_required') ws.send(JSON.stringify({ type: 'auth', access_token: token }));
    else if (m.type === 'auth_ok') resolve(api);
    else if (m.type === 'auth_invalid') reject(new Error('auth_invalid'));
    else if (m.id && waiters.has(m.id)) { waiters.get(m.id)(m); waiters.delete(m.id); }
  });
  ws.on('error', reject);
});

(async () => {
  const results = [];
  const check = (name, ok, detail = '') => { results.push(ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`); };

  const viaGuard = await session(`127.0.0.1:${PROXY}`);

  const states = await viaGuard.call({ type: 'get_states' });
  check('reads pass through the guard (get_states)', states.success && states.result.length > 100, `${states.result?.length} entities`);

  const w = await viaGuard.call({ type: 'call_service', domain: 'system_log', service: 'write', service_data: { message: M_WS, level: 'warning' } });
  check('guard answers a blocked call_service with a fake success', w.success === true && w.result?.context?.id === 'GUARD-BLOCKED');

  const restPost = await fetch(`http://127.0.0.1:${PROXY}/api/services/system_log/write`, {
    method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: JSON.stringify({ message: M_REST, level: 'warning' }),
  });
  check('guard refuses a REST POST', restPost.status === 403, `HTTP ${restPost.status}`);

  const restGet = await fetch(`http://127.0.0.1:${PROXY}/api/config`, { headers: { Authorization: `Bearer ${token}` } });
  check('guard forwards a REST GET', restGet.ok, `HTTP ${restGet.status}`);

  viaGuard.close();

  // CONTROL: straight to HA, no guard.
  const direct = await session(HA);
  await direct.call({ type: 'call_service', domain: 'system_log', service: 'write', service_data: { message: M_CONTROL, level: 'warning' } });
  await new Promise((r) => setTimeout(r, 1500));
  const log = await direct.call({ type: 'system_log/list' });
  const text = JSON.stringify(log.result ?? []);
  direct.close();

  check('CONTROL: a marker sent directly DOES appear in the HA system log', text.includes(M_CONTROL), 'proves the check can see markers');
  check('the call_service marker sent through the guard did NOT reach HA', !text.includes(M_WS));
  check('the REST POST marker sent through the guard did NOT reach HA', !text.includes(M_REST));

  const status = await (await fetch(`http://127.0.0.1:${PROXY}/__guard/status`)).json();
  check('guard counted the blocked traffic', status.blocked >= 1 && status.restBlocked >= 1, `blocked=${status.blocked} restBlocked=${status.restBlocked}`);

  console.log(results.every(Boolean) ? '\nALL PASS: the guard blocks writes.' : '\nFAILED: DO NOT start the local app.');
  process.exit(results.every(Boolean) ? 0 : 1);
})().catch((e) => { console.error('selftest error:', e.message); process.exit(2); });

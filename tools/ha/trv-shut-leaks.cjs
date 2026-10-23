// Usage: node tools/ha/trv-shut-leaks.cjs   (DAYS=10 by default; the recorder keeps about 10)
// Read-only: for each TRV, finds spells where its valve was idle (shut) and its own sensor still climbed 2 °C or more,
// i.e. hot water getting past a shut valve. Rises within 45 minutes of the valve last heating are ignored
// (a radiator coasts up a little after closing).
const { rest } = require('./lib/ha.cjs');
const DAYS = Number(process.env.DAYS || 10);
const RISE = 2;
const COAST_MIN = 45;
(async () => {
  const states = await rest('GET', '/states');
  const trvs = states.filter((s) => s.entity_id.startsWith('climate.') && /radiator/.test(s.entity_id)).map((s) => s.entity_id);
  const start = new Date(Date.now() - DAYS * 86400e3).toISOString();
  const end = new Date().toISOString();
  const data = await rest('GET', `/history/period/${start}?end_time=${end}&filter_entity_id=${trvs.join(',')}`);
  const fmt = (t) => new Date(t).toLocaleString('en-GB', { timeZone: 'Europe/London', dateStyle: 'short', timeStyle: 'short' });
  for (const series of data) {
    const id = series[0].entity_id;
    let lastHeating = null;
    let spell = null; // { from, low, lowAt, high, highAt }
    const spells = [];
    const close = () => { if (spell && spell.high - spell.low >= RISE) spells.push(spell); spell = null; };
    for (const s of series) {
      const a = s.attributes || {};
      const t = Date.parse(s.last_updated);
      const cur = Number(a.current_temperature);
      if (a.hvac_action === 'heating') { lastHeating = t; close(); continue; }
      if (!Number.isFinite(cur) || a.hvac_action !== 'idle') continue;
      if (lastHeating && t - lastHeating < COAST_MIN * 60e3) continue;
      if (!spell) spell = { low: cur, lowAt: t, high: cur, highAt: t };
      if (cur < spell.low && spell.high - spell.low < RISE) { spell.low = cur; spell.lowAt = t; spell.high = cur; spell.highAt = t; }
      if (cur > spell.high) { spell.high = cur; spell.highAt = t; }
      if (spell.high - cur >= 1.5) close(); // it has turned and fallen: end of this rise
    }
    close();
    console.log(`${id}: ${spells.length} rise(s) of ${RISE}+ °C while shut`);
    for (const sp of spells.slice(-6)) console.log(`   ${fmt(sp.lowAt)} ${sp.low} -> ${fmt(sp.highAt)} ${sp.high}`);
  }
})().catch((e) => { console.error(e.message); process.exit(1); });

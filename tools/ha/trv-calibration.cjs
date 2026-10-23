// Read-only: every Sonoff TRVZB's valve degree settings and calibration limit voltages, read from ZHA (slow: battery TRVs
// answer when they wake). A TRV that shuts properly reads closing > opening; den radiator 2 read the other way round
// while it let water through a shut valve (HOME-ASSISTANT.md, Radiator TRVs). Usage: node tools/ha/trv-calibration.cjs
const { connect } = require('./lib/ha.cjs');
(async () => {
  const ha = await connect();
  try {
    const devices = (await ha.call({ type: 'zha/devices' })).filter((d) => /TRVZB/.test(d.model || ''));
    for (const d of devices) {
      const values = {};
      const clusters = await ha.call({ type: 'zha/devices/clusters', ieee: d.ieee });
      const c = clusters.find((c) => c.type === 'in' && c.id === 0xfc11);
      if (c) {
        const attrs = await ha.call({ type: 'zha/devices/clusters/attributes', ieee: d.ieee, endpoint_id: c.endpoint_id, cluster_id: c.id, cluster_type: 'in' });
        for (const a of attrs.filter((a) => /limit_voltage|closing_degree|opening_degree/i.test(a.name))) {
          for (let attempt = 0; attempt < 3 && values[a.name] === undefined; attempt++) {
            try {
              values[a.name] = await ha.call({ type: 'zha/devices/clusters/attributes/value', ieee: d.ieee, endpoint_id: c.endpoint_id, cluster_id: c.id, cluster_type: 'in', attribute: a.id });
            } catch { /* sleepy device: retry */ }
          }
        }
      }
      const close = Number(values.valve_closing_limit_voltage);
      const open = Number(values.valve_opening_limit_voltage);
      const verdict = Number.isFinite(close) && Number.isFinite(open) ? (close > open ? 'closing > opening' : 'REVERSED (closing <= opening)') : 'incomplete';
      console.log(`${d.user_given_name || d.name}: closing ${values.valve_closing_limit_voltage} / opening ${values.valve_opening_limit_voltage} mV, degrees close ${values.valve_closing_degree} open ${values.valve_opening_degree}  -> ${verdict}`);
    }
  } finally {
    ha.close();
  }
})().catch((e) => { console.error(e.message || JSON.stringify(e)); process.exit(1); });

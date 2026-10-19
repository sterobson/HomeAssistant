// Find and install HACS repositories over the websocket (the HACS panel's own commands).
//
//   node tools/ha/hacs.cjs <regex>                  repositories whose name matches: id, category,
//                                                   installed version, latest version
//   node tools/ha/hacs.cjs --download <owner/repo>  download (install or update) its latest release
//
// Downloading only copies the files into /config/custom_components. Home Assistant has to be
// restarted before an integration can be added, and adding it is a separate config flow.
const { connect, parseArgs, run } = require('./lib/ha.cjs');

run(async () => {
  const args = parseArgs();
  const ha = await connect();
  const repos = await ha.call({ type: 'hacs/repositories/list' });

  if (args.download) {
    const name = String(args.download).toLowerCase();
    const repo = repos.find((r) => r.full_name.toLowerCase() === name);
    if (!repo) throw new Error(`${args.download} is not known to HACS`);
    const version = repo.available_version || undefined;
    console.log(`downloading ${repo.full_name} ${version || '(default branch)'}...`);
    await ha.call({ type: 'hacs/repository/download', repository: repo.id, version });
    const after = (await ha.call({ type: 'hacs/repositories/list' })).find((r) => r.id === repo.id);
    console.log(`${after.full_name}: installed ${after.installed_version || '-'}, pending restart: ${after.pending_restart ?? 'unknown'}`);
  } else {
    const pattern = new RegExp(args._[0] || '.', 'i');
    const hits = repos.filter((r) => pattern.test(r.full_name) || pattern.test(r.name || ''));
    for (const r of hits) {
      console.log(`${r.full_name} | id=${r.id} | ${r.category} | installed=${r.installed ? r.installed_version : 'no'} | latest=${r.available_version || '-'}`);
    }
    console.log(`${hits.length} match(es)`);
  }
  ha.close();
});

// Repro: Gearbox (BL3 synthetic test save, dumped from plugin tests) — time edit + GET changes on native Api vs in-browser wasm runtime.
// usage: node gearbox-changes-perf.cjs <nativePort> <previewOrigin>
const path = require("path"), fs = require("fs");
const { native, wasm, multipart } = require(path.join(__dirname, "transport.cjs"));
const dir = path.join(__dirname, "gearbox-fixture");
const files = ["PPSA01463/sdimg_TEST/1", "PPSA01463/sdimg_TEST/profile"].map(n => ({ name: n, bytes: fs.readFileSync(path.join(dir, n)) }));
async function go(t) {
  const J = { "content-type": "application/json" };
  const mp = await multipart(files);
  const ws = JSON.parse((await t.send("POST", "/api/workspaces", { "content-type": mp.contentType }, mp.body)).body);
  const W = `/api/workspaces/${ws.id}`;
  const fid = ws.titles[0].directories[0].files.find(f => f.path === "1").id;
  const doc = JSON.parse((await t.send("POST", `${W}/files/${fid}/open`, J, Buffer.from(JSON.stringify({ pluginId: "gearbox" })))).body);
  const D = `${W}/documents/${doc.id}`;
  const time = async (label, m, u, b) => { const t0 = Date.now(); const r = await t.send(m, u, J, b ? Buffer.from(JSON.stringify(b)) : null); console.log(`${t.name} ${label}: ${r.status} ${Date.now() - t0}ms ${r.body.length}B`); return r; };
  await time("edit character/xp", "POST", `${D}/edits`, { edits: [{ valueId: "character/xp", value: "1" }], revision: doc.state.revision });
  await time("GET changes", "GET", `${D}/changes`);
  await t.send("DELETE", W);
}
(async () => {
  await go(native(`http://127.0.0.1:${process.argv[2] ?? 5231}`));
  const w = await wasm(process.argv[3] ?? "http://127.0.0.1:4221");
  await go(w); await w.close();
})();

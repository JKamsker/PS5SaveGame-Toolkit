// Repro: in-browser .NET runtime crashes ("RuntimeError: memory access out of bounds") on Witcher3
// catalog/inspect after edits + planned actions; native Api answers 200. After the crash the worker never answers again.
// usage: node w3-oob-crash.cjs <nativePort> [steps: e=both edits l=level x=xp r=repair f=refill]
const path = require("path"), fs = require("fs");
const { native, wasm, multipart } = require(path.join(__dirname, "transport.cjs"));
const steps = process.argv[3] ?? "erf";
const files = [{ name: "PPSA04018/sdimg_TEST/Manual.sav", bytes: fs.readFileSync(path.join(__dirname, "w3/Manual.sav")) }];
async function go(t) {
  const J = { "content-type": "application/json" };
  const send = async (m, u, b) => { const r = await Promise.race([t.send(m, u, J, b === undefined ? null : Buffer.from(JSON.stringify(b))), new Promise(res => setTimeout(() => res({ status: "TIMEOUT 30s", body: Buffer.from("") }), 30000))]); let j = null; try { j = JSON.parse(r.body); } catch {} return { status: r.status, json: j, text: r.body.toString().slice(0, 120) }; };
  const mp = await multipart(files);
  const ws = JSON.parse((await t.send("POST", "/api/workspaces", { "content-type": mp.contentType }, mp.body)).body);
  const f = ws.titles[0].directories[0].files[0];
  const doc = (await send("POST", `/api/workspaces/${ws.id}/files/${f.id}/open`, { pluginId: "witcher3" })).json;
  const D = `/api/workspaces/${ws.id}/documents/${doc.id}`;
  let rev = doc.state.revision;
  const log = (l, r) => { console.log(`${t.name} ${l}: ${r.status}`); if (r.json?.state) rev = r.json.state.revision; return r; };
  if (/[el]/.test(steps)) log("edit player/level", await send("POST", `${D}/edits`, { edits: [{ valueId: "player/level", value: "2" }], revision: rev }));
  if (/[ex]/.test(steps)) log("edit player/xp", await send("POST", `${D}/edits`, { edits: [{ valueId: "player/xp", value: "1" }], revision: rev }));
  for (const a of [steps.includes("r") && "inventory/repair", steps.includes("f") && "inventory/refill-imported"].filter(Boolean)) {
    const p = log(`plan ${a}`, await send("POST", `${D}/actions/plan`, { actionId: a, parameters: {}, revision: rev }));
    if (p.json?.planId) log(`apply ${a}`, await send("POST", `${D}/actions/apply`, { planId: p.json.planId, revision: p.json.revision }));
  }
  const r = log("action catalog/inspect", await send("POST", `${D}/actions`, { actionId: process.env.ACTION ?? "catalog/inspect", parameters: process.env.PARAMS ? JSON.parse(process.env.PARAMS) : {}, revision: rev }));
  if (r.status !== 200) console.log("   ", r.text);
  log("GET /api/version afterwards", await send("GET", "/api/version"));
}
(async () => {
  await go(native(`http://127.0.0.1:${process.argv[2] ?? 5233}`));
  const w = await wasm("http://127.0.0.1:4221"); await go(w);
  console.log("browser console:", w.consoleLines.slice(0, 2).map(s => s.slice(0, 200)));
  await w.close();
})();

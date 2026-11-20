// Variant: Witcher3 synthetic Manual.sav, one field edit, then GET download (serializes dirty doc, LZ4) and an export.
const path = require("path"), fs = require("fs"), crypto = require("crypto");
const { native, wasm, multipart } = require(path.join(__dirname, "transport.cjs"));
const files = [{ name: "PPSA04018/sdimg_TEST/Manual.sav", bytes: fs.readFileSync(path.join(__dirname, "w3/Manual.sav")) }];
async function go(t) {
  const J = { "content-type": "application/json" };
  const send = (m, u, b) => Promise.race([t.send(m, u, J, b === undefined ? null : Buffer.from(JSON.stringify(b))), new Promise(res => setTimeout(() => res({ status: "TIMEOUT", body: Buffer.alloc(0) }), 30000))]);
  const mp = await multipart(files);
  const ws = JSON.parse((await t.send("POST", "/api/workspaces", { "content-type": mp.contentType }, mp.body)).body);
  const f = ws.titles[0].directories[0].files[0];
  const doc = JSON.parse((await send("POST", `/api/workspaces/${ws.id}/files/${f.id}/open`, { pluginId: "witcher3" })).body);
  const D = `/api/workspaces/${ws.id}/documents/${doc.id}`;
  console.log(t.name, "edit", (await send("POST", `${D}/edits`, { edits: [{ valueId: "player/level", value: "2" }], revision: doc.state.revision })).status);
  const d = await send("GET", `${D}/download`);
  console.log(t.name, "download", d.status, d.body.length, crypto.createHash("sha256").update(d.body).digest("hex").slice(0, 16));
  for (const [m, u] of [["GET", "/api/version"], ["GET", D], ["GET", `/api/workspaces/${ws.id}`], ["POST", "/api/workspaces"]]) {
    const t0 = Date.now(); const r = await send(m, u, m === "POST" ? {} : undefined); console.log(t.name, "after:", m, u.replace(ws.id, "<ws>"), r.status, `${Date.now() - t0}ms`);
  }
}
(async () => { await go(native(`http://127.0.0.1:${process.argv[2] ?? 5233}`)); const w = await wasm("http://127.0.0.1:4221"); await go(w); console.log("console:", w.consoleLines.slice(0, 1).map(s => s.slice(0, 160))); await w.close(); })();

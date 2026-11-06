// Repro: plugin stats formatted with ToString("N0") follow the browser language in wasm, invariant on native.
// usage: node culture-n0.cjs <nativePort> <lang> <ff7 remake.zip>
const path = require("path"), fs = require("fs");
const { native, wasm, multipart } = require(path.join(__dirname, "transport.cjs"));
async function go(t, zip) {
  const J = { "content-type": "application/json" };
  const mp = await multipart([{ name: "remake.zip", bytes: zip, type: "application/zip" }]);
  const ws = JSON.parse((await t.send("POST", "/api/workspaces", { "content-type": mp.contentType }, mp.body)).body);
  const f = ws.titles[0].directories[0].files[0];
  const doc = JSON.parse((await t.send("POST", `/api/workspaces/${ws.id}/files/${f.id}/open`, J, Buffer.from('{"pluginId":"ff7r"}'))).body);
  const stats = JSON.stringify(doc.view).match(/"label":"Bytes","value":"[^"]*"/)[0];
  console.log(t.name, stats);
  await t.send("DELETE", `/api/workspaces/${ws.id}`);
}
(async () => {
  const zip = fs.readFileSync(process.argv[4]);
  await go(native(`http://127.0.0.1:${process.argv[2]}`), zip);
  const w = await wasm("http://127.0.0.1:4221", { wlang: process.argv[3] }); await go(w, zip); await w.close();
})();

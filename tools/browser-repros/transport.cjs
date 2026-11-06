// Transports: native HTTP Api and in-browser worker runtime (Playwright).
const { chromium } = require(require("path").join(__dirname, "../../web/node_modules/playwright"));

const KEEP = ["content-type", "etag", "allow", "content-disposition", "cache-control", "location", "x-content-type-options"];

function pickHeaders(pairs) {
  const out = {};
  for (const [k, v] of pairs) { const n = k.toLowerCase(); if (KEEP.includes(n)) out[n] = out[n] ? out[n] + ", " + v : v; }
  return out;
}

function native(base) {
  return {
    name: "native",
    async send(method, url, headers = {}, body = null) {
      const res = await fetch(base + url, { method, headers, body: body ?? undefined });
      const buf = Buffer.from(await res.arrayBuffer());
      return { status: res.status, headers: pickHeaders([...res.headers.entries()]), body: buf };
    },
    async close() {},
  };
}

function initScript() {
  const Native = window.Worker;
  const pending = new Map();
  let ids = 0;
  let current = null;
  window.Worker = class extends Native {
    constructor(u, o) {
      super(u, o);
      current = this;
      super.onmessage = e => {
        const d = e.data;
        if (d && d.id !== undefined && pending.has(d.id)) {
          if (d.type === "response" || d.type === "error") { const r = pending.get(d.id); pending.delete(d.id); r(d); }
          return;
        }
        this._l && this._l(e);
      };
    }
    set onmessage(l) { this._l = l; }
    get onmessage() { return this._l; }
  };
  const enc = buf => { let t = ""; const b = new Uint8Array(buf); for (let i = 0; i < b.length; i += 0x8000) t += String.fromCharCode(...b.subarray(i, i + 0x8000)); return btoa(t); };
  const dec = s => Uint8Array.from(atob(s), c => c.charCodeAt(0)).buffer;
  window.__ready = () => current !== null;
  window.__dispatch = (method, url, headers, b64) => new Promise(resolve => {
    const id = -1000000 - (++ids);
    pending.set(id, resolve);
    const body = b64 === null ? null : dec(b64);
    current.postMessage({ type: "request", request: { id, method, url, headers, body } }, body ? [body] : []);
  }).then(r => r.type === "error" ? { error: r.message } : { status: r.status, headers: r.headers, body: enc(r.frame.slice(r.bodyOffset)) });
}

async function wasm(origin, o = {}) {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ locale: o.locale || undefined, timezoneId: o.tz || undefined });
  const page = await context.newPage();
  const consoleLines = [];
  page.on("console", m => { if (m.type() === "error" || m.type() === "warning") { consoleLines.push(m.text()); if (process.env.TRACE) process.stderr.write("CONSOLE " + m.text().slice(0, 2000) + "\n"); } });
  page.on("worker", w => w.on("console", m => { if (process.env.TRACE) process.stderr.write("WCONSOLE " + m.type() + " " + m.text().slice(0, 3000) + "\n"); }));
  page.on("pageerror", e => consoleLines.push("pageerror " + e.message));
  await page.addInitScript(initScript);
  if (o.wlang) await page.route(/\/assets\/worker-[^/]*\.js$/, async route => {
    const r = await route.fetch(); const body = await r.text();
    const pre = `Object.defineProperty(self.navigator,"languages",{get:()=>["${o.wlang}"]});Object.defineProperty(self.navigator,"language",{get:()=>"${o.wlang}"});
`;
    await route.fulfill({ response: r, body: pre + body });
  });
  await page.goto(origin + "/");
  await page.waitForFunction(() => window.__ready(), null, { timeout: 60000 });
  const t = {
    name: "wasm",
    page, consoleLines,
    async send(method, url, headers = {}, body = null) {
      const pairs = Object.entries(headers).map(([k, v]) => [k.toLowerCase(), v]);
      const r = await page.evaluate(([m, u, h, b]) => window.__dispatch(m, u, h, b), [method, url, pairs, body ? Buffer.from(body).toString("base64") : null]);
      if (r.error) process.stderr.write("WORKER ERROR " + method + " " + url + ": " + r.error + "\n");
      if (r.error) return { status: -1, headers: {}, body: Buffer.from("WORKER ERROR: " + r.error) };
      return { status: r.status, headers: pickHeaders(r.headers), body: Buffer.from(r.body, "base64") };
    },
    async close() { await browser.close(); },
  };
  // wait for boot
  for (let i = 0; i < 120; i++) { const r = await t.send("GET", "/api/version"); if (r.status === 200) break; await new Promise(r => setTimeout(r, 500)); }
  return t;
}

async function multipart(files, extra = {}) {
  const form = new FormData();
  for (const [k, v] of Object.entries(extra)) form.append(k, v);
  for (const f of files) form.append("files", new Blob([f.bytes], { type: f.type ?? "application/octet-stream" }), f.name);
  const req = new Request("http://x/", { method: "POST", body: form });
  return { contentType: req.headers.get("content-type"), body: Buffer.from(await req.arrayBuffer()) };
}

module.exports = { native, wasm, multipart };

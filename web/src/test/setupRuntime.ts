import { vi } from "vitest";
import type { RuntimeClient } from "../runtime/client";

/**
 * jsdom has no Worker or WebAssembly runtime. Component and API tests keep stubbing the global
 * `fetch`; this stand-in forwards the runtime transport to it (looked up per call, so
 * `vi.stubGlobal("fetch", ...)` keeps working). runtime/client.test.ts covers the real client.
 */
vi.mock("../runtime", () => {
  const runtime: RuntimeClient = {
    fetch: (url, init, events) => { events?.onStart?.(); return globalThis.fetch(url, init); },
    state: () => ({ status: "ready", boot: { version: "0.0.0-test", plugins: [], failedAssemblies: [], persistent: true, tabCoordination: true, elapsedMs: 0, initializeMs: 0 } }),
    subscribe: () => () => undefined,
    ready: () => Promise.resolve(),
  };
  return { runtime };
});

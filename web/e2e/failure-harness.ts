import { test as base, expect, type Page } from "@playwright/test";
import { apiFlush, installApiHooks, onApi, routeApi, type ApiRequest, type ApiRoute } from "./apiRoutes";

export const failures = [
  { name: "422 JSON", status: 422, message: "Synthetic request rejected. Please try again." },
  { name: "503 JSON", status: 503, message: "Synthetic service unavailable. Please try again." },
  { name: "502 HTML", status: 502, message: "Request failed (502). Please try again." },
] as const;

export type Failure = typeof failures[number] | { name: "network"; status: 0; message: string };
/**
 * A request the runtime failed: apiRoutes' abort hands the app the worker's per-request `error` reply with
 * the fixed detail "Failed to fetch", which the UI shows verbatim. It is not a transport failure (the API
 * has no network); a stopped runtime is covered by ApiRoute.crashRuntime instead.
 */
export const networkFailure: Failure = {
  name: "network", status: 0, message: "The in-browser editor runtime failed while handling the request (Failed to fetch). "
    + "If it keeps happening, export your work and reload the page.",
};

/**
 * Fails a test on uncaught page errors, console errors, and API requests that failed or answered with an
 * HTTP error unless the test injected that failure (`block`) or declared it (`expectNetworkFailures`).
 * The API runs in the page's runtime worker, so its failures never reach the browser's network log; they
 * are observed through the API hooks' request lifecycle (apiRoutes.ts) instead.
 */
class FailureMonitor {
  private injected = new Set<ApiRequest>();
  private expectedNetworkResources: RegExp[] = [];
  private expectedConsole: RegExp[] = [];
  readonly unexpectedConsole: string[] = [];
  readonly unexpectedApi: string[] = [];
  readonly pageErrors: string[] = [];

  constructor(readonly page: Page) {
    page.on("pageerror", error => this.pageErrors.push(error.message));
    page.on("console", message => {
      if (message.type() !== "error" || this.expectedConsole.some(pattern => pattern.test(message.text()))) return;
      this.unexpectedConsole.push(`${message.text()} (${message.location().url})`);
    });
  }

  /** @internal Starts observing the API; must run before the first navigation. */
  async attach() {
    await installApiHooks(this.page);
    onApi(this.page, "response", response => {
      if (response.status() >= 400) this.unexpected(response.request(), `HTTP ${response.status()}`);
    });
    onApi(this.page, "requestfailed", request => {
      // The app cancelling its own request (navigation, a superseded query) is not a failure.
      if (request.failure()?.errorText === "net::ERR_ABORTED") return;
      this.unexpected(request, request.failure()?.errorText ?? "failed");
    });
  }

  private unexpected(request: ApiRequest, outcome: string) {
    if (this.injected.has(request) || this.expectedNetworkResources.some(pattern => pattern.test(request.url()))) return;
    this.unexpectedApi.push(`${request.method()} ${request.url()}: ${outcome}`);
  }

  /** Declares API failures (HTTP status >= 400 or no answer) the test provokes without `block`. */
  expectNetworkFailures(pattern: RegExp) {
    this.expectedNetworkResources.push(pattern);
  }

  /** Declares console errors the test provokes (for example the runtime logging its own failure). */
  expectConsoleErrors(pattern: RegExp) {
    this.expectedConsole.push(pattern);
  }

  async block(pattern: string | RegExp, failure: Failure, method?: string) {
    let blocked = true;
    let count = 0;
    await routeApi(this.page, pattern, async route => {
      if (!blocked || (method && route.request().method() !== method)) return route.fallback();
      count++;
      this.injected.add(route.request());
      await this.reject(route, failure);
    });
    return { release: () => { blocked = false; }, count: () => count };
  }

  private async reject(route: ApiRoute, failure: Failure) {
    if (failure.status === 0) return route.abort("failed");
    return route.fulfill(failure.name === "502 HTML"
      ? { status: 502, contentType: "text/html", body: "<!doctype html><h1>Proxy gateway failure</h1>" }
      : { status: failure.status, json: { error: failure.message } });
  }

  async assertClean() {
    // Failures are reported over async page bindings: let those already sent arrive first.
    if (!this.page.isClosed()) await apiFlush(this.page).catch(() => undefined);
    expect(this.pageErrors, "Uncaught errors and unhandled promise rejections").toEqual([]);
    expect(this.unexpectedConsole, "Console errors").toEqual([]);
    expect(this.unexpectedApi, "Only injected or declared API failures are expected").toEqual([]);
  }
}

export const test = base.extend<{ faults: FailureMonitor }>({
  faults: [async ({ page }, use) => {
    const monitor = new FailureMonitor(page);
    await monitor.attach();
    await use(monitor);
    await monitor.assertClean();
  }, { auto: true }],
});

export { expect };

export async function focusedControl(page: Page) {
  await expect.poll(() => page.evaluate(() => {
    const active = document.activeElement;
    if (!(active instanceof HTMLElement) || active === document.body || active === document.documentElement) return false;
    return !active.matches(":disabled, [hidden]") && !active.closest("[inert], [hidden], dialog:not([open])") && active.getClientRects().length > 0;
  }), { message: "Focus remains on a usable control or named heading" }).toBe(true);
}

import { test as base, expect, type Page } from "@playwright/test";
import { routeApi, type ApiRoute } from "./apiRoutes";

export const failures = [
  { name: "422 JSON", status: 422, message: "Synthetic request rejected. Please try again." },
  { name: "503 JSON", status: 503, message: "Synthetic service unavailable. Please try again." },
  { name: "502 HTML", status: 502, message: "Request failed (502). Please try again." },
] as const;

export type Failure = typeof failures[number] | { name: "network"; status: 0; message: string };
/**
 * A transport failure (the old server's network failure): the runtime worker answers the request with an
 * error. apiRoutes' abort reports "Failed to fetch" as the detail, which the UI shows verbatim.
 */
export const networkFailure: Failure = {
  name: "network", status: 0, message: "The in-browser editor runtime failed while handling the request (Failed to fetch). "
    + "If it keeps happening, export your work and reload the page.",
};

/** A failed-resource browser diagnostic is expected only for explicitly injected API faults. */
class FailureMonitor {
  private expectedResources = new Set<string>();
  private expectedNetworkResources: RegExp[] = [];
  readonly unexpectedConsole: string[] = [];
  readonly pageErrors: string[] = [];

  constructor(readonly page: Page) {
    page.on("pageerror", error => this.pageErrors.push(error.message));
    page.on("console", message => {
      if (message.type() !== "error") return;
      if (message.text().startsWith("Failed to load resource:") &&
        (this.expectedResources.has(message.location().url) || this.expectedNetworkResources.some(pattern => pattern.test(message.location().url)))) return;
      this.unexpectedConsole.push(`${message.text()} (${message.location().url})`);
    });
  }

  expectNetworkFailures(pattern: RegExp) {
    this.expectedNetworkResources.push(pattern);
  }

  async block(pattern: string | RegExp, failure: Failure, method?: string) {
    let blocked = true;
    let count = 0;
    await routeApi(this.page, pattern, async route => {
      if (!blocked || (method && route.request().method() !== method)) return route.fallback();
      count++;
      this.expectedResources.add(route.request().url());
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
    expect(this.pageErrors, "Uncaught errors and unhandled promise rejections").toEqual([]);
    expect(this.unexpectedConsole, "Only injected failed-resource diagnostics are expected").toEqual([]);
  }
}

export const test = base.extend<{ faults: FailureMonitor }>({
  faults: [async ({ page }, use) => {
    const monitor = new FailureMonitor(page);
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

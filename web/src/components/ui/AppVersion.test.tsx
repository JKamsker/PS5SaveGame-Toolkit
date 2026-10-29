import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { AppVersion } from "./AppVersion";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

function showVersion() {
  const client = new QueryClient({ defaultOptions: { queries: { gcTime: 0 } } });
  render(<QueryClientProvider client={client}><AppVersion /></QueryClientProvider>);
}

it("shows the running server's version including prerelease and commit metadata", async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ version: "1.2.3-rc.1+abc123" })));
  vi.stubGlobal("fetch", fetcher);
  showVersion();
  expect(await screen.findByText(/v1\.2\.3-rc\.1\+abc123/)).toBeTruthy();
  expect(fetcher.mock.calls[0]?.[0]).toBe("/api/version");
});

it.each([new Response("offline", { status: 503 }), new Response('{"other":"value"}')])(
  "keeps the application usable when version lookup fails", async (response) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response));
    showVersion();
    expect(await screen.findByText(/Version unavailable/)).toBeTruthy();
  },
);

it("handles an unreachable server", async () => {
  vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));
  showVersion();
  expect(await screen.findByText(/Version unavailable/)).toBeTruthy();
});

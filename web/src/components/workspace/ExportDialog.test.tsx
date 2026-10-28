import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import type { ExportPlan } from "../../api/protocol";
import { ExportDialog } from "./ExportDialog";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const empty: ExportPlan = { changedCount: 0, files: [] };
const changed: ExportPlan = { changedCount: 1, files: [{ relativePath: "save.dat", length: 1, reason: "edited" }] };

function setup() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 30_000, gcTime: 0 } } });
  const view = (open: boolean) => <QueryClientProvider client={client}>
    <ExportDialog ref={element => { if (element) element.open = open; }} workspaceId="w" open={open} onClose={vi.fn()} onToast={vi.fn()} />
  </QueryClientProvider>;
  const { rerender } = render(view(true));
  return { client, show: (open: boolean) => rerender(view(open)) };
}

describe("export plan freshness", () => {
  it("reloads the plan on each opening and disables download until the current plan arrives", async () => {
    let finish!: (value: ExportPlan) => void;
    vi.spyOn(api, "exportPlan").mockResolvedValueOnce(empty)
      .mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const { show, client } = setup();
    await screen.findByText("Nothing has been changed yet.");
    show(false);
    show(true);
    await waitFor(() => expect(api.exportPlan).toHaveBeenCalledTimes(2));
    expect((screen.getByRole("button", { name: "Download zip" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByText("Nothing has been changed yet.")).toBeNull();
    await act(async () => { finish(changed); });
    await waitFor(() => expect((screen.getByRole("button", { name: "Download zip" }) as HTMLButtonElement).disabled).toBe(false));
    client.clear();
  });

  it("does not download using a cached plan when the new plan fails", async () => {
    vi.spyOn(api, "exportPlan").mockResolvedValueOnce(changed).mockRejectedValueOnce(new ApiError("Plan unavailable", 503));
    const { show, client } = setup();
    await screen.findByText("save.dat");
    show(false);
    show(true);
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Plan unavailable");
    expect((screen.getByRole("button", { name: "Download zip" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByText("save.dat")).toBeNull();
    client.clear();
  });
});

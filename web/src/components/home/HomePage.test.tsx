import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import { library } from "../../state/libraryStore";
import { HomePage } from "./HomePage";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("focuses the import heading on entry and provides a main landmark", () => {
  vi.spyOn(api, "plugins").mockResolvedValue([]);
  vi.spyOn(api, "workspaces").mockResolvedValue([]);
  vi.spyOn(api, "titles").mockResolvedValue([]);
  vi.spyOn(library, "restore").mockResolvedValue(undefined);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  render(<MemoryRouter><QueryClientProvider client={client}><HomePage /></QueryClientProvider></MemoryRouter>);
  expect(screen.getByRole("main")).toBeTruthy();
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Open a PS5 save dump" }));
  client.clear();
});

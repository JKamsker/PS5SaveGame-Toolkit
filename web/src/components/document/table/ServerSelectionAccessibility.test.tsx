import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClientProvider } from "@tanstack/react-query";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../../api/client";
import { DocumentContext } from "../../../state/documentStore";
import { createDocumentFixture, createQueryClientScope } from "../../../test/documentFixture";
import { ServerTableView } from "./ServerTableView";

const clients = createQueryClientScope();
afterEach(() => { cleanup(); clients.clear(); vi.restoreAllMocks(); });

async function setup() {
  const columns = [{ key: "name", label: "Name" }];
  vi.spyOn(api, "queryCollection").mockResolvedValue({ collectionId: "items", columns,
    totalCount: 3, filteredCount: 3, offset: 0, limit: 2, revision: 1,
    rows: ["Potion", "Ether"].map(name => ({ key: name, cells: { name: { type: "text", text: name } } })),
  });
  const { context } = createDocumentFixture({ state: { canUndo: false, canRedo: false, isDirty: false, revision: 1 } });
  render(<QueryClientProvider client={clients.create({ defaultOptions: { queries: { retry: false } } })}><DocumentContext.Provider value={context}>
    <ServerTableView node={{ type: "queryableTable", collectionId: "items", title: "Items", columns, totalCount: 3 }} />
  </DocumentContext.Provider></QueryClientProvider>);
  await screen.findByRole("checkbox", { name: "Select Potion" });
}

it("exposes partial page selection as mixed and resets it for all/none", async () => {
  await setup();
  const all = screen.getByRole<HTMLInputElement>("checkbox", { name: "Select all rows on this page" });
  fireEvent.click(screen.getByRole("checkbox", { name: "Select Potion" }));
  await waitFor(() => expect(all.indeterminate).toBe(true));
  fireEvent.click(all);
  expect(all.checked).toBe(true);
  expect(all.indeterminate).toBe(false);
  fireEvent.click(all);
  expect(all.checked).toBe(false);
  expect(all.indeterminate).toBe(false);
});

it("keeps an atomic selection status mounted through first, query and cleared selection", async () => {
  await setup();
  const status = screen.getByRole("status", { name: "Items selection" });
  expect(status.getAttribute("aria-atomic")).toBe("true");
  fireEvent.click(screen.getByRole("checkbox", { name: "Select Potion" }));
  expect(screen.getByRole("status", { name: "Items selection" })).toBe(status);
  expect(status.textContent).toBe("1 selected");
  fireEvent.click(screen.getByRole("button", { name: "Select all 3 filtered rows" }));
  expect(status.textContent).toContain("All filtered rows selected");
  const clear = screen.getByRole("button", { name: "Clear selection" });
  clear.focus();
  await act(async () => fireEvent.click(clear));
  expect(document.activeElement).toBe(screen.getByRole("checkbox", { name: "Select all rows on this page" }));
  expect(status.textContent).toBe("No rows selected");
  expect(screen.getByRole("status", { name: "Items selection" })).toBe(status);
});

it("provides a named keyboard viewport without consuming child editing or selection keys", async () => {
  await setup();
  const viewport = screen.getByRole("region", { name: "Items rows" });
  Object.defineProperties(viewport, {
    clientHeight: { configurable: true, value: 560 },
    scrollHeight: { configurable: true, value: 2000 },
  });
  expect(viewport.tabIndex).toBe(0);
  expect(viewport.hasAttribute("inert")).toBe(false);
  viewport.focus();
  expect(document.activeElement).toBe(viewport);
  expect(fireEvent.keyDown(viewport, { key: "PageDown" })).toBe(false);
  expect(viewport.scrollTop).toBe(560);
  fireEvent.keyDown(viewport, { key: "End" });
  expect(viewport.scrollTop).toBe(1440);
  fireEvent.keyDown(viewport, { key: "Home" });
  expect(viewport.scrollTop).toBe(0);
  expect(fireEvent.keyDown(viewport, { key: "PageDown", ctrlKey: true })).toBe(true);
  const checkbox = screen.getByRole("checkbox", { name: "Select Potion" });
  checkbox.focus();
  expect(fireEvent.keyDown(checkbox, { key: "ArrowDown" })).toBe(true);
  expect(viewport.scrollTop).toBe(0);
  expect(document.activeElement).toBe(checkbox);
  fireEvent.click(checkbox);
  expect((checkbox as HTMLInputElement).checked).toBe(true);
});

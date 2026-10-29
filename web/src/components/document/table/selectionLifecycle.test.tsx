import { act, cleanup, fireEvent, renderHook, screen, within } from "@testing-library/react";
import { useState } from "react";
import { afterAll, afterEach, beforeAll, expect, it, vi } from "vitest";
import { actionReview } from "../../../api/actionReview";
import type { TableRow } from "../../../api/protocol";
import { ViewRenderer } from "../ViewRenderer";
import { renderSpatial } from "../spatial/spatialTestUtils";
import { useQueryableSelection } from "./selection";
import { stubViewport } from "./tableTestUtils";

let restore: () => void;
beforeAll(() => { restore = stubViewport(); });
afterAll(() => restore());
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("keeps memoized query selection and exclusions stable on unrelated rerenders", () => {
  const rows: TableRow[] = [{ key: "a", cells: {} }, { key: "b", cells: {} }];
  const query = { q: "item", includeEmpty: true };
  const hook = renderHook(() => useQueryableSelection(rows, "items", query, 4, 100));
  act(() => hook.result.current.selectFiltered?.());
  act(() => hook.result.current.toggle("b"));
  const selection = hook.result.current;
  hook.rerender();
  expect(hook.result.current).toBe(selection);
  expect(hook.result.current.queryScope).toMatchObject({ collectionId: "items", revision: 4,
    query: { q: "item", includeEmpty: true, offset: 0 }, excludedRowKeys: ["b"] });
});

it("retires a sibling's selection when the table unmounts and remounts", async () => {
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  const plan = vi.spyOn(actionReview, "plan").mockResolvedValue({ revision: 0, scope: {}, plan: { changes: [], errors: [], warnings: [], skippedRows: [] } });
  function Content() {
    const [visible, setVisible] = useState(true);
    return <><button onClick={() => setVisible(previous => !previous)}>Toggle table</button><ViewRenderer node={{ type: "section", title: "Root", children: [
      { type: "action", actionId: "restore", label: "Restore", supportsPlanning: true, selectionTableId: "items" },
      ...(visible ? [{ type: "table" as const, title: "Inventory", selectionId: "items", columns: [{ key: "name", label: "Name" }],
        rows: [{ key: "a", cells: { name: { type: "text" as const, text: "Potion" } } }] }] : []),
    ] }} /></>;
  }
  renderSpatial(<Content />);
  fireEvent.click(screen.getByRole("checkbox", { name: "Select Potion" }));
  fireEvent.click(screen.getByRole("button", { name: "Toggle table" }));
  expect(screen.getByRole("button", { name: "Restore" }).hasAttribute("disabled")).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Toggle table" }));
  fireEvent.click(screen.getByRole("button", { name: "Restore" }));
  await within(await screen.findByRole("dialog")).findByText("0 field changes · 0 skipped rows · one undo step");
  expect(plan.mock.calls[0]?.[4]).toEqual({});
});

import { cleanup, fireEvent, screen, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../../api/client";
import { actionReview } from "../../../api/actionReview";
import { ViewRenderer } from "../ViewRenderer";
import { renderSpatial } from "../spatial/spatialTestUtils";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("submits all explicitly selected row keys across collection pages", async () => {
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  const columns = [{ key: "name", label: "Name" }, { key: "action", label: "Action" }];
  vi.spyOn(api, "queryCollection").mockImplementation(async (_w, _d, _c, query) => {
    const later = query?.offset === 50;
    return { collectionId: "items", columns, totalCount: 100, filteredCount: 100, offset: query?.offset ?? 0, limit: 50, revision: 0,
      rows: [{ key: later ? "b" : "a", cells: { name: { type: "text", text: later ? "Ether" : "Potion" },
        action: { type: "action", actionId: "refill", label: "Refill", supportsPlanning: true } } }] };
  });
  const plan = vi.spyOn(actionReview, "plan").mockResolvedValue({ planId: "p", revision: 0, scope: { rowKeys: ["a", "b"] },
    plan: { changes: [], skippedRows: [], warnings: [], errors: [] } });
  renderSpatial(<ViewRenderer node={{ type: "queryableTable", collectionId: "items", title: "Items", columns, totalCount: 100 }} />);
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select Potion" }));
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select Ether" }));
  expect(screen.getByText("2 selected")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Refill: Ether" }));
  const dialog = within(await screen.findByRole("dialog", { name: "Preview: Refill" }));
  await dialog.findByText("0 field changes · 0 skipped rows · one undo step");
  expect(plan).toHaveBeenCalledExactlyOnceWith("w1", "d1", "refill", {}, { rowKeys: ["a", "b"] }, 0);
}, 10_000); // Two server pages and a completed action preview; 1.2 s in the full Windows suite.

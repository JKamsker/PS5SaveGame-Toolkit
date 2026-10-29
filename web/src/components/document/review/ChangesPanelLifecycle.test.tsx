import { act, cleanup, fireEvent, screen, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { actionReview, type SemanticChangeLog } from "../../../api/actionReview";
import { ChangesPanel } from "./ChangesPanel";
import { clearReviewClients, renderDocument } from "./reviewTestUtils";

afterEach(() => { cleanup(); clearReviewClients(); vi.restoreAllMocks(); });

const log: SemanticChangeLog = { revision: 1, complete: true, warnings: [],
  changes: Array.from({ length: 101 }, (_, index) => ({ valueId: `item/${index}`, label: `Item ${index}`,
    path: ["section:Items"], originalValue: "0", currentValue: "1", originalExists: true, currentExists: true, canRevert: true })),
};

async function setOpen(open: boolean) {
  const details = screen.getByText("Changes against original").closest("details")!;
  await act(async () => { details.open = open; fireEvent(details, new Event("toggle")); });
}

it("retains changed-field search and paging while the review section is closed", async () => {
  vi.spyOn(actionReview, "changes").mockResolvedValue(log);
  renderDocument(<ChangesPanel />);
  await setOpen(true);
  await screen.findByRole("button", { name: "Revert Item 0" });
  fireEvent.change(screen.getByRole("searchbox", { name: "Find a changed field" }), { target: { value: "Item" } });
  fireEvent.click(within(screen.getByRole("navigation", { name: "Changed fields pages" })).getByRole("button", { name: "Next" }));
  expect(screen.getByRole("button", { name: "Revert Item 100" })).toBeTruthy();
  await setOpen(false);
  expect(screen.queryByRole("searchbox")).toBeNull();
  await setOpen(true);
  expect(screen.getByRole("searchbox", { name: "Find a changed field" })).toHaveProperty("value", "Item");
  expect(await screen.findByRole("button", { name: "Revert Item 100" })).toBeTruthy();
});

it("keeps retained review rows inert after a document revision changes", async () => {
  vi.spyOn(actionReview, "changes").mockResolvedValue(log);
  const { store } = renderDocument(<ChangesPanel />);
  await setOpen(true);
  await screen.findByRole("button", { name: "Revert Item 0" });
  await act(async () => { store.apply({ ok: true, errors: [], changes: [], warnings: [], structureChanged: false,
    state: { canUndo: true, canRedo: false, isDirty: true, revision: 2 } }); });
  expect(await screen.findByRole("button", { name: "Jump to Item 0" })).toHaveProperty("disabled", true);
  expect(screen.getByRole("button", { name: "Revert Item 0" })).toHaveProperty("disabled", true);
});

import { act, cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { actionReview, type SemanticChangeLog } from "../../../api/actionReview";
import type { EditResult } from "../../../api/protocol";
import { deferred } from "../../../test/deferred";
import { ChangesPanel } from "./ChangesPanel";
import { clearReviewClients, renderDocument } from "./reviewTestUtils";

afterEach(() => { cleanup(); clearReviewClients(); vi.restoreAllMocks(); });
const log: SemanticChangeLog = { revision: 1, complete: true, warnings: [], changes: [
  { valueId: "count", label: "Count", path: [], originalValue: "1", currentValue: "2",
    originalExists: true, currentExists: true, canRevert: true },
] };

async function openReview() {
  const summary = screen.getByText("Changes against original").closest("summary")!;
  await act(async () => { summary.closest("details")!.open = true; fireEvent(summary.closest("details")!, new Event("toggle")); });
  return summary;
}

it("keeps review focus on the summary while reverting the final row", async () => {
  const pending = deferred<EditResult>();
  vi.spyOn(actionReview, "changes").mockResolvedValueOnce(log).mockResolvedValue({ ...log, revision: 2, changes: [] });
  vi.spyOn(actionReview, "revert").mockReturnValue(pending.promise);
  renderDocument(<ChangesPanel />);
  const summary = await openReview();
  const revert = await screen.findByRole("button", { name: "Revert Count" });
  revert.focus();
  fireEvent.click(revert);
  expect(document.activeElement).toBe(summary);
  expect(screen.getByRole("status", { name: "Changes review" }).textContent).toBe("Updating changes…");
  await act(async () => pending.resolve({ ok: true, errors: [], warnings: [], changes: [], structureChanged: false,
    state: { canUndo: true, canRedo: false, isDirty: false, revision: 2 } }));
  await screen.findByText("No semantic changes.");
  expect(document.activeElement).toBe(summary);
  expect(screen.getByRole("status", { name: "Changes review" }).textContent).toBe("0 of 0 changed fields");
});

it("updates the same live region from loading to ready without moving unrelated focus", async () => {
  const pending = deferred<SemanticChangeLog>();
  vi.spyOn(actionReview, "changes").mockReturnValue(pending.promise);
  renderDocument(<ChangesPanel />);
  await openReview();
  const status = screen.getByRole("status", { name: "Changes review" });
  expect(status.textContent).toBe("Loading changes…");
  const search = screen.getByRole("searchbox");
  search.focus();
  await act(async () => pending.resolve(log));
  await waitFor(() => expect(status.textContent).toBe("1 of 1 changed fields"));
  expect(screen.getByRole("status", { name: "Changes review" })).toBe(status);
  expect(document.activeElement).toBe(search);
});

import { act, cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { actionReview, type SemanticChangeLog } from "../../../api/actionReview";
import { ApiError } from "../../../api/client";
import { deferred } from "../../../test/deferred";
import { ChangesPanel } from "./ChangesPanel";
import { clearReviewClients, renderDocument } from "./reviewTestUtils";

afterEach(() => { cleanup(); clearReviewClients(); vi.restoreAllMocks(); });

const log: SemanticChangeLog = { revision: 1, complete: true, warnings: [], changes: [{ valueId: "quantity", label: "Potion quantity",
  path: [], originalValue: "2", currentValue: "10", originalExists: true, currentExists: true, canRevert: true }] };
const openReview = () => fireEvent.click(screen.getByText("Changes against original"));

it.each([
  new ApiError("Invalid changes request", 400),
  new ApiError("Unknown workspace. Import the save again.", 404),
  new ApiError("Changes service unavailable", 503),
  new ApiError("Request failed (502).", 502),
  new TypeError("Failed to fetch"),
])("retries a failed review load with drafts, document state and focus intact (%s)", async failure => {
  const changes = vi.spyOn(actionReview, "changes").mockRejectedValueOnce(failure).mockResolvedValue(log);
  const { store } = renderDocument(<ChangesPanel />);
  openReview();
  expect((await screen.findByRole("alert")).textContent).toBe(failure.message);
  const search = screen.getByRole("searchbox", { name: "Find a changed field" });
  fireEvent.change(search, { target: { value: "Potion" } });
  expect(screen.queryByText("No semantic changes.")).toBeNull();
  const retry = screen.getByRole("button", { name: "Retry changes" });
  retry.focus();
  fireEvent.click(retry);
  expect(document.activeElement).toBe(search);
  await screen.findByRole("button", { name: "Revert Potion quantity" });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(search).toHaveProperty("value", "Potion");
  expect(document.activeElement).toBe(search);
  expect(changes).toHaveBeenCalledTimes(2);
  expect(store.state).toEqual({ canUndo: true, canRedo: false, isDirty: true, revision: 1 });
});

it.each(["success", "failure"])("ignores a delayed review %s after a newer revision loads", async outcome => {
  const old = deferred<SemanticChangeLog>();
  const changes = vi.spyOn(actionReview, "changes").mockReturnValueOnce(old.promise).mockResolvedValue({ ...log, revision: 2,
    changes: [{ ...log.changes[0]!, label: "Current quantity" }] });
  const { store } = renderDocument(<ChangesPanel />);
  openReview();
  await waitFor(() => expect(changes).toHaveBeenCalledTimes(1));
  act(() => store.apply({ ok: true, errors: [], changes: [], warnings: [], structureChanged: false,
    state: { canUndo: true, canRedo: false, isDirty: true, revision: 2 } }));
  await screen.findByRole("button", { name: "Revert Current quantity" });
  await act(async () => {
    if (outcome === "success") old.resolve(log);
    else old.reject(new Error("Obsolete failure"));
    await old.promise.catch(() => {});
  });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(screen.queryByRole("button", { name: "Revert Potion quantity" })).toBeNull();
  expect(screen.getByRole("button", { name: "Revert Current quantity" })).toHaveProperty("disabled", false);
});

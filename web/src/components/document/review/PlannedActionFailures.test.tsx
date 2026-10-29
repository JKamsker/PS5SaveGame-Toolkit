import { act, cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { actionReview, type ActionPreview } from "../../../api/actionReview";
import { ApiError } from "../../../api/client";
import type { EditResult } from "../../../api/protocol";
import { deferred } from "../../../test/deferred";
import { installDialogStub } from "../../../test/dialog";
import { ActionButton } from "../ActionButton";
import { clearReviewClients, renderDocument } from "./reviewTestUtils";

let restoreDialog: () => void;
beforeEach(() => { restoreDialog = installDialogStub({ dispatchCloseEvent: true }); });
afterEach(() => { cleanup(); clearReviewClients(); vi.restoreAllMocks(); restoreDialog(); });

const preview: ActionPreview = { planId: "plan", revision: 1, scope: {}, plan: { errors: [], warnings: [], skippedRows: [],
  changes: [{ valueId: "quantity", label: "Potion quantity", oldValue: "2", newValue: "15", oldExists: true, newExists: true, derived: false }] } };
const applied: EditResult = { ok: true, errors: [], changes: [{ valueId: "quantity", oldValue: "2", newValue: "15" }], warnings: [],
  structureChanged: false, state: { canUndo: true, canRedo: false, isDirty: true, revision: 2 } };
const failures = [new ApiError("Preview rejected", 422), new ApiError("Unknown document. Reopen the save.", 404),
  new ApiError("Action service unavailable", 503), new ApiError("Request failed (502).", 502), new TypeError("Failed to fetch")];

function setup() {
  const fixture = renderDocument(<ActionButton node={{ type: "action", actionId: "refill", label: "Refill", supportsPlanning: true,
    parameters: [{ type: "field", valueId: "quantity", label: "Target quantity", spec: { kind: "integer", min: "0", max: "99" }, value: "10" }] }} />);
  fireEvent.click(screen.getByRole("button", { name: "Refill" }));
  const input = screen.getByRole("textbox", { name: "Target quantity" });
  fireEvent.change(input, { target: { value: "15" } });
  fireEvent.blur(input);
  return fixture;
}

it.each(failures)("preserves action parameters after a failed preview and focuses the actionable error (%s)", async failure => {
  const plan = vi.spyOn(actionReview, "plan").mockRejectedValueOnce(failure).mockResolvedValue(preview);
  const apply = vi.spyOn(actionReview, "apply").mockResolvedValue(applied);
  const { store } = setup();
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  const error = await screen.findByRole("alert");
  expect(error.textContent).toBe(failure.message);
  await waitFor(() => expect(document.activeElement).toBe(error));
  expect(screen.getByRole("textbox", { name: "Target quantity" })).toHaveProperty("value", "15");
  expect(store.state.revision).toBe(1);
  expect(apply).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  await screen.findByRole("button", { name: "Apply" });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(plan).toHaveBeenLastCalledWith("w1", "d1", "refill", { quantity: "15" }, {}, 1);
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Preview: Refill" }));
});

it.each(failures)("requires a fresh preview after a failed apply without phantom changes (%s)", async failure => {
  const plan = vi.spyOn(actionReview, "plan").mockResolvedValueOnce(preview).mockResolvedValue({ ...preview, planId: "fresh" });
  const apply = vi.spyOn(actionReview, "apply").mockRejectedValueOnce(failure).mockResolvedValue(applied);
  const { store, context } = setup();
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  fireEvent.click(await screen.findByRole("button", { name: "Apply" }));
  const error = await screen.findByRole("alert");
  expect(error.textContent).toBe(failure.message);
  await waitFor(() => expect(document.activeElement).toBe(error));
  expect(screen.getByRole("textbox", { name: "Target quantity" })).toHaveProperty("value", "15");
  expect(screen.queryByRole("button", { name: "Apply" })).toBeNull();
  expect(store.state.revision).toBe(1);
  expect(context.acceptResult).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  fireEvent.click(await screen.findByRole("button", { name: "Apply" }));
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  expect(plan).toHaveBeenCalledTimes(2);
  expect(apply).toHaveBeenLastCalledWith("w1", "d1", "fresh", 1);
  expect(store.state.revision).toBe(2);
  expect(document.activeElement).toBe(screen.getByRole("button", { name: "Refill" }));
});

it.each(["success", "failure"])("retires a pending preview observer after navigation (%s)", async outcome => {
  const pending = deferred<ActionPreview>();
  vi.spyOn(actionReview, "plan").mockReturnValue(pending.promise);
  const apply = vi.spyOn(actionReview, "apply");
  const { unmount } = setup();
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  unmount();
  await act(async () => {
    if (outcome === "success") pending.resolve(preview);
    else pending.reject(new Error("Obsolete failure"));
    await pending.promise.catch(() => {});
  });
  expect(apply).not.toHaveBeenCalled();
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(screen.queryByRole("alert")).toBeNull();
});

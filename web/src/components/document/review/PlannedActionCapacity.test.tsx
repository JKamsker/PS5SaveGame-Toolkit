import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { actionReview, type ActionPreview } from "../../../api/actionReview";
import { ApiError } from "../../../api/client";
import { installDialogStub } from "../../../test/dialog";
import { ActionButton } from "../ActionButton";
import { clearReviewClients, renderDocument } from "./reviewTestUtils";

let restoreDialog: () => void;
beforeEach(() => { restoreDialog = installDialogStub({ dispatchCloseEvent: true }); });
afterEach(() => { cleanup(); clearReviewClients(); vi.restoreAllMocks(); restoreDialog(); });

const preview: ActionPreview = { planId: "plan", revision: 1, scope: {}, plan: { errors: [], warnings: [], skippedRows: [],
  changes: [{ valueId: "caption", label: "Caption", oldValue: "old", newValue: "🎮", oldExists: true, newExists: true, derived: false }] } };

function setup(maxUtf8Bytes: number | null = 4) {
  const fixture = renderDocument(<ActionButton node={{ type: "action", actionId: "rename", label: "Rename", supportsPlanning: true,
    parameters: [{ type: "field", valueId: "caption", label: "Caption", description: "Save manager caption", spec: { kind: "text", maxUtf8Bytes }, value: "old" }] }} />);
  fireEvent.click(screen.getByRole("button", { name: "Rename" }));
  const input = screen.getByRole("textbox", { name: "Caption" });
  fireEvent.change(input, { target: { value: "🎮" } });
  fireEvent.blur(input);
  return fixture;
}

it("previews explicit-null wire capacity parameters without a byte counter or validation block", async () => {
  const plan = vi.spyOn(actionReview, "plan").mockResolvedValue(preview);
  setup(null);
  const input = screen.getByRole("textbox", { name: "Caption" });
  fireEvent.change(input, { target: { value: "🎮🎮" } });
  fireEvent.blur(input);
  expect(document.querySelector(".text-capacity")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  await screen.findByRole("button", { name: "Apply" });
  expect(plan).toHaveBeenCalledExactlyOnceWith("w1", "d1", "rename", { caption: "🎮🎮" }, {}, 1);
});

it("shows a byte-aware action parameter and disables an empty preview without applying or changing history", async () => {
  const plan = vi.spyOn(actionReview, "plan").mockResolvedValue({ ...preview, plan: { ...preview.plan, changes: [] } });
  const apply = vi.spyOn(actionReview, "apply");
  const { store } = setup();
  const originalState = store.state;
  expect(screen.getByText("4 / 4 UTF-8 bytes used · 0 bytes remaining")).toBeTruthy();
  const input = screen.getByRole("textbox", { name: "Caption" });
  const descriptions = input.getAttribute("aria-describedby")!.split(" ").map(id => document.getElementById(id)?.textContent);
  expect(descriptions).toContain("Save manager caption");
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  expect(await screen.findByText("No changes. No undo step will be created.")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Apply" })).toHaveProperty("disabled", true);
  fireEvent.submit(screen.getByRole("dialog").querySelector("form")!);
  await waitFor(() => expect(screen.getByRole("button", { name: "Cancel" })).toHaveProperty("disabled", false));
  expect(apply).not.toHaveBeenCalled();
  expect(store.state).toEqual(originalState);
  expect(plan).toHaveBeenCalledExactlyOnceWith("w1", "d1", "rename", { caption: "🎮" }, {}, 1);
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  expect(document.activeElement).toBe(screen.getByRole("button", { name: "Rename" }));
});

it.each(["envelope", "exception"])("preserves Unicode parameters and focuses a stale 409 reason before fresh preview (%s)", async kind => {
  const plan = vi.spyOn(actionReview, "plan").mockResolvedValue(preview);
  const apply = vi.spyOn(actionReview, "apply");
  if (kind === "exception") apply.mockRejectedValue(new ApiError("Metadata changed", 409, { code: "stale" }));
  else apply.mockResolvedValue({ ok: false, stale: true, errors: [{ valueId: "caption", message: "Metadata changed" }], changes: [], warnings: [], structureChanged: false,
    state: { canUndo: true, canRedo: false, isDirty: true, revision: 1 } });
  const { store } = setup();
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  fireEvent.click(await screen.findByRole("button", { name: "Apply" }));
  const reason = await screen.findByRole("alert");
  expect(reason.textContent).toContain("This preview is stale");
  await waitFor(() => expect(document.activeElement).toBe(reason));
  expect(screen.getByRole("textbox", { name: "Caption" })).toHaveProperty("value", "🎮");
  expect(screen.queryByRole("button", { name: "Apply" })).toBeNull();
  expect(store.state.revision).toBe(1);
  fireEvent.click(screen.getByRole("button", { name: "Preview again" }));
  await screen.findByRole("button", { name: "Apply" });
  expect(plan).toHaveBeenLastCalledWith("w1", "d1", "rename", { caption: "🎮" }, {}, 1);
  expect(apply).toHaveBeenCalledTimes(1);
});

it.each([undefined, "conflict"])("does not mislabel a thrown non-stale 409 in the action hook (code=%s)", async code => {
  vi.spyOn(actionReview, "plan").mockResolvedValue(preview);
  vi.spyOn(actionReview, "apply").mockRejectedValue(new ApiError("Action conflicts with current settings", 409, code === undefined ? undefined : { code }));
  setup();
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  fireEvent.click(await screen.findByRole("button", { name: "Apply" }));
  const reason = await screen.findByRole("alert");
  await waitFor(() => expect(document.activeElement).toBe(reason));
  expect(reason.textContent).toBe("Action conflicts with current settings");
  expect(screen.getAllByRole("alert")).toHaveLength(1);
  expect(screen.getByRole("textbox", { name: "Caption" })).toHaveProperty("value", "🎮");
  expect(screen.queryByRole("button", { name: "Preview again" })).toBeNull();
  expect(screen.getByRole("button", { name: "Preview" })).toHaveProperty("disabled", false);
});

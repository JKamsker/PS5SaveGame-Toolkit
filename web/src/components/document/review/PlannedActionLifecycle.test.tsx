import { cleanup, fireEvent, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { actionReview, type ActionPreview } from "../../../api/actionReview";
import { installDialogStub } from "../../../test/dialog";
import { ActionButton } from "../ActionButton";
import { clearReviewClients, renderDocument } from "./reviewTestUtils";

let restoreDialog: () => void;
beforeEach(() => { restoreDialog = installDialogStub(); });
afterEach(() => { cleanup(); clearReviewClients(); vi.restoreAllMocks(); restoreDialog(); });

const preview: ActionPreview = { planId: "plan", revision: 1, scope: {}, plan: {
  errors: [], warnings: [],
  changes: Array.from({ length: 101 }, (_, index) => ({ valueId: `value/${index}`, label: `Change ${index}`,
    oldValue: "0", newValue: "1", oldExists: true, newExists: true, derived: false })),
  skippedRows: Array.from({ length: 101 }, (_, index) => ({ rowKey: `row/${index}`, reason: "Unchanged" })),
} };

it("retains both preview page positions while parameters are shown and planned again", async () => {
  vi.spyOn(actionReview, "plan").mockResolvedValue(preview);
  renderDocument(<ActionButton node={{ type: "action", actionId: "run", label: "Run", supportsPlanning: true,
    parameters: [{ type: "field", valueId: "flag", label: "Flag", spec: { kind: "bool" }, value: false }] }} />);
  fireEvent.click(screen.getByRole("button", { name: "Run" }));
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  await screen.findByText("Change 0");
  const changes = screen.getByRole("navigation", { name: "Preview changes pages" });
  fireEvent.click(within(changes).getByRole("button", { name: "Next" }));
  const skipped = document.querySelector('nav[aria-label="Skipped rows pages"]')!;
  fireEvent.click(within(skipped as HTMLElement).getByRole("button", { name: "Next", hidden: true }));
  expect(screen.getByText("Change 100")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Change parameters" }));
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  expect(await screen.findByText("Change 100")).toBeTruthy();
  expect(document.querySelector('nav[aria-label="Skipped rows pages"]')?.textContent).toContain("Page 2 of 2");
});

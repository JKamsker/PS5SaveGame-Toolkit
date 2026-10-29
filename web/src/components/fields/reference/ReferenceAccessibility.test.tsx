import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../../api/client";
import type { ReferenceCandidate } from "../../../api/protocol";
import { DocumentContext } from "../../../state/documentStore";
import { createDocumentFixture } from "../../../test/documentFixture";
import { deferred } from "../../../test/deferred";
import { field, renderTable, stubViewport, table, text } from "../../document/table/tableTestUtils";
import { FieldRow } from "../FieldRow";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const node = field("item/id", { kind: "reference", catalogId: "items" }, "1001",
  { label: "Item", description: "Keep the existing format", warning: "Unverified choice" });
const candidate: ReferenceCandidate = { rawId: "1001", label: "Potion",
  eligibility: { available: true, eligible: true, selectable: true, reasons: [] } };

it("names the current raw ID and resolved batch label without losing help or focus", async () => {
  const pending = deferred<Awaited<ReturnType<typeof api.referenceRecords>>>();
  vi.spyOn(api, "referenceRecords").mockReturnValue(pending.promise);
  const { context } = createDocumentFixture();
  render(<DocumentContext.Provider value={context}><FieldRow node={node} /></DocumentContext.Provider>);
  const trigger = screen.getByRole("button", { name: "Item 1001", description: "Unverified choice Keep the existing format" });
  trigger.focus();
  await waitFor(() => expect(api.referenceRecords).toHaveBeenCalledOnce());
  await act(async () => pending.resolve({ revision: 0, results: [
    { fieldId: node.valueId, recordKey: "1001", status: 200, candidate },
  ] }));
  expect(screen.getByRole("button", { name: "Item Potion (1001)", description: "Unverified choice Keep the existing format" })).toBe(trigger);
  expect(document.activeElement).toBe(trigger);
});

it("includes the row context and current item in compact reference names", async () => {
  const restoreViewport = stubViewport();
  try {
    vi.spyOn(api, "referenceRecords").mockResolvedValue({ revision: 0, results: [
      { fieldId: node.valueId, recordKey: "1001", status: 200, candidate },
    ] });
    renderTable(table([{ key: "a", cells: { name: text("Slot A"), value: node } }]));
    expect(await screen.findByRole("button", { name: "Item (Slot A) Potion (1001)",
      description: "Unverified choice. Keep the existing format" })).toBeTruthy();
  } finally { restoreViewport(); }
});

import { cleanup, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it } from "vitest";
import { renderTable, stubViewport, table } from "./tableTestUtils";

let restoreViewport: () => void;
beforeEach(() => { restoreViewport = stubViewport(); });
afterEach(() => { cleanup(); restoreViewport(); });

it("preserves text tone, numeric alignment and full statistic summaries in compact cells", () => {
  renderTable(table([{ key: "row", cells: {
    number: { type: "text", text: "123", tone: "Warning", monospace: true },
    stats: { type: "stats", items: [{ label: "Level", value: "12" }, { label: "Time", value: "30h" }] },
  } }], ["number", "stats"]));
  const number = screen.getByText("123");
  expect(number.title).toBe("123");
  expect(number.className).toBe("cell-text cell-text--Warning mono");
  expect(number.closest('[role="cell"]')?.classList.contains("table__cell--number")).toBe(true);
  const stats = screen.getByText("Level 12 · Time 30h");
  expect(stats.title).toBe("Level 12 · Time 30h");
});

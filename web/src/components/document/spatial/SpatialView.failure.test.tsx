import { cleanup, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { ViewRenderer } from "../ViewRenderer";
import { graph, renderSpatial } from "./spatialTestUtils";

vi.mock("./GraphView", () => { throw new Error("Chunk unavailable"); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("keeps the overview available when a deferred view fails to load", async () => {
  vi.spyOn(console, "error").mockImplementation(() => {});
  renderSpatial(<ViewRenderer node={graph} />);
  expect(await screen.findByRole("alert")).toHaveProperty("textContent", expect.stringContaining("Could not load this view"));
  expect(screen.getByText(graph.textAlternative)).toBeTruthy();
});

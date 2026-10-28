import { cleanup, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { ViewRenderer } from "../ViewRenderer";
import { graph, map, renderSpatial } from "./spatialTestUtils";

afterEach(cleanup);

describe("deferred spatial views", () => {
  it("announces graph loading, then renders the diagram and text alternative", async () => {
    renderSpatial(<ViewRenderer node={graph} />);
    expect(screen.getByRole("status").textContent).toBe("Loading Skills…");
    expect(await screen.findByRole("region", { name: "Skills diagram" })).toBeTruthy();
    expect(screen.getAllByText(graph.textAlternative).length).toBeGreaterThan(0);
    expect(screen.getByText(/Text alternative/)).toBeTruthy();
  });

  it("loads maps independently with their accessible marker list", async () => {
    renderSpatial(<ViewRenderer node={map} />);
    expect(screen.getByRole("status").textContent).toBe("Loading Courtyard…");
    expect(await screen.findByRole("region", { name: "Courtyard diagram" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Jump to Courtyard cache" })).toBeTruthy();
    expect(screen.getByText(/Text alternative/)).toBeTruthy();
  });
});

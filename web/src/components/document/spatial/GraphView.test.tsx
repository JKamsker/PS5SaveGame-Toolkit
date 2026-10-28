import { cleanup, fireEvent, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { ViewRenderer } from "../ViewRenderer";
import { graphLayout } from "./graphLayout";
import { graph, renderSpatial } from "./spatialTestUtils";

afterEach(cleanup);

describe("graph views", () => {
  it("renders states, costs, directed prerequisites and a full text alternative", () => {
    const { container } = renderSpatial(<ViewRenderer node={graph} />);
    expect(screen.getByRole("button", { name: "Dash: Available, cost 2 points, jump to control" }).tagName).toBe("BUTTON");
    expect(container.querySelectorAll("svg line")).toHaveLength(2);
    const list = screen.getByRole("list");
    expect(within(list).getAllByRole("listitem")).toHaveLength(4);
    expect(list.textContent).toContain("Cost: 4 points");
    expect(list.textContent).toContain("Prerequisites: Dash");
    expect(screen.getByText("Text alternative (4 items)").closest("details")?.open).toBe(true);
    const positions = graphLayout(graph)!.positions;
    expect(positions.get("dash")!.x).toBeGreaterThan(positions.get("walk")!.x);
    expect(positions.get("glide")!.x).toBeGreaterThan(positions.get("dash")!.x);
  });

  it("filters both graphic and text by status, branch and search", () => {
    const { container } = renderSpatial(<ViewRenderer node={graph} />);
    fireEvent.change(screen.getByLabelText("Status"), { target: { value: "Locked" } });
    expect(screen.getAllByRole("listitem")).toHaveLength(1);
    expect(screen.getByRole("list").textContent).toContain("Glide");
    expect(container.querySelectorAll("svg line")).toHaveLength(0);
    fireEvent.change(screen.getByLabelText("Status"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("Branch"), { target: { value: "Movement" } });
    expect(screen.getAllByRole("listitem")).toHaveLength(2);
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "dash" } });
    expect(screen.getAllByRole("listitem")).toHaveLength(1);
    expect(screen.getByText(/1 of 4 skills shown/)).toBeTruthy();
  });

  it("supports bounded zoom, keyboard panning and reset without intercepting control keys", () => {
    renderSpatial(<ViewRenderer node={graph} />);
    const diagram = screen.getByRole("region", { name: "Skills diagram" });
    diagram.focus();
    fireEvent.keyDown(diagram, { key: "ArrowRight" });
    expect(diagram.scrollLeft).toBe(80);
    fireEvent.keyDown(screen.getByRole("button", { name: /Walk: Unlocked/ }), { key: "ArrowRight" });
    expect(diagram.scrollLeft).toBe(80);
    const zoom = screen.getByRole("button", { name: "Zoom in Skills" });
    for (let i = 0; i < 20; i++) fireEvent.click(zoom);
    expect((zoom as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByLabelText("Zoom").textContent).toBe("300%");
    fireEvent.click(screen.getByRole("button", { name: "Reset view" }));
    expect(screen.getByLabelText("Zoom").textContent).toBe("100%");
  });

  it("bounds list rendering and rejects an oversized payload before layout", () => {
    const items = Array.from({ length: 51 }, (_, i) => ({ id: String(i), label: `Skill ${i}`, state: "Unknown" as const }));
    const rendered = renderSpatial(<ViewRenderer node={{ ...graph, items, edges: [] }} />);
    expect(screen.getAllByRole("listitem")).toHaveLength(50);
    fireEvent.click(screen.getByRole("button", { name: "Next items" }));
    expect(screen.getAllByRole("listitem")).toHaveLength(1);
    rendered.unmount();
    const { container } = renderSpatial(<ViewRenderer node={{ ...graph, items: Array.from({ length: 513 }, () => items[0]!) }} />);
    expect(screen.getByRole("alert").textContent).toContain("limits");
    expect(container.querySelector("svg")).toBeNull();
  });

  it("falls back to the text list for cyclic or dangling graph data", () => {
    const invalid = { ...graph, edges: [...graph.edges, { from: "glide", to: "walk" }] };
    renderSpatial(<ViewRenderer node={invalid} />);
    expect(screen.getByRole("alert").textContent).toContain("text alternative");
    expect(screen.getAllByRole("listitem")).toHaveLength(4);
    expect(graphLayout({ ...graph, edges: [{ from: "absent", to: "walk" }] })).toBeNull();
  });
});

import { useState, type ReactNode } from "react";
import { cleanup, fireEvent, screen } from "@testing-library/react";
import { afterEach, expect, it } from "vitest";
import { GraphView } from "./GraphView";
import { MapView } from "./MapView";
import { graph, map, renderSpatial } from "./spatialTestUtils";

afterEach(cleanup);

function Snapshots<T>({ nodes, children }: { nodes: T[]; children: (node: T) => ReactNode }) {
  const [index, setIndex] = useState(0);
  return <>
    <button type="button" onClick={() => setIndex(previous => previous + 1)}>Next snapshot</button>
    {children(nodes[index]!)}
  </>;
}

it("keeps graph placement and zoom through filtering, then resets the view for a new title", () => {
  renderSpatial(<Snapshots nodes={[graph, { ...graph, title: "Other skills" }]}>{node => <GraphView node={node} />}</Snapshots>);
  const originalLeft = screen.getByRole("button", { name: /Dash: Available/ }).style.left;
  fireEvent.click(screen.getByRole("button", { name: "Zoom in Skills" }));
  fireEvent.change(screen.getByRole("searchbox"), { target: { value: "dash" } });
  expect(screen.getByLabelText("Zoom").textContent).toBe("125%");
  expect(screen.getByRole("button", { name: /Dash: Available/ }).style.left).toBe(originalLeft);
  expect(screen.getAllByRole("listitem")).toHaveLength(1);
  fireEvent.click(screen.getByRole("button", { name: "Next snapshot" }));
  expect(screen.getByRole("searchbox")).toHaveProperty("value", "");
  expect(screen.getByLabelText("Zoom").textContent).toBe("100%");
  expect(screen.getAllByRole("listitem")).toHaveLength(graph.items.length);
});

it("retains map selection and zoom when filters temporarily hide the selected marker", () => {
  renderSpatial(<MapView node={map} />);
  fireEvent.click(screen.getByRole("button", { name: "Courtyard cache: Missing, Loot" }));
  fireEvent.click(screen.getByRole("button", { name: "Zoom in Courtyard" }));
  fireEvent.change(screen.getByLabelText("Status"), { target: { value: "Found" } });
  expect(screen.queryByRole("complementary")).toBeNull();
  expect(screen.getByLabelText("Zoom").textContent).toBe("125%");
  fireEvent.change(screen.getByLabelText("Status"), { target: { value: "" } });
  expect(screen.getByRole("complementary", { name: "Selected location: Courtyard cache" })).toBeTruthy();
  expect(screen.getByRole("button", { name: "Courtyard cache: Missing, Loot" }).getAttribute("aria-pressed")).toBe("true");
});

it("retains a failed background for the same asset, retries a new asset, and resets map state for a new title", () => {
  const next = { ...map, background: { ...map.background!, assetId: "test.spatial:replacement.png" } };
  renderSpatial(<Snapshots nodes={[map, { ...map }, next, { ...next, title: "Other region" }]}>{node => <MapView node={node} />}</Snapshots>);
  fireEvent.error(screen.getByRole("img"));
  fireEvent.click(screen.getByRole("button", { name: "Courtyard cache: Missing, Loot" }));
  fireEvent.click(screen.getByRole("button", { name: "Zoom in Courtyard" }));
  fireEvent.click(screen.getByRole("button", { name: "Next snapshot" }));
  expect(screen.queryByRole("img")).toBeNull();
  expect(screen.getByRole("complementary")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Next snapshot" }));
  expect(screen.getByRole("img").getAttribute("src")).toContain("replacement.png");
  expect(screen.getByLabelText("Zoom").textContent).toBe("125%");
  fireEvent.change(screen.getByRole("searchbox"), { target: { value: "cache" } });
  fireEvent.click(screen.getByRole("button", { name: "Next snapshot" }));
  expect(screen.getByRole("searchbox")).toHaveProperty("value", "");
  expect(screen.getByLabelText("Zoom").textContent).toBe("100%");
  expect(screen.queryByRole("complementary")).toBeNull();
});

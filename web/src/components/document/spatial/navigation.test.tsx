import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "../../../api/client";
import type { GraphNode } from "../../../api/spatial";
import type { ViewNode } from "../../../api/protocol";
import { ViewRenderer } from "../ViewRenderer";
import { stubViewport } from "../table/tableTestUtils";
import { graph, map, renderSpatial } from "./spatialTestUtils";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const controls: ViewNode = { type: "section", title: "Controls", collapsed: true, children: [{ type: "tabs", tabs: [
  { title: "Other", children: [] }, { title: "Skills", children: [
    { type: "field", valueId: "skill/dash", label: "Dash unlocked", spec: { kind: "bool" }, value: false },
    { type: "action", actionId: "reset", label: "Reset discoveries" },
  ] },
] }] };

describe("spatial navigation", () => {
  it("opens map and graph cells through the existing table detail renderer", async () => {
    const restore = stubViewport();
    try {
      renderSpatial(<ViewRenderer node={{ type: "table", title: "Regional overview", columns: [
        { key: "name", label: "Region" }, { key: "map", label: "Map" }, { key: "graph", label: "Graph" },
      ], rows: [{ key: "region", cells: { name: { type: "text", text: "Test region" }, map, graph } }] }} />);
      fireEvent.click(await screen.findByRole("button", { name: "Show Courtyard for Test region" }));
      expect(await screen.findByRole("region", { name: "Courtyard" })).toBeTruthy();
      expect(await screen.findByRole("region", { name: "Skills" })).toBeTruthy();
      expect(screen.getAllByText(/Text alternative/)).toHaveLength(2);
    } finally { restore(); }
  });

  it("jumps from a graph card into a collapsed section and inactive tab without editing", async () => {
    const { context } = renderSpatial(<><ViewRenderer node={graph} /><ViewRenderer node={controls} /></>);
    fireEvent.click(await screen.findByRole("button", { name: /Dash: Available/ }));
    const checkbox = await screen.findByLabelText("Dash unlocked");
    await waitFor(() => expect(document.activeElement).toBe(checkbox));
    expect(context.commit).not.toHaveBeenCalled();
    expect(screen.getByRole("tab", { name: "Skills" }).getAttribute("aria-selected")).toBe("true");
  });

  it("jumps to an action without invoking it from either the diagram or the text alternative", async () => {
    const { context } = renderSpatial(<><ViewRenderer node={graph} /><ViewRenderer node={controls} /></>);
    fireEvent.click(await screen.findByRole("button", { name: "Jump to Respec" }));
    const action = await screen.findByRole("button", { name: "Reset discoveries" });
    await waitFor(() => expect(document.activeElement).toBe(action));
    expect(context.runAction).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: /Respec: Unknown/ }));
    await waitFor(() => expect(document.activeElement).toBe(action));
    expect(context.runAction).not.toHaveBeenCalled();
  });

  it("jumps from a marker list to an existing field", async () => {
    renderSpatial(<><ViewRenderer node={map} /><ViewRenderer node={{ type: "field", valueId: "world/cache", label: "Cache found", spec: { kind: "bool" }, value: false }} /></>);
    fireEvent.click(await screen.findByRole("button", { name: "Jump to Courtyard cache" }));
    await waitFor(() => expect(document.activeElement).toBe(screen.getByLabelText("Cache found")));
  });

  it("opens a lazy section and focuses its virtualised row", async () => {
    const restore = stubViewport();
    const targetMap = { ...map, markers: [{ ...map.markers[0]!, target: { kind: "Row" as const, id: "cache", path: ["section:world"] } }] };
    vi.spyOn(api, "expand").mockResolvedValue([{ type: "table", title: "Locations", columns: [{ key: "name", label: "Name" }], rows: [
      { key: "cache", cells: { name: { type: "text", text: "Cache row" } } },
    ] }]);
    try {
      const { context } = renderSpatial(<><ViewRenderer node={targetMap} /><ViewRenderer node={{ type: "section", title: "World", lazyId: "world", collapsed: true, children: [] }} /></>);
      fireEvent.click(await screen.findByRole("button", { name: "Jump to Courtyard cache" }));
      const selection = await screen.findByRole("checkbox", { name: "Select Cache row" });
      await waitFor(() => expect(document.activeElement).toBe(selection));
      expect(context.commit).not.toHaveBeenCalled();
    } finally { restore(); }
  });

  it("focuses an already-loaded query-table field without searching for an unloaded identity", async () => {
    const linkedGraph: GraphNode = { ...graph, items: [{ ...graph.items[1]!, target: { kind: "Field", id: "count", path: ["row:cache"] } }], edges: [] };
    const query = vi.spyOn(api, "queryCollection").mockResolvedValue({ collectionId: "world", columns: [{ key: "count", label: "Count" }],
      rows: [{ key: "cache", cells: { count: { type: "field", valueId: "count", label: "Cache count", spec: { kind: "integer", min: "0", max: "99" }, value: "1" } } }],
      totalCount: 1, filteredCount: 1, offset: 0, limit: 50, revision: 0 });
    renderSpatial(<><ViewRenderer node={linkedGraph} /><ViewRenderer node={{ type: "queryableTable", collectionId: "world", title: "World", totalCount: 1, columns: [{ key: "count", label: "Count" }] }} /></>);
    const input = await screen.findByRole("textbox", { name: "Cache count (1)" });
    const calls = query.mock.calls.length;
    fireEvent.click(screen.getByRole("button", { name: "Jump to Dash" }));
    await waitFor(() => expect(document.activeElement).toBe(input));
    expect(query).toHaveBeenCalledTimes(calls);
  });
});

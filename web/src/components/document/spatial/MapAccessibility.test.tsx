import { cleanup, fireEvent, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { MapView } from "./MapView";
import { map, renderSpatial } from "./spatialTestUtils";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it.each(["Courtyard cache: Missing, Loot", "Unknown region"])("returns focus to %s after clearing its detail", name => {
  renderSpatial(<MapView node={map} />);
  const marker = screen.getByRole("button", { name });
  marker.focus();
  fireEvent.click(marker);
  const clear = screen.getByRole("button", { name: "Clear selection" });
  clear.focus();
  fireEvent.click(clear);
  expect(screen.queryByRole("complementary")).toBeNull();
  expect(document.activeElement).toBe(marker);
});

it("uses the heading when a connected selection opener cannot receive focus", () => {
  renderSpatial(<MapView node={map} />);
  const marker = screen.getByRole("button", { name: "Unknown region" });
  fireEvent.click(marker);
  const clear = screen.getByRole("button", { name: "Clear selection" });
  clear.focus();
  // jsdom accepts focus in closed details; model the browser refusing that focus attempt.
  vi.spyOn(marker, "focus").mockImplementation(() => {});
  fireEvent.click(clear);
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: map.title }));
});

it("falls back to the map heading when the initiating list button has been paged away", () => {
  renderSpatial(<MapView node={{ ...map, background: undefined, coordinateEmbeddingPermitted: false,
    markers: Array.from({ length: 51 }, (_, index) => ({ ...map.markers[0]!, id: String(index), label: `Location ${index}` })),
  }} />);
  fireEvent.click(screen.getByRole("button", { name: "Location 0" }));
  fireEvent.click(screen.getByRole("button", { name: "Next items" }));
  const clear = screen.getByRole("button", { name: "Clear selection" });
  clear.focus();
  fireEvent.click(clear);
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: map.title }));
});

import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import type { SectionNode } from "../../api/protocol";
import { ViewList } from "./ViewRenderer";
import { viewKeys } from "./viewKeys";

afterEach(cleanup);
const section = (title: string): SectionNode => ({ type: "section", title, collapsed: true, children: [] });

describe("view identities", () => {
  it("keeps an expanded section attached to its identity after sibling insertion and reordering", () => {
    const a = section("Character");
    const b = section("Inventory");
    const { rerender } = render(<ViewList nodes={[a, b]} />);
    const details = screen.getByText("Character").closest("details")!;
    details.open = true;
    fireEvent(details, new Event("toggle"));
    rerender(<ViewList nodes={[section("World"), b, a]} />);
    expect(screen.getByText("Character").closest("details")).toBe(details);
    expect(details.open).toBe(true);
    expect(screen.getByText("World").closest("details")!.open).toBe(false);
    expect(screen.getByText("Inventory").closest("details")!.open).toBe(false);
  });

  it("disambiguates repeated labels and opaque IDs without colliding", () => {
    const repeated = section("Inventory");
    const keys = viewKeys([repeated, repeated, { ...repeated, lazyId: "Inventory:1" },
      { type: "action", actionId: "Inventory", label: "Inventory" }]);
    expect(new Set(keys).size).toBe(keys.length);
    expect(viewKeys([{ ...repeated, lazyId: "stable", title: "Renamed" }]))
      .toEqual(viewKeys([{ ...repeated, lazyId: "stable" }]));
  });
});

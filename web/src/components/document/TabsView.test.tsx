import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import type { TabsNode } from "../../api/protocol";
import { TabsView } from "./TabsView";

afterEach(cleanup);
const node: TabsNode = { type: "tabs", tabs: ["Character", "Inventory", "World"].map(title => ({ title,
  children: [{ type: "text", text: `${title} content` }] })) };

describe("document tabs", () => {
  it("connects tabs and panels and supports wrapped arrow/Home/End navigation", () => {
    render(<TabsView node={node} />);
    const tabs = screen.getAllByRole("tab");
    const assertActive = (index: number) => {
      const tab = tabs[index]!;
      const panel = screen.getByRole("tabpanel", { name: node.tabs[index]!.title });
      expect(panel.id).toBe(tab.getAttribute("aria-controls"));
      expect(panel.getAttribute("aria-labelledby")).toBe(tab.id);
      expect(tabs.filter(tab => tab.tabIndex === 0)).toEqual([tab]);
      expect(tab.getAttribute("aria-selected")).toBe("true");
    };
    assertActive(0);
    tabs[0]!.focus();
    for (const [key, index] of [["ArrowLeft", 2], ["ArrowRight", 0], ["ArrowRight", 1], ["End", 2], ["Home", 0]] as const) {
      fireEvent.keyDown(document.activeElement!, { key });
      assertActive(index);
      expect(document.activeElement).toBe(tabs[index]);
    }
    expect(screen.queryByText("Inventory content")).toBeNull();
  });

  it("retains the selected title when tabs reorder and falls back when it disappears", () => {
    const { rerender } = render(<TabsView node={node} />);
    fireEvent.click(screen.getByRole("tab", { name: "World" }));
    rerender(<TabsView node={{ ...node, tabs: [...node.tabs].reverse() }} />);
    expect(screen.getByRole("tab", { name: "World" }).getAttribute("aria-selected")).toBe("true");
    rerender(<TabsView node={{ ...node, tabs: node.tabs.slice(0, 2) }} />);
    expect(screen.getByRole("tab", { name: "Character" }).getAttribute("aria-selected")).toBe("true");
  });
});

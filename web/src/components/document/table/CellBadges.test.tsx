import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { CellBadges } from "./CellBadges";

afterEach(cleanup);

describe("confidence and diagnostic labels", () => {
  it.each([
    ["Verified", "verified"], ["Inferred", "inferred"], ["Unknown", "meaning unknown"],
  ] as const)("shows %s in text while retaining the accessible meaning", (confidence, meaning) => {
    render(<CellBadges confidence={confidence} />);
    const badge = screen.getByRole("img", { name: `Confidence: ${meaning}` });
    expect(badge.textContent).toContain(confidence);
    expect(badge.title).toBe(`Confidence: ${meaning}`);
  });

  it("keeps warning and error meaning explicit alongside confidence", () => {
    render(<CellBadges confidence="Unknown" warning="Not validated on console" error="Invalid quantity" />);
    expect(screen.getByRole("img", { name: "Warning: Not validated on console" }).textContent).toContain("Warning");
    expect(screen.getByRole("img", { name: "Error: Invalid quantity" }).textContent).toContain("Error");
    expect(screen.getByRole("alert").textContent).toBe("Invalid quantity");
  });

  it("does not claim confidence when the plugin did not publish it", () => {
    const { container } = render(<CellBadges />);
    expect(container.textContent).toBe("");
    expect(screen.queryByRole("img")).toBeNull();
  });
});

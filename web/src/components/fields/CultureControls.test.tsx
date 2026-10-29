import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { FieldControl } from "./FieldControl";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("commits a German decimal keyboard draft as a numeric value", () => {
  const NumberFormat = Intl.NumberFormat;
  vi.spyOn(Intl, "NumberFormat").mockImplementation(function (locale, options) {
    return new NumberFormat(locale ?? "de-AT", options);
  });
  const commit = vi.fn(() => true);
  render(<FieldControl id="float" spec={{ kind: "float", singlePrecision: false }} value={0} onCommit={commit} />);
  const input = screen.getByRole("textbox");
  expect(input.getAttribute("inputmode")).toBe("decimal");
  fireEvent.change(input, { target: { value: "1,5" } });
  fireEvent.blur(input);
  expect(commit).toHaveBeenCalledExactlyOnceWith(1.5);
});

it("searches enum IDs consistently on Turkish browsers", () => {
  const lowerCase = String.prototype.toLocaleLowerCase;
  vi.spyOn(String.prototype, "toLocaleLowerCase").mockImplementation(function (this: string) {
    return lowerCase.call(this, "tr-TR");
  });
  const options = Array.from({ length: 20 }, (_, index) => ({ value: `ID${index}`, label: `Item ${index}` }));
  render(<FieldControl id="enum" spec={{ kind: "enum", options }} value="ID0" onCommit={() => true} />);
  fireEvent.change(screen.getByRole("searchbox"), { target: { value: "id1" } });
  expect(screen.getByRole("option", { name: "Item 1" })).toBeTruthy();
  expect(screen.getByRole("option", { name: "Item 19" })).toBeTruthy();
});

import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import type { DocumentSource } from "../../api/protocol";
import { SourcesList } from "./SourcesPanel";

const source = (overrides: Partial<DocumentSource>): DocumentSource => ({
  packId: "test.items.en",
  packTitle: "Items Game reference data",
  packVersion: "1.0.0",
  origin: "Embedded",
  locale: "en",
  datasetId: "items",
  kind: "Records",
  title: "Item names",
  itemCount: 3,
  used: true,
  attribution: {
    sourceUrl: "https://example.org/wiki/items",
    author: "Example wiki contributors",
    license: "CC-BY-SA-4.0",
    licenseUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
    retrieved: "2026-10-01",
    modifications: "Extracted id and name columns.",
  },
  ...overrides,
});

afterEach(cleanup);

describe("SourcesList", () => {
  it("shows full attribution with licence and source links for datasets in use", () => {
    render(<SourcesList datasets={[source({})]} pluginReferences={[]} />);

    expect(screen.getByText("Item names")).toBeTruthy();
    expect(screen.getByText(/Example wiki contributors/)).toBeTruthy();
    const licence = screen.getByRole("link", { name: "CC-BY-SA-4.0" });
    expect(licence.getAttribute("href")).toBe("https://creativecommons.org/licenses/by-sa/4.0/");
    expect(licence.getAttribute("rel")).toBe("noopener noreferrer");
    expect(screen.getByRole("link", { name: "example.org" }).getAttribute("href")).toBe("https://example.org/wiki/items");
    expect(screen.getByText(/Extracted id and name columns/)).toBeTruthy();
    expect(screen.getByText(/built-in · en · 3 records/)).toBeTruthy();
  });

  it("lists loaded-but-unused datasets separately and explains when nothing is used", () => {
    render(<SourcesList datasets={[source({ used: false, datasetId: "icons", kind: "Assets", title: "Icons" })]} pluginReferences={["https://example.org/tool"]} />);

    expect(screen.getByText(/No reference pack is used here/)).toBeTruthy();
    expect(screen.getByText("Also loaded for this game (1)")).toBeTruthy();
    expect(screen.getByRole("link", { name: "https://example.org/tool" })).toBeTruthy();
  });

  it("never renders non-http URLs as links", () => {
    const evil = source({ attribution: { ...source({}).attribution, licenseUrl: "javascript:alert(1)" } });

    render(<SourcesList datasets={[evil]} pluginReferences={[]} />);

    expect(screen.queryByRole("link", { name: "CC-BY-SA-4.0" })).toBeNull();
    expect(screen.getByText("CC-BY-SA-4.0")).toBeTruthy();
  });
});

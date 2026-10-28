import { cleanup, fireEvent, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ViewRenderer } from "../ViewRenderer";
import { api } from "../../../api/client";
import { map, renderSpatial } from "./spatialTestUtils";
import { ExternalLink, httpsUrl } from "./SpatialShared";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe("map views", () => {
  it("loads only a local F2 image, transforms markers and retains unpositioned text locations", () => {
    const fetch = vi.spyOn(globalThis, "fetch");
    const { container } = renderSpatial(<ViewRenderer node={map} />);
    const image = screen.getByRole("img", { name: "Original courtyard" });
    expect(image.getAttribute("src")).toBe(api.referenceAssetUrl("w1", "d1", "test.spatial:schematic.png"));
    const marker = screen.getByRole("button", { name: "Courtyard cache: Missing, Loot" });
    expect(marker.style.left).toBe("20%");
    expect(parseFloat(marker.style.top)).toBeCloseTo(300 / 7);
    expect(screen.getByRole("list").textContent).toContain("Unknown region");
    expect(screen.getAllByText("Original schematic authors").length).toBeGreaterThan(0);
    expect(fetch).not.toHaveBeenCalled();
    expect(container.querySelector("iframe")).toBeNull();
  });

  it("selects markers with native buttons and exposes details, navigation and attributed external links", () => {
    renderSpatial(<ViewRenderer node={map} />);
    const marker = screen.getByRole("button", { name: "Courtyard cache: Missing, Loot" });
    marker.focus();
    expect(document.activeElement).toBe(marker);
    fireEvent.click(marker);
    expect(marker.getAttribute("aria-pressed")).toBe("true");
    const details = screen.getByRole("complementary", { name: "Selected location: Courtyard cache" });
    expect(within(details).getByRole("button", { name: "Jump to Courtyard cache" })).toBeTruthy();
    const link = within(details).getByRole("link", { name: "Open cache guide (example.org)" });
    expect(link.getAttribute("target")).toBe("_blank");
    expect(link.getAttribute("rel")).toBe("noopener noreferrer");
    expect(details.textContent).toContain("Example community, link only.");
    fireEvent.click(within(details).getByRole("button", { name: "Clear selection" }));
    expect(screen.queryByRole("complementary")).toBeNull();
  });

  it("filters the graphic, list and selection together", () => {
    renderSpatial(<ViewRenderer node={map} />);
    fireEvent.click(screen.getByRole("button", { name: "Courtyard cache: Missing, Loot" }));
    fireEvent.change(screen.getByLabelText("Status"), { target: { value: "Found" } });
    expect(screen.queryByRole("complementary")).toBeNull();
    expect(screen.queryByRole("button", { name: "Courtyard cache: Missing, Loot" })).toBeNull();
    expect(screen.getAllByRole("listitem")).toHaveLength(1);
    fireEvent.change(screen.getByLabelText("Status"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("Category"), { target: { value: "Travel" } });
    expect(screen.getByRole("list").textContent).toContain("East gate");
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "absent" } });
    expect(screen.getByText("No items match the filters.")).toBeTruthy();
  });

  it("renders an external-only region without importing artwork or coordinates", () => {
    const externalOnly = { ...map, background: undefined, coordinateAttribution: undefined, coordinateEmbeddingPermitted: false,
      markers: map.markers.map(marker => ({ ...marker, x: undefined, y: undefined })),
      externalSource: { url: "https://community.example/region", label: "Open community map", attribution: "Community authors; licence unknown, link only." } };
    renderSpatial(<ViewRenderer node={externalOnly} />);
    expect(screen.queryByRole("img")).toBeNull();
    expect(screen.queryByRole("region", { name: "Courtyard diagram" })).toBeNull();
    expect(screen.getByRole("link", { name: "Open community map (community.example)" }).getAttribute("rel")).toBe("noopener noreferrer");
    expect(screen.getAllByRole("listitem")).toHaveLength(3);
  });

  it("refuses remote/unlicensed backgrounds and unlicensed coordinates; missing assets retain text and pins", () => {
    const rendered = renderSpatial(<ViewRenderer node={{ ...map, background: { ...map.background!, assetId: "https://remote.example/map.png" }, coordinateEmbeddingPermitted: false }} />);
    expect(screen.queryByRole("img")).toBeNull();
    expect(screen.getByRole("alert").textContent).toContain("local pack asset");
    expect(screen.queryByRole("button", { name: "Courtyard cache: Missing, Loot" })).toBeNull();
    rendered.unmount();
    renderSpatial(<ViewRenderer node={map} />);
    fireEvent.error(screen.getByRole("img"));
    expect(screen.queryByRole("img")).toBeNull();
    expect(screen.getByText(/Background unavailable. The schematic/)).toBeTruthy();
    expect(screen.getAllByRole("listitem")).toHaveLength(3);
  });

  it("rejects oversized marker lists before rendering", () => {
    renderSpatial(<ViewRenderer node={{ ...map, markers: Array.from({ length: 513 }, () => map.markers[0]!) }} />);
    expect(screen.getByRole("alert").textContent).toContain("limits");
    expect(screen.queryByRole("img")).toBeNull();
  });
});

describe("external link safety", () => {
  it.each(["javascript:alert(1)", "http://example.org", "//example.org", "data:text/html,hello", "https://user:pass@example.org", "https://example.org\\@evil.example", "https://example.org/\npath"])("rejects %s", url => {
    renderSpatial(<ExternalLink link={{ url, label: "Guide", attribution: "Link-only attribution" }} />);
    expect(screen.queryByRole("link")).toBeNull();
    expect(httpsUrl(url)).toBeNull();
    expect(screen.getByText("Link-only attribution")).toBeTruthy();
  });
});

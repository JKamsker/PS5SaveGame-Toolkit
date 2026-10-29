// @vitest-environment node
import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";

const tokens = readFileSync("src/styles/tokens.css", "utf8");
const viewStyles = readFileSync("src/styles/view.css", "utf8") + readFileSync("src/styles/table.css", "utf8");
const uiStyles = readFileSync("src/styles/ui.css", "utf8");

type Palette = Record<string, string>;
const themes = [...tokens.matchAll(/:root\s*\{([^}]+)\}/g)].map(match =>
  Object.fromEntries([...match[1]!.matchAll(/--([\w-]+):\s*(#[\da-f]{6})/gi)].map(value => [value[1]!, value[2]!])),
);
const light = themes[0]!;
const dark: Palette = { ...light, ...themes[1] };

// WCAG relative luminance uses linearised sRGB, not a simple average of colour channels.
function luminance(hex: string): number {
  const channels = [1, 3, 5].map(start => parseInt(hex.slice(start, start + 2), 16) / 255)
    .map(channel => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4);
  return channels[0]! * 0.2126 + channels[1]! * 0.7152 + channels[2]! * 0.0722;
}

function contrast(first: string, second: string): number {
  const a = luminance(first);
  const b = luminance(second);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

function uiColor(selector: string, property: string, palette: Palette): string {
  const escaped = selector.replaceAll(".", "\\.");
  const block = new RegExp(`${escaped}\\s*\\{([^}]+)\\}`).exec(uiStyles)![1]!;
  const value = new RegExp(`(?:^|;)\\s*${property}:\\s*([^;]+)`).exec(block)![1]!;
  const token = /var\(--([\w-]+)\)/.exec(value)?.[1];
  const color = token ? palette[token]! : value.trim();
  return /^#[\da-f]{3}$/i.test(color) ? `#${[...color.slice(1)].map(channel => channel + channel).join("")}` : color;
}

describe.each([["light", light], ["dark", dark]] as const)("%s confidence contrast", (_name, palette) => {
  it.each([".btn__count", ".toast", ".toast--error"])("keeps %s status text readable", selector => {
    expect(contrast(uiColor(selector, "color", palette), uiColor(selector, "background", palette))).toBeGreaterThanOrEqual(4.5);
  });

  it.each(["verified", "inferred", "unknown", "modified", "readonly", "experimental", "stable"])("keeps %s status tags readable", status => {
    expect(contrast(uiColor(`.tag--${status}`, "color", palette), uiColor(".tag", "background", palette))).toBeGreaterThanOrEqual(4.5);
  });

  it("keeps small hints and metadata readable across workspace surfaces", () => {
    for (const surface of ["ground", "surface", "surface-sunk", "modified-soft", "signal-soft", "row-hover"]) {
      expect(contrast(palette["ink-faint"]!, palette[surface]!), `ink-faint on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it.each(["verified", "inferred", "unknown"])("keeps %s text readable on field and table surfaces", confidence => {
    for (const surface of ["surface", "surface-sunk", "modified-soft", "signal-soft", "row-hover"]) {
      expect(contrast(palette[confidence]!, palette[surface]!), `${confidence} on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it.each(["inferred", "unknown", "info", "danger"])("keeps %s badge glyphs readable", confidence => {
    const block = /\.cell-badge\s*\{([^}]+)\}/.exec(viewStyles)![1]!;
    const specific = new RegExp(`\\.cell-badge--${confidence}\\s*\\{([^}]+)\\}`).exec(viewStyles)?.[1] ?? "";
    const foreground = /\bcolor:\s*var\(--([\w-]+)\)/.exec(specific)?.[1]
      ?? /\bcolor:\s*var\(--([\w-]+)\)/.exec(block)![1]!;
    const background = /\bbackground:\s*var\(--([\w-]+)\)/.exec(specific)?.[1] ?? confidence;
    expect(contrast(palette[foreground]!, palette[background]!)).toBeGreaterThanOrEqual(4.5);
  });

  it("keeps the quiet Verified badge readable on its own surface", () => {
    expect(viewStyles).toMatch(/\.cell-badge--verified\s*\{\s*color:\s*var\(--verified\);\s*background:\s*var\(--surface-sunk\)/);
    expect(contrast(palette.verified!, palette["surface-sunk"]!)).toBeGreaterThanOrEqual(4.5);
  });

  it("keeps warning badge glyphs, warning text and notice text readable", () => {
    expect(viewStyles).toMatch(/\.cell-badge--warning\s*\{\s*background:\s*var\(--inferred\)/);
    expect(viewStyles).toMatch(/\.field__warning\s*\{[^}]*color:\s*var\(--inferred\)/);
    expect(uiStyles).toMatch(/\.notice--Warning\s*\{\s*background:\s*var\(--modified-soft\)/);
    expect(contrast(palette.surface!, palette.inferred!)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(palette.ink!, palette["modified-soft"]!)).toBeGreaterThanOrEqual(4.5);
  });
});

it("disables transitions and animations for reduced motion, including pseudo-elements", () => {
  const reducedMotion = /@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{([\s\S]*)\}\s*$/.exec(tokens)?.[1];
  expect(reducedMotion).toMatch(/\*,\s*\*::before,\s*\*::after/);
  expect(reducedMotion).toMatch(/transition-duration:\s*0ms\s*!important/);
  expect(reducedMotion).toMatch(/animation-duration:\s*0ms\s*!important/);
});

// @vitest-environment node
import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";

const tokens = readFileSync("src/styles/tokens.css", "utf8");
const viewStyles = readFileSync("src/styles/view.css", "utf8");

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

describe.each([["light", light], ["dark", dark]] as const)("%s confidence contrast", (_name, palette) => {
  it("keeps small hints and metadata readable across workspace surfaces", () => {
    for (const surface of ["ground", "surface", "surface-sunk", "modified-soft", "signal-soft"]) {
      expect(contrast(palette["ink-faint"]!, palette[surface]!), `ink-faint on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it.each(["verified", "inferred", "unknown"])("keeps %s text readable on field and table surfaces", confidence => {
    for (const surface of ["surface", "surface-sunk", "modified-soft", "signal-soft"]) {
      expect(contrast(palette[confidence]!, palette[surface]!), `${confidence} on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it.each(["inferred", "unknown"])("keeps %s badge glyphs readable", confidence => {
    const block = /\.cell-badge\s*\{([^}]+)\}/.exec(viewStyles)![1]!;
    const foreground = /\bcolor:\s*var\(--([\w-]+)\)/.exec(block)![1]!;
    expect(contrast(palette[foreground]!, palette[confidence]!)).toBeGreaterThanOrEqual(4.5);
  });
});

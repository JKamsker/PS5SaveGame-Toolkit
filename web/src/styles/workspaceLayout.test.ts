import { readFileSync } from "node:fs";
import { afterEach, describe, expect, it } from "vitest";

const css = readFileSync("src/styles/workspace.css", "utf8");

/** jsdom has no layout engine; characterize the CSS constraints, then verify real sizes in-browser. */
function workspaceAt(width: number) {
  const source = document.createElement("style");
  source.textContent = css;
  document.head.append(source);
  const rules = [...source.sheet!.cssRules].flatMap(rule => {
    if (rule instanceof CSSMediaRule) {
      const maximum = /max-width:\s*(\d+)px/.exec(rule.conditionText);
      return maximum && width <= Number(maximum[1]) ? [...rule.cssRules].map(inner => inner.cssText) : [];
    }
    return [rule.cssText];
  });
  source.remove();
  const active = document.createElement("style");
  active.textContent = rules.join("\n");
  document.head.append(active);
  document.body.innerHTML = `<div class="shell"><header class="bar"><a class="bar__brand">Savebench</a>
    <span class="bar__workspace">A long workspace name</span><span class="bar__spacer"></span>
    <div class="bar__actions"><button>Close dump</button><button>Export</button></div></header>
    <nav class="rail"></nav><main class="main"></main></div>`;
  return (selector: string) => getComputedStyle(document.querySelector(selector)!);
}

afterEach(() => { document.head.querySelectorAll("style").forEach(style => style.remove()); document.body.innerHTML = ""; });

describe("workspace reflow constraints", () => {
  it.each([375, 768])("allows the header and main column to shrink and wrap at %ipx", width => {
    const style = workspaceAt(width);
    expect(style(".bar").flexWrap).toBe("wrap");
    expect(parseFloat(style(".bar").minWidth)).toBe(0);
    expect(style(".shell").gridTemplateColumns).toBe("minmax(0, 1fr)");
    expect(style(".bar__actions").flexShrink).toBe("0");
  });

  it.each([1024, 1440, 1920])("retains the desktop rail and a shrinkable editor at %ipx", width => {
    const style = workspaceAt(width);
    expect(style(".shell").gridTemplateAreas).toBe('"bar bar" "rail main"');
    expect(style(".shell").gridTemplateColumns).toContain("minmax(0, 1fr)");
    expect(style(".bar__actions").flexShrink).toBe("0");
  });

  it("bounds the desktop rail and lets document headings wrap within the editor", () => {
    expect(css).toMatch(/grid-template-columns:\s*var\(--rail-width\) minmax\(0, 1fr\)/);
    expect(css).toMatch(/\.editor__head\s*>\s*div:first-child\s*\{[^}]*min-width:\s*0/);
    expect(css).toMatch(/\.editor__title\s*\{[^}]*overflow-wrap:\s*anywhere/);
    expect(css).toMatch(/\.empty\s+p\s*\{[^}]*max-width:\s*var\(--measure-copy\)/);
  });
});

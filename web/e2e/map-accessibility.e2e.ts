import { expect, test } from "@playwright/test";
import type { MapNode } from "../src/api/spatial";
import { importSave, openJson } from "./helpers";

const map: MapNode = { type: "map", title: "Test locations", textAlternative: "A synthetic location list.",
  bounds: { minX: 0, minY: 0, maxX: 100, maxY: 100 }, coordinateSpace: "Unpositioned",
  transform: { scaleX: 1, scaleY: 1, offsetX: 0, offsetY: 0 },
  markers: [{ id: "cache", label: "Test cache", status: "Unknown", category: "Loot" }],
};

test("clearing map details falls back when the opener is inside a collapsed list (#40)", async ({ page }) => {
  // Exercise native details/focus behavior with an authored presentation on a real imported document.
  await page.route(/\/api\/workspaces\/[^/]+\/files\/[^/]+\/open$/, async route => {
    const response = await route.fetch();
    const body = await response.json() as { view: unknown };
    await route.fulfill({ response, json: { ...body, view: { type: "tabs", tabs: [{ title: "JSON", children: [map] }] } } });
  });
  await importSave(page);
  await openJson(page);
  const marker = page.getByRole("button", { name: "Test cache", exact: true });
  await marker.focus();
  await page.keyboard.press("Enter");
  const list = page.getByText("Text alternative (1 item)", { exact: true });
  await list.focus();
  await page.keyboard.press("Enter");
  await expect(marker).not.toBeVisible();
  const clear = page.getByRole("button", { name: "Clear selection", exact: true });
  await clear.focus();
  await page.keyboard.press("Enter");
  await expect(clear).toHaveCount(0);
  await expect(page.getByRole("heading", { name: map.title, exact: true })).toBeFocused();
});

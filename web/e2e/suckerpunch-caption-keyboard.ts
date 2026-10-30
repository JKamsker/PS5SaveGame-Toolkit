import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";
import { captionLabel, editedCaption, isCaptionDocumentRequest } from "./suckerpunch-caption-helpers";

/** Native Tab must reveal the focused target; no focus/scroll call substitutes for reachability. */
async function assertUsableFocus(target: Locator, testInfo: TestInfo, name: string, button = false) {
  await expect(target).toBeFocused();
  await expect(target).toBeVisible();
  const geometry = await target.evaluate(element => {
    const rect = element.getBoundingClientRect();
    const bounds = { left: 0, top: 0, right: innerWidth, bottom: innerHeight };
    const clips: { tag: string; left: number; top: number; right: number; bottom: number }[] = [];
    for (let parent = element.parentElement; parent; parent = parent.parentElement) {
      const style = getComputedStyle(parent);
      const clipX = /(auto|scroll|hidden|clip)/.test(style.overflowX);
      const clipY = /(auto|scroll|hidden|clip)/.test(style.overflowY);
      if (!clipX && !clipY && parent.tagName !== "DIALOG") continue;
      const box = parent.getBoundingClientRect();
      const clip = { tag: parent.tagName, left: box.left + parent.clientLeft, top: box.top + parent.clientTop,
        right: box.left + parent.clientLeft + parent.clientWidth, bottom: box.top + parent.clientTop + parent.clientHeight };
      clips.push(clip);
      if (clipX || parent.tagName === "DIALOG") { bounds.left = Math.max(bounds.left, clip.left); bounds.right = Math.min(bounds.right, clip.right); }
      if (clipY || parent.tagName === "DIALOG") { bounds.top = Math.max(bounds.top, clip.top); bounds.bottom = Math.min(bounds.bottom, clip.bottom); }
      // A modal is in the top layer: document ancestors cannot clip its rendered descendants.
      if (parent.matches(":modal")) break;
    }
    return { rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height }, bounds, clips };
  });
  await testInfo.attach(`${name}-focus.json`, { body: JSON.stringify(geometry, null, 2), contentType: "application/json" });
  expect(geometry.rect.width).toBeGreaterThan(0);
  expect(geometry.rect.height).toBeGreaterThan(0);
  expect(geometry.rect.left).toBeGreaterThanOrEqual(geometry.bounds.left - 1);
  expect(geometry.rect.top).toBeGreaterThanOrEqual(geometry.bounds.top - 1);
  expect(geometry.rect.right).toBeLessThanOrEqual(geometry.bounds.right + 1);
  expect(geometry.rect.bottom).toBeLessThanOrEqual(geometry.bounds.bottom + 1);
  if (button) {
    await expect(target).toBeEnabled();
    // Trial checks hit testing/actionability of this same keyboard-focused control without activating it.
    await target.click({ trial: true });
    await expect(target).toBeFocused();
  }
}

async function tabTo(page: Page, target: Locator, testInfo: TestInfo, name: string, button = false) {
  for (let index = 0; index < 24; index++) {
    if (await target.evaluate(element => element === document.activeElement)) break;
    await page.keyboard.press("Tab");
  }
  await assertUsableFocus(target, testInfo, name, button);
}

async function traversePlanned(page: Page, dialog: Locator, testInfo: TestInfo, width: number) {
  const region = dialog.getByRole("region", { name: "Preview changes", exact: true });
  const table = region.getByRole("table", { name: "Preview changes", exact: true });
  await expect(table).toContainText(captionLabel);
  await expect(table).toContainText(editedCaption);
  await tabTo(page, region, testInfo, `caption-${width}-region`);
  await expect(table).toBeInViewport({ ratio: 1 });
  await expect(table.getByRole("rowheader")).toBeInViewport({ ratio: 1 });
  await page.screenshot({ path: testInfo.outputPath(`caption-planned-region-${width}.png`), fullPage: true });
  const footer = dialog.locator(".dialog__footer");
  for (const name of ["Cancel", "Change parameters", "Apply"]) {
    await tabTo(page, footer.getByRole("button", { name, exact: true }), testInfo, `caption-${width}-${name}`, true);
  }
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  const bounds = await dialog.boundingBox();
  expect(bounds!.x).toBeGreaterThanOrEqual(0);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
  // The screenshot records the actual Apply focus produced by Tab, without moving another target.
  await page.screenshot({ path: testInfo.outputPath(`caption-planned-apply-${width}.png`), fullPage: true });
}

export async function exerciseCaptionKeyboardLayout(page: Page, documentPath: string, width: number, testInfo: TestInfo) {
  const action = page.getByRole("button", { name: /Set save caption \(manual, Experimental\)/ });
  await page.getByRole("tab", { name: "Overview", exact: true }).focus();
  await tabTo(page, action, testInfo, `caption-${width}-opener`, true);
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog", { name: /Preview: Set save caption/ });
  const heading = dialog.getByRole("heading", { name: /Preview: Set save caption/ });
  await expect(heading).toBeFocused();
  const input = dialog.getByRole("textbox", { name: "Caption" });
  await tabTo(page, input, testInfo, `caption-${width}-input`);
  await input.fill(editedCaption);
  await expect(input).toHaveAttribute("aria-invalid", "false");
  const cancel = dialog.getByRole("button", { name: "Cancel", exact: true });
  await tabTo(page, cancel, testInfo, `caption-${width}-parameter-cancel`, true);
  const preview = dialog.getByRole("button", { name: "Preview", exact: true });
  await tabTo(page, preview, testInfo, `caption-${width}-preview`, true);
  const planning = page.waitForResponse(response => response.request().method() === "POST" &&
    isCaptionDocumentRequest(response.url(), documentPath, "/actions/plan"));
  await page.keyboard.press("Enter");
  expect((await planning).ok()).toBe(true);
  await traversePlanned(page, dialog, testInfo, width);
  await page.keyboard.press("Escape");
  await expect(action).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(heading).toBeFocused();
  await tabTo(page, input, testInfo, `caption-${width}-reopen-input`);
  await tabTo(page, cancel, testInfo, `caption-${width}-reopen-cancel`, true);
  await page.keyboard.press("Enter");
  await expect(action).toBeFocused();
}

import { expect, test } from "@playwright/test";
import type { ActionPreview } from "../src/api/actionReview";
import {
  captionCompanionPath, captionPrimaryPath, captionSfoPath, captionValueId, documentSnapshot,
  exportCaptionApi, generateSuckerPunchFixtures, openSuckerPunch, type SuckerPunchFixture,
} from "./suckerpunch-caption-helpers";
import { expectFiles } from "./helpers";

let fixture: SuckerPunchFixture;
test.beforeAll(async ({ browserName }, testInfo) => {
  expect(browserName).toBe("chromium");
  fixture = await generateSuckerPunchFixtures(testInfo);
});

const boundaryCases = [
  { name: "literal-feff", value: "\uFEFF", accepted: true },
  { name: "empty", value: "", accepted: true },
  { name: "exact-31", value: "a".repeat(31), accepted: true },
  { name: "over-32", value: "a".repeat(32), accepted: false },
  { name: "embedded-nul", value: "a\0b", accepted: false },
  { name: "lone-surrogate", value: "\uD800", accepted: false },
  { name: "carriage-return", value: "a\rb", accepted: false },
  { name: "line-feed", value: "a\nb", accepted: false },
] as const;

test("real caption JSON boundary values preserve canonical diagnostics and bytes", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 768, height: 900 });
  for (const boundary of boundaryCases) {
    const documentPath = await openSuckerPunch(page, fixture.files);
    const before = await documentSnapshot(page, documentPath);
    const beforeFiles = await exportCaptionApi(page, documentPath);
    const response = await page.request.post(`${documentPath}/actions/plan`, {
      data: {
        actionId: "metadata/set-save-caption",
        parameters: { caption: boundary.value },
        scope: null,
        revision: before.state.revision,
      },
    });
    expect([200, 422]).toContain(response.status());
    const body = await response.json() as ActionPreview;
    await testInfo.attach(`caption-boundary-${boundary.name}.json`, {
      body: JSON.stringify(body, null, 2), contentType: "application/json",
    });
    if (boundary.accepted) {
      expect(response.status()).toBe(200);
      expect(body.plan.errors).toEqual([]);
      expect(body.plan.skippedRows).toEqual([]);
      expect(body.plan.changes).toHaveLength(1);
      expect(body.plan.changes[0]).toMatchObject({ valueId: captionValueId, oldValue: fixture.caption,
        newValue: boundary.value, oldExists: true, newExists: true, derived: false });
      expect(Buffer.byteLength(boundary.value, "utf8")).toBeLessThanOrEqual(fixture.maxUtf8Bytes);
      if (boundary.name === "literal-feff") expect(body.plan.changes[0]!.newValue).toBe("\uFEFF");
      if (boundary.name === "exact-31") expect(body.plan.changes[0]!.newValue).toBe("a".repeat(31));
    } else {
      expect(body.plan.changes).toEqual([]);
      expect(body.plan.errors.length).toBeGreaterThan(0);
    }
    const after = await documentSnapshot(page, documentPath);
    expect(after.state).toEqual(before.state);
    expectFiles(await exportCaptionApi(page, documentPath), beforeFiles);
    if (boundary.accepted && boundary.name === "literal-feff") {
      expectFiles(beforeFiles, {
        [captionPrimaryPath]: fixture.files[captionPrimaryPath]!,
        [captionSfoPath]: fixture.files[captionSfoPath]!,
        [captionCompanionPath]: fixture.files[captionCompanionPath]!,
      });
    }
  }
});

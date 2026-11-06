import { expect, test } from "@playwright/test";
import type { SemanticChangeLog } from "../src/api/actionReview";
import { apiRequest } from "./apiRequest";
import { onApi, routeApi, waitForApiRequest } from "./apiRoutes";
import { coinsField, editedCoins, importSave, loadJson, openJson } from "./helpers";

const editedPlayer = "Ordered behind the app's queued edit";

/**
 * apiRequest() reads must observe every write the app already queued, not only the writes it already
 * handed to the runtime worker. A document's edits run one after another in the app's own queue: the
 * second is sent only once the first one's answer was handled. A read sent while the first edit is in
 * flight would otherwise run before the second edit and report stale state.
 */
test("a test's API read waits for the edits the app queued behind an edit in flight", async ({ page }) => {
  await importSave(page);
  await openJson(page);
  await loadJson(page);
  const location = new URL(page.url());
  const documentPath = `/api/workspaces/${location.pathname.split("/").at(-1)}/documents/${location.searchParams.get("doc")}`;
  const edits: string[] = [];
  onApi(page, "request", request => { if (request.method() === "POST" && request.url().endsWith("/edits")) edits.push(request.postData() ?? ""); });
  let release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  await routeApi(page, "**/edits", async route => { await held; await route.continue(); }, { times: 1 });

  const first = waitForApiRequest(page, request => request.url().endsWith("/edits"));
  await coinsField(page).fill(editedCoins);
  await coinsField(page).press("Enter");
  await first;
  const player = page.getByRole("textbox", { name: /^player\s*meaning unknown/ });
  await player.fill(editedPlayer);
  // Generic JSON strings use a multiline editor and commit on blur.
  await player.press("Tab");
  // The second edit waits in the app's queue behind the held first one; nothing sent it yet.
  expect(edits).toHaveLength(1);

  const reading = apiRequest(page).get(`${documentPath}/changes`);
  release();
  const log = await (await reading).json() as SemanticChangeLog;
  expect(edits).toHaveLength(2);
  expect(log.changes.map(change => change.currentValue)).toEqual(expect.arrayContaining([
    { kind: "union", type: "number", value: { kind: "exactNumber", text: editedCoins } },
    { kind: "union", type: "string", value: editedPlayer },
  ]));
  expect(log.changes).toHaveLength(2);
  await expect(coinsField(page)).toHaveValue(editedCoins);
  await expect(player).toHaveValue(editedPlayer);
});

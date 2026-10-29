// @vitest-environment node
import { zipSync } from "fflate";
import { afterEach, expect, it, vi } from "vitest";
import { groupIntoGames } from "./library";

afterEach(() => vi.restoreAllMocks());

it.each([
  ["de-DE", ["a", "ä", "z"]],
  ["sv-SE", ["a", "z", "ä"]],
] as const)("intentionally sorts human library names for %s", async (locale, names) => {
  const compare = String.prototype.localeCompare;
  vi.spyOn(String.prototype, "localeCompare").mockImplementation(function (this: string, other) {
    return compare.call(this, other, locale);
  });
  const bytes = zipSync({ "sdimg_SLOT/data.bin": new Uint8Array([1]) });
  const sources = ["z", "ä", "a"].map(name => ({ path: `${name}.zip`, file: new File([bytes], `${name}.zip`) }));
  const { games } = await groupIntoGames(sources);
  expect(games.map(game => game.fallbackName)).toEqual(names);
});

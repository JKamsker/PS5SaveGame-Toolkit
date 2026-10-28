// @vitest-environment node
import { describe, expect, it } from "vitest";
import { groupIntoGames, type ScannedFile } from "./library";

const file = (name: string, size = 4) => new File([new Uint8Array(size)], name.split("/").at(-1)!);
const scanned = (path: string, size?: number): ScannedFile => ({ path, file: file(path, size) });

describe("groupIntoGames", () => {
  it("groups folder dumps by title id and keeps paths from the title folder", async () => {
    const { games, skipped } = await groupIntoGames([
      scanned("dumps/PPSA02530/sdimg_SLOT0/data000.bin", 10),
      scanned("dumps/PPSA02530/sdimg_SLOT0/sce_sys/icon0.png"),
      scanned("dumps/PPSA02530/sdimg_sce_bu_SLOT0/data000.bin", 10),
      scanned("dumps/PPSA02530/sdimg_SLOT1/data001.bin", 10),
      scanned("dumps/ppsa01463/sdimg_profile/profile"),
      scanned("dumps/notes.txt"),
    ]);

    expect(skipped).toBe(1);
    expect(games.map((g) => g.key)).toEqual(["folder:PPSA01463", "folder:PPSA02530"]);
    const pragmata = games[1]!;
    expect(pragmata.saveDirs).toBe(2);
    expect(pragmata.totalBytes).toBe(34);
    expect(pragmata.sources.map((s) => s.path)).toContain("PPSA02530/sdimg_sce_bu_SLOT0/data000.bin");
    expect(pragmata.icon).not.toBeNull();
  });

  it("ignores folders that are not save dumps and zips that are not archives", async () => {
    const { games, skipped } = await groupIntoGames([scanned("PPSA02530/readme.md"), scanned("broken.zip")]);
    expect(games).toEqual([]);
    expect(skipped).toBe(2);
  });
});

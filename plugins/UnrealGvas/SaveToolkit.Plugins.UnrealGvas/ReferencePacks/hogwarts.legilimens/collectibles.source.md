# Hogwarts collectible location catalog

- Source: [Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder, revision 02525dde0fd9bc0be72982478680377dccb41246](https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/tree/02525dde0fd9bc0be72982478680377dccb41246).
- Dataset: `collectibles.json`; table/column associations from `legilimens.py` at the same revision.
- Authors: Malin001 and Legilimens contributors. The upstream credits 100% Guides,
  Game Guides Channel and Lukinator 2321 for the linked location videos.
- License: [MIT](https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/blob/02525dde0fd9bc0be72982478680377dccb41246/LICENSE).
  The complete copyright/permission notice is retained in `LICENSE.txt` beside this file.
- Retrieved: 2026-10-28.
- Modifications: project 699 records into F2 records keyed by the exact original ID;
  create labels from collectible type, region and guide index; retain region/index
  and YouTube timestamp links as metadata. Table/column qualifiers restrict joins.
  Finishing Touch enemy IDs refer to members of `AchievementDynamic.OneOfEach`:
  no CSV membership inference is implemented, so these records do not join a whole cell.
- No icons, thumbnails, video/audio media, game prose, proprietary code or private
  save contents are included. No downloaded program was executed.
- Scope: PC-derived location labels. Exact PS5 saved IDs are matched, but state,
  completion, reward, chronology and console behavior are not inferred from that match.
  Guide links are references only; the plugin performs no network I/O.
- Integrity: the manifest pins SHA-256 of the LF-normalized records file. The
  pack is v1.0.0, English, scoped to `engine.unreal-gvas` / `PPSA01603`.

This dataset remains under MIT; no CC BY-SA data was imported in this round.

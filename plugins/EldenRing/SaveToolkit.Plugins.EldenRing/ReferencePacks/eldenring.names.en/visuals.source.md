# Publisher visuals and text — asset round, 2026-10-29

Source: [EthanShoeDev / elden-ring-compass, be49e96b0513d8e08d22806b5b3fee9654e3e9de](https://github.com/EthanShoeDev/elden-ring-compass/tree/be49e96b0513d8e08d22806b5b3fee9654e3e9de).
Underlying game content © FromSoftware / Bandai Namco. Credit also EthanShoeDev and Compass contributors for extraction,
the master-tile calibration and WorldMapLegacyConvParam facts. No upstream implementation was executed or copied.
The importer is original bounded Python that reads JSON literals and reimplements the documented projection.

Licence: **Rights reserved by FromSoftware / Bandai Namco; used with attribution per owner decision 2026-10-29**.
This records the owner's policy, not a publisher licence grant or MIT relicensing of game content.
ClayAmore identity tables retain MIT; their original source links, hashes and complete notice remain adjacent.
The combined records dataset declares the publisher restriction and explains this distinction in `modifications`.

## Inputs and exact provenance

Every consumed upstream file is pinned by its original-byte SHA-256 in
[`Evidence/visual-inputs.json`](../../../Evidence/visual-inputs.json). Entries with `upstreamPath` use that path;
otherwise `path` is also the upstream path, beneath the pinned repository revision. No changing main-branch URLs are used.
The F2 manifest separately hashes every final JSON/image (JSON hashes use LF-normalized UTF-8).

- Text and icon joins: `packages/data/src/generated/{weapons,goods,talismans,armor,ashes-of-war}.ts`.
  Join **both exact parameter ID and unchanged name** to the existing catalogue, with one reviewed punctuation alias:
  goods 8600 is `Map: Limgrave- West` in ClayAmore and `Map: Limgrave, West` in Compass. Preserve its original name/ID.
  Category and existing `upstreamId`
  identify the original row; category mapping is weapon→weapons, goods→goods, talisman→talismans, armor→armor, ash→ashes-of-war.
  Copy only the `description` paragraphs and `icon` ID, not stats, caps, scaling or equipment eligibility.
- Icons: `packages/data/images/icons/items/<icon>.webp`. Read with Pillow after RIFF/WEBP signature and image validation,
  reduce to at most 64×64 using Lanczos, encode WebP quality 65/method 6, deduplicate final identical bytes.
  Output names use the first 24 hex characters of the final SHA-256; the manifest retains the complete hash.
- Map art: existing nonblank `packages/data/images/map-tiles/{M00,M01,M10}/base/3/<y>/<x>.webp` tiles.
  Stitch at `(256*x,256*y)` on **1312×1312** canvases, preserving transparent margins and all layers' shared origin.
  Native geometry is 10496×10496; zoom 3 is 1/8 native resolution. Re-encode WebP quality 55/method 6.
  The pinned `packages/data/images/map-tiles/manifest.json` and `apps/web/src/lib/map-affine.ts` document geometry/calibration.
- Placement inputs: generated `graces.ts`, `bosses.ts`, `markers.ts`, `world-map-legacy-conv.ts`.
  Graces join `flagId→bonfireEntityId→unique marker.entityId`; bosses use `defeatFlagId` and their own position.
  Output records use exact event IDs, not translated names or nearest-grace guesses. Conflicting event positions,
  ambiguous entity IDs, unsupported layers and out-of-bounds points are omitted.

All image processing runs in `%TEMP%/eldenring-assets-round`; no full-resolution inputs or sample data enter the repository.
Regenerate with `python -I plugins/EldenRing/Evidence/import-visual-assets.py <reviewed-scratch-directory>` (Pillow required).
The importer verifies every input before processing and refuses assets above the 4,000,000-byte total budget.

## Selection and limits

**1,245 illustrated catalogue records and descriptions**: 437 base weapons/shields, 593 common goods/key/crafting/upgrade
items, 115 talismans, 9 starter armour pieces, 91 Ashes of War. The 5,770 identity records remain intact, including empty
names and unused/test rows. No affinity/upgrade ID rounding, inherited icon inference or new DLC identity is introduced.
**1,096 deduplicated icons, three map backgrounds, 597 event-placement records**. Total embedded image bytes:
**2,640,798** (all new binary assets). The machine-readable counts are in `Evidence/visual-summary.json`.

## Coordinate transform and uncertainty

Overworld tile `m60_CC_RR_LT` maps to M00, `m61` to M10. Size is `256*2^T` for tiers 0–2.
World X/Z are tile centre plus local x/z; elevation is ignored. Master pixels are
`X=worldX-7168`, `Y=16640-worldZ`. Legacy dungeons use the nearest source basepoint's `addX/addZ` translation,
then the same affine and the basepoint's declared master layer. These facts come from the pinned calibration source;
its offset derives from a game-map click, and large dungeons use piecewise approximations. **Alignment remains Inferred**.
M11 and absent conversions remain unpositioned. Map-fragment grid fields do not establish pickup coordinates and are not used.

F9 uses identity display transform over bounds `(0,0)–(10496,10496)`. A regional node chooses its most populated layer;
other-layer, missing and ambiguous candidates remain in its complete accessible list and raw-bit table. Missing images retain
the attributed schematic; missing coordinate packs retain the original external/list fallback. No remote image request occurs.
Coordinate provenance and image provenance are separate F2 datasets, both visible in Sources and next to the map.
All saved progress statuses stay **Unknown**; no source bit is promoted to discovery/completion or console validation.

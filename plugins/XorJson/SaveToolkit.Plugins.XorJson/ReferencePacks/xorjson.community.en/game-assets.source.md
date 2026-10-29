# Game icons, descriptions and Overworld reference art

Retrieved 2026-10-29 from dungeons.tools, which identifies its source as the
Minecraft Dungeons II PC 1.1.2.0 / Steam build 25754144 game files.
Game artwork and text: Mojang Studios/Microsoft. Extraction: dungeons.tools.
Conversion/crosswalks: SaveToolkit contributors. Identifier data remains MIT,
Ishiaki and mcd2-save-editor contributors, pinned revision
`84ffee67b73e49d5b888e31257ce9767aa8ab826`; see LICENSE.txt and catalogs.source.md.

Licence: **Rights reserved by Mojang Studios/Microsoft; used with attribution per owner decision 2026-10-29**.
Ownership/provenance notice: https://www.dungeons.tools/privacy (not a licence grant).
The owner's 2026-10-29 asset-round decision is the recorded reuse basis.
It supersedes the earlier publisher-asset exclusions, without changing the
licensing of the original MIT identifiers or community-authored summaries.

## Sources and exact crosswalk

- https://www.dungeons.tools/2/weapons
- https://www.dungeons.tools/2/armor
- https://www.dungeons.tools/2/artifacts
- https://www.dungeons.tools/2/effects
- https://www.dungeons.tools/2/enchantments
- https://www.dungeons.tools/2/map

`game-assets.sources.json` pins the six HTML snapshots, each original icon URL
and SHA-256, record-to-source/asset associations, 48 map-tile URLs/hashes and the
map-client source hash. `pack.json` hashes the processed image bytes and LF
normalized catalog JSON. No HTML or upstream JavaScript is embedded or executed.
The response's Next.js flight JSON was decoded as data, never evaluated.

Every existing catalog record is matched by a unique case-insensitive English
name within its class: ordinary gear, enchantment book, effect, or enchantment.
The class restriction matters: some artifacts and enchantments share names.
No fuzzy match, generated save identifier, new catalog ID or PS5 compatibility
claim is introduced. Enchantment-book item icons use the source's explicit book
icon, while enchantment records use the enchantment icon; identical decoded
images share one local asset. Sources and names remain available for review.

## Image processing and budget

Processing ran in `%TEMP%/xorjson-assets` with Python `-I` and Pillow 12.2.0.
Downloads were treated as image data: PNG or RIFF/WEBP signatures checked,
Pillow format/dimensions checked, and `verify()` called before conversion.
Icons were converted to RGBA and downscaled with Lanczos to at most 64 × 64.
The smaller of lossless WebP (method 6) and PNG-8 (256 colors) was kept.
Identical resized RGBA pixels/dimensions were deduplicated before encoding.

- 418 icon associations: 328 items, 60 effects, 30 enchantments.
- 388 distinct icon files, **784,515 bytes**.
- One Overworld map, **189,492 bytes**, WebP quality 82/method 6.
- Total new image assets: **974,007 bytes**, below the 4,000,000-byte budget.
- 399 official descriptions: 309 items, 60 effects, 30 enchantments.
  Nineteen talismans have no source description and receive no invented prose.

Descriptions append game wording to existing summaries, where present. Effect
game wording is explicitly tier I display prose. Existing behavior summaries,
gear counts, tier observations, identifier source and original revision survive.
Per-record `source` links identify the official text source; shared build,
rights and conversion metadata reside in dataset attribution and this sidecar.
Descriptions add no write limits, serialized strengths or game-aware synthesis.

## Map transform and uncertainty

The inspected map client uses `CRS.Simple`, unprojects native pixel coordinates
at zoom 5, and loads 256-pixel tiles at
`/images/d2/map/overworld/{z}/{x}/{y}.webp`. The Overworld native size is 8192;
its region box is `[0,2832,8088,8192]`. At zoom 3, divide native coordinates by
four. Stitch tile columns 0–7 and rows 2–7, then crop the stitched image at
`[0,708,2022,2048]`, producing **2022 × 1340** pixels. Thus a *known source
pixel* would transform as `displayX = nativeX / 4`,
`displayY = nativeY / 4 - 708`. No rotation or guessed projection is involved.

The saved location IDs have no verified crosswalk to those source pixels and
may refer to Camp/The Sift or other regions. Consequently **no markers are
positioned** and none implies discovery/completion. The background is clearly
reference art; all saved identifiers remain Unknown in the text-list fallback.
The published bounds match the processed image. Missing assets omit the
background. Map attribution explicitly records the unresolved alignment.

## Loading and validation

Core loads and validates this embedded pack once per ReferencePackStore. The
plugin performs no filesystem/network/image-decoder I/O. Documents reuse the
pinned asset objects; query rows create only ImageNode references. The host's
existing pack loader is eager, so startup validation cannot be made lazy within
this plugin. No additional per-document pack cache or loader is introduced.

GameAssetTests verifies all record icons, byte budget, pack hash/type validation,
native picker image/text provenance, actual sample inventory images, asset reuse
across documents, map bounds/provenance, unpositioned and no-pack fallbacks, and
byte-identical serialization. Existing write warnings and Experimental status
remain. Empty PS5 effect arrays still provide no effect/strength verification.

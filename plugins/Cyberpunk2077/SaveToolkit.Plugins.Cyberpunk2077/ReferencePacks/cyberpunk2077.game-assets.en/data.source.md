# Attributed Cyberpunk 2077 art and reference text

Retrieved **2026-10-29**. Exact source revisions, original image SHA-256/SHA-1,
wikitext hashes, tile URLs/hashes and planner chunk SHA-256 are in [upstream.json](upstream.json).
Final normalized JSON/binary hashes are in [pack.json](pack.json).

| Dataset | Source / author / rights | Extraction and limitations |
| --- | --- | --- |
| `item-art`, 146 records / 16 distinct captions | [Cyberpunk Wiki](https://cyberpunk.fandom.com/wiki/Cyberpunk_Wiki), Cyberpunk Wiki contributors / CD PROJEKT RED. Adapted text/ID joins: [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/). | Six common item families and ten cyberware families. Exact page titles, explicit `baseid` fields and in-game infobox captions; Smart Link uses the first effect sentence because its caption is empty. Wiki markup removed; CRC32/UTF-8-length TweakDBID join. Representative family art, not verified instance appearance. No tier stats, limits or write eligibility. |
| `icons`, 16 images | [Cyberpunk Wiki inventory images](https://cyberpunk.fandom.com/wiki/Category:Cyberpunk_2077_images_-_Inventory_Icons), CD PROJEKT RED / Cyberpunk Wiki uploaders. **Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29**. | PNG originals validated, reduced with Lanczos to ≤64×64 RGBA, WebP quality 82/method 6; identical final bytes deduplicated by SHA-256. 27,826 bytes total. Publisher art is not relicensed under the wiki text licence. |
| `perks`, 193 records | [Official CD PROJEKT RED build planner](https://www.cyberpunk.net/en/build-planner), [English localization chunk](https://www.cyberpunk.net/build/8603-b6e6f72c476c734a52a4.js). **Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29**. | Parse the JSON string as data; never evaluate downloaded JavaScript. Exact `Perk-Name-*` / `Perk-Description-*` pairs use modern enum keys. Remove React emphasis placeholders, retain rank-labelled prose. Source build coverage is unknown; not a PS5 2.21 rule database, no prerequisites or writer changes. |
| `map`, one 557,120-byte background | [Piggyback official Night City map](https://maps.piggyback.com/cyberpunk-2077/maps/night-city), Piggyback Interactive Limited / CD PROJEKT RED. **Rights reserved by Piggyback / CDPR; used with attribution per owner decision 2026-10-29**. [Site terms](https://www.piggyback.com/us/terms-of-use/). | 49 public zoom-3 WebP tiles, x/y 0..6, each 256px, assembled at `(x*256,y*256)` with top-left image origin. Encode the 1,792×1,792 image as WebP quality 72/method 6. No geographic projection or saved-ID crosswalk has been verified. All saved markers retain absent X/Y and remain in the accessible text list. |

The Piggyback holder abbreviation in the manifest keeps the licence field within the SDK's
100-character limit; `author` and UI attribution spell out both holders. The owner decision is
the repository's reuse policy, not a grant from either rights holder. Preserve rights notices;
no endorsement is implied. CC BY-SA adaptations retain attribution, modifications and share-alike.

`reference-data/generate-assets.py` is an original offline generator. Supply the reviewed
MediaWiki revision response (`item-pages.json`), image-info response (`image-info.json`),
the PNGs named there, official `perks.js` chunk and `tile-X-Y.webp` inputs in an external
scratch directory. Run `python -I .../generate-assets.py <scratch>`. The generator decodes,
validates and processes images only in scratch, then copies bounded final WebPs into this pack.
Only public downloads were used; no private sample/game resource files are embedded.

**Total new image bytes: 584,946**. Minified JSON contains 146 item descriptions and 193 perk
descriptions. The host validates/hashes and caches the embedded pack once; subsequent documents
share its immutable asset bytes. View cells and map asset resolution are lazy. No plugin I/O.
Missing packs retain baseline/raw IDs, omit images and preserve travel text/status/navigation.

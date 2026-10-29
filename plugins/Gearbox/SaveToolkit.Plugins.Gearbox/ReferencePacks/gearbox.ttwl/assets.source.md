# Wonderlands artwork and card text

Retrieved 2026-10-29. Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29.

Sources:
- [Lothrik (MaXiMiUS), Joshua Seigler and contributors, d57e7aa](https://github.com/Lothrik/wonderlands-skill-calc/tree/d57e7aa68af13afb50d4b54aa0ac5d501655c5b8): 110 skill icons. The community calculator adaptation retains its [MIT notice](../../../Notices/WonderlandsSkills-MIT.txt); publisher art/text has the separate rights notice above.
- [Lootlemon (Levin) and contributors](https://www.lootlemon.com), [terms](https://www.lootlemon.com/terms): 11 curated item thumbnails and verbatim item-card flavor lines. `assets.source.json` lists every exact page/image URL, original byte SHA-256, processed SHA-256, and source-page SHA-256. No community prose was copied from the item guides.

Skill joins reproduce `main.js`'s documented `images/<class>/<normalized-name>.webp` mapping: replace whitespace with underscores, remove non-ASCII-word characters, lowercase. All 110 names resolve at the pinned revision; no guessed save-subobject join. Source HTML/JavaScript was parsed as text, never executed.

Curated gear: Liquid Cooling, Live Wire, White Rider, Gluttony, Masterwork Handbow, Queen's Cry, Swordsplosion, Body Rune, Cursed Wit, Ruby's Spite and Buffmeister. Each matches exactly one existing balance name. Balance `assetId` and matching serial indices reuse the whole-item image. The 45 serial-index joins require the same exact unique-item directory prefix as the balance; no general manufacturer/family parts inherit these images. The UI labels them representative whole-item thumbnails, not individual part appearance or a rendering of saved configuration. Card text has a separate `item-cards` dataset and provenance; no stats or legality rules are inferred.

All downloads and processing used Python `-I` in `%TEMP%/gearbox-assets-20261029`, outside the repository. Pillow validated headers before decoding; PNG/WebP/JPEG/AVIF inputs only, dimensions capped at 8192. LANCZOS downscaling to at most 64×64; WebP quality 82 / method 6 or smaller PNG-8 (128 colors). SHA-derived paths deduplicate identical encoded images: **121 unique images, 148,366 bytes** (110 skills + 11 items). Manifest hashes cover stored image bytes and LF-normalized record files. `assets.source.json` is an attribution sidecar, not a runtime catalogue.

No map art imported: saved mission states have no verified positions or background transform. Map Genie remains a linked guide with a bounded checklist and no invented alignment. Missing coordinate evidence, not a licence refusal, limits map backgrounds here.

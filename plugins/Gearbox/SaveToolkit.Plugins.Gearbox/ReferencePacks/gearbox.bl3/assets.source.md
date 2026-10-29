# BL3 artwork and card text

Retrieved 2026-10-29. Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29.

Sources:
- [Joshua Seigler / BL3Skills and contributors, 66b0bd0](https://github.com/seigler/bl3skills.com/tree/66b0bd08777452abd05e5d9246c27f2a8096ca13): 315 skill-image assignments. The community calculator adaptation retains its [MIT notice](../../../Notices/Bl3Skills-MIT.txt); publisher art/text has the separate rights notice above.
- [Lootlemon (Levin) and contributors](https://www.lootlemon.com), [terms](https://www.lootlemon.com/terms): 12 curated item thumbnails and verbatim item-card flavor lines. `assets.source.json` lists every exact page/image URL, original byte SHA-256, processed SHA-256, and source-page SHA-256. No community prose was copied from the item guides.

Skill joins reproduce the pinned renderer's explicit `src/components/VaultHunter/index.js` mapping: class / zero-based tree index / tier index / skill index selects `src/assets/hunters/<Class>/<tree><tier><skill>.png`. Source object/property order was read as text, never executed. It joins named calculator entries only, never opaque saved allocations.

Item names match one exact existing balance name (including the pinned `Superconducting Plasma Coil` label). No fuzzy joins. Curated gear: Hellwalker, The Monarch, Light Show, Plasma Coil, The Butcher, The Transformer, Stop-Gap, Re-Volter, Free Radical, Flipper, Unkempt Harold and Soulrender. Balance `assetId` and matching serial indices reuse the whole-item image. The 42 serial-index joins require the same exact unique-item directory prefix as the balance; no general manufacturer/family parts inherit these images. The UI explicitly says these are representative whole-item thumbnails, not individual part appearance or a rendering of the saved configuration. Card text has its own `item-cards` dataset and provenance; no stats or legality rules are inferred from it.

All downloads and image processing used Python `-I` in `%TEMP%/gearbox-assets-20261029`, outside the repository. Headers were verified with Pillow before decode; accepted inputs PNG/WebP/JPEG/AVIF only, dimensions capped at 8192. Images were resized with LANCZOS to at most 64×64, then encoded as WebP quality 82 / method 6 or PNG-8 (128 colors), whichever was smaller. Identical encoded images share one SHA-derived path. One duplicate skill image was deduplicated: **326 unique images, 723,237 bytes** (314 unique skill images + 12 item images). Manifest hashes cover stored image bytes and LF-normalized record files. `assets.source.json` is an attribution sidecar, not a runtime catalogue.

No map art imported: stored mission statuses have no verified native positions or background transform. Map Genie remains a linked guide with a bounded mission list and no invented marker alignment. This is a missing coordinate-evidence limitation, not a licence refusal. Map art is not required for the current checklist.

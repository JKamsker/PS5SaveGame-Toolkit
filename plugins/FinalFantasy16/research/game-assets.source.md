# Curated game media — 2026-10-29

Rights holder: **Square Enix** (Final Fantasy XVI game art and game text).
Source provider: **Game8**, including screenshot/guide annotations. This is not
a claim that the assets have a community software licence.

Licence notice: **Rights reserved by Square Enix and Game8; used with attribution
per owner decision 2026-10-29**. Official Valisthea art has the corresponding
Square Enix-only notice. Existing Final Fantasy Wiki adaptations remain CC BY-SA
3.0, and Nenkai identifiers retain their separate 2026-10-28 owner-decision notice.

The [machine-readable ledger](game-assets.json) pins each original image URL,
individual item/ability article, input SHA-256, processed SHA-256, dimensions and
byte size. Retrieval: 2026-10-29. Only exact normalized name matches (Unicode
apostrophes/whitespace, and the documented `Will o' the Wykes` spelling) were used.
There is no base-to-upgrade icon reuse or invented identity. Item selection is
restricted to observed main-inventory IDs plus the seven common consumables.

Selected Game8 indexes:

- [Consumables](https://game8.co/games/Final-Fantasy-XVI/archives/415576)
- [Accessories](https://game8.co/games/Final-Fantasy-XVI/archives/414895)
- [Materials](https://game8.co/games/Final-Fantasy-XVI/archives/415577)
- [Weapons](https://game8.co/games/Final-Fantasy-XVI/archives/409998)
- [Armor](https://game8.co/games/Final-Fantasy-XVI/archives/416024)
- [Abilities](https://game8.co/games/Final-Fantasy-XVI/archives/409930)
- [Regional maps](https://game8.co/games/Final-Fantasy-XVI/archives/410003)

World artwork: [Square Enix's official story page](https://na.finalfantasyxvi.com/story),
`https://na.finalfantasyxvi.com/_next/static/media/realms_background.3465cfcf.jpg`.
Direct Fandom page/API/CDN requests returned `HTTP Error 403: Forbidden`; this is
an access limitation, not a continuing licence refusal. The external wiki
location reference and previous CC BY-SA citations are retained.

## Processing and storage

Downloads were handled only as untrusted image/text data in `%TEMP%/ffxvi-assets-round`,
outside the repository, using Python with `-I`. No downloaded code was executed.
Image signatures were checked for PNG/JPEG/WebP, then Pillow `verify()` checked
the container before decoding. Downloads were capped at 12,000,000 bytes and
Pillow's decompression-bomb warning was treated as an error (40 million pixels).

Pillow 12.2.0 resized with LANCZOS, preserving aspect ratio, with an upper bound
of 64×64 for icons and 2048×2048 for maps. WebP settings: quality 86 for icons,
quality 78 for maps, method 6, `exact=True`; no original metadata retained.
Processed image hashes were deduplicated. All 140 selected icons and five maps
are unique. A contact sheet and map thumbnails were visually inspected in scratch.

The **145 new image files total 398,116 bytes**: 260,306 icon bytes and 137,810
map bytes. Largest map: 75,802 bytes. No image exceeds 64 pixels per icon edge or
1920 pixels per map edge; all maps are below 600,000 bytes. The 140 descriptions
are short excerpts of the source's **Description** table, at most 24 words per
independent article, with `…` marking truncation. Effect tables, caps and acquisition
claims were not imported. No upgraded-record descriptions were synthesized.

`apply_game_assets.py` decorates the original generated catalogues, validates
committed image hashes/lengths, writes minified JSON, adds asset provenance and
updates F2 manifest hashes. It is offline and idempotent. After regenerating the
base published/ability catalogues, rerun:

```powershell
python -I plugins/FinalFantasy16/research/apply_game_assets.py
```

## Runtime and evidence limits

Only assembly resources are embedded. Core's shared reference-pack store loads,
validates and caches bytes once; repeated asset resolution returns the same
`AssetContent`. The plugin performs no filesystem/network I/O. Ability rows and
the four regional map views retain lazy expansion, and missing reference contexts
keep text-only views. No publisher media is relabelled CC BY-SA or MIT.

Every icon is attached to its record's `assetId` in the same F2 pack. Inventory
and ability tables use `ImageNode`; the Sources panel attributes the asset datasets.
Record descriptions credit their exact Game8 source, Square Enix, date and owner
policy. Maps resolve local F2 assets/provenance and explicitly permit embedding.
World markers are **unpositioned**: save-to-art alignment is Unknown. Baked-in
regional obelisk symbols are reference artwork, not saved discovery/completion.
No save byte, write rule, ownership flag or saved skill-index relationship is added.

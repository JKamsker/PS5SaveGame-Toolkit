# Source and modifications

Copyright (c) 2026 Ishiaki. MIT licence: see LICENSE.txt in this folder.

Source: Ishiaki and mcd2-save-editor contributors,
https://github.com/IshiakiZ/mcd2-save-editor/tree/84ffee67b73e49d5b888e31257ce9767aa8ab826
Retrieved: 2026-10-28. Revision: 84ffee67b73e49d5b888e31257ce9767aa8ab826.

- `items.json`: extracted base and unique ID/name pairs, category (`kind`) and
  optional slot from upstream `dungeons2_editor/data/items.json`; deduplicated by
  exact ID, preserving the final record in original catalog order. 328 records.
- `effects.json`: extracted `effect` → ID, `name`, `category` from upstream
  `dungeons2_editor/data/effects.json`. 60 records.
- `world.json`: keys/values identifying quests, stations, doors, areas, regions,
  achievements and cutscenes from upstream `dungeons2_editor/data/world.json`.
  Labels remove initial `SW.`, replace periods with ` / `, and separate a
  lowercase letter followed by an uppercase letter. Deduplicated by ID.
  420 records. These labels are not official localized place or quest names.

Every record links to its upstream dataset and records the pinned revision and
Inferred PS5 applicability. The pack is not build-compatible evidence; its
build-independent manifest means advisory text can be shown with the warning.

No scripts from the upstream project were executed. No tooltip descriptions,
quest prose, icons, numeric limits, effect tiers/strengths, coordinates or game
assets were copied. Upstream credits MetaBot and community/player-read game
data; those upstream research links are not independently licensed description
or artwork sources. Only the MIT community catalog's curated facts/short labels
are embedded. The complete upstream MIT notice is preserved alongside the data.

Per-dataset attribution and normalized-file SHA-256 hashes are in `pack.json`.
Source links are also in plugin Info.References, README, and displayed field
provenance. The F2 Sources panel exposes licence/author/modification metadata.

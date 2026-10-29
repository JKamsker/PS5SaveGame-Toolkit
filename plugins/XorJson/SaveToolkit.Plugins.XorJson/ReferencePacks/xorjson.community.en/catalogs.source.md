# Source and modifications

This records the original MIT identifier conversion. Pack 1.4.0 adds game icons
and official descriptions with separate publisher attribution; see
game-assets.source.md. The MIT notice here continues to cover identifier data.

Copyright (c) 2026 Ishiaki. MIT licence: see LICENSE.txt in this folder.

Source: Ishiaki and mcd2-save-editor contributors,
https://github.com/IshiakiZ/mcd2-save-editor/tree/84ffee67b73e49d5b888e31257ce9767aa8ab826
Retrieved: 2026-10-28. Revision: 84ffee67b73e49d5b888e31257ce9767aa8ab826.

- `items.json`: extracted base and unique ID/name pairs, category (`kind`) and
  optional slot from upstream `dungeons2_editor/data/items.json`; deduplicated by
  exact ID, preserving the final record in original catalog order. 328 records.
- `effects.json`: extracted `effect` → ID, `name`, `category` from upstream
  `dungeons2_editor/data/effects.json`. 60 records.
- `enchantments.json`: extracted `effect` → ID and `name` from the same
  pinned `effects.json`'s `enchantments` table; category is Enchantment.
  30 records. No strength, tier, enchantment points or limits were extracted.
- `world.json`: keys/values identifying quests, stations, doors, areas, regions,
  achievements and cutscenes from upstream `dungeons2_editor/data/world.json`.
  Labels remove initial `SW.`, replace periods with ` / `, and separate a
  lowercase letter followed by an uppercase letter. Deduplicated by ID.
  420 original records. Round 2 adds all 926 distinct task IDs from `quests`
  and `labels`, uses all 42 short quest names from `names`, and all 502 short
  task labels from `labels`. A task's parent quest is retained when present in
  the `quests` table. Total: 1,346 records. Labels without an upstream short
  name keep the expanded/raw identifier. These are advisory PC labels;
  applicability to PS5 localization and behavior is unverified.

Every record links to its upstream dataset and records the pinned revision and
Inferred PS5 applicability. The pack is not build-compatible evidence; its
build-independent manifest means advisory text can be shown with the warning.

No scripts from the upstream project were executed. This MIT identifier conversion
copied no verbatim tooltip descriptions, long quest prose, icons, serialized
effect strengths, coordinates or game assets. Pack 1.4.0's separate game-asset
import is documented in `game-assets.source.md`. Upstream credits MetaBot and
community/player-read game data; the
MIT notice covers the curated identifiers and short labels, not proprietary
artwork or game prose. The complete upstream MIT notice remains alongside the data.

Round-2 conversion verified exact set equality for all pinned base/unique item
IDs and retained every pinned quest name, task label and task ID. Downloads and
the authored conversion script stayed under the system temporary directory;
the generated data is validated by the F2 manifest hashes and conformance test.
Pack version 1.3.0 retains the upstream revision above. No CC BY-SA wiki content
is included: direct page/licence retrieval returned HTTP 403 on 2026-10-28.

Per-dataset attribution and normalized-file SHA-256 hashes are in `pack.json`.
Source links are also in plugin Info.References, README, and displayed field
provenance. The F2 Sources panel exposes licence/author/modification metadata.


## Completion additions

`attributes.json` (9 records) and `rarities.json` (5 records) are derived from the
same pinned MIT `hero.py` mapping. Attribute bounds are PC community observations,
marked Inferred and used to validate F6 attribute plans; raw scalar edits do not
inherit them, and they are never presented as proven PS5 legal limits.
`world.json` retains only quest follows relationships as prerequisite metadata;
coordinates, rewards and artwork are excluded from that identifier dataset.

## Unlicensed-source additions (owner decision 2026-10-28)

`effects.json` now supplements all 60 MIT identifiers with original descriptions
of factual behavior, compatible gear counts and tier display percentages from
dungeons.tools. See [effect-notes.source.md](effect-notes.source.md) for the exact
snapshot hash, author, licence status, extraction and provenance boundary.
Pack 1.3.0's effect dataset attributed both sources and recorded the unlicensed
factual supplement in its licence field. Pack 1.4.0's combined attribution now
records publisher rights as well; this sidecar and `LICENSE.txt` preserve the MIT
terms for the identifiers. No effect values became write validators.

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

No scripts from the upstream project were executed. No tooltip descriptions,
long quest prose, icons, numeric limits, effect tiers/strengths, coordinates or game
assets were copied. Upstream credits MetaBot and community/player-read game
data; those upstream research links are not independently licensed description
or artwork sources. Only the MIT community catalog's curated facts/short labels
are embedded. The complete upstream MIT notice is preserved alongside the data.

Round-2 conversion verified exact set equality for all pinned base/unique item
IDs and retained every pinned quest name, task label and task ID. Downloads and
the authored conversion script stayed under the system temporary directory;
the generated data is validated by the F2 manifest hashes and conformance test.
Pack version 1.2.0 retains the upstream revision above. No CC BY-SA wiki content
is included: direct page/licence retrieval returned HTTP 403 on 2026-10-28.

Per-dataset attribution and normalized-file SHA-256 hashes are in `pack.json`.
Source links are also in plugin Info.References, README, and displayed field
provenance. The F2 Sources panel exposes licence/author/modification metadata.


## Completion additions

`attributes.json` and `rarities.json` are derived from the same pinned MIT `hero.py` mapping. Attribute bounds are conservative PC community observations, marked Inferred and never presented as PS5 legal limits. `world.json` retains only quest follows relationships as prerequisite metadata; coordinates, rewards and artwork are excluded.

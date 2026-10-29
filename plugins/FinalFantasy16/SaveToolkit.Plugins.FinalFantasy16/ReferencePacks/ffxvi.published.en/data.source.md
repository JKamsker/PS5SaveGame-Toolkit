# Published community identifiers — F2 pack

Author/source: [Nenkai and ffxvi-modding contributors](https://github.com/Nenkai/ffxvi-modding/tree/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids),
revision `4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03`, retrieved 2026-10-29.
Licence: **No licence published; reused with attribution per owner decision 2026-10-28**.
The `licenseUrl` in each manifest attribution links to the pinned repository,
not a nonexistent licence file. No MIT/CC BY-SA grant is asserted for this pack.

The [reviewed CSV sources and per-file hashes](../../../research/published-ids/data.source.md)
are retained separately from existing wiki/MIT packs. The plugin-owned converter
`python -I plugins/FinalFantasy16/research/build_published_catalog.py` is offline
and checks pinned LF-normalized source hashes before generating JSON and F2 hashes.

| Catalogue | Source | Records / modifications |
| --- | --- | --- |
| `ffxvi_published_items` | `item_ids.csv`, `equip_item_ids.csv` | 879 named IDs: 353 general items, 526 equipment. Omitted 31 unnamed placeholders. |
| `ffxvi_published_maps` | `gamemap_ids.csv` | 98 named IDs from 110 named rows. Omitted 40 unnamed rows; grouped repeated IDs without discarding variant paths/notes. |
| `ffxvi_published_bnpc` | `bnpc_ids.csv` | 1,758 battle-NPC reference identifiers. Speaker-linked subset, not all NPCs. |
| `ffxvi_published_enpc` | `enpc_ids.csv` | 1,085 event-NPC reference identifiers. Speaker-linked subset, not all NPCs. |

The original dataset imports published identifier/name facts and map variant path/note facts.
Documented italic/color display tags are removed for plain-text names; original
tagged names remain in record attributes. Original descriptions are mapping warnings;
the asset round appends separately attributed short official-description excerpts.
Categories are an **Inferred**, plugin-authored taxonomy from the published ID
bands: resources 1–3; consumables 5–11; materials 21–499 and 1001–1199; curiosities
501–599 and 705–799; orchestrion rolls 604–699; quest items 1203–1352; weapons
100001–100199; belts 100201–100299; vambraces 100301–100399; accessories
100401–100622. Only named rows in these bands are included. The converter rejects
unreviewed bands rather than guessing. These broad reference categories do not
establish slot eligibility, gameplay behavior or ownership.

`ffxvi_published_items` feeds inventory/equipment/shortcut F4 pickers and F8 labels,
search and categories. The earlier `ffxvi_items` wiki pack remains separately
attributed; matching records retain its additional provenance in the view.
The Published references tab provides read-only map/NPC F4 browsers. Existing
MIT map annotations continue to drive the location map and candidate stored map
field; imported map variants do not establish a unique current asset or sky.

The sample-backed audit resolves all 41 previously unnamed observed main IDs,
for **172/172 retail and 2/2 demo**. Numeric IDs match actual sample bytes; the
name joins remain **Inferred**. No controlled name observation or console edit
acceptance is claimed. Build coverage, learned/unlocked states, NPC save joins,
legal caps and dependencies remain unknown. Zero, 900000 and unmapped IDs keep
numeric fallbacks. No coordinates, saves or upstream code are included. The asset
round adds 92 separately attributed item icons and official-description excerpts:
see [media.source.md](media.source.md). Their rights are separate from the original
identifier facts. After rebuilding the base pack, run `research/apply_game_assets.py`.
All existing write ranges and console warnings remain in force.

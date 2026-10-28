# Hogwarts Legacy inspection and write gates

Applies to the supplied PPSA01603 snapshots: UE 4.27.2, package 524,
72 custom versions, `/Script/PersistentData.PersistentGameData` and the two
database array signatures. Other schemas retain the raw GVAS inspector. All
PPSA01603 files, including list/settings metadata, remain read-only.

## Decode and views

`HogwartsImage` validates the GVAS byte-array count and walks independent UE frames:
six little-endian u64s `(0x9E2A83C1, 0x20000, compressed, decoded, compressed, decoded)`.
It checks both size pairs, exact input consumption, per-chunk bounds and a 32 MiB
decoded-image cap. The shared `OodleCodec` handles Kraken; no local decoder exists.
Decoded bytes begin with int32 `(databaseLength + 4, databaseLength)`, followed by
the exact SQLite image, with no silently discarded trailer.

Each image opens independently through shared `SqliteImage`, with native SQLite
deserialize, memory-only storage, read-only parameterized queries and integrity
checking. No file handles, temp databases or raw connections escape into this plugin.
At most 256 tables and 64 columns per table are accepted. The shared SQL VM,
result/value and image limits still apply. Unsupported/corrupt images lose their
overlay with a visible warning, while the unaffected image and raw original remain
available. No failed overlay makes the original editable.

Both images have separate Character, Inventory, Progression and Collections F8
tables. All other tables appear under lazy Database tables sections. Every query
supports raw text search, exact column filters and deterministic sorting with a
rowid tie-break. Pages contain at most 200 rows; long rendered cells are shortened
with a hash while original values stay unchanged. Search uses full raw SQLite
values, not friendly catalog captions. Null is distinct from empty text and the
literal string `NULL`; blobs show size/hash, not an invented interpretation.

Inventory preserves `CharacterID / HolderID / SlotNumber`, item ID, variation,
count and flags. Gear instances and equipped slots are separate tables; no guessed
gear join or currency-stack choice is applied. `Knuts` is searchable by exact
`ItemID`. Miscellaneous character/XP values retain their owner/name/value tuple;
names, capacities, curve parameters, perks and rewards do not become editable.
Collection and mission flags retain raw state values. A row is not reported as a
completed or rewarded objective merely because its name or flag resembles one.

F4 fields use the F2 catalog only when the raw ID **and** catalog table/column
qualifiers match. Every resolved field has attribution and Inferred confidence;
unknown IDs retain raw text. Labels identify type, region and guide index, not
invented item names. Missing catalogs affect only labels. Read-only F4 fields
resolve names/candidates through the host; the current UI disables their picker
button along with editing.

The Spatial tab is intentionally link-first. It projects the 699 pinned catalogue
records into 25 region markers plus one unjoined fast-travel domain, with original
schematic coordinates and a text alternative. A companion GraphNode groups the same
records by region and category; its edges are catalogue grouping only, never saved
prerequisites or completion dependencies. Marker and graph states remain Unknown,
external map art is not embedded, and every external link carries its source and
licensing note. `SpatialViewValidation` is run by `HogwartsSpatialViewTests`.

## Reproducible sample evidence

`HogwartsSampleTests` validates all 15 distinct HL0000–HL0014 gameplay payloads:
30 database images, each with 84 tables, successful native SQLite integrity checks,
byte-identical database serialize/reopen and original GVAS no-op serialization.
HL0010 images have 4,534,272 and 4,231,168 SQLite bytes (the codec handoff's decoded
lengths include the eight-byte prefix). The database images are not assumed mirrors.

`HogwartsSaveDecoder` is an F1 `ISaveDecoder` hook in the test project, with ID
`unrealgvas.hogwarts.rows`. It emits table schema/count evidence and SHA-256 hashes
of full typed rows, aligned by confirmed unique tuples. CollectionDynamic and
MissionDynamic have duplicate candidate tuples in both images; their rows use
content-hash multisets with multiplicity, explicitly avoiding invented row
correspondence. Other unmodeled tables receive schema/count coverage only.
F8 may use rowid within the unchanged document; F1 never equates those rowids across
snapshots. Raw row contents and character/account identifiers are not published.

The supplied archive has importer generation **0** for all 15 slots. There are no
same-path before/after generations to compare. The test compares all 14 adjacent
slot-name pairs as uncontrolled snapshots; slot naming does not prove chronology
or any particular in-game event. No churn masks are invented without a control pair.

Selected aggregate row differences over those 14 comparisons, both images combined:

| Table | Row changes / multiset entry changes |
| --- | ---: |
| InventoryDynamic | 4,942 |
| GearItemsDynamic | 794 |
| EquippedGearDynamic | 29 |
| AchievementDynamic | 770 |
| PerkDynamic | 54 |
| CollectionDynamic | 860 |
| MapLocationDataDynamic | 830 |
| SphinxPuzzleDynamic | 156 |

For HL0010 → HL0011, the complete F1 tree reports 6 added, 2 removed and 24 changed
values, including table-count values. This is pinned in the regression test, with
neither truncated input nor guessed causal meaning. The test writes a counts-only
summary to `%TEMP%/hogwarts-f1-summary.json`; it never exports save bytes or cells.

## Write investigation and owner checklist

Existing integer, real and text cells in a complete pair are writable. Identity
columns, blobs, catalog reference cells, metadata files and malformed pairs remain
read-only. The write path uses the shared codec in this order: SQLite `Serialize()`
and `PRAGMA integrity_check`, stored Kraken blocks, then a size-adjusted GVAS
replacement. The separate test-only `HogwartsWriteProbe` remains useful as a framing
oracle, but UI writes now use the production `GvasFile.SerializeReplacements` path.

`HogwartsWriteResearchTests` proves on HL0010 that both no-op SQLite images serialize
byte-identically, stored frames can rebuild both arrays, a fresh GVAS parse and a
separate frame walker re-decode those arrays, and native SQLite passes integrity
checks after reopen. The walker still uses the same shared Kraken implementation;
no independent proprietary Oodle decoder has been run. Synthetic coverage also
checks multi-chunk stored output and malformed framing/prefixes/query bounds.

HL0010 grows from **2,073,197 to 8,787,444 bytes** with stored frames. The plugin
rejects edited output above its conservative **16 MiB** slot budget. This is a safety
bound, not a claim about the PS5 allocation, and it establishes neither slot quota
acceptance nor console loading. The other samples retain their original compressed
arrays until an edit is made.

Before relying on any write, the owner must provide/validate:

- [ ] Unchanged load/re-save control pair from the same spare slot/build, preserving
  both images and SaveGameList plus every companion. Record region, build and DLC.
- [ ] Separate before/after pairs for buying one item, selling one item, earning
  and spending an exact currency amount, using one potion, crossing one XP/level
  threshold, spending one talent point, equipping one gear instance and collecting
  one identified collectible. Record displayed quantities/XP/slots and the exact
  action. Retain primary and backup independently; do not overwrite old captures.
- [ ] Determine which image/rows change for each event and whether list metadata,
  rewards or dependent rows must also change. Include relevant houses and ownership
  states. Do not apply a blanket update-both-images policy.
- [ ] Establish effective limits and the maximum accepted stored-frame output/save
  allocation on PS5, especially the measured 8.79 MB HL0010 reconstruction.
- [ ] On a backed-up spare slot, first validate an unchanged reconstruction, then
  each single approved edit: import, load, check the intended value and side effects,
  save in game, exit, reload, verify again, export and run integrity/re-decode checks.
  Record build/slot/results and restore the backup if anything differs unexpectedly.
- [ ] Retain a byte-identical unmodified path, passing `PRAGMA integrity_check`, and
  independently re-decodable output for every write-capable schema. Validate stored
  streams with another decoder when available. Only the owner may mark Stable.

No quest-complete, reward, trophy, entitlement, absent-item creation, identity-column
or bulk-write action is enabled. Existing-value writes are Experimental and remain
gated for confidence and console acceptance by the evidence above.

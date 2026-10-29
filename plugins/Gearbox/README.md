# Gearbox (`gearbox`)

**Experimental** editing for decrypted PS5 **Borderlands 3 (PPSA01463)** and **Tiny Tina's Wonderlands
(PPSA01493)**. **PPSA01494 / Borderlands 4 is excluded.** Console-layer encryption must already have
been removed. Edited files have not been loaded on a console.

## Features and confidence

| Area | Support | Evidence / limits |
| --- | --- | --- |
| Character | Editable name; read-only class asset paths and Wonderlands primary/secondary branches | Schema names and sample strings; attributed class joins are Inferred, unmatched paths Unknown |
| Level / XP | Editable; BL3 levels 1–72, Wonderlands 1–40; existing recorded-level stat updated | Upstream exact XP tables; all sample XP/stat values agree |
| Skills | Editable unspent points/tree grade; F8 stored allocations and 425 named reference skills; F9 allocation/calculator graphs | Stored paths get bounded class/tree joins from pinned community prefixes; exact saved-skill names remain Unknown |
| Currency | Money, eridium / moon orbs | Documented hashes `618814354` / `3679636065` match all character samples |
| Keys | Golden / skeleton keys; create absent category only on a nonzero edit | Published path/hash algorithm; `2420693941`; all supplied profiles omit this record |
| Ammo | Editable finite resource-pool amounts | Schema floats and full resource paths; game capacity may clamp them |
| SDUs | Edit existing known purchased tiers | Pinned upstream tier bounds; conservative capacity policy rejects overflowing reductions |
| World | Edit existing Mayhem/Chaos levels and Chaos unlocked ceiling; F8 missions and F9 external-map checklist | BL3 0–11, TTWL 0–100 editor bounds; unlock challenges/rewards remain unchanged; no coordinates/art imported |
| Inventory | Named, queryable backpack/bank/lost loot; lazy serial/part details, original codes, level, CRC/layout status, raw flags/pickup/equipment indices | Attributed family labels plus 499 community localized balance labels and factual part descriptions; generated effects/legal combinations remain unknown |
| Reference browser | F2 title-scoped packs, F4 inspection and constrained existing-part replacement, F8 catalogues | Offline, versioned zlib/MIT resources, community facts and publisher assets with separate attribution; raw fallback; inspection makes no save changes |
| Profile | Planned bank/lost-loot operations; existing Guardian/Myth edits inside F7 | Unknown mirror precedence and budgets are explicitly warned, never inferred |
| Coordinated group | Explicit character + profile selection from one title/imported generation/backup status; bank, progression and account mirrors in one writable transaction | F7 ownership/fingerprint/conflict handling; separate PS5 save directories supported; generation numbers alone do not establish gameplay compatibility |
| Account / character mirrors | Separate BL3 legacy/character/profile Guardian ranks, old/new XP, tokens, raw rewards/perks/enabled flags; TTWL Hero stats, Myth XP/indexed points and last-tree index when present | Independently checked selected wire tags; absent records stay absent; raw assets stay raw; Myth index labels inferred from upstream, no calculated rank or automatic reconciliation; existing raw edits only in F7 |
| World / progression labels | Searchable English missions/maps/travel destinations/classes, BL3 named decorations/skins/trinkets, TTWL Myth/Hero/backstories | 1,846 additional attributed zlib records; no artwork, unlock/repair actions or capacity rules |
| Raw | Lazy schema-labelled tree; unknown tags and bytes shown as opaque data | Unknown meanings remain unknown; account-like metadata is hidden |

Curated field meanings use `Confidence.Verified` because they are named in cited schemas/upstream
tools, with sample evidence below. This does **not** mean edits have been verified in-game. Limits
are editor policies, not universal game limits: money/eridium/keys ≤2,000,000,000, moon orbs ≤16,000,
ammo ≤100,000. Existing out-of-range amounts remain read-only. Name length uses the stored limit
when reasonable, otherwise an inferred fallback of 32 UTF-16 units. Invalid Unicode/control
characters are rejected.

Level/XP edits preserve mission unlocks and milestone challenges. Levelling down preserves spent
skills and does not remove unspent points. A session high-water level prevents repeated down/up
levelling in Wonderlands from awarding the same points twice; undo restores that policy state. Reopening a saved lower-level file starts a new
baseline; review the explicit points field and do not treat this as a complete respec/budget model.
BL3's level-one tree is enabled when raised above level one. Missing level statistics/ability messages are not fabricated. Warnings describe
these limits in the UI.

## Item browser and versioned data

The inventory uses F8 queries over the complete container for search, exact category filters,
numeric level/index sorting and paging (at most 200 rows). F3 rows show a confidence-labelled family
name, raw part-family key and lazy details. Details include original `BL3(...)` codes (both games use
this wrapper), balance/inventory/manufacturer paths, raw parts, flags, pickup order, reroll count and
Wonderlands' raw Chaos tier when the entire known layout parses. No exact rarity, combat statistics,
generated full item names or legal part combinations are inferred. Optional local skill icons and curated
whole-item reference thumbnails are bundled; these never render the saved part configuration.

F4's **Inspect catalogue entry** and **Inspect serial index / part** actions browse F2 catalogues with
provenance. They are inspection actions with no changes, undo steps or dirty state. Unknown candidate
IDs are rejected by the host. Balance name packs must declare the matching internal database version;
incompatible labels fall back to the pinned data. With no host packs, ordinary inventory/catalogue
views still use the same embedded records. Pickers require the host F2 context.

The binary layout always uses the pinned embedded database; name overrides cannot redefine bit widths.
The packs contain all original serial indices, including duplicate asset paths, and all historic width
transitions. The keyed balance catalogue collapses duplicate normalized paths; indices are never
renumbered. Versions and counts:

| Pack | Internal database | Index categories / records | Distinct balances | Source name mappings | Balance→part-family mappings | Slot/SDU/ammo labels |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `gearbox.bl3` 1.3.0 | 62 | 48 / 11,453 | 2,803 (2,848 index slots) | 774 | 1,435 | 25 |
| `gearbox.ttwl` 1.3.0 | 59 | 56 / 13,674 | 2,030 (2,037 index slots) | 375 | 659 | 29 |

The added `world-labels` datasets contain **1,387 BL3** records (208 missions, 74 maps, 864 travel-to-map
joins, four classes, 100 room decorations, 37 weapon skins, 100 trinkets) and **459 TTWL** records
(90 missions, 20 maps, 299 travel-to-map joins, seven classes, 28 Myth indices, six Hero fields, nine
backstories). Only explicitly named upstream records were extracted. Travel labels describe the
destination **map**, not a station name. Mission lookup removes the object/class suffix and lowercases
the asset path; records retain `originalId`. Other asset keys are lowercased; map keys use `map:`.
Unknown IDs remain raw. Class/mission label joins and Myth array meanings are `Inferred`; source
names/caps are not verified PS5 gameplay rules. The upstream zero Myth cap means uncapped there and
is retained as an attribute. Writable Myth limits use the embedded upstream cap as an Inferred
editor policy; uncapped indices use max(observed, 1,000). Host F2 overrides supply labels only,
not write limits or new indices. The F8 label browser covers
the pinned key set, applying attributed host labels when available; additional override-only keys are
not enumerated. Every displayed label carries its record attribution, including raw fallback handling.

Game patch, DLC/hotfix state and completeness are **unknown**; internal serial database versions are
not PS5 build numbers. Every dataset has attribution and file hashes in `pack.json`, plus a source
sidecar with source revisions/retrieval dates and conversion notes. The serial-resource sidecars
also pin the original compressed-file SHA-256 values. No licence is
published for the pinned bl3data/wldata game exports; the historical NC assertion had no exact
terms/source pin, and no verified NC/no-redistribution restriction is established for those sources.
Object/path conventions were cross-checked, but export payloads are not redistributed; pinned zlib
editor resources and separately attributed community labels supply the shipped data. Publisher icons and
transcribed card text are now imported separately under the owner decision dated 2026-10-29, with
the exact notice **Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29**.
This does not relicense publisher material as MIT/zlib/GPL; community notices remain retained.

### Game artwork and card text (asset round)

The packs attach **425 skill-icon assignments** to named calculator records and **23 item thumbnails**
to curated balances (12 BL3 / 11 TTWL). F2 pickers and F3/F8 item/skill tables show local images with
adjacent provenance; skill graph detail controls also show their icons. The same item thumbnails
appear on **87 matching serial indices** (42 BL3 / 45 TTWL, including 61 actual part records), explicitly labelled **whole-item reference**,
never an individual part image or a rendering of saved stats/appearance. Other parts/items retain
text/raw fallbacks. Joins require an exact existing balance name and exact unique-item path prefix;
no generic family inherits a named legendary image. No artwork is assigned to opaque saved skills.

New descriptions contain **23 official item-card flavor lines** and **134 literal BL3 skill-card
effect descriptions**, read from the pinned calculator without executing interpolation/formulas.
All 425 existing skill descriptions remain, with publisher provenance distinguished from the MIT
community adaptation. Card text is reference material with unknown patch/hotfix coverage, not a
combat simulator or proof of saved item stats. Item flavor lines have their own `item-cards` dataset.

Images were header-validated and processed outside the repo using Python `-I`. Each is at most
64×64, WebP or smaller PNG-8. SHA-derived filenames deduplicate one identical BL3 skill image:
**447 image files, exactly 871,603 new image-asset bytes** (BL3 723,237; TTWL 148,366), below 4 MB.
All newly added embedded files, including record JSON and provenance sidecars, total **1,073,187 bytes**.
Original/processed hashes and every exact upstream image/page URL are in each pack's
`assets.source.json`; `assets.source.md` records processing and mapping details. Separate
`skill-icons` and `item-icons` datasets keep image-source provenance accurate. F2 validates/caches
packs once per store; the plugin's existing static lazy record fallback skips asset datasets and
never decodes/loads image files. Images enter views only on requested rows or lazy detail expansion.
An empty reference context still displays names/card text and explicit image-unavailable cells.

No map backgrounds were added. Stored mission states have no verified positions or background
transform; the existing bounded checklist/text alternative and external guide remain usable.
This is missing coordinate evidence, not a remaining publisher-art refusal.

Historical asset-round measurement, 2026-10-29 (`AssetLoadMeasurements`, Debug/.NET 10, fresh process; largest sample,
store creation + pin + open + root view + first 20 rows of every collection, archive import excluded):

| Title / largest sample bytes | Before ms / allocated bytes | After ms / allocated bytes | Warm reopen before → after |
| --- | --- | --- | --- |
| BL3 / 1,567,384 | 560.034 / 314,788,192 | 461.654 / 324,371,664 | 36.800 → 34.293 ms; 24,491,360 → 25,345,656 bytes |
| TTWL / 538,381 | 425.275 / 276,077,264 | 386.406 / 284,866,688 | 23.315 → 65.356 ms; 9,848,472 → 10,415,880 bytes |

These single stopwatch observations include JIT/GC and are not timing assertions. The historical
allocation counter was process-wide and could include other test activity; these figures are not
isolated per-load allocation evidence. The current test uses current-thread allocation with explicit
first/repeated-load budgets. The warm measurement reuses the same pack store; tests also prove
repeated pins serve the same cached image buffers.

The later [performance review (#40)](PERFORMANCE.md) measures separate pack, fallback, open/view,
lazy-detail, serial codec and F7 phases on the largest samples in Release. Per-catalog lazy fallback
loading reduces allocation through the first root view by about 78% (BL3) / 85% (Wonderlands).
Retaining validated F7 members reduces group open/root allocation by about 19% for both titles;
the same round-trip validation remains required. Full measurements, limitations and reproduction
commands are recorded there; elapsed times are observations, not assertions.

The serial envelope is capped at 4,096 bytes; bit reads and part/additional counts are bounded. It
retains the signed seed, checksum, all trailing bits and original serial bytes. Unsupported versions,
bad CRCs, unmapped indices/families, nonzero customizations and unknown tails produce item-local
diagnostics. They never prevent unrelated currency/progression editing. Header-only items do not
advertise decoded reroll/Chaos values. Stable document-local row IDs distinguish identical serials.

## Inventory and list presentation (F14)

Every stored inventory/allocation list is presented as one table. Equipped slots, SDUs, ammo
pools, Guardian rewards/perks and Myth allocations previously used linked 100-row eager pages;
these now use one eager table through 100 rows and one F8 searchable/filterable/sortable table
above that limit. Default order, slot/record row keys, field IDs and save bytes are retained.
Bindings for large lists are created only for the requested query page.

Confirmed `equipped_inventory_list.inventory_list_index == -1` rows publish `IsEmpty` and
`EmptyRows("slot")`, hiding vacant equipment by default. Showing them displays **Empty slot**;
the original slot asset, enabled flag and raw index remain available in details. F8 filtering
runs over the complete list before visibility, sorting and paging; hidden counts include only
matching vacant slots. Direct SDK queries and F6 whole-query scopes use the same visibility
rule. Equipment row keys remain equipment identities and are still rejected by item actions.
Zero indices and disabled slots are not empty; other negative indices stay visible as unknown.

The vacancy fact was checked against all supplied BL3 and Wonderlands character snapshots,
including backups, and against apocalyptech's pinned Zlib sources (retrieved 2026-10-29):
[BL3 `get_equipped_items` / `wipe_inventory`](https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/bl3save.py)
and [Wonderlands equivalents](https://github.com/apocalyptech/ttwl-cli-saveedit/blob/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/ttwlsave/ttwlsave.py).
Both clear equipment references to `-1`. This change implements presentation metadata from
that existing format fact; no upstream code was copied.

Audit of the remaining tables: Backpack, Bank and Lost loot already use one F8 table each and
store repeated item records, with no confirmed vacancy sentinel. Empty/invalid/unmapped serial
records therefore remain visible and preserved. Balance/world/skill catalogues, stored skill
allocations and missions already use F8; zero allocations/statuses are real records, not vacant
slots. The item-detail parts table holds at most 63 regular plus 15 generic serial parts, not
unused slots. Zero ammo, SDU levels and Guardian/Myth allocations remain visible. Bounded raw,
skill-graph and map expansions are inspection/spatial views, not chunked inventory tables.

## Skill graphs, mission checklists and part selection

The Skills tab separates **stored allocations** from **named calculator references**. The save's
`item_asset_path` identifies opaque Unreal tree subobjects, not a named skill. F8 searches every
stored allocation, including later pages, by raw path/tree/points/cap. F9 shows positive stored
points as Unlocked and zero/negative points as Unknown; zero does not prove a skill is available
or locked. These graphs have no invented prerequisite edges. Stored rank/asset editing and full
respec remain unavailable: a reliable subobject→skill crosswalk and dependent class/budget model
have not been established. Existing unspent-points/tree-grade edits remain enabled.

The `skills` datasets contain **315 BL3** and **110 Wonderlands** skills from pinned MIT community
calculators: names, plain-text descriptions, calculator tiers and rank caps. BL3 covers
the trees actually present upstream, including its partial DLC coverage; missing fourth trees
stay unknown. Class feats and executable combat-effect formulas were not imported. Skill icons and
literal card-effect lines were added in the asset round with separate publisher-rights attribution.
Reference IDs are calculator slugs, never save IDs. The additional crosswalk records map known tree
prefixes to class/tree and mark the join `Inferred`; unresolved paths remain raw. The current
allocation view always reports `Unknown skill (tree only)`: it does not resolve exact skill names,
even when a tier/rank-cap combination looks unique. Every reference graph state is Unknown.
Gate→skill edges mean a calculator's cumulative earlier-tier point requirement, **not** that every
preceding skill must be bought. These are upstream build facts, not verified PS5/hotfix rules.
Targets open attributed read-only controls; the text alternative contains the same facts.

Missions now use F8 search/filter/sort across all stored playthroughs. F9 provides an external-map
checklist with no imported coordinates/art. Found means only stored mission status Complete;
other statuses remain Unknown in the map legend. No reward/branch/collectible ownership is
inferred. [Map Genie BL3](https://mapgenie.io/borderlands-3) and
[Wonderlands](https://mapgenie.io/tiny-tinas-wonderlands) are **external links only**: Map Genie
attribution is visible in the map, its reuse terms are unverified, and no content was copied.
Graphs/maps load in 100-record lazy pages with stable path-derived marker/node identities and
links to their actual fields, including correctly namespaced links inside F7 groups.

Serial index names now display the original **asset object token** with category/index/raw path
details. These are not localized part names or generated item cards. The F4 part picker can replace
an existing regular/generic part index only in a fully decoded serial. Eligibility and the writer
both enforce the immutable pinned category and original database bit width; label overrides cannot
grant eligibility. It patches only the selected bits and CRC, retaining all other bits, count/order,
seed and padding. Unknown/customization tails, invalid indices, wrong categories and width overflow
are rejected atomically. This establishes binary compatibility, **not legal part pools**, required or
excluded combinations, item stats or DLC ownership. The game may reject/remove edited gear. Console
validation is pending [#31][owner-validation]; all controls remain Experimental/Inferred and warned.

Ordinary F8 inventory index paging now decodes only the requested page. Searches and semantic sorts
decode the full relevant container once and reuse per-item results until its serial changes. Item
lookup and full-query selection use indexed/hash lookups rather than repeated linear membership
scans. Codec paths avoid redundant envelope/payload copies, reuse decoded envelopes during writes,
and use an immutable CRC table. Cache invalidation covers failed batches, scalar edits, structure
changes and undo/redo; consecutive direct SDK edits retain accurate change reports.
The quality pass also fixed omission of newly created byte-only protobuf records (notably BL3 bank
imports/deposits) and repeated default values. Serialize now independently checks reopened inventory
counts against the intended model; transfer/import regressions assert persisted destination counts.

## Framework feature adoption

| Feature | Status | Where / reason |
| --- | --- | --- |
| F1 SaveDiff | Used | Gameplay-only production hook, independent test oracle, pinned fixture manifest and actual CLI verification; no identities/sample bytes committed |
| F2 reference packs | Used | Expanded in the asset round (2026-10-29): title-scoped 1.3.0 packs, 425 skill icons, 23 item images/card texts, 134 literal skill effect descriptions; separate publisher attribution and retained community notices |
| F3 rich cells | Used | Expanded in the asset round (2026-10-29): attributed local skill/item images, explicitly whole-item part reference thumbnails, confidence-labelled fields and lazy details; raw/text fallback when unavailable |
| F4 reference pickers | Used | Added in the completion round: existing serial-part replacement with category/width eligibility and independent writer checks; browse-only asset/class/mission/skill references and attributed inspection actions |
| F5 structural history | Used | Import/duplicate/remove and bank moves retain full wire bytes, duplicate identities and counters in snapshots |
| F6 planning / #27 | Used | Item levels accept document scope (all member inventory containers), explicit rows or host-resolved F8 query rows. Duplicate/remove and F7 transfers require explicit or host-resolved rows; import uses document scope into backpack/bank. Plans preview changes/skips/warnings |
| F7 groups | Used | Extended in the completion round: explicit character/profile transactions; graph/map links and query selections are namespaced to their owning member |
| F8 collections | Used | Backpack/bank/lost loot/balance/world catalogues, named skills, stored allocations and missions; equipment, SDUs, ammo, Guardian and Myth lists use F8 above 100 rows (F14). Full-set search/filter/sort with a 200-row query cap; duplicate item identities remain distinct |
| F9 graph/map | Used | Added in the completion round: stored allocation graphs, calculator gate graphs, external-only mission checklist; bounded lazy pages, text alternatives and control links |
| F10 exact / nullable / union | N/A | Exposed scalars are typed protobuf integers/float32, with no decimal lexemes, explicit null or scalar type union. Existing Int128-backed IntegerSpec / integer-string transport and FloatSpec.Single suffice; ExactNumberSpec, NullableSpec and UnionSpec are not used. Absent records are preserved except for the documented write-created records |
| F11 shared codecs | N/A | GVAS feedback-obfuscated protobuf and item bitstreams use neither Kraken/Oodle nor SQLite; no local implementation of those codecs exists |
| F12 decoder providers / F8 traversal | Used | Added in the completion round: SDK-only `ISaveDecoderProvider` registers `gearbox-evidence`; fixtures use the production hook, independently cross-checked on every sample generation. Host traversal covers F8 views with its shared budgets |
| F13 inspection / browse / sort | Used | Added in the completion round: both catalogue actions set `Mutates = false`; read-only reference fields support attributed browse-only pickers, category filters and name/key sorting. Float duration N/A: stored play time is integral seconds, resource floats are ammunition amounts |

Shared SDK helpers are **Used**: `GvasReader` uses `BoundedBinaryCursor` / `FStringCodec`,
`SerialEnvelope` uses incremental `Crc32Ieee` with Gearbox-specific coverage/folding, and
`GearboxImages` uses `ReferenceViews.Icon` with separate record/image provenance and a text fallback.
`BoundSaveDocument` supplies bindings and structural history; the plugin retains its cloned-tree
scalar transaction and custom collection queries. `InMemoryCollection<T>` is not adopted: these
queries retain item decode caches, lazy bindings, stored-order and equipment-vacancy semantics.

## Completion gap audit

| Plan requirements / acceptance | Final disposition |
| --- | --- |
| R1, R8 names/parts/skills | Added MIT skill text, 499 localized BL3 balance labels, 2,100 BL3 part descriptions, 4,327 TTWL mapping labels and bounded class/tree prefixes. Generated effects, exact joins and DLC legality remain explicit unknowns |
| R3–R7, R9–R12 framework/evidence/editor | Supported implementations retained and verified; constrained existing-part picker, F8/F9 and performance/bookkeeping fixes completed |
| R13 legal pools/dependent builds | Existing-slot binary edits enabled with warnings; unrestricted construction, class replacement and respec require a reliable compatibility/dependency model and controlled captures (#31) |
| R2, R15–R17 captures/console/account precedence | Owner-blocked (#31); writes already enabled where a bounded writer exists, with Experimental warnings |
| R14 redistribution | Applied owner policies dated 2026-10-28/29; attributed MIT/zlib/community facts plus publisher icons and card text embedded. Map backgrounds still need coordinate evidence; no remaining licence refusal for this imported material |
| R18–R19, R22 BL4 | Owner-blocked (#31): unknown PS5 codec and file role; research-only feasibility evidence retained, no writes |
| R20 mission/collectible repair | Owner-blocked (#31): named observation/checklist shipped; safe reward/branch recipes require isolated game captures |
| R21 world maps + requested skill trees | F9 delivered without imported map art/coordinates or invented prerequisites; actual location transforms remain unknown |
| Host-stored reusable loadouts, combat simulation, platform conversion, new slots, premium entitlements | Won't do in this plugin round: no safe format/dependency or host persistence contract; runtime plugin I/O is prohibited |
| Preservation, malformed input, history, attribution and boundedness acceptance | Sample-backed regressions, independent CRC/bit checks, 1,200-row progression tests, real F1 CLI fixtures and clean API build; console acceptance stays owner-only |

## File roles and layout

`profile` is the profile; one-to-three ASCII-digit filenames with no leading zero (such as `1`)
are character slots. Probe requires the supported title ID, file role and `GVAS` magic.
Open validates branch/class/version as well.
Ordinary single-file documents return only their opened file. Explicit F7 groups return both selected
members exactly once. Console backups are separate selections, never silently rewritten.

## Coordinated character + profile groups

Use **Choose related files** to inspect exact character/profile paths before **Open group**. Discovery
requires one title/imported generation/backup status, supports PS5's separate save directories and
shows ambiguous partners explicitly. It never inspects account identities to guess relationships.
Generation identity does not prove matching gameplay captures. Backups are separate explicit groups.

F7 exclusively owns both selected paths, rejects dirty/overlapping writers, stages changes privately
and validates/exports both roles together. Independent scalar edits and all planned inventory actions
also work through namespaced member controls. Account/Guardian/Hero/Myth fields become writable only
inside the group. Edit both stored mirrors explicitly if desired: the plugin does not choose an
authoritative mirror, calculate rank, grant derived rewards or invent missing account records.

Select rows from the source F8 table, then preview **deposit**, **withdraw** or **claim lost loot**.
F6 shows the original serial, source/destination counts, equipment index changes and warnings.
Unresolved whole-query scopes are rejected; the host resolves bounded full-filter selections.
Every operation is atomic. One group undo restores both files exactly. F5 snapshots retain cloned
wire trees, member origins, duplicate identities, allocation counters and the character level high-water
state; they restore into a fresh group. Moved IDs retain source identity, while duplicates get new IDs.

BL3's bank stores bytes, whereas TTWL's bank stores item messages. TTWL transfers clone every item
field, including unknown ones. A byte-only destination cannot retain item metadata: known favourite/
junk and pickup-order fields are dropped with a warning, but nonempty skins, development data and
unknown fields or unknown flag bits cause rejection. Withdrawal/claim creates seen/pickup metadata for byte-only entries.
Removal/transfer unequips removed items and rebases all surviving equipment indices; source order is
otherwise preserved. New items append; removal never discards overflow elsewhere.

Capacity is an **Inferred conservative editor policy**, not a measured PS5 limit: backpack tiers
0–8 use 16 + 3*tier, tiers 9–13 use 40 + 2*(tier-8); bank tiers 0–5 use 10 + 5*tier,
then 40 + 20*(tier-6); lost loot uses 6 + tier. Published tier maxima are separately enforced.
An entirely absent SDU list uses only the base tier-zero capacity; explicit unknown SDUs reject
growth. Counts above these policies remain preserved, but cannot grow or be reduced further through
an SDU edit. Ammo capacity, bank-category counters and cloud mirrors are not guessed/reconciled.

## Enabled writes and their evidence

Owner decision on 2026-10-28: **enable writes, keep warnings**. All enabled writes below are
Experimental. Legacy name/progression/currency/ammo fields retain `Verified` schema meanings;
serial/SDU/difficulty/account fields are `Inferred`. Neither label establishes console acceptance.
The overview and write controls describe their limits. Unknown integrity is a rejection risk:
the game may reject/clamp/remove edited content.
All outputs are reparsed; known item CRCs are repaired. No additional wrapper integrity algorithm is
known. Controlled game captures and console acceptance are tracked separately in [#31][owner-validation].

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Character name | Verified | Schema/string samples; stored limit 1–128 or fallback 32 UTF-16 units; invalid names rejected | Strict UTF-8 encode and complete output reparse | Display, load/resave/reload [#31][owner-validation] |
| Character level / XP | Verified | Exact upstream XP thresholds, every-snapshot edit/reopen and history tests | XP, existing level stats and supported ability-point/tree-grade consequences change atomically; milestones unchanged | Level, skill points, unlock dependencies [#31][owner-validation] |
| Unspent skill points / tree grade | Verified | Existing ability message and named schema scalars | Nonnegative Int32 policy; spent allocations and budgets unchanged | Point budget, unlock behavior [#31][owner-validation] |
| Recorded level statistic | Verified | Existing named stat, sample agreement with XP-derived level | Direct nonnegative Int32 edit; this stat alone does not change XP/level | Display and resave consistency [#31][owner-validation] |
| Money / eridium / moon orbs / golden or skeleton keys | Verified | Published category hashes and sample/edit/reopen tests; absent key category creation tested synthetically | Create absent category only for nonzero edits; existing out-of-policy values read-only; complete output reparse | Clamping, balances and profile keys [#31][owner-validation] |
| Ammo | Verified | Schema float32 resource pools; signed-zero no-op and warning tests | Only finite values 0–100,000 editable; pool paths/SDUs unchanged; complete output reparse | Capacity clamping and refill behavior [#31][owner-validation] |
| 1. Item level | Inferred | Pinned datalib header; all 161 primary serial occurrences; exact-bit/F1 tests | Original seed/tail retained; folded CRC repaired | Card level, load/resave/reload [#31][owner-validation] |
| 2. Reroll count | Inferred | Complete known v4/v5 layout; 8-bit counter | CRC repaired; other bits retained | Reroll cost/enchantment unchanged [#31][owner-validation] |
| 3. TTWL item Chaos tier | Inferred | Pinned v5 layout, tiers 0–4 | CRC repaired; other bits retained | Card/tier/stats after reload [#31][owner-validation] |
| 4. Favourite/junk mark | Inferred | Upstream flags 2/4; unknown flag bits retained | Protobuf reparsed; no extra wrapper checksum known | UI mark, sale protection [#31][owner-validation] |
| 5. Existing known SDU tier | Inferred | Pinned upstream maxima plus sample paths | Capacity policy checked; output reparsed | Displayed tier/capacity/ammo clamping [#31][owner-validation] |
| 6. Existing Mayhem/Chaos level | Inferred | Separate schemas, sample playthrough records | Output reparsed; modifiers/rewards unchanged | Difficulty/modifiers/loot [#31][owner-validation] |
| 7. TTWL Chaos unlocked ceiling | Inferred | Known mayhem_unlocked_level scalar; 0–100 policy | Output reparsed; trial milestones unchanged | Trial menu, rewards, relock behavior [#31][owner-validation] |
| 8. Bulk item levels | Inferred | F6 full-query/selected-row tests; unsupported rows skipped | Same serial CRC repair; one F5 undo | Selected/full batches and skipped gear [#31][owner-validation] |
| 9. Duplicate selected items | Inferred | Existing complete-layout templates, full message clone | Unknown fields retained; backpack append assigns a new pickup order; F5 identity/counter snapshots | Duplicates, capacity, order [#31][owner-validation] |
| 10. Import serial | Inferred | Bounded wrong-game/version/CRC/layout checks; original code retained | CRC validated, new protobuf record reparsed | Imported legitimate card and DLC [#31][owner-validation] |
| 11. Remove selected items | Inferred | Known list/equipment schema; derived index preview | F5 restores exact original bytes/IDs | First/middle/last and equipped removal [#31][owner-validation] |
| 12. Backpack → bank | Inferred | Role-specific bytes/message schemas; sample F7 transactions | F6 inside F7, both outputs validated; metadata-loss rejection | Matched pair, duplicates, full bank [#31][owner-validation] |
| 13. Bank → backpack | Inferred | Same schema evidence; seen/pickup metadata policy | F6/F7/F5; original serial unchanged | Counts, order, UI marks, full backpack [#31][owner-validation] |
| 14. Lost-loot claim | Inferred | Known byte-only profile list and character item schema | F6/F7/F5; source/destination atomic | Claim menu, uniqueness, cloud precedence [#31][owner-validation] |
| 15. Character + profile field batch | Member field's label | F7 host staging/ownership and complete snapshots | One history/export group for both roles | Combined independent edits and clean reload [#31][owner-validation] |
| 16. Existing Guardian rank/XP/tokens/enabled scalars | Inferred | Independent gameplay-only wire probes; schemas | F7 only, no missing records or inferred mirror direction | Profile plus every character, budgets [#31][owner-validation] |
| 17. Existing Guardian reward/perk tokens/enabled | Inferred | Known rank_rewards/rank_perks messages | Unknown fields retained; F7 only | Perk effects and token budget [#31][owner-validation] |
| 18. Existing Hero stats | Inferred | TTWL uint32 schema, upstream labels; max(observed,30) policy | F7 only; backstory/budget untouched | Raw/base/bonus stats, respec [#31][owner-validation] |
| 19. Existing Myth XP | Inferred | Independent profile/cloud-mirror tags; conservative observed bound | F7 only; rank not computed | Rank/available points and mirror precedence [#31][owner-validation] |
| 20. Existing Myth last-tree index | Inferred | Known profile scalar; 0–3 policy | F7 only; rotation not recomputed | Next allocation tree [#31][owner-validation] |
| 21. Existing named Myth allocation | Inferred | Upstream indices/caps; packed/unpacked exact-varint tests | F7 only; other bytes/noncanonical values retained | XP budget, caps, rotation, each tree [#31][owner-validation] |
| 22. Existing serial part slot | Inferred | Located index in a fully mapped layout; immutable category/width validation; independent sample bit/CRC checks | Original seed/counts/tail unchanged; CRC repaired; output reparsed | Allowed pools, stats, duplicate/excluded parts, DLC and rejection/removal after reload [#31][owner-validation] |
| 23. Match selected gear to current character level (#40 shortlist 2) | Inferred | Existing XP threshold lookup composed with the existing serial-level setter; synthetic bit checks and BL3/TTWL sample host/group history tests | Explicit selection only; clip to existing 72/40 title policy; preserve seed/tail, repair folded CRC and reparse envelope | Owned item/card and character-level relation before/after, equip/use and load/resave/reload [#31][owner-validation] |
| 24. Restore imported ammo amounts (#40 shortlist 3) | Verified pool schema; Experimental workflow | Immutable opening float32 amounts, pinned finite ammo paths, existing 0–100,000 scalar policy; synthetic and sample history/reopen tests | Only lower current values increase; unknown/ambiguous/missing/nonfinite/out-of-policy pools skipped; signed-zero no-ops retain bytes; no SDU or pool creation | Isolated firing/reload/native refill captures, reserve display and capacity clamping [#31][owner-validation] |
| 25. Batch ordinary/favourite/junk marks (#40 shortlist 10) | Inferred | Existing mark setter; pinned flags 2/4; explicit raw old/new flag preview; synthetic unknown-bit/conflict tests and sample history/reopen tests | Only selected complete known serial records with mark metadata; conflicting flags/unsupported records skipped; unknown bits and serial bytes unchanged | Separate mark/unmark captures for each title, UI and sale protection after resave [#31][owner-validation] |

### Edit coverage actions (#40)

Select the intended rows in the backpack/bank table, open a row's **Serial and parts** details,
then use **Match selected gear to character level** or **Mark selected items**. The controls live
inside the table's selection context, so explicit selections (including hidden selected rows) and
resolved full-query selections reach the preview. Empty selections change nothing. Gear matching
is character-only: it uses the current character's XP, including staged XP edits, never a profile
or another character. Existing level-bit eligibility is retained, including preservation of an
uninterpreted serial tail when the header is safely mapped; missing/invalid headers and current
levels outside policy are skipped. No part, rarity or stat synthesis is performed.

**Restore imported ammo amounts** is in Progression and previews all existing pools in document
scope (the API also accepts explicit/resolved ammo-row selections). Its baseline is captured when
the document opens and survives edits, undo/redo and grouped member reconstruction. It is neither
the last edited value nor a game maximum, and cannot recover amounts lost before import. Reopening
an exported save establishes a new baseline. Already equal/higher amounts stay unchanged. Only
unique pinned ammo paths with one existing path/amount field in both the opening and current record
qualify; indices and paths must still match. Nonfinite, negative, above-policy, missing, duplicate
and unrecognized pools are reported as skips. Capacity remains governed by the game's SDUs.

All three actions are pure previews until Apply; Cancel discards the plan. Apply revalidates the
reviewed content with shared `ActionPlans`, uses the existing atomic snapshot edit path, serializes
and verifies the output, and records one undo step. Group actions localize prefixed plans and
selections without changing member ownership. Batch marks reuse the scalar mark implementation:
0 clears the two known bits, 1 chooses favourite, 2 chooses junk, and every other flag bit is retained.
BL3 byte-only bank records and lost loot have no mark metadata and are skipped; TTWL bank messages
can qualify. This does not create/remove/sell items. No shortlist item was deferred. These workflows
remain **Experimental**, retain the common “Not validated on console” warning, and require the
owner captures/console checks above before any Stable claim.

Research rechecked 2026-10-29 against the already pinned primary sources:
[BL3 XP lookup and item flags](https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/bl3save.py),
[TTWL item flags][ttwl-serial], [BL3 ammo paths][bl3-constants], [TTWL ammo paths][ttwl-constants]
and the [serial level/envelope implementation][bl3-serial]. These are original convenience-action
compositions of existing attributed policies; no new external datasets or downloaded code were added.

Reroll/Chaos edits require a fully located tail; level patches can preserve an unknown remainder.
Imports and duplicates require complete pinned layouts; wrong-game, corrupt and unsupported imports
are rejected. Conflicting favourite+junk flags remain raw and read-only rather than normalized.
Arbitrary balance/part-count construction, slot compatibility editing, mission completion/story resets,
class replacement and automatic account reconciliation remain unsupported because their dependent
structure/legality contracts have not been implemented. No known operation is disabled merely while
waiting for owner evidence. BL4 remains research-only, excluded from probing and all writes.

[owner-validation]: https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31

GVAS framing, little-endian:

1. `GVAS`, int32 save version **2**, int32 package version.
2. uint16 engine major/minor/patch (**4.20.3**), int32 build number.
3. FString build branch (`OAK-*` / `DAFFODIL-*`).
4. int32 custom-version format **3**, int32 count, count × (16-byte GUID + int32 version).
5. FString save class: BL3 `OakSaveGame` / `BP_DefaultOakProfile_C`; Wonderlands
   `BPSaveGame_Default_C` / `OakProfile`.
6. int32 payload length, obfuscated protobuf bytes, optional opaque trailing bytes.

FStrings support positive UTF-8 byte lengths or negative UTF-16 code-unit lengths, including checked
terminators. All header/custom-version metadata is retained verbatim; only the payload length is
patched on size changes. Opaque tails are retained, although supplied samples have none.

## BogoCrypt, protobuf and integrity

The published **PS4** character/profile mask pairs decode **both** PS5 games. Separate game schemas
are essential; separate Wonderlands PS4 masks were not needed here. PC mask trials failed protobuf
parsing on all twelve primary records. PS4 trials parse all twelve, plus all twelve backup twins.
This proves the supplied builds, not every PS5 release.

Plaintext byte `i` is ciphertext XOR the role's XOR mask at `i mod 32`, XOR either the prefix mask
at `i` for the first 32 bytes or ciphertext at `i−32`. Encoding uses already produced ciphertext
as feedback. This is rolling XOR obfuscation, with no compression/authentication. No additional
wrapper checksum is present in the cited format or observed samples. **Well-formed scalar/unknown
field corruption can go undetected.** Item serials have independent encoding/checksums; edits patch
exact bits, preserve the signed seed and repair the folded CRC. Every Serialize rechecks every
changed serial as well as the complete wrapper/protobuf output.

Google.Protobuf managed coded streams parse/rewrite wire records without reflection, generated
classes, protoc or Grpc.Tools. Embedded TSV resources contain factual names/numbers/types/cardinality
extracted from each game's three schemas and exact XP thresholds: BL3 80 messages / 529 fields;
Wonderlands 73 / 486. The schemas stay separate: game-state field 53 versus 19, bank field 137 versus 212.

Every field retains its complete original wire record. Editing rebuilds only changed values and
enclosing messages/lengths. Original order, unknown records, noncanonical varints and opaque bytes
survive. No-op serialization is byte-identical across the entire sample corpus. Unknown groups and
packed primitive data are preserved; known packed values are wire-validated. Repeated fragments of
a known singular message are rejected because protobuf merge semantics need additional work; none
occur in the samples.

Edits run on a cloned message tree; failure discards it, retaining original wire encodings and the
provenance of newly created records. This includes failures during setter rollback: an imported
over-capacity inventory must not retain earlier edits from a rejected SDU batch. Batches with no
semantic changes also discard their clone and level-award policy changes. Unchanged packed Myth
values retain noncanonical varints; unchanged ammo retains the original floating-point zero sign.
These cases must not change output bytes without an undo entry. Derived changes are reported for framework undo/redo, including
exact XP, skills/statistics and removal of newly added key records. Serialize parses its own complete
output wrapper, decodes/validates the protobuf and compares it with the intended wire message.

Bounds: 32 MiB input, 1,024 custom versions, 16 KiB header strings (8,192 UTF-16 code units),
40 combined message/group nesting levels and 250,000 wire records/packed elements. Raw/difficulty
pages, stored skill graphs and mission checklists use 100-record lazy pages; small stored-list tables
use at most 100 eager rows, while F8 queries return at most 200 rows. Every-sample opening has a warmed
32 MiB allocation budget, not an elapsed-time deadline. Runtime I/O is limited to embedded resources;
there is no filesystem/network I/O or shared mutable document state.

## Sample evidence

Both titles contain three character/profile generations with identical primary/backup twins: 24
records total. Archive occurrence order does not establish chronology. Source level stats agree with
the following independently decoded values:

| Title / generation | XP | Level | Money | Eridium / moon orbs |
| --- | ---: | ---: | ---: | ---: |
| BL3 / 0 | 9,520,932 | 72 | 25,526,805 | 4,897 |
| BL3 / 1 | 9,520,932 | 72 | 13,296,047 | 2,592 |
| BL3 / 2 | 6,494 | 5 | 5,947 | 3 |
| Wonderlands / 0 | 70,754 | 12 | 36,748 | 0 |
| Wonderlands / 1 | 75,695 | 12 | 23,304 | 0 |
| Wonderlands / 2 | 70,565 | 12 | 30,754 | 0 |

Wonderlands moon-orb records omit the default-zero quantity. All profiles omit key-category records;
key creation is supported by the schema/hash and edit/reopen tests, with console behavior untested.
Samples/decoded payloads were inspected only in external scratch storage and are never committed.

## Validation

Edit-coverage implementation (#40 shortlist 2/3/10), 2026-10-29: the complete Release Gearbox
suite passed **361 tests, 0 failed, 0 skipped**, using task-owned external D: copies of the BL3,
TTWL and BL4 archives. This includes the existing serial codec/write/CRC/envelope and byte-identical
sample round trips, plus pure preview/cancel, explicit/query selection, unchanged unknown bits,
opening-ammo baseline, limits/skips, stale/forged plan rejection, export/reopen and exact undo/redo
tests for the new actions (standalone host sessions and coordinated groups). The Release API build
with `--warnaserror` passed with **0 warnings, 0 errors**. The branch's documentation audit
(`--code-ref HEAD`) passed **27 checks**; `--code-ref main` mixed this checkout's older documentation
with a newer main and failed its obsolete `test.fixme` expectation. Central documentation remains
outside this plugin-only change. Console validation is still required under [#31][owner-validation].

Documentation audit, 2026-10-29, against implementation at `6169d14`: the full Release Gearbox
suite passed **330 tests, 0 failed, 0 skipped** with the prescribed external sample archives.
The full multi-plugin F1 CLI passed **2 fixtures, 0 failed, 0 skipped**, without isolated discovery.
`python docs/tests/test_documentation.py` passed **14 checks**; the Release API build with
`--warnaserror` passed with **0 warnings, 0 errors**. This audit also corrects factually wrong
display/provenance strings about exact skill joins and duplicate pickup metadata, updates their
existing test expectations and record-file hashes, and retains behavior, IDs and confidence labels.
There are no pinned Gearbox view fingerprints to update.
The review totals and measurements below are dated historical runs, not the current test count.

SDK helper migration ([#40](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40)), 2026-10-29:
`GvasReader` delegates bounds and strict string decoding to `BoundedBinaryCursor` / `FStringCodec`
with the existing 16,384-byte / 8,192-UTF-16-code-unit cap. Its thin adapter retains the exact Gearbox
truncation, length, terminator and encoding diagnostics. `GearboxImages` delegates to
`ReferenceViews.Icon`, retaining the `Image unavailable` fallback and whole-item/part caveat; the
shared helper adds separate record and image attribution and checks image bytes before rendering.
`SerialEnvelope` uses `Crc32Ieee` while retaining header/payload coverage, `FF FF` sentinels, folding,
big-endian storage and original-seed obfuscation locally. Coverage was rechecked against the pinned
[upstream serial implementation][bl3-serial] and the independent folded SDK fixture. No new format
claims or imported code were introduced. Protobuf framing/shared budgets, unsupported scalar records,
lazy fallback catalogues and retained validated group documents keep their existing implementations.
The pre-migration baseline passed **285 tests**; characterization passed **311 tests** before production
edits. Final Release validation passed **312 tests, 0 failed, 0 skipped**, including all 80 corruption
cases and the catalogue/group performance regressions. The Release API build with warnings as errors
passed with **0 warnings, 0 errors**. Production code is **21 net lines shorter**; scratch and reports
were kept under `D:/Temp/saveeditor/test-scratch/refactor-sdk-helpers-gearbox`.

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:TEMP='D:/Temp/saveeditor/test-scratch/docs/accuracy-gearbox'
$env:TMP=$env:TEMP
$env:SAVETOOLKIT_TEST_TEMP=$env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet test --project plugins/Gearbox/SaveToolkit.Plugins.Gearbox.Tests -c Release
dotnet run --project tools/SaveToolkit.SaveDiff -c Release -- verify plugins/Gearbox/fixtures.json --artifacts $env:SAVETOOLKIT_ARTIFACTS
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj -c Release --warnaserror
python docs/tests/test_documentation.py
```

Asset-round validation, 2026-10-29: **156 Gearbox tests passed, 0 failed, 0 skipped**; F1 CLI **2 fixtures passed,
0 failed, 0 skipped** with Gearbox-only plugin discovery; API build **0 warnings, 0 errors**. At that
time the full multi-plugin SaveDiff command failed before decoding because another plugin registered
the duplicate hook ID `ffxvi`; the current FinalFantasy16 hook is `ffxvi-evidence`.
For that historical Gearbox check, the built CLI/dependencies and Gearbox assembly
were copied to an external temporary folder with other plugin assemblies omitted; no repository
files were changed for this isolation. The environment variable was exported in the
same shell command as each sample-backed test run. Sample-dependent tests still skip when absent.

Write-safety review ([#40](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40)), 2026-10-29:
**179 Gearbox tests passed, 0 failed, 0 skipped**; API build **0 warnings, 0 errors**. Ten new cases
cover rollback-setter failure with imported over-capacity inventories, byte/identity/level-award
restoration, packed/unpacked no-op allocations, signed-zero ammo, net-zero edit batches and F7
no-op output/history. Defect cases were confirmed failing before their fixes. Build outputs and
test scratch were directed to `D:/Temp/saveeditor/test-scratch/review-writes-gearbox`.

Consistency review ([#40](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40)), 2026-10-29:
**205 Gearbox tests passed, 0 failed, 0 skipped**; API build **0 warnings, 0 errors**. Eight new
cases pin the overview and legacy name/ammo/currency warnings for both titles and file roles,
including the existing clamping/category-creation qualifications and unchanged confidence labels.
All eight failed before the wording fix. README checks verified five-column exclusions, resolved
relative links and F1–F13 adoption statuses. Test scratch was directed to
`D:/Temp/saveeditor/test-scratch/review-consistency-gearbox`.

Malformed-input review ([#40](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40)), 2026-10-29:
**285 Gearbox tests passed, 0 failed, 0 skipped**; Release API build with warnings as errors:
**0 warnings, 0 errors**. The 80 added cases cover every incomplete synthetic BL3/Wonderlands
character/profile header (UTF-8 and UTF-16), custom-version count/table boundaries, corrupt string
encodings and payload lengths, overflowing protobuf tags/lengths/values, malformed opaque groups,
shared message/group depth and field/packed-element budgets, and small-allocation length rejection.
The original implementation failed eight protobuf and twelve scalar regressions before their fixes.
Valid noncanonical encodings, unknown groups, scalar upper bounds and unsupported read-only SDUs
retain byte-identical output. Every supplied primary/backup save is mutated only in memory to check
header truncations, re-encrypted malformed payloads and item-local truncated-serial diagnostics;
an empty bank receives a synthetic damaged record for that test. Serial tests cover every raw and
CRC-repaired payload prefix at signed seed boundaries, every encrypted body bit, excessive counts
and nonzero padding. Scratch and TRX outputs use `D:/Temp/saveeditor/test-scratch/review-robust-gearbox`.

Malformed containers and protobufs reject with the plugin's documented `SaveFormatException`.
Wire validation checks physical lengths and the existing 40-level / 250,000-record limits before
record allocation; packed elements and opaque group contents now share those limits. Unsupported
character XP/play-time values reject with field names before document construction. Unsupported
SDU values stay read-only without narrowing. Corrupt serials retain item-local diagnostics and
original bytes; tail edits require located, validated tail fields. Existing header-only level patches
remain available when the exact header bits and CRC are known, preserving an opaque tail.

Format research was rechecked against the pinned
[upstream GVAS/custom-table and ciphertext-feedback decoder](https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/bl3save.py)
and the [Protocol Buffers wire specification](https://protobuf.dev/programming-guides/encoding/).
The save cipher has no block padding: partial 32-byte feedback windows and trailing plaintext zeros
are retained exactly. Serial bit padding is a separate, already-validated zero tail. No new upstream
code or private sample data was copied into the plugin.

Tests include conformance with meaningful currency/key edits, valid F2 packs and fixture archive hashes,
every generation/twin, independent
feedback-window vectors, known values, money/level/key reopen, Wonderlands extra points, framework
undo/redo byte restoration, atomic rejection, preservation of every real-sample unknown record,
noncanonical encodings/nested lengths, UTF-16 GVAS strings, header/tail preservation, malformed
payload/framing rejection and invalid names. Independent test-only GVAS/feedback/wire and integer/bit
serial probes reproduce these **primary-file occurrences** (backups tested separately, not counted twice):

| Evidence | BL3 | Wonderlands |
| --- | ---: | ---: |
| CRC / header / original-seed inverse | 93 / 93 | 68 / 68 |
| Known family labels | 83 / 93 | 51 / 68 |
| Complete known part layout / zero padding | 85 / 93 | 51 / 68 |

Unmapped labels and part families remain explicit gaps, primarily cosmetic pickups; no names were
invented to improve coverage. Synthetic vectors cover seeds 0/−1/int32 extrema, legacy v3 envelopes,
CRC corruption, truncation, wrong game, newer databases, unknown customizations/tails, bad indices and
1,000 checksum-valid random payloads. Browser tests cover duplicate identities, unknown serials,
full-collection queries beyond the first page, confidence-labelled rich details, attributed pickers and clean
inspection. Currency edits preserve every serial and unrelated top-level wire record, then undo to
exact original bytes.

Round-2 tests additionally cover all 12 primary/backup character+profile pairs across both titles,
role/generation/title/backup/import-set rejection, explicit multiple-partner alternatives, host
ownership/dirty conflicts and clean replacement, JSON/group view identities, attributed title-pinned
catalog queries, independent gameplay-only Guardian/Hero/Myth wire checks, mixed packed/unpacked Myth
arrays, bounded continuation pages/unknown indices, unchanged complete ZIP export, and fresh-instance
byte/duplicate-identity snapshot restoration. Write-round tests add F6/F5/F7 transfer/edit histories,
atomic rejection, edited role reopening/export and independent bit/F1 preservation checks. They do
not establish console acceptance or BL4 codec progress.

Completion tests add independently checked single-part bit/CRC changes, category/width rejection,
host picker edits and history, attributed skill graphs/maps with reachable F7 targets, 1,200-row
skill/mission queries and 5,000-item banks for both games, using both real and sample-free synthetic
fixtures. Bank tests assert warmed allocation budgets for a fresh view/page (12 MiB) and a repeated
page (256 KiB); every-sample opening uses a 32 MiB allocation budget instead of an elapsed-time
deadline, followed by a separate byte-identical no-op assertion. Test-quality-review observations
(2026-10-29) were about 7 MiB, 115 KiB and 20 MiB respectively. Part-write tests independently
assert the selected index bits; synthetic regular/generic slots cover signed seed boundaries, exact
payload/CRC output, no-ops, inversion and rejected slot/category/index/CRC inputs. A no-op writer
mutation passes the former corpus oracle but fails the synthetic success cases. New-record tests assert actual persisted import/transfer
counts after reopening; consecutive direct edits and failed batches verify bookkeeping/cache behavior.
F12 tests compare the production hook to the independent probe across every primary/backup generation;
F13 tests verify inspection annotations, browse-only filtering/sorting and rejection of edits.

[`fixtures.json`](fixtures.json) pins archive SHA-256 and observed generation-0/1 XP/money facts.
These are uncontrolled existing generations, **not** proven before/after game actions or chronology.
No speculative churn masks are applied. `GearboxGameplayDecoder` exposes the F12 production hook
`gearbox-evidence` without a Testing dependency, selecting character XP/money and serial content identities; duplicate
serials retain multiplicity. A changed serial appears removed/added because durable cross-capture
item identity is unknown. Tests run the same manifests through the independent test decoder, production hook and
the CLI's `PluginViewDecoder`. No sample bytes, decoded payloads or account identifiers are committed.

## Unknowns and open work

- No edited file has been loaded on PS5. Only a human may promote Experimental to Stable.
- Some BL3 PS5 profile tags postdate the reference schema and remain opaque.
- Controlled game tests are needed for clamping, milestone challenges, low-level unlock dependencies
  and missing progression records. Existing mission/unlock data is preserved.
- Mission repair, arbitrary part-count/balance construction, slot compatibility and platform conversion are not implemented.
- Other regions/builds need samples. PPSA01494 is a separate BL4 format and never claimed.
- Inventory/reference/evidence work and bounded F9 observation are delivered. Complete cosmetic,
  exact skill-subobject/mission/challenge crosswalks, generated item effects and exact individual-part imagery remain open. Enabled writes remain Experimental;
  full gameplay dependency/budget models and console validation remain open.
- BL4 codec/file-role research and the exact owner checklist are in [BL4 feasibility](BL4-feasibility.md).

## Licence exclusions

The terms and import decisions below are recorded in the existing pinned source sidecars.
Resolved imports stay in this register; missing image/coordinate evidence is separate from a
licence refusal.

| Source / revision | Licence found | Disposition | Alternative | Unblock |
| --- | --- | --- | --- | --- |
| [BL3Skills, 66b0bd0](https://github.com/seigler/bl3skills.com/tree/66b0bd08777452abd05e5d9246c27f2a8096ca13); [skill terms](SaveToolkit.Plugins.Gearbox/ReferencePacks/gearbox.bl3/skills.source.md), [asset sidecar](SaveToolkit.Plugins.Gearbox/ReferencePacks/gearbox.bl3/assets.source.md), retrieved 2026-10-29 | Community calculator: MIT, [notice retained](Notices/Bl3Skills-MIT.txt). Publisher art/text: Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29 | Resolved: imported (owner decision 2026-10-29) | 315 skill-icon assignments, existing skill descriptions and 134 additional literal effect descriptions; separate publisher and community notices | None for the recorded imports; exact saved-subobject joins and omitted DLC trees still need evidence |
| [Wonderlands calculator, d57e7aa](https://github.com/Lothrik/wonderlands-skill-calc/tree/d57e7aa68af13afb50d4b54aa0ac5d501655c5b8); [skill terms](SaveToolkit.Plugins.Gearbox/ReferencePacks/gearbox.ttwl/skills.source.md), [asset sidecar](SaveToolkit.Plugins.Gearbox/ReferencePacks/gearbox.ttwl/assets.source.md), retrieved 2026-10-29 | Community calculator: MIT, [notice retained](Notices/WonderlandsSkills-MIT.txt). Publisher art/text: Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29 | Resolved: imported (owner decision 2026-10-29) | 110 skill icons and existing plain-text descriptions; separate publisher and community notices | None for the recorded imports; exact saved-subobject joins still need evidence |
| [Lootlemon](https://www.lootlemon.com), [terms](https://www.lootlemon.com/terms); retrieved 2026-10-29, exact page/image URLs and hashes in [BL3](SaveToolkit.Plugins.Gearbox/ReferencePacks/gearbox.bl3/assets.source.json) / [TTWL](SaveToolkit.Plugins.Gearbox/ReferencePacks/gearbox.ttwl/assets.source.json) sidecars | Publisher imagery and official card text: Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29. Community guide prose is outside the recorded import | Resolved: imported (owner decision 2026-10-29) | 23 curated thumbnails/flavor lines in local attributed asset datasets; no community guide prose copied | None for the recorded publisher imports; other material requires its own provenance/terms record |
| Map Genie [BL3](https://mapgenie.io/borderlands-3) / [Wonderlands](https://mapgenie.io/tiny-tinas-wonderlands), checked 2026-10-28; missing-coordinate status recorded in the 2026-10-29 asset sidecars above | Reuse terms unverified; no licence determination recorded | No current licence refusal; matching coordinate evidence missing | External guide and bounded mission checklist retained; no art, coordinates or descriptions copied and no invented alignment | Verified mission coordinates and a background transform; record applicable source terms/provenance before any import |
| Individual part artwork: no specific image source/revision identified; scoped searches summarized in this README and the BL3/TTWL asset sidecars above | No specific material/licence identified; calculator MIT notices do not license publisher artwork | Matching data not found in searched sources; no licence refusal established | Calculator repositories contain skill icons; inspected editor repositories contain UI glyphs, not exact individual-part images. Matching unique-item serial records reuse clearly labelled whole-item reference thumbnails | Exact individual-part imagery with a reliable join, source/revision and applicable rights attribution |

## Sources and attribution

Research date: 2026-10-29. Downloaded source was read as untrusted text, never executed. Independent
implementations and altered C# adaptations are identified below; reference resources contain schema,
numeric and display data with separate artwork/text attribution. Notices are retained
under [Notices](Notices/). Serial envelope/layout code is explicitly marked as an altered C# adaptation
of the pinned zlib `datalib.py` implementations. No downloaded implementation was executed.

| Dataset / code port | Pinned source | Author | Licence | Retrieved | Modification |
| --- | --- | --- | --- | --- | --- |
| BL3 `serial-widths` | [Resources][bl3-res] | CJ Kucera | [Zlib][bl3-license] | 2026-10-28 | All category/version bit widths → F2 records |
| BL3 `serial-indices` | [Resources][bl3-res] | CJ Kucera | [Zlib][bl3-license] | 2026-10-28 | All 1-based indices/asset paths retained |
| BL3 `balances` | [Resources][bl3-res] | CJ Kucera | [Zlib][bl3-license] | 2026-10-28 | Joined family labels; normalized/deduplicated balance paths; raw fallback |
| BL3 `part-families` | [Resources][bl3-res] | CJ Kucera | [Zlib][bl3-license] | 2026-10-28 | Exact balance→part-category crosswalk |
| BL3 `labels` | [Constants][bl3-constants] | CJ Kucera | [Zlib][bl3-license] | 2026-10-28 | Explicit slot/SDU/ammo labels; no capacity policy |
| TTWL `serial-widths` | [Resources][ttwl-res] | CJ Kucera / Abram Hindle | [Zlib][ttwl-license] | 2026-10-28 | All category/version bit widths → F2 records |
| TTWL `serial-indices` | [Resources][ttwl-res] | CJ Kucera / Abram Hindle | [Zlib][ttwl-license] | 2026-10-28 | All 1-based indices/asset paths retained |
| TTWL `balances` | [Resources][ttwl-res] | CJ Kucera / Abram Hindle | [Zlib][ttwl-license] | 2026-10-28 | Joined family labels; normalized/deduplicated balance paths; raw fallback |
| TTWL `part-families` | [Resources][ttwl-res] | CJ Kucera / Abram Hindle | [Zlib][ttwl-license] | 2026-10-28 | Exact balance→part-category crosswalk |
| TTWL `labels` | [Constants][ttwl-constants] | CJ Kucera / Abram Hindle | [Zlib][ttwl-license] | 2026-10-28 | Explicit slot/SDU/ammo labels; no capacity policy |
| BL3 `world-labels` | [Constants][bl3-constants] | CJ Kucera / apocalyptech | [Zlib][bl3-license] | 2026-10-28 | 1,387 explicit mission/map/travel/class/named-cosmetic records; normalized IDs and original IDs retained |
| TTWL `world-labels` | [Constants][ttwl-constants] | CJ Kucera / Abram Hindle | [Zlib][ttwl-license] | 2026-10-28 | 459 mission/map/travel/class/Myth/Hero/backstory records; embedded caps used as Inferred editor policies |
| BL3 `skills` / `skill-icons` | [BL3Skills 66b0bd0](https://github.com/seigler/bl3skills.com/tree/66b0bd08777452abd05e5d9246c27f2a8096ca13) | Gearbox Software / 2K; Joshua Seigler and contributors | Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29; community [MIT notice](Notices/Bl3Skills-MIT.txt) retained | 2026-10-29 | 315 icon assignments; 134 static effect lines added to descriptions; no formulas or saved-subobject joins |
| TTWL `skills` / `skill-icons` | [Wonderlands calculator d57e7aa](https://github.com/Lothrik/wonderlands-skill-calc/tree/d57e7aa68af13afb50d4b54aa0ac5d501655c5b8) | Gearbox Software / 2K; Joshua Seigler / Lothrik and contributors | Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29; community [MIT notice](Notices/WonderlandsSkills-MIT.txt) retained | 2026-10-29 | 110 named skill icons with existing plain-text descriptions and calculator facts; no saved-subobject joins |
| BL3/TTWL `item-icons` / `item-cards` | [Lootlemon](https://www.lootlemon.com), [terms](https://www.lootlemon.com/terms); exact image/page URLs and hashes in each `assets.source.json` | Gearbox Software / 2K; Levin / Lootlemon contributors | Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29 | 2026-10-29 | 12 BL3 + 11 TTWL thumbnails and official flavor lines; 87 exact unique-directory serial-index thumbnail joins; no community guide prose or generated stats copied |
| BL3 `localized-cards` | [Sandsmark editor labels](https://github.com/sandsmark/borderlands3-save-editor/tree/a1c54a2fd6c190029e3e724ac0a0a5c65d3b4fc2/data) | Sandsmark / community contributors | No licence published; reused with attribution per owner decision 2026-10-28 | 2026-10-29 | 499 human labels joined to pinned balance object tokens; no artwork or extracted game text |
| BL3 `part-labels` | [Sandsmark description tables](https://github.com/sandsmark/borderlands3-save-editor/tree/a1c54a2fd6c190029e3e724ac0a0a5c65d3b4fc2/data/descriptions) | Sandsmark / community contributors | No licence published; reused with attribution per owner decision 2026-10-28 | 2026-10-29 | 2,100 factual part labels/effect notes; raw fallback retained |
| BL3 `skill-crosswalk` | [Sandsmark editor data](https://github.com/sandsmark/borderlands3-save-editor/tree/a1c54a2fd6c190029e3e724ac0a0a5c65d3b4fc2/data) | Sandsmark / community contributors | No licence published; reused with attribution per owner decision 2026-10-28 | 2026-10-29 | Three Beastmaster tree-prefix joins; exact skill names are never inferred by order |
| TTWL `part-labels` | [TTWLSaveEditor mapping](https://github.com/somefunguy/TTWLSaveEditor/tree/c6d701ec4a5ae1c115017de54d39df4813c035c1/BL3Tools/GameData/Items/Mappings) | somefunguy / contributors | No licence published; reused with attribution per owner decision 2026-10-28 | 2026-10-29 | 4,327 raw path-to-token labels; not presented as localized game text |
| TTWL `skill-crosswalk` | [TTWLSaveEditor mapping](https://github.com/somefunguy/TTWLSaveEditor/tree/c6d701ec4a5ae1c115017de54d39df4813c035c1/BL3Tools/GameData/Items/Mappings) | somefunguy / contributors | No licence published; reused with attribution per owner decision 2026-10-28 | 2026-10-29 | Seven class tree-prefix joins; exact skill names are never inferred by order |
| BL3/TTWL export provenance | [`bl3data`](https://github.com/apocalyptech/bl3data/tree/b0bfc16865d82d01bed7104bb357561c702c2561), [`wldata`](https://github.com/apocalyptech/wldata/tree/7d1d53ff86f227b4574fd8b1a294590b42b5f7aa) | apocalyptech / community contributors | No licence published for the game exports; reused with attribution per owner decision 2026-10-28 | 2026-10-29 | Object/path conventions were cross-checked; no proprietary export payloads, artwork or icons are redistributed |
| External community map links | [BL3](https://mapgenie.io/borderlands-3), [Wonderlands](https://mapgenie.io/tiny-tinas-wonderlands) | Map Genie | Reuse unverified; **link only** | 2026-10-28 | No art, coordinates, map content or descriptions imported; automated access was blocked during that research |
| Equipment vacancy (`-1`) | BL3 `bl3save.py` at [d459b28][bl3-root]; TTWL `ttwlsave.py` at [1fde90d][ttwl-root] (direct links in F14 section) | CJ Kucera / apocalyptech | [Zlib][bl3-license], [Zlib][ttwl-license] | 2026-10-29 | Existing format fact cross-checked against supplied PS5 snapshots; original F14 presentation implementation, no copied code |
| SDU tiers, item flags and new-record metadata policy | [BL3 constants][bl3-constants], [TTWL constants][ttwl-constants], BL3 `bl3save.py`, TTWL `datalib.py` in the pinned repositories | CJ Kucera / Abram Hindle | [Zlib][bl3-license], [Zlib][ttwl-license] | 2026-10-28 | Altered C# definitions/flag handling; capacity formulas are original conservative editor policies |
| Serial envelope / layout adapter | [BL3][bl3-serial], [TTWL][ttwl-serial] | CJ Kucera / Abram Hindle; upstream credits Baysix | [Zlib][bl3-license], [Zlib][ttwl-license] | 2026-10-28 | Altered bounded C# adaptation with exact-bit writes and original-seed CRC repair |
| `Bl3.tsv`, `Bl3Xp.tsv` | [BL3 editor][bl3-root] | CJ Kucera; schemas credit Rick/Gibbed | [Zlib][bl3-license] | 2026-10-28 | Separate schema facts and exact XP thresholds |
| `Wonderlands.tsv`, `WonderlandsXp.tsv` | [TTWL editor][ttwl-root] | CJ Kucera / Abram Hindle; schemas credit Rick/Gibbed | [Zlib][ttwl-license] | 2026-10-28 | Separate schema facts and exact XP thresholds |
| PS4 feedback masks | [Gibbed crypt files](https://github.com/FromDarkHell/BL3SaveEditor/tree/c044e6d6f1fd78b259f2147119fbe2cdbbc7a3ee/BL3Tools/Decryption) | Rick/Gibbed | Zlib, retained notice | 2026-10-28 | Independent managed feedback implementation / test probe |

[bl3-root]: https://github.com/apocalyptech/bl3-cli-saveedit/tree/d459b28a7e88f1a5ae0f16118272d8a5383b8b04
[ttwl-root]: https://github.com/apocalyptech/ttwl-cli-saveedit/tree/1fde90dc43e7f7ee261413d85c366e0a7236dd9b
[bl3-res]: https://github.com/apocalyptech/bl3-cli-saveedit/tree/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/resources
[ttwl-res]: https://github.com/apocalyptech/ttwl-cli-saveedit/tree/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/ttwlsave/resources
[bl3-constants]: https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/__init__.py
[ttwl-constants]: https://github.com/apocalyptech/ttwl-cli-saveedit/blob/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/ttwlsave/__init__.py
[bl3-serial]: https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/datalib.py
[ttwl-serial]: https://github.com/apocalyptech/ttwl-cli-saveedit/blob/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/ttwlsave/datalib.py
[bl3-license]: https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/COPYING.txt
[ttwl-license]: https://github.com/apocalyptech/ttwl-cli-saveedit/blob/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/COPYING.txt

- [apocalyptech/bl3-cli-saveedit, d459b28](https://github.com/apocalyptech/bl3-cli-saveedit/tree/d459b28a7e88f1a5ae0f16118272d8a5383b8b04):
  `bl3save.py`, `bl3profile.py`, `__init__.py`, `protobufs/Oak{Save,Profile,Shared}.proto`: framing,
  schema facts, currency hashes, XP thresholds, level/key semantics. Also [save documentation](https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/README-saves.md).
  Contrary to the task's GPLv3 note, this revision's COPYING/source headers use **zlib** (CJ Kucera);
  retained as `Notices/Bl3-COPYING.txt`. Protobuf research credits Rick/Gibbed.
- [apocalyptech/ttwl-cli-saveedit, 1fde90d](https://github.com/apocalyptech/ttwl-cli-saveedit/tree/1fde90dc43e7f7ee261413d85c366e0a7236dd9b):
  corrected repository name (`wl-cli-saveedit` returned 404); Wonderlands schemas/XP, dual classes,
  moon orbs/skeleton keys, Chaos mapping and skill-point exceptions. **zlib**, retained as
  `Notices/Wonderlands-COPYING.txt`.
- [FromDarkHell/BL3SaveEditor, c044e6d](https://github.com/FromDarkHell/BL3SaveEditor/tree/c044e6d6f1fd78b259f2147119fbe2cdbbc7a3ee/BL3Tools/Decryption):
  `SaveBogoCrypt.cs` / `ProfileBogoCrypt.cs`: PS4 mask facts/feedback behavior; `GameData/Experience.cs`
  cross-checks BL3 levels. Repository **GPL-3.0**, but these crypt files carry Rick/Gibbed's **zlib**
  notice, retained as `Notices/Gibbed-BogoCrypt.txt`. No GPL application/UI code was copied.
- [xcier/TTWLSaveEditor, 48984d8](https://github.com/xcier/TTWLSaveEditor/tree/48984d81a9a9f11951c7d3c4bca48f8cbc0abc11/BL3Tools):
  Wonderlands schema/XP comparison and matching PS4 masks; repository **GPL-3.0**, crypt file headers
  **zlib**. Reference only; no application code copied.
- [cfi2017/bl3-save, fdfc80b](https://github.com/cfi2017/bl3-save/tree/fdfc80b1781ad386e2182eb6ca4dad07a9be94c8):
  **MIT** web/CLI editor, reviewed for ecosystem/support scope, no implementation code used.
- [ZakisM/bl3_save_edit](https://github.com/ZakisM/bl3_save_edit): **GPL-3.0**; decrypted PS4 support
  documentation and attribution to HackerSmacker's PS4 mask research. Sample tests, not that support
  claim, establish PS5 compatibility here.
- [Protocol Buffers wire specification](https://protobuf.dev/programming-guides/encoding/): tags,
  wire types, packed fields and unknown-field behavior.
- [Google.Protobuf 3.36.2](https://www.nuget.org/packages/Google.Protobuf/3.36.2): managed C# runtime,
  **BSD-3-Clause** package dependency; no build-time compiler dependency.

## SDK requests

**GEARBOX-R4 / F7: resolved by [#29](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/29);
writable adoption implemented.** Supported bank writes use F6 inside F7. The delivered contract
addresses the original requirements:

1. Explicitly select a supported title, character path and matching profile path from the **same imported
   generation**; report unsupported/missing/ambiguous partners before opening an owning transaction.
2. Acquire one owner and version/fingerprint locks over all affected imported paths, detect other dirty
   sessions and reject overlapping writers. Never pick a sibling generation or resolve a dirty conflict silently.
3. Let the plugin validate all container/serial relationships and capacities against one immutable input
   set, then stage all changes atomically. A failure must publish no changes to either live document.
4. Record one undo/redo unit restoring bytes **and stable duplicate identities** across both documents.
   Reconcile queries/lazy pages and invalidate stale revisions together.
5. Preflight every `FileOutput` against the locked fingerprints, re-open **each** character/profile output
   in its correct role, reject overlaps and publish/export the set atomically. Define backup ownership
   explicitly; do not silently overwrite independent backup sessions.

F7 supplies explicit proposals and immutable inputs (1/3), exclusive ownership/fingerprint and dirty
conflict checks (2), candidate staging with full snapshots/rebinding and shared revisions (3/4), and
all-member role-specific validation/atomic export with explicit backup ownership (5). Its framework
tests and this plugin's sample-backed tests cover writable adoption, transfer history/export and
fresh-snapshot restoration. Planned coordinated actions use F6 planning and F7 staging with F5
structural snapshots. F12's `ISaveDecoderProvider` / `gearbox-evidence` and F13 inspection/browse
contracts are also adopted. Shared binary/string/CRC and reference-icon helpers are implemented
as listed under Framework feature adoption; these are resolved integrations, not SDK requests.
No outstanding framework contract request or alternative output API is needed. Remaining capture,
console-validation and gameplay-model gaps are listed above and tracked in [#31][owner-validation].

## Owner capture and validation checklist

- For each BL3/TTWL capture, record PS5 game version, region, DLC/hotfix state, slot and a transcribed
  item card; include the character, matching profile and backup from each snapshot. Do not include
  credentials or account-derived codec material in reports.
- Export all affected character/profile directories together immediately before and after each action,
  with matching backups. State which files form one gameplay capture; occurrence/generation numbers
  alone do not prove a match. For profile-wide operations include every character that can supply a mirror.
- Capture a no-action save/reload/save pair, then separate before/after pairs for **one** pickup,
  sale/removal, equip/unequip, favourite/junk toggle, backpack↔bank move, lost-loot claim and
  enchantment/reroll. Include duplicates and full/near-full containers; isolate each action.
- Bank/structure: capture one backpack→bank move and a separate bank→backpack move, then one lost-loot
  claim. Repeat with two byte-identical items, empty/near-full/full destination and equipped/un-equipped
  source items; record the exact slot, capacity, order, equipped slots and card/count before/after.
  For sale/removal, isolate first/middle/last inventory positions and verify surviving equipment indices.
- Include BL3 starter guns/cosmetic pickups/legacy serial versions, and TTWL level-40/endgame gear
  with nonzero rerolls and each observed Chaos tier. Current TTWL characters are level 12.
- Serials: capture one enchant/reroll and one favourite/junk toggle separately, with all card fields
  transcribed. Supply legitimate same-family level/Mayhem/Chaos examples and all observed serial
  versions, keeping uncontrolled examples distinct from controlled same-item action pairs.
- Existing-part picker: on a disposable backed-up item, change one regular part and separately one
  generic/anointment part within its known category. Record original/replacement raw tokens, exact
  cards, DLC/hotfix/build, load acceptance, stat changes and any rejection/removal. Re-save/reload and
  export matching character/profile/backups. Width/category acceptance is not a legal-pool claim.
- Skill joins: capture no-action controls and exactly one named passive allocation in each relevant
  class/tree/tier, then a separate respec. Record the displayed name, rank/cap, unspent points and
  primary/secondary class before/after. These pairs must identify the changed opaque subobject;
  calculator order or matching rank caps alone are insufficient. Include BL3 fourth-tree/DLC examples.
- Capture one level gain, one skill allocation/respec, one SDU purchase and one ammo refill, separately.
  Guardian: profile + multiple characters before/after one token/perk. Myth: each indexed allocation,
  rotation and capped stat, with matched profile. These validate the enabled Experimental writes.
- Guardian: capture a no-action load of each affected character after spending exactly one profile
  token or enabling one perk, returning profile + every character after each load to establish mirror
  precedence. Myth: start at level 40, capture one allocation in each tree, one rotation boundary and
  one capped-stat boundary; record XP, displayed rank, available points and selected stat on both sides.
- On a backed-up spare slot, validate each enabled write family independently: restore, load, confirm
  intended visible effect and unrelated inventory, re-save, reload, then return the new dump plus
  observations. Report game clamping. Only the owner can promote maturity to Stable.
- For BL4 use the separate [file-role/codec capture checklist](BL4-feasibility.md). No BL4 writes.

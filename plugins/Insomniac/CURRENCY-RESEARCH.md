# Rift Apart currency and upgrade discovery (#40)

2026-10-29; decrypted PPSA01474 only. Public sources, access dates and terms are in
the [README](README.md#field-discovery--bolts-raritanium-and-upgrades-40) and
[currency source notice](SaveToolkit.Plugins.Insomniac/Resources/CurrencyFields.source.md).
This addendum supersedes the old career-minus-spent wallet interpretation, while
preserving the historical measurements in RESEARCH.md and EVIDENCE.md.

## Method and coverage

Read the external archive without modifying it. Scratch harness, scripts, downloaded
text dictionaries/schema, derived comparisons and test temp live under
`D:/Temp/saveeditor/test-scratch/research-insomniac-currency`. No sample archive was
copied/junctioned into the checkout. Downloaded implementations were never executed.

The harness references the local plugin and uses `Dat1Container.Parse`, `DdlSections`,
`InventoryReader` and the production `RiftEvidenceDecoder` via `HookSaveDecoder`.
It decoded **72 gameplay copies**: **36 primary records** across both generations/
campaigns and their 36 backup twins. Both primary preferences and their backup twins
are covered by the existing 76-file no-op/conformance tests; they contain no currency
inventory. Neither raw campaign IDs nor payloads are added to the repository.

Candidates were compared by **typed record/config identity**, not fixed file offsets
or coincidental small/large words. All 36 inventories have 27 bounded containers.
All bolt and present raritanium records have the same 23-byte generic layout:

| Payload offset | Storage | Evidence |
| --- | --- | --- |
| +0 | `0F` | Supported full presence mask |
| +1 | 8-byte instance token | Not a currency/actor identity; retained raw |
| +9 | 8-byte config token | Public path hash, reproduced independently on PS5 |
| +17 | LE u32 ItemCount | Variable count; exact bytes and semantic-hook count agree |
| +21 | BE u16 ItemId | Varies across samples; rejected as a stable currency key |

The public hash list identifies **bolts** (`947dc517d343ad64`) and **raritanium**
(`95ffa4c1254a6f29`). Every bolt record is unique across its complete inventory;
raritanium is unique in 34/36 primary records and absent in two. Observed counts are
315–320,640 bolts and 5–109 raritanium when present. Missing does not establish zero.
The view requires a unique supported match across all containers and rejects
ambiguous matches instead of selecting an occurrence or summing caches.

## Differential results

Each row is an independent derived observation, not a controlled action capture.
All 36 primary values and their backup twins are pinned in `CurrencyCorpusTests`.

| Slot/snapshot (generation) | Career/spent bolts | Inventory bolts | Career/spent raritanium | Inventory raritanium |
| --- | --- | ---: | --- | ---: |
| slot0 snapshot 1802099279 (0) | 4,713 / 2,500 | 2,213 | 0 / 0 | Absent |
| slot0 snapshot 353949189 (0) | 11,013 / 7,500 | 315 | 10 / 10 | Absent |
| slot0 autosave (1) | 23,444 / 14,858 | 2,417 | 40 / 20 | 20 |
| slot0 snapshot 108976056 (0) | 363,925 / 226,875 | 37,856 | 306.7997131347656 / 304 | 72 |
| slot0 snapshot 1867211690 (0) | 477,161 / 226,875 | 151,092 | 328.8497009277344 / 304 | 94 |
| slot0 autosave (0) | 977,095 / 260,875 | 320,640 | 531.1996459960938 / 584 | 109 |
| slot1 autosave (0) | 1,297,735 / 260,875 | 320,640 | 531.1996459960938 / 584 | 109 |

Early raritanium records agree with career-minus-spent where present; later they
do not. Snapshot 108976056 → 1867211690 has a career gain of about 22.05, inventory
gain 22 and unchanged spending 304. The later jump to spending 584 still leaves 109
in inventory. Armor bonuses and entitlement/reward contributors are plausible
research leads, not an established accounting equation.

Bolt counters likewise cannot substitute for inventory: the late autosave's counter
difference is **716,220**, versus **320,640** in the bolt item. The slot1 transition
adds 320,640 to career bolts with unchanged spent and inventory counts. This is
uncontrolled branch/mode evidence, not proof of a conversion rule. The early
snapshot 1802099279 happens to agree; that single match cannot calibrate a wallet.
The old field ID and writer IDs remain compatible, but their UI now explicitly
describes a **counter diagnostic**. Its target action changes only CareerBolts and
does not edit the newly discovered inventory count.

## Weapon upgrade evidence and missing joins

All **1,120** supported primary weapon/melee records retain the existing inferred
Mods / sixteen BE u16 ModGrid rows / thirty-two u8 ModCount slots / SelectedCell /
Omega interpretation. Aggregate checks across the *entire inventory* give:

| CareerRaritaniumSpend | Sum of grid set bits | Sum of ModCount bytes | Sum of Mods set bits |
| ---: | ---: | ---: | ---: |
| 0 | 0 | 0 | 0 |
| 10 | 13 | 12 | 6 |
| 20 | 23 | 22 | 9 |
| 304 | 398 | 337 | 152 |
| 584 | 725 | 630 | 263 |

These are deliberately **not** costs, purchased-node totals or deduplicated owned
weapons. Repeated containers and shared/cached records lack an ownership join. All
Omega candidates remain false. The per-sample tests pin the full aggregates rather
than interpreting population counts as a spend equation.

The CC0 schema names a concrete config dependency chain:
`WeaponItemConfig.WeaponConfig` → `WeaponConfig.GridConfig` / `ModConfig` →
`WeaponGridConfig.Layers` → `GridLayerData.Tiles` → `GridTileData.GridIndex/TileIndex`,
and `WeaponModConfig.Mods` → `WeaponModData` (Cost, Type, Value, TileIndex and localization
tags). Public hash entries include `configs/equipment/wpn_burstpistol_grid.config`
(`a3d1d0a4da444869`) and `wpn_burstpistol_mod.config` (`bfdb6c0dc06000a5`), but **paths
and type declarations contain no build-matched instance data**. No PS5 mod costs,
tile-to-mask orientation, prerequisite graph, golden-node award rule, stat repair
or owned-record synchronization recipe was obtained. PC runtime offsets are not
ported. Existing raw state stays read-only/Inferred; named purchase nodes remain
a negative result.

## Integrity and next evidence

DAT1 section bounds, DDL names/types and inventory framing are understood and
reparsed on Serialize. Name CRC32 and config CRC64 are identity hashes, not body
checksums. No public PS5 save-body checksum/signature or repair routine was found.
Counts can be inspected without mutation, but no **new** write is justified. In
particular, `CurrencyItemConfig.MaxCount` describes a config field and zero-as-u32
capacity convention; its actual PS5 instance value is absent. It is not a game cap.

The next useful evidence is one same-build/slot no-action churn pair, then separate
gain-one / spend-one captures with both HUD counts, the selected weapon/node and
displayed price. Include before/after native save/load/re-export and the relevant
PS5 currency/grid/mod config instances. Compare inventory counts, campaign counters,
all copies of the weapon's Mods/Grid/Count state and bonus/award transitions. This
would test the wallet interpretation, caps, node joins, dependency synchronization
and integrity; warning text or a guessed subtraction cannot replace it.

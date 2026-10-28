# Read-only roadmap evidence (#20)

The original sections below describe round 1. The round-2 addendum records the
newly corroborated joins and explicitly supersedes the earlier unresolved-path notes.

Validated 2026-10-28 against the private PPSA01474 archive. No private save or
extracted payload is committed. Historical integrity research remains in
[RESEARCH.md](RESEARCH.md); this document records the new structural observations.

## Corpus and DDL

- All 76 game records retain byte-identical no-op serialization: 36 primary gameplay
  records, two preferences and 38 identical backup twins. Duplicate ZIP paths remain
  distinct generations. The two campaign IDs are used only inside private comparisons.
- Twelve gameplay DDL roots and the preference root are decoded recursively. Segment
  roots have a four-byte prefix; objectives have eight. Prefix bytes are preserved.
- Names/hash words, string bounds/UTF-8, stored numeric types, object sizes, array
  counts and parent bounds are checked. Nested map keys are actual stored field names,
  including hexadecimal identities. No schema ordinal is substituted for a key.
- An unaligned nested object's descriptor/value boundary aligns to four bytes in
  the file; scalar fields then remain packed. The activity `DerivedSaveData` is a
  concrete example. Synthetic tests cover this independently of sample bytes.
- Reader budgets per section: depth 32, 32,768 objects, 65,536 fields, 524,288 values,
  four MiB decoded text, 1,024 fields per object and 65,536 bytes per string. The
  largest fog section needs more than the old 4,096-value root budget. Views expand
  arrays in pages of 100 and keep raw bytes available.
- The representative late save has 15 map-area keys, 47 award records and ten
  objective graph keys. Map dimensions/bounds are stored observations. Set-bit counts
  include padding bits and are not an exploration percentage. Award codenames are
  neither mission labels nor proof of platform trophy status.

## Inventory framing

The `00D0A9DF` section is traversed sequentially, not split at marker occurrences:

1. LE u32 container tag and LE u32 declared payload length.
2. LE u32 `0x494E5654` (`TVNI` bytes), followed by LE u32 record count.
3. Exactly that many records: LE u32 schema type, LE u32 payload length, bounded bytes.
4. Remaining container state is retained opaque. Two observed tag families have a
   separate four-byte zero suffix outside their declared payload length.

Every boundary reaches EOF exactly across all 36 primary gameplay records. There
are 27 containers in each file, not 27 weapons. Container tags repeat; UI identities
use tag plus occurrence. Containers are not assigned to Ratchet, Rivet, equipped,
owned or shared state without controlled evidence.

| Container tag | Opaque bytes inside declared payload | External suffix |
| --- | ---: | ---: |
| `78CB152E` | 4 | 0 |
| `62D731C8` | 218 | 0 |
| `37475015` | 0 | 4 zero bytes |
| `272E811F` | 0 | 0 |
| `C7D08B36` | 23, 298 or 309 | 0 |
| `31D06004` | 0 | 0 |
| `4BF3942C` | 0 | 4 zero bytes |

The tail sizes are observations, not parser constants or edit rules. Unknown record
types and unfamiliar presence masks retain bounded opaque payloads.

## Item type and field candidates

All five record type IDs match IDs and seeded name hashes in Rivet's CC0 schema:

| Stored type | Schema name | Measured payload | Prefix bytes |
| --- | --- | ---: | --- |
| `4E5CE3CB` | GameItemPrius | 23 | `0F` |
| `388B19BC` | GadgetSwingshotItemPrius | 23 | `0F` |
| `A89E9C3C` | WeaponItemPrius | 119 | `FF 3F` |
| `2F1E071F` | MeleeWeaponItemPrius | 119 | `FF 3F` |
| `F6AD1389` | VanityItemPrius | 460 | `FF 1F` |

Matching schema fields suggest presence bits, eight-byte GUID/config tokens, LE u32
item count and type-specific state. Tokens are displayed in stored byte order;
numeric asset-ID normalization and config-path hashing are not established.
Instance keys use the exact GUID candidate plus duplicate occurrence within a
container. Unknown layouts use an explicitly positional opaque key.

For the fixed 119-byte weapon layout, payload offsets 24/28/32 give BE u32 XP and
BE f32 checkpoint/loaded-ammo candidates. These differ from LE framing/counts.
The fields match schema order and vary plausibly across saves; values alone are not
HUD confirmation. All remain **Inferred**, with no level conversion, cap, refill,
purchase or ownership behavior. Non-finite candidate values suppress interpretation
and retain the record instead of rejecting an unproven semantic layout.

## F1 differential checks

`RiftEvidenceDecoder` in the test project is an F1 adapter. It emits
campaign counters, stored wire kinds, inventory identities, payload fingerprints,
state candidates and world collection sizes. It omits account/system metadata and
avoids materializing every fog byte as a semantic value.

- `DifferentialEvidenceTests.SameCampaignSnapshotsKeepIdentityWhileWeaponStateCandidatesChange`
  compares snapshot `108976056` with the late autosave after first checking campaign
  equality. Career bolts change 363925 → 977095; at least ten weapon candidate paths
  match by identity, and XP/ammo candidates change. Both trees and the diff are complete.
- The generation test uses F1 `SaveSetDiffer` on ZIP generations 0 → 1 of the autosave,
  observes 977095 → 23444 career bolts and collapses only byte-identical backup twins.
  This second comparison spans generations and is not claimed to isolate one action.
- Neither comparison is a one-variable experiment. They establish framing, persistent
  identity candidates and changing state, not item names, legal bounds or causality.

Run the tests using the README command with `SAVETOOLKIT_ARTIFACTS` set in the same
shell invocation. Fixture expectations contain gameplay facts only; no save bytes,
account IDs or archive contents are stored in the repository.

## Source and confidence boundary

Schema facts: [Neptuwunium / Rivet, a631c96e](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json),
[CC0 sidecar](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json.license).
Section strategies were cross-checked with the pinned
[SaveData research](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet/Data/SaveData.cs).
No upstream implementation was copied or executed. New inventory framing and mixed
byte-order candidates were independently tested against the PS5 corpus.

Complete integrity coverage, config/localization joins, ownership, legal limits,
collectible locations and console acceptance remain open. There are no writable
bindings or mutation actions, and maturity remains ReadOnly.

## Round 2: config identities and weapon/vanity state

All 36 primary gameplay records were rechecked. They contain **1,120** supported
weapon/melee records and **3,687** supported vanity records. These are serialized
records across snapshots, not counts of distinct owned items. Twenty candidate
weapon paths hash to twenty observed non-melee config tokens. Six further non-melee
config tokens remain outside the friendly-name catalog, including an internal
`blaster` path candidate; no NPC/actor/ownership meaning is assigned to them.

The independently implemented asset identity uses slash-normalized lower-case paths,
reflected CRC64 polynomial/seed `C96C5795D7870F42`, no final inversion, a two-bit right
shift and high asset flag. Reference: pinned ALERT `crc64.py` (research only, GPL;
no code/table copied). Token byte order is big endian. Known vectors:

| Path suffix under `configs/equipment/` | Stored config token |
| --- | --- |
| `wpn_burstpistol_item.config` | `b33fb3994f5e7ec3` |
| `wpn_mrfunguy_item.config` | `8d632a5622511f00` |
| `wpn_warmonger_item.config` | `b29993da27515a36` |
| `wpn_warprifle_item.config` | `87ab40964da66dda` |

The twenty friendly names/descriptions are **inferred localization aliases**, not
proven build-specific localization joins. All catalog entries carry confidence and
attribution. Unresolved tokens, optional absent packs and unknown bits remain usable.

Supported weapon/melee full-mask layout (offsets relative to its 119-byte payload):

| Offset / bytes | CC0 schema slot | Interpretation |
| --- | --- | --- |
| 22 / 2 | ItemId | BE u16 candidate; corrects the unexamined byte-order assumption for this slot |
| 24 / 4 | XP | BE u32 candidate |
| 28, 32 / 4 each | LastCheckpointAmmo, LoadedAmmo | BE f32 candidates |
| 44 / 8 | Mods | BE u64 candidate mask |
| 52 / 32 | ModGrid | Sixteen BE u16 row masks |
| 84 / 32 | ModCount | Thirty-two u8 counters |
| 116 / 1 | SelectedCell | u8 candidate |
| 117 / 1 | Test | Observed empty collection; nonzero suppresses upgrade interpretation |
| 118 / 1 | Omega | bool candidate; all 1,120 observed values are false |

Unknown masks, lengths, nonempty Test or unsupported Omega encoding retain opaque
state instead of inventing a new layout. ModGrid masks and ModCount counters change
in the F1 same-campaign comparison; there is no proven grid coordinate, node name,
prerequisite, price or effective stat join. No positive Omega/cap evidence exists.

VanityItemPrius has 460-byte full-mask payloads. Nested locator/texture/material/
color/conduit/model-variant presence bytes are checked before interpreting its tail.
Offsets 453/457/458/459 give BE u32 ExpirationTime, u8 BeenPreviewed, nested Color
presence mask and u8 ColorId. All measured expirations/preview codes are zero;
observed color codes include 0, 2, 33, 34, 35 and 37. No color enum/cap is assumed.
Config-to-piece and equipped character/slot joins remain unresolved. Raw override
bytes are preserved; the UI does not equate these records with collection bonuses.

## Round 2: armour and world joins

The CC0 enum `D230F155` supplies 57 VanityBundleSaveId candidates; all 64 stored
VanityPickupsCollected bits remain visible, including unresolved/reserved positions.
The population of base-game bits 0–26 **excluding preorder bits 9–11** equals the
sum of LevelVanityPickups in every supplied primary record. Set/slot names and bonus
categories are attributed CC BY-SA derivatives, with inferred ordinal and set aliases.
Menu-viewed flags are independently shown from exact NewItemsViewed strings. No
effective bonus percentage, availability or equipped appearance is derived.

The CC0 enum `81D3E717` supplies ten RYNO/spybot labels. Its inferred bit positions
map to LevelRYNOPlans indices **[2, 3, 4, 4, 8, 7, 6, 5, 9, 10]** respectively.
Grouped bit counts equal the entire stored per-location array in **all 36** records,
including the two distinct Zurkon's/Scarstu bits. Location labels come from the
eleven-entry CC0 enum `4223680A`; index 0 remains unresolved. These cross-checks
corroborate the joins but do not replace controlled pickup/location captures.

Collectible views show per-location gold-bolt/spybot/vanity/raritanium counters and
complete 64-bit RYNO, RuinTokens and ZerpStones checklists. No exact gold-bolt hash,
Lorb, Zurpstone or CraiggerBear guide instance is invented. Unset/reserved bits are
not labelled as existing missing pickups; lifetime/current-run semantics remain open.

World views flatten GraphDataTable/NodeDataTable into stored MissionState,
ObjectiveState, WasStarted and CompletedCount values. Current-save NewMissionsViewed
codes join to numeric node keys through their seeded name CRC. The UI retains raw
states/codes; a viewed code is not completion proof. Dimensions show actual Complete
flags; globals retain raw keys/values. Portal/arena labels and map transforms need
the owner pairs in [OWNER-CAPTURES.md](OWNER-CAPTURES.md).

CraiggerBear and other collectible awards additionally expose separate CurrentValue
and PrizeGiven fields. The late All_CraigBears record has CurrentValue=1 with
PrizeGiven=false. The view does not turn that progress into a bear count, completion
flag or platform unlock, and still requests exact bear-pickup captures.

Twelve F8 collections provide text search, exact column filtering, numeric/text
sorting and stable-key offset pages of at most 200 rows. Only returned page cells
become ViewNodes/bindings. Tests check 37-row walks, no repeated/missing keys, every
read-only binding, Core DocumentSession acceptance, name absence fallback and
single-page construction from a 1,000-row synthetic collection.

## Round 2: write boundary

`WriteBoundaryTests` mutates **isolated in-memory copies only**. It locates fields
through the parsed descriptors, adds one CareerBolts unit in the early UInt16 and
late UInt32 autosaves, and independently toggles the late save's existing RPGEnabled
bool (seven gold bolts present). Each probe preserves size and every byte outside
the target width, reparses and gives **exactly one F1 semantic atom change**. No
research candidate is written to disk or exposed through the production plugin.

This is parser acceptance only. Another probe changes CareerExp without repairing
the BC60B495 mirror, and the file **still reparses**. This demonstrates a concrete
limit of using the parser as a write-integrity oracle. The historical negative
digest scan is not an integrity specification and was not rerun/rebranded as new
evidence. Complete integrity, console load/effect/re-save/reimport and independent
preference acceptance remain unproven. At the end of round 2, maturity stayed
ReadOnly with no setters/actions; the write-enable decision below supersedes that gate.

Required exact captures, positive Omega evidence, separate armour collect/equip/color
pairs and baseline/bolt/RPG/prefs acceptance protocols are numbered in
[OWNER-CAPTURES.md](OWNER-CAPTURES.md). Round-2 read-only view delivery does not mark
the plan's owner-dependent semantic/write acceptance requirements complete.

## Write-enable round (supersedes the historical write gate)

Owner decision: “Enable writes. Keep warnings.” Console evidence is now tracked
separately in issue #31. Version 0.5.0 is Experimental after edit conformance passed.
The round-2 integrity observations above still apply; no body-integrity algorithm
or console acceptance has been established.

Production writes cover CareerBolts, SpentBolts, seven gold-bolt booleans and 61
existing scalar boolean preference keys. Corpus upper bounds are 1,297,735 career
and 260,875 spent bolts; actual stored unsigned widths further constrain them.
The final atomic balance must be nonnegative. All writable controls keep Inferred
confidence and an explicit game-may-reject warning.

The sample-backed suite passes 94 tests with zero skipped. New checks independently
edit every exposed scalar in both representative gameplay widths and both primary
preference records, then serialize/reopen, compare every byte and F1 semantic atom,
reject wrong types/ranges and undo/redo through Core. Nine-control atomic batches
also persist and undo byte-identically across all 36 primary gameplay generations.
All 76 supplied records still pass byte-identical no-op output. These are toolkit
validation results, not PS5 acceptance evidence. See README for remaining unsafe
dependencies and OWNER-CAPTURES.md for the exact console checklist.

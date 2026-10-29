# Pragmata (`pragmata`)

PS5 title **PPSA02530**. Plugin **0.6.0**; maturity: **Experimental**. All supplied samples pass
integrity, parsing, editing and byte-for-byte reversal checks. No console load
test has been performed; only a human should promote this plugin to Stable.

## What works

- Gameplay overview: slot, console-local save date, hours played, cleared flag,
  difficulty hash and editable fixed-size header fields.
- Inventory: existing item IDs in hex and decimal, int32 quantities, equipped
  marker, and F6 preview/apply actions to set quantities or restore eligible quantities
  to their immutable workspace opening amounts, for selected or all existing rows. F2 reference packs
  supply attributed names/descriptions/icons through writable F4 reference fields
  and F3 detail cells. The embedded item catalog names three resources Inferred;
  the remaining 87 IDs stay Unknown and unmapped.
- A lazy illustrated reference library contains 29 compact images: 14 items and
  15 common weapons. Nine short official-description excerpts are transcribed by
  the wiki; six descriptions are explicitly labelled wiki summaries. Its stable
  `wiki-file:` keys are source identities, never inventory IDs. F4 browsing is
  read-only; no save-ID/name/icon association or ownership is guessed.
- Bounded Gadgets, Loadout, Progress, Quests and World existing-value editors, grouped by
  hash-confirmed section classes. F8 queries search/filter/sort evidence arrays
  before paging; the largest sampled world list has 6,988–6,989 records. Stored
  record counts are not completion totals. Details retain confidence, source attribution
  and console/integrity warnings. Strings, structs and collection structure remain read-only.
- Six observed inventory IDs cross-reference an MIT PC mission dataset's
  `OpenConditionItemID` fields. This **Inferred** role is shown separately from
  the still-Unknown player-facing names and limits; no PS5 unlock behavior is claimed.
- F9 mission opening-condition graph: six item references and 18 mission references,
  with browse-only F4 fields, navigation and text alternatives. All saved mission
  states stay Unknown. The attributed catalogue contains 43 PC mission IDs with
  category hashes, difficulty values, display order and unresolved message/chapter
  GUIDs. F13 inspection reports metadata without changing save values or history.
- F9 checkpoint map/list: raw hashes link to existing values. Coordinates, locations
  and discovered/completed states stay Unknown. A local Cradle atlas displays 26
  attributed community escape-hatch locations, while stored checkpoint hashes
  stay unpositioned in the same text list. Community locations are not joined to
  those hashes or used to infer saved unlock/completion state. Missing packs keep
  the external link and raw checkpoint list.
- Inventories above 200 rows use F8 search, exact column filters, numeric quantity
  sorting and stable physical row keys. F6 all-matching selection includes unloaded
  pages and exclusions through Core's resolver; querying publishes bindings before
  planning. The supplied 90-row inventories retain their compact table.
- An F12 SDK decoder hook, `pragmata-canonical-v1`, selectable by the stock
  `savediff` CLI, plus a versioned section/ID manifest. Canonical paths align
  reordered sections and uniquely keyed records; shared bounded F8 traversal
  covers the inventory through `--decoder pragmata`.
  See [evidence and exact owner capture checklist](evidence/README.md).
- Synchronized playtime and elapsed/demo/pause editing, with every derived value
  reported in `EditOutcome.Changes` for framework undo/redo.
- System/settings overview with localized `Title`, `SubTitle`, `Detail` strings.
- A lazy explorer of the complete typed tree. Supported primitives are editable
  by exact hash/index path; strings, structs, array sizes and container metadata
  are read-only. Non-finite floats are displayed as text.
- Five restored hash names and attributed community schema/enum metadata:
  459 class/field declarations and 230 enum values across eight types. Raw and
  family primitive descriptions show exact matching enum aliases as Inferred;
  the original storage values, editor bounds and warnings remain intact.
- Fixed-size patching preserves unknown fields, padding, unused encrypted
  capacity, ElGamal pairs, unknown metadata bytes and the RSA/key trailer.

The slot rail labels gameplay saves by the normalized directory name:
`SAVESERVICE-LINE-0-<n>Slot` or `SAVESERVICE-LINE-0-0` becomes
“Gameplay save, slot N”. `data00-1.bin` is labelled “System data”; unrecognized
directory names fall back to “Gameplay save”. Probe still reads only the 16-byte
DSSS header and never decrypts. Backup twins use the same normalized names.
After opening, gameplay titles include the parsed slot and inferred playtime in
hours, e.g. “Pragmata · Slot 0 · 25.7 h”.

Each tab starts with one experimental notice. Editable fields and the quantity
action parameter also publish the same console/integrity warning beside the control
and through table warning indicators. Field descriptions retain provenance
(`path · u32 @0x50`, with plaintext offsets), source attribution and editor bounds;
confidence describes unverified gameplay meaning, including Inferred quantity storage.
Inventory-specific risks and clock guidance remain in
their tabs. The overview integrity stat reads “Verified”, with hash block counts
and embedded key/IV verification details in its hint.

No new NuGet dependencies. Production code depends only on `SaveToolkit.Sdk`
and .NET libraries, and does no filesystem/network I/O. Embedded identifier data
is immutable and shared; mutable state is isolated per document.

## Enabled writes and their evidence

The 2026-10-28 owner decision **"Enable writes. Keep warnings."** supersedes the
earlier console-evidence gate. Version 0.4.0 enables six prepared write surfaces:
item-ID references and the five family tabs below. This edits existing primitive
storage only, using the same fixed-size patcher as Raw. Names identify stored
fields, not verified gameplay effects. Every writable field describes its source,
console status and integrity limitation; F6 preview/apply outcomes retain warnings.

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Inventory item-ID replacement through F4 | Unknown; imported names at most Inferred | Exact uint32 storage; 90 observed IDs; edit/reopen and plaintext-byte isolation in all 11 gameplay files | CityHash64/Murmur3 repaired; RSA trailer unverified | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): replace one existing ID; inspect equipped state, quantities, unlocks and sibling files after reload |
| Gadgets / shortcut existing primitives | Unknown | Hash-confirmed identifiers/types; all-sample F1 preservation and undo/redo | Same fixed-size repair; RSA unverified | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one gadget/shortcut change; verify equip/use behavior |
| Loadout existing primitives (weapons, enhancements, presets, character) | Unknown | Same; no invented enum meanings or dependency repairs | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one weapon/perk/preset value; verify ownership, effects and dependent state |
| Progress existing primitives | Unknown | Same; record counts are not completion percentages | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one chapter/event/campaign/difficulty value with an exact field path and visible result |
| Quests existing primitives | Unknown | Same; objective identifiers do not prove quest semantics | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one objective state; verify prerequisites and downstream quest behavior |
| World existing primitives | Unknown | Same; fixed physical row identities and bounded F8 details | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one checkpoint/gimmick/prop/environment value; verify travel, interactions and dependent state |
| Existing quantity controls and F6 selected/all quantity action (retained) | Inferred | Independent golden hashes; all-sample F1 quantity-only deltas; atomic planned apply and group undo | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): small quantity change; preview, apply, undo/redo, export/re-import and console load |
| Restore imported eligible quantities (edit-coverage #14) | Inferred storage; three resource names Inferred, other item meanings Unknown; Experimental | Immutable opening plaintext; existing int32 setters/bounds; all 11 gameplay samples restored byte-identically after staged reductions; synthetic eligibility, alias, cancel, selection/query, stale/forged-plan and undo/redo checks | CityHash64/Murmur3 and encryption verified by export/reopen; RSA trailer preserved, unverified | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): identify/use one restored stack, load/resave and inspect equipped/unlock/dependency side effects |
| Existing header/clock/raw primitive edits (retained) | Inferred for named fields; Unknown otherwise | 12 no-op identities; primitive/clock edit/reopen/reverse and derived-clock undo tests | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): unchanged control first, then one small edit per family |

**The game may reject edited saves.** RSA trailer verification/regeneration is
not implemented. Serialization preserves that trailer and every unknown byte,
repairs the two known hash layers, decrypts its own result and fully re-parses it.
Successful parsing cannot establish console acceptance or repair unknown gameplay
dependencies. Item-ID replacement changes only the selected stored ID; it does
not create a record or update equipment, ownership, unlocks or other references.

New numeric family controls and their Raw aliases use immutable editor bounds
computed when the document opens: match containing class/array, field hash,
primitive type and width; take the observed minimum/maximum in that save, include
0 and 1, and retain the native type bounds. The added 0/1 endpoints are an explicit
editor policy, not observations or claimed legal enum states. Boolean fields
accept booleans only; floats must be finite and fit their stored precision.
Reference IDs use exact decimal uint32 text, with the same observed bounds;
custom IDs are permitted within those bounds even without a reference pack.
Imported names or limits never override this policy. Invalid batches and alias
conflicts reject atomically. Existing quantity, header, clock and system/raw
metadata controls retain their documented storage bounds and clock invariants.

Structural add/remove/reorder, string/struct edits and resizing remain disabled.
There is no typed structural writer: byte-identical fixed-size no-ops do not
prove that unknown alignment, padding, counts and metadata survive rebuilding a
different layout. No prepared cross-file transaction or proven sibling dependency
exists to enable through F7. Existing operations emit only their owned file;
no legal-max or progression preset is fabricated. Maturity stays Experimental.

**Restore imported quantities** implements [ranked addition #14](../../docs/EDIT_COVERAGE.md#ranked-top-15-additions)
over the existing quantity writer. Its preview uses the immutable plaintext captured
when this document opened, independently of later edits or undo history. It raises
only existing quantities below that opening amount, with both amounts nonnegative
and the raw item ID unchanged. Equal/above-baseline amounts, negative opening/current
amounts and changed IDs are listed as skips. Zero-count records remain eligible;
the target can never exceed that row's opening int32 amount or existing field bounds.
This is no legal-cap, named-resource or mission preset. It cannot recover a balance
lost before import; re-importing edited bytes establishes a new baseline.

Duplicate current or opening inventory IDs reject the whole restore preview, as do
duplicate/unknown row keys, non-inventory or unresolved query scopes and unexpected
parameters. SDK-02 resolves selected canonical quantity bindings before planning,
so explicit/document scopes work without opening Inventory, Raw or evidence pages.
The unloaded-page guard is removed: migration tests prove unloaded edits and exact
one-step undo/redo across 251 rows. A binding-success invariant remains. Bounded F8
whole-query resolution still honors exclusions. Empty selections
and zero-match/fully excluded queries stay empty. The format has no verified
per-record lock flag to reinterpret; unknown gameplay fields and dependencies remain
untouched. Unmapped player-facing names do not disqualify known quantity storage;
previews retain raw IDs, Inferred storage confidence and the common Experimental,
RSA, console and dependency warnings. Cancel discards a pure preview; applying
revalidates through `ActionPlans.ApplyScalar`, uses atomic `ApplyEdits`, reports
exposed aliases and records one undo/redo step. No records or unlocks are granted.

For each enabled surface, use a spare slot: record build/region/DLC, path and
displayed old/new values; capture an unchanged load/save/reload control; make
one small change; export/re-import; load, inspect side effects, re-save, reload,
re-export and diff. Keep system/gameplay siblings in the capture. The detailed
[capture checklist](evidence/README.md) and [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31)
track evidence separately; writes no longer wait for those captures.

## Files and sample coverage

| File | Role in supplied samples |
| --- | --- |
| `data00-1.bin` | System/settings, 10,484 plaintext bytes, four roots |
| `data000.bin` | Gameplay slot 0; not proven to be an autosave |
| `data001Slot.bin` … `data010Slot.bin` | Gameplay slots 1–10 |
| `sce_sys/*`, keystone and other metadata | Not claimed or modified |

The archive contains 12 distinct game files, 503 Mandarin blocks and 519,872
class records. All plaintexts parse exactly. Every gameplay file contains 27
section records and 90 inventory entries. Section order varies: inventory and
clock sections are located by class hash, never fixed indices. Importer snapshot
generations and backup twins remain independent; siblings are not rewritten.

## DSSS/Mandarin format

Integers are little-endian. Only DSSS v2, flags `0x10` (Mandarin without
compression), is supported. “Decrypted PS5 save” still contains this game layer.

| Offset | Contents |
| --- | --- |
| `0x00` | `DSSS`, uint32 version 2, uint32 flags `0x10`, four preserved padding bytes |
| `0x10` | Mandarin blocks |
| `F - 0x8c` | Preserved 128-byte RSA/key trailer |
| `F - 12` | uint64 exact plaintext length |
| `F - 4` | MurmurHash3 x86-32 of `[0, F-4)`, seed `0xffffffff` |

Mandarin uses public PS5 key input `526253637` and encryption seed
`0x7da24a9e1479f3d7`. Generate **all** `ceil(length / 0x4000)` potential
capacities first: `((state & 7) + 1) * 0x4000`. Generator steps add
`0x9e3779b97f4a7c15`, then perform the SplitMix64 mixing operations. The **mixed
output becomes the next state**, unlike conventional SplitMix64. After the
capacities, add the bitwise complement of the 64-bit key input.

Each actual block uses 16 outputs for low-byte AES key bytes and next-byte IV
bytes, then 528 outputs for the metadata XOR mask. A block contains:

- Four 128-byte ElGamal-style key/IV metadata pairs.
- A masked uint64 CityHash64 of **valid plaintext bytes**, at `+0x200`.
- Eight preserved unknown bytes at `+0x208`.
- AES-128-OFB ciphertext at `+0x210`, occupying its generated capacity.

ElGamal metadata is decoded with the reference modulus and complemented key
using `BigInteger.ModPow`; recovered key/IV words must match. This checks
consistency, not cryptographic authenticity. RSA trailer validation/generation
is not implemented. Portable AES-OFB ECB-encrypts the previous feedback block
and XORs the stream; NIST vectors cover full/partial blocks and in-place use.

Serialization starts from original ciphertext, XORs only
`old plaintext XOR new plaintext` into changed blocks, repairs their masked
CityHash64 values, then repairs outer Murmur3. It never resizes or regenerates
keys. **Every `Serialize()`** decrypts the output, verifies both hash layers and
embedded key/IV, compares all plaintext, and fully re-parses it, including no-ops.

## Typed stream and bounds

Roots have a native hash followed by field-count/class-hash records. Fields have
name hashes and signed type tags: array `-1`, enum `1`, bool `2`, signed/unsigned
integers `3–10`, floats `11–12`, char integers `13–14`, counted UTF-16LE string
`15`, opaque struct `16`, class `17`. Class arrays may contain an `0xffeeffee`
marker and per-element hashes, preserved verbatim. Unsupported tags are rejected.

Alignment includes the removed 16-byte DSSS header:
`aligned = ((offset + 16 + size - 1) & ~(size - 1)) - 16`.
The bit-mask rule also applies to non-power-of-two struct sizes such as 24.
Origin zero fails on several supplied slots.

The parser rejects truncation, duplicate field/root hashes, invalid sizes/counts,
incomplete streams, nesting beyond 100, arrays above 1,000,000 elements, trees
above 2,000,000 nodes, and plaintext above 64 MiB. These are editor support
bounds, not claimed game limits. Lazy expansions and inventory pages contain at
most 200 entries; very large arrays use recursive range sections. String/struct
previews are bounded without altering their stored bytes.

Primary value IDs are exact paths, e.g.
`7c416083/7c018857[6]/3185b7a5[5]/d18b252d`. Inventory, Raw and evidence quantity
controls reuse that canonical ID and the exact same spec. For 250 changed quantities
in a 251-row F8 inventory, semantic review now lists **250 entries, down from 500**
(250 canonical + 250 raw), with **0 duplicate entries, down from 250**. Individual
revert addresses the same quantity from either projection.

SDK-02 also resolves explicitly requested legacy `raw/<quantity path>` and
`inspect/value/<quantity path>` IDs as read-only `Derived` aliases, backed by the
canonical quantity. They have no independent setter and are not rendered as separate
controls. Changes report every registered alias; Core replays writable primaries only.
Other primitive projections retain their existing IDs and writable behavior, with
metadata for already exposed bindings so history remains valid after view rebuilds.
Contradictory edits to the same primitive still reject atomically.

Restoring a primitive's imported semantic value also restores its original stored
bytes. This preserves accepted noncanonical nonzero boolean bytes and signed
floating-point zero through edits and undo/revert. Numeric no-ops keep the stored
representation. Synthetic accepted-input tests exercise these cases; they do not
establish that consoles produce noncanonical values or accept edited saves.

## Clock decisions

All 11 gameplay samples satisfy:

`_SavePlayActualTime = _GameElapsedTime - _DemoSpendingTime - _PauseSpendingTime`.

Microsecond units are inferred. Timer controls use Duration, 1,000,000 units per
second, and `Confidence.Inferred`; their names are independently hash-confirmed.

- Playtime edits derive elapsed = playtime + demo + pause, preserving demo/pause.
- Elapsed, demo or pause edits recompute playtime.
- A playtime + demo/pause batch derives elapsed unless elapsed is also supplied;
  when both are supplied, the whole relation must agree.
- Overflow, underflow and conflicting batches reject all edits, including any
  unrelated fields. All derived values are reported and undoable.

Save-date ticks remain editable raw int64 values. The overview interprets valid
ticks as .NET ticks with an unknown timezone; invalid ticks are displayed
explicitly. No UTC conversion is made.

## Verified versus inferred and unknown

`Resources/HashNames.json` embeds **408 identifiers** selected from the pinned
MIT RE_RSZ schema; `Resources/RestoredHashNames.json` restores **five identifiers**
from the pinned community string dump under the owner decision of 2026-10-28.
Every entry reproduces its
MurmurHash3 over UTF-8 with seed `0xffffffff`, checked on resource load and in
tests. The restored names are
`Slot`, `Detail`, `_IsFirstGetWeapon`, `_IsFirstGetActiveSkill` and
`_AcquireContentsHashes`; all five hashes occur in the supplied samples. Their
own attribution appears beside labels, without relabelling MIT data. Class hashes
`00000000` and `6cb10f10` remain unnamed; unmatched native root hashes remain numeric.

Upstream dumps resolve additional names beyond the earlier map, including
`_ApplicationVersion`, `_SaveSlotIconHash`, `_SaveSlotStageHash`,
`_SaveSlotDescriptionHash`, `_SavePlayActualTime`, `_LapCount`, `_MainSlot`,
`_Gadgets` and `_ShortcutSlot`. These are verified **identifier matches**, not
proof of legal values or game behavior. `_MainSlot` is not labelled money,
`_LapCount` is not equated to NG+, and difficulty/inheritance IDs have no invented
friendly labels. Writable controls use Inferred for named raw fields and Unknown
for unmapped fields/family semantics. Identifier provenance remains hash-verified;
it does not confer Verified gameplay confidence. Experimental console/integrity
warnings appear in every field description and at the top of each tab.

Three resource ID/name pairs are Inferred as documented below; the remaining
87 item names, legal stack limits, unlock dependencies, many field
semantics, unknown metadata and console compatibility remain unverified. The
reviewed enum dump supplies type-specific symbols, not a confirmed item dictionary. Quantities retain
the full signed int32 storage range with a warning; bulk N is nonnegative int32.
The F6 action changes existing entries, including zero-count entries, without
adding records or setting unlock flags. The preview lists raw-ID old/new values,
aliases, skips and warnings. Explicit empty selection changes nothing; unknown,
duplicate or non-inventory keys and duplicate item IDs reject the entire action.
Direct bulk invocation is rejected. For large inventories, all-matching query
selection publishes target bindings before planning. SDK-02 also resolves explicit
row/document targets without loading their pages, retaining the same framework
specs and complete semantic review. No new gameplay write family, legal-max preset
or structural operation is introduced in the completion round.

Regenerate the pack from reviewed local copies of all pinned inputs (downloads
stay outside the repository):

```powershell
python -I plugins/Pragmata/scripts/build-reference-pack.py --rsz <MIT-rszpragmata.json> --missions <MIT-missionuserdata.user.3.json> --community-dir <directory-with-three-REE-dumps>
```

That command writes the base/community 1.3.0 manifest. Follow it with the
[visual-pack rebuild](SaveToolkit.Plugins.Pragmata/ReferencePacks/pragmata.evidence.en/visual-reference.source.md)
using the reviewed external captures to restore the complete 1.5.0 pack.

The generator validates source SHA-256
`67e5ed72e0e9efff0287214ad36737a8959d799aa1510b6b76591731a21f4df2`,
also validates the pinned mission-data and three community-input SHA-256 values
(see the pack sidecars), and reads
only the reviewed JSON/string inputs, current known-hash subset and evidence manifest,
and does not download/execute upstream code or read saves. HashNames is the
reviewed observed-hash allowlist; adding new hashes requires reviewing that subset.

The embedded `pragmata.evidence.en` v1.5.0 pack contains `pragmata-identifiers`
(408 MIT names), `pragmata-sections` (27 MIT-derived group labels) and
`pragmata-items` (three MIT Inferred resource names and 87 CC0 Unknown placeholders) and `pragmata-item-evidence`
(six MIT mission-opening-condition cross-references) and `pragmata-missions`
(43 MIT PC reference records; raw hashes/GUIDs, no localized names), plus
`pragmata-restored-identifiers` (five names), `pragmata-field-schema` (459 declarations)
and `pragmata-enum-members` (230 values, eight types, aliases retained). The latter
three carry **No licence published; reused with attribution per owner decision 2026-10-28**.
The separate `pragmata-illustrated-reference` catalogue contains 29 item/equipment
references with 29 icons, nine official-description excerpts and six wiki summaries.
`pragmata-map-locations` contains 26 community escape-hatch locations; the pack
also includes one Cradle atlas background. These attributed references have no
proven association with saved item IDs or checkpoint hashes.
Schema records require known class/field hashes; individual declarations need not
all occur in a PS5 build. Enum descriptions require matching class, field, storage
type and exact value, including parent array declarations. Unknown values and flag
combinations remain raw; declared sizes/alignment never replace typed-stream parsing.
These Inferred hints do not establish legal limits, completion or unlock rules.
User packs can supply
names/descriptions/icons under another pack ID; item keys are invariant decimal
uint32 strings. Identifier and section keys are eight-digit lower-case hex.
See [pack provenance and import contract](SaveToolkit.Plugins.Pragmata/ReferencePacks/pragmata.evidence.en/data.source.md).
See also the [community-input hashes, transformations and confidence rules](SaveToolkit.Plugins.Pragmata/ReferencePacks/pragmata.evidence.en/community.source.md)
and [resource-name source ledger](SaveToolkit.Plugins.Pragmata/ReferencePacks/pragmata.evidence.en/currency-labels.source.md).
F4 item references allow experimental existing-ID replacements, imported labels are at most Inferred, and
reference limits do not become edit caps. Artwork and description excerpts are
confined to the separate reference library and atlas described above; they do not
establish saved ownership or completion. Counts in the new family tabs denote
records, not completion totals.
Evidence row keys use fixed plaintext offsets, which remain stable under this
size-preserving editor and across page/filter/sort changes. They identify physical
records, not proven gameplay identities. Search covers raw IDs, types and up to
12 direct stored fields per record; nested arrays are queried separately, and text
payloads remain in details. GUIDs are fingerprinted in query summaries. Sorting is
ordinal text with a stable row-key tie-break; queries use offsets, not cursors.

## Currency ID field discovery (#40)

Research date: **2026-10-29**, after SDK-02 adoption `a22ee64` (included in
the starting merge `7430250`). The result is three **Inferred** resource labels
in the existing attributed `pragmata-items` catalogue, with raw decimal/hex IDs
retained. It adds no writes, actions, caps, eligibility rules or icon associations.

The public [jestelf trainer source, pinned revision 5704004](https://github.com/jestelf/PragmataTrainer/blob/570400495c8d29106b84caea43de2ba8aee7bfff/src/PragmataTrainer.cs)
explicitly pairs three IDs with resource names in `Currencies`; its own English
translation dictionary supplies their English names. Static inspection only:
downloaded code/data were never executed. The input hash, MIT notice,
attribution and exact extracted records are in the
[resource-name ledger](SaveToolkit.Plugins.Pragmata/ReferencePacks/pragmata.evidence.en/currency-labels.source.md).

| Raw ID | Label / confidence | Consistent PS5 evidence (aggregate only) |
| --- | --- | --- |
| `0x84c2f1f3` / `2227368435` | Lunafilament — **Inferred** | Exactly once in all 11 gameplay saves; 10 distinct quantities; range 863–5,258. The only ID with quantities in the thousands, consistent with the guide's printing/upgrading currency. |
| `0x0b30ca95` / `187746965` | Upgrade Components — **Inferred** | Exactly once in all 11; five distinct quantities, range 0–19. Smaller spendable-resource pattern, consistent with the explicit source crosswalk. |
| `0x7d9b1f63` / `2107318115` | Pure Lunum — **Inferred** | Exactly once in all 11; five distinct quantities, range 0–12. Scarcer-resource pattern, consistent with the explicit source crosswalk and upgrade guide. |

Method: an authored scratch-only C# probe opened the external ZIP read-only,
read matching entries into memory, deduplicated by complete-file SHA-256 and used
the plugin's own `DsssContainer` → `SaveLayout` → `PrimitiveCodec` path. No source
archive was copied or junctioned. All 12 distinct files were decoded: 11 gameplay
files with 90 inventory records each, and one system file without inventory.
Comparison keyed each quantity by exact uint32 ID, never row number, slot order
or a guessed timestamp chronology. Every gameplay inventory has the same 90 IDs,
each unique within that save. All 55 unordered gameplay pairs were compared;
there are 10 distinct inventory states, so the 11 files are not independent
controlled experiments. Full per-file observations and scripts remain in
`D:/Temp/saveeditor/test-scratch/research-pragmata-currency`, not in git.

Presence and uniqueness alone do not distinguish currencies: **every** observed
item meets those conditions. Of 90 IDs, 60 stay zero, seven stay one, 15 vary only
between zero and one, and eight have a quantity above one. The three source-backed
labels are among those eight. Cross-save variation is differential evidence of
storage and a consistency check on the public mapping, not proof that any
particular gameplay event changed a balance. The source is PC, the supplied PS5
build is unconfirmed, and no controlled UI gain/spend capture is available.
Confidence therefore stays Inferred; legal maxima and console acceptance stay Unknown.

### Negative results: remaining resource candidates

These are the other five IDs with quantities above one. **None is named.** All
occur once per gameplay save, but no reviewed public source supplies their exact
numeric-to-name join. The aggregate ranges below are triage facts, not legal caps.

| Raw ID / decimal | Observed range | Rejected inference / reason |
| --- | --- | --- |
| `0x24f0ff44` / `619773764` | 1–2 | A low stack could be a key, consumable or other resource; no ID crosswalk. |
| `0x29c9936b` / `701076331` | 0–14 | Resource-like variation does not establish Cabin Coins, Data Shards or another named item. |
| `0x5d5bbd51` / `1566293329` | 1–2 | Same ambiguity as `0x24f0ff44`; matching count patterns do not establish equal meaning. |
| `0x90d3dce4` / `2429803748` | 0–3 | Small changing count could represent a capacity/consumable or collectible; no supported name. |
| `0xab13a98a` / `2870192522` | 1–7 | A plausible limited resource, but selecting a guide name by magnitude would be guessing. |

The other 82 IDs also remain Unknown. In particular, **Cabin Coins have no proven
raw ID**: AppMedia documents their acquisition and Stamp Club use, but gives no
save IDs. The previously reviewed wiki item/illustrated tables supply names and
artwork without a numeric join; direct Fandom reads in this round were unavailable
(HTTP 402 / restricted URL), so no newly verified wiki mapping is claimed.
REE's pinned enum dump has no `app.ObjectIDs` numeric table or currency-name
crosswalk. The MIT neuro-pragmata resolver obtains categories/names dynamically
through `app.ObjectIDs.getKindHash` and `GuiDataManager.getItemData`; its code is a
capture lead, not a dumped mapping. No relevant Cabin Coin/raw-candidate mapping
was found in the bounded public web/GitHub searches; this is not an exhaustive
claim that none exists.

To strengthen the three labels or resolve a rejected candidate, capture the
build/platform and displayed balance, an unchanged control, then one isolated
gain or spend (one Stamp Club tile for Cabin Coins). Re-export, decode by ID and
check the exact delta plus every other inventory row. A reviewed runtime dump
that joins each uint32 ID to localized `GUIItemData` is another useful source;
PC/PS5 equivalence and native load/resave acceptance still need owner evidence.

Suggested maintainer update to `docs/EDIT_COVERAGE.md` PR05: **E — I labels for
Lunafilament `0x84c2f1f3`, Upgrade Components `0x0b30ca95`, Pure Lunum `0x7d9b1f63`,
from a pinned MIT PC crosswalk and all 11 PS5 gameplay inventories. Existing PR01
quantity writes only; 87 names, including Cabin Coins, remain U. Build-matched
gain/spend, legal caps and console validation remain open.** Replace the blanket
deferred-name statement with these three discoveries and the remaining Cabin
Coin/other-resource research gap. Central documentation is outside this branch's scope.

## Sample-free mutation fuzzing (#40)

The conformance seed hook supplies one `data000.bin` from `SyntheticInventory.Create(2)`.
It includes two authored inventory records, typed roots and clock values inside the existing
synthetic DSSS/Mandarin envelope. The plugin probes, decrypts, parses, builds its view and queries
every collection from this seed. Synthetic consistency metadata and hashes satisfy the parser;
this does **not** reproduce an authentic RSA signature or establish console acceptance.
No private sample bytes are embedded. `SyntheticFuzzSeedsOpenAndPreserveBytes` checks the unchanged
seed before the nightly 5,000 deterministic mutations; envelope integrity failures are expected
clean rejections, so byte mutation does not imply coverage of every decrypted typed field.

## Validation and development

Documentation audit, **2026-10-29**, against plugin code at `b369fa4`:
**284 plugin tests passed, 0 failed, 0 skipped** with all 12 supplied files.
The API build passed with **0 warnings, 0 errors**. This is software validation;
console acceptance remains untested and maturity remains Experimental.

Edit-coverage #14 validation, **2026-10-29**: **299 plugin tests passed, 0 failed,
0 skipped** with samples (13 new restore cases). All 11 gameplay files return
byte-identically to their imported bytes after restoring staged reductions;
the system-file no-op remains covered. Existing allocation bounds, including
`IdentityAllocationTests`, are unchanged and pass. The API build passed with
**0 warnings, 0 errors**. No console acceptance is claimed; #31 remains open.

SDK-02 adoption validation, **2026-10-29**: **309 plugin tests passed, 0 failed,
0 skipped** with samples (10 new metadata/history migration cases). Conformance,
mutation fuzz (200 cases), restore-imported-quantities, all-sample no-op round trips,
independent edited-output golden hashes and all unchanged allocation limits pass.
The API Release build has **0 warnings, 0 errors**; all **27 documentation checks**
pass against `main`. The 251-row review regression changes 250 quantities and now
reports 250 entries instead of 500. Unloaded explicit/document planning and direct
edits, exact undo/redo and derived-alias replay tests justify removing the page-load
guard. Scratch, TEMP, TMP and TMPDIR were under
`D:/Temp/saveeditor/test-scratch/adopt-sdk02-pragmata`; samples stayed in the external
artifact directory. `DOTNET_TieredCompilation=0` and `DOTNET_PROCESSOR_COUNT=1`
stabilized allocation measurements without changing their limits.

Currency discovery validation, **2026-10-29**: **324 plugin tests passed, 0 failed,
0 skipped** with samples (15 new cases, first observed failing before the labels
were added). These include labels and byte-identical no-ops in each gameplay
sample, all 12 existing container/no-op checks, conformance, 200 mutation cases
and every unchanged allocation limit. A separate run with an empty artifacts
directory passed all **three synthetic resource-label cases, 0 failed, 0 skipped**;
it compares signed int32 bounds and restore eligibility with/without reference names.
The API Release build passed with **0 warnings, 0 errors**. Pack/source hashes,
generator syntax, local report links, sub-300-line C# files and `git diff --check`
also pass. Builds used `--artifacts-path` under
`D:/Temp/saveeditor/test-scratch/research-pragmata-currency`; all test scratch,
TEMP/TMP/TMPDIR and research scripts stayed under that same task root.

The additional central documentation command, `python -B docs/tests/test_documentation.py
--code-ref main`, has **27 passing checks and one pre-existing failure**:
`test_documented_reference_and_action_routes_exist` expects `/reference-fields/records`
in the untouched plugin guide. The starting guide at `7430250` is byte-identical
to the working guide and has the same missing route. This central documentation
mismatch is outside the plugin-only edit scope; it is not reported as passing.

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/feat-coverage-pragmata'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$env:TEMP = $scratch
$env:TMP = $scratch
$env:TMPDIR = $scratch
$env:SAVETOOLKIT_TEST_TEMP = $scratch
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj --warnaserror
dotnet test --project plugins/Pragmata/SaveToolkit.Plugins.Pragmata.Tests --results-directory "$scratch/results"
python docs/tests/test_documentation.py
```

Conformance exercises every distinct sample with quantity edits or a system
boolean edit. Format tests verify the research map's values, offsets, classes,
array counts and complete payload consumption, all-sample byte-identical no-ops,
quantity edit/reopen/reversal and independent whole-output golden hashes.
Additional coverage includes:

- CityHash64/Murmur3 vectors, NIST AES-OFB vectors, block extents, damaged hashes
  and key/IV metadata, typed truncation, alignment, array and nesting bounds.
- Atomic type/range/alias rejection, synchronized clocks and Core undo/redo,
  noncanonical boolean bytes and signed-zero restoration.
- Existing item-ID and family writes with plaintext-byte isolation and reopen/
  reversal checks, F6 selection/preflight/stale-plan handling, and F8 query scopes
  with exclusions, complete paging, deterministic sorting and cache invalidation.
- SDK-02 targeted metadata, canonical quantity projections, read-only legacy aliases,
  one-entry semantic review, unloaded explicit/document planning and direct edits,
  imported-quantity restoration, exact byte undo/redo and unchanged allocation budgets.
- Pinned reference hashes, attribution, enum matching, user-pack overrides,
  browse-only inspection/history, map/graph bounds and missing-pack fallbacks.
- Production canonical decoder discovery, bounded shared view traversal, complete
  node emission and explicit warnings for unproven positional identities.

Sample tests skip through `SampleSaves` when the archive is absent; the audit run
above supplied it and had no skips. No private sample payload is committed.
Handwritten C# files are below 300 lines. No browser or console test is claimed.

Historical measurements and round-specific results remain in the dated
[evidence manifest and capture checklist](evidence/README.md),
[round-2 report](evidence/round2.md), [completion audit](evidence/completion.md),
[community-source report](evidence/unlicensed.md), [asset report](evidence/assets.md)
and [performance review](evidence/performance.md). Those reports describe their
recorded revisions and environments; their test counts and timings are not current
suite totals or performance guarantees.

## Framework feature adoption

The embedded pack has 29 icons and one atlas totaling **99,634 binary bytes**.
The library and map are lazy views over shared F2 assets; the historical load
measurements and remaining mission-text/save-ID gaps are in the
[asset report](evidence/assets.md).

| Feature | Status | Where / concrete limitation |
| --- | --- | --- |
| F1 SaveDiff | Used | Production `pragmata-canonical-v1` SDK hook, shared diff engine and versioned evidence manifest; all-sample synthetic quantity/write isolation. |
| F2 reference packs | Used | Pack 1.5.0 adds three Inferred resource ID/name pairs; retains 87 Unknown item names, 29 illustrated references, 26 community hatch locations, 29 WebP icons and one atlas background; hashes, provenance and missing-pack fallbacks. |
| F3 rich cells | Used | Existing inventory/evidence cells plus lazy image/name/category/source rows for the illustrated library; icons are 64×64 with attribution. |
| F4 pickers | Used | Writable inventory item IDs; browse-only mission/item-evidence and illustrated references. Wiki file keys are never eligible inventory IDs. |
| F5 structural undo | N/A | Only fixed-size primitive patching exists; no preserve-first resizing writer. Unknown padding/count metadata prevents safe add/remove/reorder. |
| F6 planned actions / #27 | Used | Set quantities and restore imported eligible quantities; shared ActionPlans revalidation/application; F8 whole-query selection, exclusions, stale-plan checks and one undo unit above 200 rows. No legal-max/loadout/progression preset has proven dependency rules. |
| F7 document groups | N/A | No proven coupled-file operation or partner identity. Claiming independent slot/system files would invent dependencies and lock unrelated saves. |
| F8 queryable tables | Used | Family-view arrays register queryable collections when their containing sections expand; complete-model search/filter/sort precedes paging. Inventory switches above 200 rows, with numeric quantity sorting. |
| F9 graphs/maps | Used | Existing mission DAG and checkpoint field targets; local 2048×585 Cradle background and 26 community hatches in the original image-pixel frame. Raw save hashes remain unpositioned and all saved states Unknown. |
| F10 exact/nullable/union | N/A | Fixed-width binary primitives have no numeric lexemes, null primitive or discriminated-union storage to expose. Int64/UInt64 use the existing Int128 integer-string transport, which is separate from F10. |
| F11 shared codecs | N/A | Supported DSSS flags 0x10 contain uncompressed Mandarin AES-OFB, neither Kraken nor SQLite. Unsupported flags reject. |
| F12 decoder/traversal | Used | SDK-only `ISaveDecoderProvider`/`ISaveDecoderHook` registers `pragmata-canonical-v1` with the stock CLI. `--decoder pragmata` uses shared bounded F8 traversal, tested across 251 inventory rows and row/page/byte caps. Research executable removed. |
| F13 inspection/browse/sort/durations | Used | `Mutates=false` mission inspection, browse-only references, catalogue category/sort. Float duration is N/A: known timers are integer microseconds already using Duration; unidentified floats have no proven time unit. |

Shared helper adoption was also checked against the current SDK. This plugin
still uses local implementations: `CollectionIndex<T>` plus inventory/evidence
queries (including retained projections and one ordered result), the origin-16 `TypedReader`, direct catalogue
`Find`/bounded `Search` calls, and local image/inspection rendering. It does not
currently call `InMemoryCollection<T>`, `BinaryRange`,
`BoundedBinaryCursor`, `ReferenceCatalogReader`, `ReferenceViews.Icon` or
`ReferenceInspection`. Those helpers are available; migrating eligible mechanics
is an adoption task, not a missing SDK capability. Both quantity actions now use
`ActionPlans.ApplyScalar` with the existing binding guard and warning policy.
`FStringCodec` and `Crc32Ieee`
are inapplicable: strings use counted UTF-16LE and integrity uses CityHash64/Murmur3.

## SDK requests

SDK-02 is adopted from `360144f`, following the
[Pragmata opt-in plan](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6489058706)
and the [SDK-02 contract](../../docs/PLUGIN_GUIDE.md#field-metadata-without-rendering-a-page-sdk-02).
`PragmataDocument` implements `IFieldMetadataDocument`; requests of at most 200 IDs
resolve against the current fixed-size layout without rendering, expanding or querying
pages. The quantity lookup is lazy and document-local; the writer changes bytes only,
never replaces parsed nodes. Shared pure spec construction keeps metadata and views
in agreement. Existing reference aliases retain stable eligibility delegates.

Quantity planning establishes selected canonical setters before calculating effects,
including unloaded explicit/document scopes and restore-imported-quantities. The
unloaded-page guard is removed after exact-byte migration tests; resolver success
remains checked. Raw/F8/evidence quantity views reuse canonical IDs, eliminating the
250 extra review entries in the 251-row regression. F8 all-matching selection,
exclusions, stale-plan rejection and one undo unit remain covered. No SDK change is
needed. This adoption does not claim targeted review beyond Core's bounded traversal
or new canonicalization of non-quantity reference projections.

F12 request resolved by [#32](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/32):
`PragmataPlugin` implements the SDK's optional `ISaveDecoderProvider` and publishes
`pragmata-canonical-v1`. The stock CLI selects this hook explicitly; `auto` retains
the framework view behavior. The production plugin still depends only on the SDK.
The research executable and its index/compare workaround are removed; tests use
only a thin adapter around the production hook. The versioned evidence manifest
and its attribution are retained.

`--decoder pragmata` uses F12's shared `PluginViewDecoder`, including bounded F8
inventory traversal above 200 rows. Query budgets and incomplete-input warnings
come from the framework; no plugin-local traversal is needed. The canonical hook
honors sink depth/output/file bounds, fingerprints strings and opaque values,
and marks unproven positional identities incomplete even when every node was
emitted. Missing canonical paths in those reports cannot establish removal.
Sections and uniquely keyed records retain their existing canonical paths.

## Sources and attribution

| Dataset / code | Source, author, license | Retrieved / transformations |
| --- | --- | --- |
| HashNames; `pragmata-identifiers`; section names / declared types | [Pinned RE_RSZ schema](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/rszpragmata.json), alphazolam / praydog, [MIT](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/LICENSE) | 2026-10-28; observed hash subset; name hashing, group labels, type cross-index. Full notice in THIRD_PARTY_NOTICES.txt. |
| `RestoredHashNames`; `pragmata-restored-identifiers` (5 names) | [Pinned REE string dump](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/strings_pragmata.txt), kvasszn / REE Save Editor; praydog (dump tooling). No licence published; reused with attribution per owner decision 2026-10-28 | 2026-10-28; only the five removed technical identifiers; Murmur3 and all-sample occurrence checks. No localized game text. Input hash and transformations in `community.source.md`. |
| `pragmata-field-schema` (459 declarations) | [Pinned REE RSZ dump](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/rszpragmata.json), same authors. No licence published; reused with attribution per owner decision 2026-10-28 | 2026-10-28; known class/field hash crosswalk, original/storage types, array flag and declared size/alignment. Inferred metadata; no parser or edit-bound changes. SHA-256 in sidecar. |
| `pragmata-enum-members` (230 numeric values, eight types) | [Pinned REE enum dump](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/enumspragmata.json), same authors. No licence published; reused with attribution per owner decision 2026-10-28 | 2026-10-28; only types referenced by selected schema declarations; aliases retained. Exact class/field/storage/value matching in primitive descriptions, Inferred semantics. SHA-256 in sidecar. |
| Item ID placeholders / sample observation statistics | [Issue #21](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21), Save Toolkit contributors, [CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/) for original descriptions / computed facts | 2026-10-28; no gameplay name mapping; GUIDs fingerprinted; no sample payloads. |
| Three resource ID/name pairs in `pragmata-items` | [Pinned PragmataTrainer source](https://github.com/jestelf/PragmataTrainer/blob/570400495c8d29106b84caea43de2ba8aee7bfff/src/PragmataTrainer.cs), jestelf, [MIT](https://github.com/jestelf/PragmataTrainer/blob/570400495c8d29106b84caea43de2ba8aee7bfff/LICENSE) | Accessed 2026-10-29; three pairs from `Currencies` and its English dictionary; capitalization normalized, PS5 cross-indexed, Inferred. No trainer code executed/copied, caps or write rules adopted. SHA and full notice beside the data; the other 87 placeholders retain CC0. |
| Resource-role guide (research only) | Ali Hashmi / Prima Games, [How to Upgrade Weapons in Pragmata](https://primagames.com/tips/how-to-upgrade-weapons-in-pragmata), published 2026-04-17; rights reserved by Prima Games / Capcom | Accessed 2026-10-29; first-hand printing/upgrading examples support Lunafilament and Pure Lunum roles, not IDs. Paraphrased evidence only; no guide text/artwork embedded. |
| Cabin Coin guide (negative-result research only) | AppMedia editorial team, [Cabin Coin acquisition and use](https://appmedia.jp/pragmata/79951345), updated 2026-04-24; rights reserved by AppMedia / Capcom | Accessed 2026-10-29; acquisition/Stamp Club use, no numeric ID. No source text/artwork embedded and no label inferred from quantity alone. |
| Runtime ID/localization join lead (research only) | Vaultic / neuro-pragmata, [inventory.lua](https://github.com/vaultic13/neuro-pragmata/blob/ae2ee572410e353ceae19bdc23f83f71687267dd/autorun/pragmata/bindings/inventory.lua), [object_names.lua](https://github.com/vaultic13/neuro-pragmata/blob/ae2ee572410e353ceae19bdc23f83f71687267dd/autorun/pragmata/bindings/object_names.lua), [catalogs.lua](https://github.com/vaultic13/neuro-pragmata/blob/ae2ee572410e353ceae19bdc23f83f71687267dd/autorun/pragmata/bindings/catalogs.lua); MIT | Accessed 2026-10-29; static inspection confirms runtime lookup approach but yields no additional hardcoded resource-name table. No code executed/copied. |
| Full enum dump recheck (negative-result research only) | kvasszn / REE, [pinned enumspragmata.json](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/enumspragmata.json); No licence published; reused with attribution per owner decision 2026-10-28 | Accessed 2026-10-29; SHA-256 `c6a828b65ca065cd05003395e6d1b7a8d08872190dacca904899a615f9527c3b`; no ObjectIDs/currency crosswalk found. No additional enum data imported. |
| `pragmata-item-evidence`: six inventory/mission opening-condition links | [Pinned mission JSON](https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/data/json/natives/stm/singletonuserdata/missionuserdata.user.3.json), Egg Targaryen / dzxrly; [MIT](https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/LICENSE) | 2026-10-28; exact numeric `OpenConditionItemID`/mission-ID pairs cross-indexed with observed PS5 item IDs. Inferred role; names/limits Unknown. Full notice embedded beside the dataset. |
| `pragmata-missions`: 43 mission references / graph | Same pinned mission JSON and MIT source above, Egg Targaryen / dzxrly | 2026-10-28; ID/category/difficulty/order/condition/message metadata only. Original raw-ID labels; six item-to-18-mission edges are PC references, not verified PS5 unlock rules. Full notice/attribution in pack, UI, Sources and Info.References. |
| Illustrated item/equipment text (29 references; 15 descriptions) | [Pragmata Wiki items](https://pragmata.fandom.com/wiki/Item?oldid=1830), [weapon index](https://pragmata.fandom.com/wiki/Template:Navbox/Weapons?oldid=3084), Pragmata Wiki contributors, [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/); Capcom owns the nine short quoted game-description excerpts, used with attribution per owner decision 2026-10-29 | 2026-10-29; nine short official excerpts, six adapted wiki summaries, 14 descriptions unavailable. Exact article revisions in `visual-reference.source.json`; adaptations retain CC BY-SA. No PS5 ID associations. |
| Item/equipment images (29) | Capcom; Pragmata Wiki uploaders, [item index](https://pragmata.fandom.com/wiki/Item?oldid=1830) and linked file histories in the source ledger. Rights reserved by Capcom; used with attribution per owner decision 2026-10-29 | 2026-10-29; 13 UI icons and 16 transparent renders alpha-cropped/resized to 64×64, encoded WebP quality 85 outside the repo. Original pixel fingerprints, file timestamps, crops, capture hashes and URLs are pinned in the ledger; final byte hashes in `pack.json`. |
| Cradle background / 26 escape-hatch coordinates | [Itemaps map](https://itemaps.com/maps/pragmata), [data snapshot](https://itemaps.com/media/maps/pragmata/markers.json?v=20261024-icons), Itemaps / Kevin Henrique; Capcom game art. Rights reserved by Capcom and Itemaps; used with attribution per owner decision 2026-10-29 | 2026-10-29; source art 7168×2048 → 2048×585 WebP, 45,564 bytes. SHA-pinned source bytes; `displayX = X × 2048/7168`, `displayY = Y × 585/2048`, top-left origin. No checkpoint-hash association, discovered state or completion claim. |
| CityHash64 algorithm adaptation | [Google CityHash](https://github.com/google/cityhash), Google, MIT | Existing port; full notice in THIRD_PARTY_NOTICES.txt. |
| AES-OFB vectors | [NIST SP 800-38A](https://csrc.nist.gov/pubs/sp/800/38/a/final), NIST, US government publication | Existing F.4.1 test vectors. |
| DSSS / Mandarin / typed-stream observations | [Supplied verified research](../../docs/research/pragmata/README.txt), owner-supplied format evidence | Existing independently implemented crypto/format; no upstream editor code embedded. |

Research history and remaining leads:

- [neuro-pragmata runtime inventory/object-name resolvers](https://github.com/vaultic13/neuro-pragmata/tree/ae2ee572410e353ceae19bdc23f83f71687267dd/autorun/pragmata/bindings), Vaultic,
  [MIT](https://github.com/vaultic13/neuro-pragmata/blob/ae2ee572410e353ceae19bdc23f83f71687267dd/LICENSE), retrieved 2026-10-28:
  identifies `app.ObjectIDs`, `getKindHash` and localized `GuiDataManager.getItemData`
  as owner capture leads; no runtime table, code or localized text is copied.
- [Pragmata Wiki item category](https://pragmata.fandom.com/wiki/Category%3AItems)
  and [Lunafilament](https://pragmata.fandom.com/wiki/Lunafilament), community CC BY-SA
  text: the 2026-10-29 asset round imports the attributed subset above. No save-ID
  linkage was found, so the library remains browse-only.

- [Verified format](../../docs/research/pragmata/FORMAT.txt),
  [research README](../../docs/research/pragmata/README.txt), `pragmata_save.py`,
  `hashes.py`, `selftest.py`, `verified_save_map.json`: primary byte-verified
  format, alignment, crypto, patch strategy, expected values and tests.
- [REE Save Editor README](https://github.com/kvasszn/ree-save-editor/blob/main/README.md):
  independently reproduced PS5 key and region hash, game support context.
- [mandarin.rs](https://github.com/kvasszn/ree-save-editor/blob/main/src/save/crypto/mandarin.rs),
  [util.rs](https://github.com/kvasszn/ree-save-editor/blob/main/src/save/crypto/util.rs),
  [game.rs](https://github.com/kvasszn/ree-save-editor/blob/main/src/save/game.rs):
  capacities, mixed-state PRNG call sequence, AES-OFB, ElGamal and public constants.
  Consulted as algorithms; no substantial Rust code copied.
- [types.rs](https://github.com/kvasszn/ree-save-editor/blob/main/src/save/types.rs),
  [type_map.rs](https://github.com/kvasszn/ree-save-editor/blob/main/src/sdk/type_map.rs):
  type tags, sized alignment, class arrays and name hashing. The supplied research
  confirms origin 16 against the PS5 samples.
- [rszpragmata.json](https://github.com/kvasszn/ree-save-editor/blob/main/assets/pragmata/rszpragmata.json),
  [strings_pragmata.txt](https://github.com/kvasszn/ree-save-editor/blob/main/assets/pragmata/strings_pragmata.txt),
  [enumspragmata.json](https://github.com/kvasszn/ree-save-editor/blob/main/assets/pragmata/enumspragmata.json):
  now imported as pinned factual subsets in the three attributed datasets above.
  The large string dump is filtered to technical identifiers; it does not provide
  a build-matched localized message-GUID table. These are no longer refused for
  lack of a licence.
- [Google CityHash](https://github.com/google/cityhash/blob/master/src/city.cc):
  CityHash64 algorithm, adapted under MIT; see [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
- [NIST SP 800-38A](https://csrc.nist.gov/pubs/sp/800/38/a/final), Appendix F.4.1:
  independent AES-128-OFB test vectors.
- [REasy releases](https://github.com/seifhassine/REasy/releases) and
  [REFramework nightly](https://github.com/praydog/REFramework-nightly/releases):
  further Pragmata dump/modding research leads. No code/data from these projects
  is embedded and no verified item dictionary was obtained from them.

## Licence exclusions

| Source (pinned revision where available) | Licence found | Reason / status | Alternative | What would unblock it |
| --- | --- | --- | --- | --- |
| REE [strings](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/strings_pragmata.txt), [RSZ](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/rszpragmata.json), [enums](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/enumspragmata.json), revision `4125e497f1ae84cc746bb4a99da612c48bea161e` | None found | **Resolved: imported (owner decision 2026-10-28)**: five names, 459 declarations, 230 enum values. Existing crypto/typed-stream code was already independently implemented, not copied. | Attributed factual subsets with exact hashes, raw-value fallback and Inferred semantics | Resolved; no further permission gate |
| Capcom item/equipment art, game-description excerpts and map imagery | Rights reserved by Capcom (map composition: Capcom and Itemaps); used with attribution per owner decision 2026-10-29 | **Resolved: imported (owner decision 2026-10-29)**: 29 icons/renders, nine short official excerpts and one map background. REE localized-message mapping remains unavailable, rather than licence-refused. | Separate illustrated reference catalogue; raw save IDs and unresolved mission labels retained | No further permission gate; exact PS5 item-ID crosswalk and GUID-to-message localization data remain evidence gaps |

No NC/no-redistribution dataset or GPL-2.0-only code is imported. Wiki images and
the SHA-pinned community atlas are imported with full attribution. Associating
their names/icons/locations with saved inventory or checkpoint IDs still needs
an exact crosswalk. The 43 mission references retain Unknown localized text;
their PC source supplies message GUIDs, not the strings those GUIDs select.
The [central licence register](../../docs/LICENCE_EXCLUSIONS.md) records both
resolved imports above and the imported Itemaps atlas/coordinates decision.

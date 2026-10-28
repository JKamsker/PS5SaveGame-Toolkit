# Gearbox (`gearbox`)

**Experimental** editing for decrypted PS5 **Borderlands 3 (PPSA01463)** and **Tiny Tina's Wonderlands
(PPSA01493)**. **PPSA01494 / Borderlands 4 is excluded.** Console-layer encryption must already have
been removed. Edited files have not been loaded on a console.

## Features and confidence

| Area | Support | Evidence / limits |
| --- | --- | --- |
| Character | Editable name; class asset paths; Wonderlands primary/secondary branches | Schema names and sample strings; class display names are not guessed |
| Level / XP | Editable; BL3 levels 1–72, Wonderlands 1–40; existing recorded-level stat updated | Upstream exact XP tables; all sample XP/stat values agree |
| Skills | Editable unspent points and tree grade; Wonderlands upward levelling awards points | Schema and upstream behavior: one per level, two at 20, three at 40 |
| Currency | Money, eridium / moon orbs | Documented hashes `618814354` / `3679636065` match all character samples |
| Keys | Golden / skeleton keys; create absent category only on a nonzero edit | Published path/hash algorithm; `2420693941`; all supplied profiles omit this record |
| Ammo | Editable finite resource-pool amounts | Schema floats and full resource paths; game capacity may clamp them |
| SDUs | Read purchased levels and full asset paths | Schema; no upgrade/capacity editing |
| World | Read missions/statuses, playthrough indices/completion, Mayhem/Chaos and unlocked Chaos | Separate game schemas; no mission/difficulty/unlock editing |
| Inventory | Named, queryable backpack/bank/lost loot; lazy serial/part details, original codes, level, CRC/layout status, raw flags/pickup/equipment indices | Attributed upstream **family** labels, not complete generated item names; serials/flags/slots stay read-only |
| Reference browser | F2 title-scoped packs, F4 searchable balance/part inspection pickers, F8 balance catalogue | Offline, versioned, attributed zlib editor data; raw fallback for unknown names; inspection makes no save changes |
| Profile | Read bank/lost-loot counts; BL3 guardian rank/XP; Wonderlands myth XP when present | Schema; no rank/reward editing |
| Raw | Lazy schema-labelled tree; unknown tags and bytes shown as opaque data | Unknown meanings remain unknown; account-like metadata is hidden |

Curated field meanings use `Confidence.Verified` because they are named in cited schemas/upstream
tools, with sample evidence below. This does **not** mean edits have been verified in-game. Limits
are editor policies, not universal game limits: money/eridium/keys ≤2,000,000,000, moon orbs ≤16,000,
ammo ≤100,000. Existing out-of-range amounts remain read-only. Name length uses the stored limit
when reasonable, otherwise an inferred fallback of 32 UTF-16 units. Invalid Unicode/control
characters are rejected.

Level/XP edits preserve mission unlocks and milestone challenges. Levelling down preserves spent
skills and does not remove unspent points. Repeated down/up levelling in Wonderlands can therefore
award extra points; review the explicit points field. BL3's level-one tree is enabled when raised
above level one. Missing level statistics/ability messages are not fabricated. Warnings describe
these limits in the UI.

## Item browser and versioned data

The read-only inventory uses F8 queries over the complete container for search, exact category filters,
numeric level/index sorting and paging (at most 200 rows). F3 rows show a confidence-labelled family
name, raw part-family key and lazy details. Details include original `BL3(...)` codes (both games use
this wrapper), balance/inventory/manufacturer paths, raw parts, flags, pickup order, reroll count and
Wonderlands' raw Chaos tier when the entire known layout parses. No exact rarity, combat statistics,
localized full names or legal part combinations are inferred. No game artwork is bundled.

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
| `gearbox.bl3` 1.0.0 | 62 | 48 / 11,453 | 2,803 (2,848 index slots) | 774 | 1,435 | 25 |
| `gearbox.ttwl` 1.0.0 | 59 | 56 / 13,674 | 2,030 (2,037 index slots) | 375 | 659 | 29 |

Game patch, DLC/hotfix state and completeness are **unknown**; internal serial database versions are
not PS5 build numbers. Every dataset has attribution and a hash in `pack.json`, plus a source sidecar
with pinned source revision, original compressed-file SHA-256 and conversion notes. NC bl3data/wldata
exports and unlicensed artwork were not imported. The data shipped in the zlib editor revisions is
used under those distributions' terms; underlying game asset identifiers remain Gearbox/2K game facts.

The serial envelope is capped at 4,096 bytes; bit reads and part/additional counts are bounded. It
retains the signed seed, checksum, all trailing bits and original serial bytes. Unsupported versions,
bad CRCs, unmapped indices/families, nonzero customizations and unknown tails produce item-local
diagnostics. They never prevent unrelated currency/progression editing. Header-only items do not
advertise decoded reroll/Chaos values. Stable document-local row IDs distinguish identical serials.

## File roles and layout

`profile` is the profile; positive numeric filenames such as `1` are character slots. Probe requires
the supported title ID, file role and `GVAS` magic. Open validates branch/class/version as well.
Only the opened file is returned; console backups are separate documents, not silently rewritten.

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
field corruption can go undetected.** Item serials have independent encoding/checksums and remain
untouched by edits. A separate bounded reader now validates and displays them.

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
provenance of newly created records. Derived changes are reported for framework undo/redo, including
exact XP, skills/statistics and removal of newly added key records. Serialize parses its own complete
output wrapper, decodes/validates the protobuf and compares it with the intended wire message.

Bounds: 32 MiB input, 1,024 custom versions, 16 KiB header strings, 40 known-message nesting levels,
250,000 fields. Tables/raw pages have ≤100 records and large areas are lazy. Opening every sample is
tested below two seconds. Runtime I/O is limited to embedded resources; there is no filesystem/network
I/O or shared mutable document state.

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

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet test --project plugins/Gearbox/SaveToolkit.Plugins.Gearbox.Tests
dotnet run --project tools/SaveToolkit.SaveDiff -- verify plugins/Gearbox/fixtures.json --artifacts $env:SAVETOOLKIT_ARTIFACTS
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

Validated 2026-10-28: **80 Gearbox tests passed, 0 failed, 0 skipped**; F1 CLI **2 fixtures passed,
0 failed, 0 skipped**; API build **0 warnings, 0 errors**. The environment variable was exported in the
same shell command as each sample-backed test run. Sample-dependent tests still skip when absent.

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
full-collection queries beyond the first page, read-only rich details, attributed pickers and clean
inspection. Currency edits preserve every serial and unrelated top-level wire record, then undo to
exact original bytes.

[`fixtures.json`](fixtures.json) pins archive SHA-256 and observed generation-0/1 XP/money facts.
These are uncontrolled existing generations, **not** proven before/after game actions or chronology.
No speculative churn masks are applied. `GearboxEvidenceDecoder` uses F1's `ISaveDecoder` hook in the
test assembly and exposes only selected gameplay scalars and serial content identities; duplicate
serials retain multiplicity. A changed serial appears removed/added because durable cross-capture
item identity is unknown. Tests run the same manifests through both this independent decoder and
the CLI's `PluginViewDecoder`. No sample bytes, decoded payloads or account identifiers are committed.

## Unknowns and open work

- No edited file has been loaded on PS5. Only a human may promote Experimental to Stable.
- Some BL3 PS5 profile tags postdate the reference schema and remain opaque.
- Controlled game tests are needed for clamping, milestone challenges, low-level unlock dependencies
  and missing progression records. Existing mission/unlock data is preserved.
- Item/rank/reward/difficulty/mission editing and platform conversion are not implemented.
- Other regions/builds need samples. PPSA01494 is a separate BL4 format and never claimed.
- Phase 1's inventory/reference/evidence work is delivered. Full cosmetic/skill/mission/challenge
  crosswalks, localized complete item names and icons remain open. Later write phases remain gated
  on controlled captures and console validation, including the existing Wonderlands levelling-budget policy.
- BL4 codec/file-role research and the exact owner checklist are in [BL4 feasibility](BL4-feasibility.md).

## Sources and attribution

Research date: 2026-10-28. Downloaded source was read as untrusted text, never executed. Implementation
code is independently written; reference resources contain schema/numeric facts. Notices are retained
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
| Serial envelope / layout adapter | [BL3][bl3-serial], [TTWL][ttwl-serial] | CJ Kucera / Abram Hindle; upstream credits Baysix | [Zlib][bl3-license], [Zlib][ttwl-license] | 2026-10-28 | Bounded read-only C# adaptation; no serial mutation |
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

**GEARBOX-R4 / F7: coordinated character + profile transaction ownership** is still required for bank
transfers, shared Guardian state and account/character progression reconciliation. Exact host contract:

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

`Serialize()` can already return multiple files; an additional output API is unnecessary. Independent
document sessions lack coordinated ownership, conflict handling and history. This round returns only
the opened file and enables no transfers or structural serial writes. F1/F2/F3/F4/F5/F8 are available;
no plugin-local substitute for host transactions was added.

## Owner capture and validation checklist

- For each BL3/TTWL capture, record PS5 game version, region, DLC/hotfix state, slot and a transcribed
  item card; include the character, matching profile and backup from each snapshot. Do not include
  credentials or account-derived codec material in reports.
- Capture a no-action save/reload/save pair, then separate before/after pairs for **one** pickup,
  sale/removal, equip/unequip, favourite/junk toggle, backpack↔bank move, lost-loot claim and
  enchantment/reroll. Include duplicates and full/near-full containers; isolate each action.
- Include BL3 starter guns/cosmetic pickups/legacy serial versions, and TTWL level-40/endgame gear
  with nonzero rerolls and each observed Chaos tier. Current TTWL characters are level 12.
- Capture one level gain, one skill allocation/respec, one SDU purchase and one ammo refill, separately.
  Guardian: profile + multiple characters before/after one token/perk. Myth: each indexed allocation,
  rotation and capped stat, with matched profile. These are later write-phase prerequisites.
- On a backed-up spare slot, validate each enabled write family independently: restore, load, confirm
  intended visible effect and unrelated inventory, re-save, reload, then return the new dump plus
  observations. Report game clamping. Only the owner can promote maturity to Stable.
- For BL4 use the separate [file-role/codec capture checklist](BL4-feasibility.md). No BL4 writes.

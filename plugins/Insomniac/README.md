# Ratchet & Clank: Rift Apart (`insomniac`)

Version **0.6.0**, **Experimental**, for decrypted **PPSA01474** saves. Phase 1 of
[#20](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/20) is implemented;
Round 2 delivers Phase 2's read-only weapons, armour, upgrades, collectibles and
world views. Controlled ownership/equipment, upgrade-node and precise pickup joins
remain evidence gaps. The write-enable round adds bounded existing scalar edits under
the owner's “Enable writes. Keep warnings.” decision; console acceptance is unproven.
The engine family name does not claim Spider-Man, other titles or other regions.
The completion round adds F9 storage diagrams/link-only maps, F6 calculated-balance
target planning, more F4 ID references, F8 raw inventory paging and robustness fixes.
See [COMPLETION-AUDIT.md](COMPLETION-AUDIT.md) for every requirement and acceptance gap.

## Views

- **Player:** career/spent counters, inferred available bolts, play time, challenge
  flags, gold-bolt options and per-location counters. Career/spent bolts and seven
  existing gold-bolt switches are editable. Career weapon XP is aggregate and read-only.
  The F6 **Set calculated bolt balance** action previews CareerBolts and derived
  balance/expanded diagnostics while preserving SpentBolts, with one-step undo/redo.
- **Loadout:** four exact quick-select paths using F4 read-only reference fields.
  The attributed F2 catalog supplies twenty English weapon-label candidates,
  including Blackhole Storm for `warprifle`. All twenty paths hash to measured
  inventory tokens; localization aliases remain inferred. Unknown paths stay raw.
- **Weapons:** F8 search/filter/sort over weapon/melee records, with candidate names,
  config tokens, container identity, per-record XP/ammo and Omega state. No record
  presence, shortcut or container position is equated with ownership/equipment.
- **Armour:** F8 collection checklist with 57 attributed piece candidates, independent
  stored collection bits and menu-viewed flags. Unknown/reserved bits stay visible.
  A separate appearance table shows vanity config/ID/color/preview slots. Catalog
  bonus categories describe collection; no effective percentages or equip joins.
- **Upgrades:** F8 Mods masks, sixteen ModGrid row masks, thirty-two ModCount slots
  and SelectedCell per supported weapon record. Values remain inferred schema
  state, not named purchased nodes, prices, prerequisites or legal caps.
  A separate F8 diagram list lazily opens an F9 graph of the sixteen stored masks.
  Every graph node links to its exact scalar text alternative; all states are Unknown
  and there are no fabricated prerequisite edges or costs.
- **Collectibles:** F8 location counters, ten inferred spybot identity candidates and
  complete 64-bit spybot/Lorb/Zurpstone checklists with unresolved slots retained.
  Collectible award CurrentValue/PrizeGiven fields include CraiggerBear progress,
  with no bear-count, completion or platform-unlock inference. No guide coordinates
  or exact gold-bolt hash joins are invented.
  F9 lists all 64 spybot storage bits with links to exact read-only controls and an
  attributed external planet-map index. All pickup statuses stay Unknown; bit set/clear
  is explicit in each description. No community artwork or coordinates are embedded.
- **Inventory:** 27 sequentially bounded TVNI containers per measured gameplay save,
  lazy F8 record tables, five F4 schema type joins, GUID/config byte-token candidates,
  candidate counts and candidate per-weapon XP/checkpoint-ammo/loaded-ammo state.
  Container tags repeat; no actor, ownership or equipped-state mapping is asserted.
- **World:** F8 mission/objective node states and completed counts, pocket dimension
  completion flags and global progress values. Stored menu mission codes join to
  node keys by CRC at runtime. Additional diagnostic tables show graph keys, award
  codenames, gold-bolt hashes and fog bounds/counts. No trophy unlock or map transform.
- **Campaign details:** character/checkpoint/location codes, multiplier and additional
  flags/counters with storage-only confidence and interpretation warnings.
- **Preferences:** more than 100 scalar fields grouped into audio, controls,
  accessibility, language/subtitles and display/other settings. Numeric menu codes
  remain read-only; existing scalar booleans are editable (61 keys in this corpus).
  No PC enum ordinals or game-safe numeric limits are assumed.
- **Systems:** recursive, bounded, lazy DDL trees for twelve gameplay roots and the
  preference root. Maps use stored field identities; large arrays expand in pages
  of 100 values, and empty collections explicitly show zero entries.
- **Overview / Sections / Raw bytes:** DAT1 descriptors, paged hex and bounded ASCII
  runs. F3 renders section cells as visible row-detail controls. Nested objects now
  expand into content instead of empty plugin-generated sections.

The plugin depends only on the SDK and performs no filesystem/network I/O. It reads
pinned `IReferenceContext` catalogs and works without packs by showing raw IDs.
Only embedded text data is distributed: no game icons, textures or map art.

## Format and confidence

The DAT1 envelope uses LE `0x44415431`, header word 8, exact file size and bounded,
non-overlapping section descriptors. Original descriptor order, string metadata,
unknown sections, padding and auxiliary records are preserved verbatim.

DDL objects use zero / `0x03150044` / field count / body size headers, named fields
with seeded CRC32 identifiers, stored numeric widths and UTF-8 strings. Name CRC
uses reflected `0xEDB88320`, seed `0xEDB88320`, no final inversion. It is a name
identifier, not a save-body checksum. Names, strings, types, bounds and finite DDL
numbers are checked. Segment and objective roots preserve their +4/+8 prefixes.
Nested value boundaries align to four bytes in the file; individual scalars remain
packed. Strings retain their original metadata and relative payload padding.

Reader limits: depth 32, 32,768 objects, 65,536 fields, 524,288 values and four MiB
of decoded text per section; 1,024 fields per object and 65,536 bytes per string.
Unknown bounded inventory payloads remain opaque. Every serialization reparses the
DAT1 envelope, supported DDL and inventory framing, then returns an isolated copy.
See [EVIDENCE.md](EVIDENCE.md) for the measured layouts and F1 comparisons.

| Display | Confidence and limitation |
| --- | --- |
| Named career counters, flags, grid dimensions, DDL names/types | Verified storage identities; this does not establish HUD calibration or write acceptance |
| Available bolts | Inferred career minus spent; independent in-game comparison still needed |
| Career raritanium / spending | Separate stored counters; late spending exceeds career total, so no available balance is offered |
| Difficulty / CurrentLevel | Stored codes only; CurrentLevel is not player level and is zero throughout this corpus |
| Weapon names | Twenty attributed localization candidates; all twenty path-to-config-token joins corroborated on PS5; ownership/equipment unproven |
| Inventory type IDs | Verified against five CC0 schema IDs and name hashes |
| Inventory GUID/count/XP/ammo/upgrade/vanity slots | Inferred supported full-mask schema interpretations; all 1,120 weapon/melee and 3,687 vanity records checked; no ownership, level conversion or ammo cap |
| Armour collection and spybot labels | Inferred schema ordinals; base armour bit population matches LevelVanityPickups and spybot bit-to-location counts match LevelRYNOPlans in all 36 primary records |
| Mission/dimension states | Verified stored fields; mission-code joins use current-save CRC identities; viewed entries are not completion proof |
| Raw DDL semantics, mission/planet/collectible joins | Unknown unless separately described; stored bytes remain available |
| Fog set-bit count | Inferred interpretation of byte population counts, including padding; not an exploration percentage |

## Integrity and writing

**Not validated on console. Integrity coverage is unknown; the game may reject the
edited save.** Every writable field repeats this warning and retains Inferred
confidence. No edited save has been validated in-game. Conformance establishes
toolkit behavior, not game acceptance; only the owner can mark Stable.

Writes patch only descriptor-located existing scalar slots, without resizing or
changing descriptors, name hashes, strings, unknown payloads or padding. The known
name CRCs identify unchanged names and therefore need no repair. No save-body
checksum/signature algorithm is established. Serialize reparses the complete DAT1,
all supported DDL roots and inventory before returning an isolated copy.

Bolt counters allow 0 through the smaller of their actual unsigned wire capacity
and the conservative corpus maximum (CareerBolts 1,297,735; SpentBolts 260,875).
An imported value above that maximum remains inspectable and extends only that
field's upper bound to its original value. These are observed bounds, not proven
game caps. Atomic batches validate final CareerBolts >= SpentBolts. Calculated
available bolts and expanded raw scalar diagnostics update live and participate
as derived changes; Core undo/redo restores the original bytes.

The seven gold-bolt switches change only their existing bool atoms. They do not
grant collectibles, satisfy unlock thresholds or alter other progress. A locked
or inconsistent option may be ignored, normalized or rejected by the game. Boolean
preferences are independent edits in the preferences file; no cross-file write
dependency has been identified, so no F7 transaction is fabricated.

XP/weapon XP, ammo, raritanium, upgrade/armour/collectible/quest state and challenge
conversion remain read-only: mirror/dependency rules, ownership, constructors or
legal ranges are incomplete, so writing them would risk inconsistent unknown
structure. Quick-select strings also stay read-only because their stored metadata
and relocation/size rules have no proven writer. Numeric preferences have no
prepared, calibrated write policy. The F6 bolt-target action reuses the existing
career-counter writer; there is no safe ammo/loadout/upgrade bulk recipe. Structural
operations require F5 and actual cross-file dependencies F7; neither has a safe
applicable mutation in this format yet.

## Enabled writes and their evidence

All validation items are tracked in [owner issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31);
the exact protocol remains in [OWNER-CAPTURES.md](OWNER-CAPTURES.md).

| Write | Confidence | Evidence | Integrity status | Owner validation needed (with a link to #31) |
| --- | --- | --- | --- | --- |
| CareerBolts | Inferred | CC0 schema/name CRC; all 36 primary records; observed UInt16/UInt32 widths; descriptor probe and production edit/reopen/F1/undo tests | Fixed-width only; unrelated bytes unchanged; body integrity unknown; game may reject | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): baseline, +1 bolt, HUD wallet, purchase, re-save/reload/re-export |
| SpentBolts | Inferred | Same schema and corpus; observed maximum 260,875; edit/reopen/F1/undo tests; final-balance batch validation | Same unknown integrity; game may reject; accounting semantics not console-calibrated | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one purchase capture, isolated spent-counter edit and wallet/downstream validation |
| Seven existing gold-bolt option booleans | Inferred | Named one-byte DDL bools; RPG research probe; each production switch edit/reopen/F1/undo tested in early/late samples | Bool only; collectible/unlock state untouched; game may reject | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): legal in-game toggle then independent edit for each unlocked option; check effect and re-save/reload |
| Existing scalar boolean preferences (61 keys) | Inferred | Descriptor/name validation in both supplied primary preference records; each edit/reopen/F1/undo tested | Bool only; numeric/string fields untouched; independent integrity unknown; game may reject | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): unchanged preference export, one-setting capture and separate edit/menu/re-save/reload checks |
| F6 calculated bolt target | Inferred | Existing CareerBolts writer; previews career and derived balance/expanded diagnostics; all 36 primary records tested for pure preview, apply/reopen, single-atom F1 diff and Core undo/redo | Only career atom changes; spent history preserved; body integrity and HUD accounting unknown; game may reject | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): lower balance by one, inspect preview, apply, purchase/native-save/reload/re-export; test zero on a disposable slot |

The observed 70 write controls are listed in [ENABLED-WRITES.md](ENABLED-WRITES.md).
The additional F6 action exposes the same career atom, rather than a new storage writer.

## Framework feature adoption

| Feature | Adoption | Where / concrete applicability |
| --- | --- | --- |
| F1 SaveDiff | **Used** | RiftEvidenceDecoder and differential/write/planning tests retain DDL identities, widths, generations and identical backup collapse; no private save bytes committed. |
| F2 reference packs | **Used** | Five embedded catalogs, immutable Open context, source sidecars, per-record UI provenance and Sources-panel attribution; no plugin I/O. |
| F3 rich cells | **Used; extended this round** | Inventory lazy detail cells and confidence/warnings; F9 diagrams expand from F8 detail cells. No image cells because no safely licensed game art is supplied. |
| F4 reference pickers | **Used; extended this round** | Four loadout paths plus arsenal config paths, armour/world identity candidates and raw inventory type IDs. Unjoined opaque GUID/config bytes are retained rather than falsely treated as catalog keys. |
| F5 structural undo | **N/A** | No lossless record constructor/remover/string relocation/dependency repair exists. Existing writes preserve size and use scalar undo; speculative structure mutation would violate data preservation. |
| F6 action planning | **Added this round** | Explicit calculated-bolt target, primary/derived previews, warnings, no-op explanation, stale/forged-plan rejection and Core grouped undo. #27 query scopes are inapplicable to a document-wide wallet target; row/query scopes reject explicitly. Ammo/refill/loadout/upgrade actions lack ownership, legal caps and dependency recipes. |
| F7 document groups | **N/A** | Gameplay and preferences have no identified coupled invariant; each writer affects one existing file. No transaction over unrelated files is invented. |
| F8 queryable tables | **Used; extended this round** | Arsenal/armour/upgrades/collectibles/objectives/dimensions/globals plus upgrade-diagram lists and every populated raw inventory container; bounded pages, search/filter/sort and lazy cell bindings. Raw DDL arrays retain bounded diagnostic pages. |
| F9 maps/graphs | **Added this round** | Read-only stored upgrade-row graphs with field targets; external-link-only spybot map/list with all bits, text alternatives and explicit Unknown semantics. Physical coordinates/fog/prerequisites require owner captures. |
| F10 exact/nullable numbers | **Used** | Width-specific integer specs and exact UInt64/128-bit transport preserve IDs/masks above 2^53 (including UInt64.MaxValue vectors). DDL floats retain their actual binary precision. DDL type-19 is an empty array, not a nullable scalar or union; no artificial nullable/union control. |
| F11 shared codecs | **N/A** | These measured DAT1/DDL/TVNI files are uncompressed, with no Kraken or SQLite payload; no local codec implementation exists to replace. |
| F12 SaveDiff F8 traversal/hooks | **Pending #32** | Not on the baseline main; retain existing F1 test adapter without inventing a replacement framework. |
| F13 browse/inspect/filter/duration | **Pending #33** | Not on the baseline main; ID references stay read-only and float play time remains its stored numeric type. |

Historical research tested 41,652 CRC/FNV/Murmur section-digest candidates across
36 primary gameplay records without finding a nondegenerate outside-range match.
That does not exclude custom seeds, other coverage, mirrored state or signatures.
Section `BC60B495` follows career XP in every sample; it is not an identified digest.
Public cheat listings did not supply a proven PS5 patch/checksum implementation.
Round-2 in-memory fixed-width bolt/option probes preserve every unrelated byte and
produce one F1 semantic change, but do not prove integrity/console acceptance. An XP
probe reparses despite its unrepaired BC60B495 mirror: parser acceptance is insufficient.
See [RESEARCH.md](RESEARCH.md), [EVIDENCE.md](EVIDENCE.md) and the exact
[owner acceptance checklist](OWNER-CAPTURES.md).

## Validation

Use the environment assignment and test command in the **same shell invocation**:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet test --project plugins/Insomniac/SaveToolkit.Plugins.Insomniac.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

Latest sample-backed plugin run: **111 passed, 0 failed, 0 skipped**. All **76** game
records round-trip byte-identically, including duplicate generations and backup twins.
The tests cover all 36 primary inventories, field/name vectors, nested alignment,
parent bounds/depth/count corruption, unknown payloads, embedded pack hashes/licensing,
read-only F4 fields, F8 lazy page construction/filtering/numeric sorting/stable IDs,
Core session page acceptance, all new query bindings, full-mask upgrade/vanity
boundaries, 20/20 config-path hashes, ordinal corroboration and in-memory write probes.
New production-write tests exercise every control in both primary preferences,
both representative gameplay widths, and atomic nine-control batches in all 36
primary gameplay records. They verify persisted values, single-atom F1 changes,
every unrelated byte, wrong types/ranges, negative-balance rejection, mixed
read-only rejection, live diagnostics and real Core single/batch undo/redo.
Foreign-title DAT1 matches remain read-only.
Unusual counters above the corpus maxima remain byte-identical on open and can be
restored after edits; malformed serialized booleans are rejected inside Serialize.
Completion tests cover F9 marker bits across all 36 primary records, Core graph
expansion and exact field targets, paged inventory details/catalog IDs, F6 pure
preview/apply/reopen/single-atom diffs/grouped undo across all 36 records, boundary
targets, no-ops, changed spending headroom, raw diagnostics and stale/forged plans.
Negative counter/mask corruption now yields a named SaveFormatException; unfamiliar
optional diagnostic shapes remain unresolved. UInt64 transport is spec-coerced
exactly above 2^53, long opaque ASCII previews are bounded, and returned export
buffers remain isolated. The immutable player-field definitions load once; Serialize
reuses the isolated validation parse instead of making another whole-file copy.
Sample-dependent
tests skip when samples are absent.

API build: **0 warnings / 0 errors**. Core's actual F8 session path is exercised in
the plugin suite. No frontend/framework source changed; historical round-1 Core and
frontend counts are recorded in issue #20 rather than presented as fresh runs.

F1 evidence tests compare a same-campaign snapshot pair with persistent inventory
identity candidates and changed XP/ammo candidates, and ZIP generations 0/1 with
correct backup collapse. They are not controlled one-action experiments. The F1
adapter is confined to the test project; production retains its SDK-only dependency.

## Sources and attribution

Retrieved/rechecked 2026-10-28. Every embedded dataset has an adjacent source notice,
manifest attribution, `Info.References` entries and field/section provenance in the UI.
The Sources panel lists the F2 datasets resolved by the document.

| Dataset / research | Author and source | License / modifications |
| --- | --- | --- |
| `schema.json`: 86 field names and containing types | Neptuwunium / [Rivet CC0 schema, a631c96e](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json) | CC0-1.0, confirmed by the file-specific sidecar; selected names only, omitted runtime offsets/defaults/code |
| `item-types.json`: five serialized type IDs/names | Same pinned Rivet schema | CC0-1.0; selected type IDs, original uncertainty notes; measured framing independently verified on PS5 |
| `Resources/PlayerFields.json` | Same pinned Rivet schema; toolkit contributors' labels/groups | CC0 schema facts, original labels; sidecar documents selection and PS5 validation |
| `ENABLED-WRITES.md`: observed writable storage identities | Independent corpus verification by toolkit contributors; same pinned Rivet CC0 schema for identity research | CC0-1.0 factual names, no user preference values or upstream code; retrieval/modification note alongside the list |
| `weapons.json`: twenty English name/description candidates | Ratchet & Clank Wiki contributors, [menu transcript revision 403465](https://ratchetandclank.fandom.com/wiki/Rift_Apart_menu_transcript?oldid=403465) | CC BY-SA 3.0 derived data; short paraphrases and inferred localization aliases; all twenty paths match PS5 tokens; no prices, limits or art |
| `armor.json`: 57 piece/slot/set candidates and bonus categories | [Armour revision 402364](https://ratchetandclank.fandom.com/wiki/Rift_Apart_armor?oldid=402364), Ratchet & Clank Wiki contributors, plus pinned Rivet CC0 schema | Entire derived dataset CC BY-SA 3.0; selected names and bonus paraphrases with inferred ordinal/set aliases; no bonus percentages or availability claims |
| `world.json`: twenty location/spybot labels | Neptuwunium / pinned Rivet schema above | CC0-1.0; selected enum labels and ordinal keys, original confidence notes; no pickup coordinates |
| Asset path identity algorithm research | [ALERT crc64.py, bc90ed9b](https://raw.githubusercontent.com/Tkachov/ALERT/bc90ed9b53fbcaa3b7acf94b36f00e77eab0dacb/dat1lib/crc64.py) | GPL research only; independently written bitwise mathematical CRC, no copied lookup table/code. Verified against PS5 inventory tokens; unrelated to integrity |
| Section strategies / format research | Neptuwunium / [Rivet SaveData, a631c96e](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet/Data/SaveData.cs) and [save CLI](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet.CLI/Utility/RivetSaveCommand.cs) | EUPL-1.2 reference only; no implementation copied or executed. Write-round recheck still does not establish PS5 integrity |
| Earlier DAT1 and integrity research | [ALERT format wiki](https://github.com/Tkachov/ALERT/wiki/Formats), [RiftRipper](https://github.com/VELD-Dev/riftripper), [NinjaSaves](https://ninjasaves.com/cheats/ratchet-clank-rift-apart-ppsa01474) | Research only; no source/artwork port or proven integrity routine |
| F9 external planet-map index | [Guides4Gamers interactive maps](https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/), accessed 2026-10-28 | Link only, reuse licence unconfirmed; no coordinates, art, marker descriptions or scripts imported. Graph descriptions/layout are original presentations of attributed CC0 schema facts and measured stored row masks. See SPATIAL-SOURCES.md. |

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the reference pack's
[data.source.md](SaveToolkit.Plugins.Insomniac/ReferencePacks/insomniac.rift.en/data.source.md).
CC BY-SA data keeps its attribution and share-alike terms; those terms do not relicense
the plugin code. No EUPL/GPL implementation is incorporated.

## Open work and exact owner capture checklist

Phase 1 and the round-2 **read-only Phase 2 view scope** are delivered. The plan's
controlled ownership/equipment, localization, legal-limit, upgrade-node and exact
pickup-location requirements remain open. Phase 3 scalar writes and the Phase 4
bounded resource-target action are enabled with warnings. Other Phase 4 dependencies
need controlled captures; Phase 5 has no safe structural writer. The complete numbered capture protocol and exact
acceptance candidates are in [OWNER-CAPTURES.md](OWNER-CAPTURES.md). In particular,
the corpus has no positive Omega state, and vanity ColorId includes codes above 19.

For each pair, use one PPSA01474 slot/build and record region, version, normal versus
Challenge Mode, character and DLC. First capture save/load/save without gameplay as
churn control. Export decrypted before/after dumps, HUD screenshots and an action log:

1. Earn one known bolt amount, then separately buy one weapon/item. Record wallet
   balances. For raritanium, separately earn one and purchase one known upgrade,
   recording displayed balances, cost and the selected grid node.
2. Acquire one ordinary weapon; then make separate pairs for one XP/level increase,
   one ammo use and one refill. Record weapon name, level, XP/bar and ammo counts.
3. Buy one Omega upgrade in Challenge Mode. Capture its name, level, upgrade tree,
   costs and mode. Do not combine it with a challenge-reset capture.
4. Collect one armor piece, then separately equip/unequip it. Record character,
   slot/appearance and collection bonus; these are distinct state changes.
5. Collect one gold bolt or spybot at a documented planet/location. Repeat for
   Lorbs, CraiggerBears and Zurpstones before expecting location-resolved checklists.
6. Supply an optional permitted config/localization dump with title/build/locale
   metadata to prove path/hash/name joins. Unknown paths remain usable without it.
7. Validate the enabled writes under [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31):
   test an unmodified export, then career/spent bolt edits and each unlocked option
   toggle separately through the plugin.
   Record load success, intended effect, subsequent re-save/reimport and downstream
   play. Preferences require their own acceptance pair. Only the owner sets Stable.

Never commit saves, extracted game assets, account metadata or research patch outputs.

## SDK requests

None blocking this release. F2/F4 support the attributed read-only path references;
F3 renders nested cells and confidence/details. INSOMNIAC-R8/R9 and the old R5 blank
cell concern are covered for this scope. F6/F9 are now adopted directly. Accurate
physical maps, prerequisite edges and purchase/refill recipes need the R6/R7/R12
owner captures; the lack of those joins is not an SDK bug. F12/F13 remain pending
[#32](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/32) /
[#33](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/33); no substitutes added.

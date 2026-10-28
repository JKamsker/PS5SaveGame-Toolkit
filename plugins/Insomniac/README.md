# Ratchet & Clank: Rift Apart (`insomniac`)

Version **0.4.0**, **ReadOnly**, for decrypted **PPSA01474** saves. Phase 1 of
[#20](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/20) is implemented;
Round 2 delivers Phase 2's read-only weapons, armour, upgrades, collectibles and
world views. Controlled ownership/equipment, upgrade-node and precise pickup joins
remain evidence gates; no unsupported Phase 2 semantics or Phase 3 writes are claimed.
The engine family name does not claim Spider-Man, other titles or other regions.

## Views

- **Player:** career/spent counters, inferred available bolts, play time, challenge
  flags, gold-bolt options and per-location counters. Career weapon XP is aggregate.
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
- **Collectibles:** F8 location counters, ten inferred spybot identity candidates and
  complete 64-bit spybot/Lorb/Zurpstone checklists with unresolved slots retained.
  Collectible award CurrentValue/PrizeGiven fields include CraiggerBear progress,
  with no bear-count, completion or platform-unlock inference. No guide coordinates
  or exact gold-bolt hash joins are invented.
- **Inventory:** 27 sequentially bounded TVNI containers per measured gameplay save,
  lazy record tables, five schema type joins, GUID/config byte-token candidates,
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
  remain numeric; no PC enum ordinals or game-safe limits are assumed.
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
Unknown bounded inventory payloads remain opaque. No-op serialization reparses the
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

**All writes remain disabled.** There are no setters, mutation actions or export
bypasses. Conformance and byte-identical round trips do not prove that a console
will accept an edit. No edited save has been validated in-game.

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

Latest sample-backed plugin run: **86 passed, 0 failed, 0 skipped**. All **76** game
records round-trip byte-identically, including duplicate generations and backup twins.
The tests cover all 36 primary inventories, field/name vectors, nested alignment,
parent bounds/depth/count corruption, unknown payloads, embedded pack hashes/licensing,
read-only F4 fields, F8 lazy page construction/filtering/numeric sorting/stable IDs,
Core session page acceptance, all new query bindings, full-mask upgrade/vanity
boundaries, 20/20 config-path hashes, ordinal corroboration and in-memory write probes.
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
| `weapons.json`: twenty English name/description candidates | Ratchet & Clank Wiki contributors, [menu transcript revision 403465](https://ratchetandclank.fandom.com/wiki/Rift_Apart_menu_transcript?oldid=403465) | CC BY-SA 3.0 derived data; short paraphrases and inferred localization aliases; all twenty paths match PS5 tokens; no prices, limits or art |
| `armor.json`: 57 piece/slot/set candidates and bonus categories | [Armour revision 402364](https://ratchetandclank.fandom.com/wiki/Rift_Apart_armor?oldid=402364), Ratchet & Clank Wiki contributors, plus pinned Rivet CC0 schema | Entire derived dataset CC BY-SA 3.0; selected names and bonus paraphrases with inferred ordinal/set aliases; no bonus percentages or availability claims |
| `world.json`: twenty location/spybot labels | Neptuwunium / pinned Rivet schema above | CC0-1.0; selected enum labels and ordinal keys, original confidence notes; no pickup coordinates |
| Asset path identity algorithm research | [ALERT crc64.py, bc90ed9b](https://raw.githubusercontent.com/Tkachov/ALERT/bc90ed9b53fbcaa3b7acf94b36f00e77eab0dacb/dat1lib/crc64.py) | GPL research only; independently written bitwise mathematical CRC, no copied lookup table/code. Verified against PS5 inventory tokens; unrelated to integrity |
| Section strategies / format research | Neptuwunium / [Rivet SaveData, a631c96e](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet/Data/SaveData.cs) | EUPL-1.2 reference only; no implementation copied or executed |
| Earlier DAT1 and integrity research | [ALERT format wiki](https://github.com/Tkachov/ALERT/wiki/Formats), [RiftRipper](https://github.com/VELD-Dev/riftripper), [NinjaSaves](https://ninjasaves.com/cheats/ratchet-clank-rift-apart-ppsa01474) | Research only; no source/artwork port or proven integrity routine |

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the reference pack's
[data.source.md](SaveToolkit.Plugins.Insomniac/ReferencePacks/insomniac.rift.en/data.source.md).
CC BY-SA data keeps its attribution and share-alike terms; those terms do not relicense
the plugin code. No EUPL/GPL implementation is incorporated.

## Open work and exact owner capture checklist

Phase 1 and the round-2 **read-only Phase 2 view scope** are delivered. The plan's
controlled ownership/equipment, localization, legal-limit, upgrade-node and exact
pickup-location requirements remain open. Phase 3 has research probes, without write
promotion; Phases 4–5 remain gated. The complete numbered capture protocol and exact
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
7. Before any write promotion, test an unmodified export, then one fixed-width bolt
   edit and one existing option toggle separately in a private research harness.
   Record load success, intended effect, subsequent re-save/reimport and downstream
   play. Preferences require their own acceptance pair. Only the owner sets Stable.

Never commit saves, extracted game assets, account metadata or research patch outputs.

## SDK requests

None blocking this release. F2/F4 support the attributed read-only path references;
F3 renders nested cells and confidence/details. INSOMNIAC-R8/R9 and the old R5 blank
cell concern are covered for this scope. Accurate maps/graphs still need the
INSOMNIAC-R10/R12/R18 data and presentation work; no plugin-local substitute or
structural/bulk edit workflow is introduced.

# Sucker Punch save plugin (`suckerpunch`)

Titles: **PPSA02225** (Ghost of Tsushima, Director's Cut) and **PPSA26344** (Ghost of Yōtei).
Version 0.8.0 is **Experimental**: Tsushima inventory and play-time edits pass conformance and exact byte-difference
tests, but have not been tested in-game. Yōtei documents remain **read-only**. No sample bytes,
extracted saves, or third-party implementation code are included in this plugin.

## Enabled writes and their evidence

The 2026-10-28 owner decision, **"Enable writes. Keep warnings."**, replaces console-evidence gates.
This round enables **two additional write paths**: the existing v48/v49 play timer and F6 resource/ammo
target-quantity planning. Existing scalar quantities and the narrower goods action stay enabled.
Console evidence is tracked separately in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31);
no write is advertised as console-validated or Stable.

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Existing resource quantities (already enabled) | Inferred | Named five-word records and consistent values across all 25 Tsushima entries | Known additive checksum repaired; output reparsed; unrelated bytes preserved | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): +1 resources, labels, carry limits, load/resave/reload |
| Existing ammo quantities and count copies (already enabled) | Verified byte structure; console effect unverified | Apollo named count-word targets; both copies agree in every sample | Both words updated together; checksum repaired; output reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): fire/pick up ammo, capacity/clamping, load/resave/reload |
| F6 target quantities for existing supported resources/ammo (**new**) and Supplies/Flowers/raw Hide (existing action) | Same Inferred/Verified byte evidence as individual fields | Uses the same existing-record eligibility and fixed-width writer; all 25 samples tested | Preview lists primary counts, derived ammo copies and checksum, skips and warnings; apply revalidates; one undo/redo group | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): validate a small multi-resource/ammo edit in each intended build/mode |
| v48/v49 play time (**new**) | Inferred | Four-byte float at difficulty-table + 231; whole minutes match all non-empty slot subtitles; 23 supported entries including backups | Only timer plus repaired checksum change; output reparsed; finite bounded values only | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): small timer edit, load/resave/reload, inspect per-difficulty counters and stale subtitle normalization |

Quantity fields, action descriptions, previews and apply outcomes retain **not validated on console**
warnings. Quantity bounds remain 0–32767 (an upstream edit value, not a game capacity). Timer bounds
are 0–198318.828125 seconds, the highest observed v48/v49 value; a larger original value extends that
document's bound so undo can restore it. This is a conservative editor range, not a game limit.
The timer warning explicitly says the game may normalize or reject the edit: per-difficulty counters
and `param.sfo SUBTITLE` remain unchanged and may disagree. No v47 or unknown-version timer writer exists.

Still disabled for structural safety, rather than pending console approval:

- **Yōtei:** unknown integrity alone would permit a warning-labelled experimental writer, but there is
  no identified gameplay quantity offset. Its two zero-valued, differently shaped name tables could
  be schemas/registries, not inventory. Guessing a uint32 count could overwrite unknown fields or
  references. A **"game may reject the edited save"** warning cannot make such a structural guess safe.
  Empty placeholders also contain no records to edit. Nothing is written to either table or header.
- **Equipment, techniques, character progression and world state:** the scanner identifies strings,
  not writable state fields or ownership/selection/reward relationships. Replacing them could damage
  definitions or references. No gated writer for these was implemented in the earlier handoffs.
- **Difficulty and structural add/remove:** general difficulty changes alter string lengths; enclosing
  sizes, offsets and references cannot be updated safely by the current fixed-width model. Item
  insertion/removal likewise has no understood enclosing structure or acquisition side effects.
  No F5/F7 transaction is exposed because no safe structural or sibling-file mutation is defined.

## Supported behavior

* Tsushima: edit quantities in existing unlocked resource and ammunition records. Ammunition edits
  update both count words targeted by the Apollo / Save Wizard refill codes. The checksum is repaired
  immediately after an atomic edit batch, exposed as a derived change, and checked again during export.
* Attributed, revision-pinned wiki names accompany defensible raw-ID mappings. `IngredCoreOre`, raw
  `Hide`, numbered charms and armor slots retain their raw names and **Unknown** mapping confidence.
  Resource quantity meanings remain **Inferred**. Ammo count meanings remain **Verified** against
  source anchors and both count copies; this is separate from name mapping and console validation.
* An F6 preview/apply action sets a target quantity for existing Supplies, Flowers and raw Hide records
  in observed container versions 47–49. Document scope covers those three records; an explicit empty
  row selection changes nothing. Locked/oversized/already-matching rows are skipped; other selected
  IDs are excluded. The preview includes count/checksum changes and warnings, is pure, and is rechecked
  on apply. A second action, `inventory/set-quantity`, extends the same workflow to all existing
  supported resource/ammo records, with derived ammo copies shown explicitly. Both actions use
  ordinary validated scalar edits and one Core undo/redo group. Neither unlocks items or claims a capacity.
* Difficulty is **Inferred** and read-only. The **Inferred** v48/v49 play timer is writable, with the
  explicit metadata/counter warning and observed bound above. Every unedited slot's whole play-time
  minutes and difficulty match its `param.sfo SUBTITLE`. The timer publishes F13
  `FloatFormat.Duration` with one stored unit per second; raw float32 editing and fractions are retained.
  The v47 timer layout is different, so no timer is published for it. No console metadata or difficulty counters are rewritten.
* Locked records, unmatched ammo copies, unmapped item kinds and existing quantities above 32767 are
  inspection-only. The editable range 0–32767 follows the upstream refill value, **not a proven game
  capacity**. The UI warns that quantities can be clamped. Ownership/upgrade flags are never changed.
* The embedded F2 pack `suckerpunch.tsushima` contains attributed raw-ID catalogs for the 48 inventory
  records plus observed equipment and technique identifiers. F4 `ReferenceSpec` fields preserve each
  exact ID and expose the catalog picker metadata; they are read-only because no safe ID replacement
  mapping has been proven. F13 opens these fields in browse-only mode, including category filters,
  declared Key/Name/Category sorting, paging, raw-ID previews and attribution. Each catalog field carries
  its dataset attribution and an Inferred/Unknown confidence label.
* Tsushima has read-only Equipment, Techniques and Character tabs. They show first observed offsets and
  explicit Unknown ownership/equipped/progression relationships. Identifier presence is not treated as
  ownership, a loadout, a prerequisite, or an editable value. Raw record words remain read-only.
* Inventory retains a visible raw-ID column alongside friendly reference labels, categories and mapping
  confidence. Equipment uses the F8 `equipment/identifiers` collection with stable row IDs, server-side
  text/category/state filtering, deterministic sorting and bounded pages; Techniques remains a small
  eager table. Both retain Unknown ownership/state. Lazy reference guides cover resources, charms, armor,
  sword kits and techniques. The Techniques tab adds an F9 read-only `GraphNode` over observed identifiers;
  every item links back to its reference field, and no prerequisite edge is invented while the state is
  unproven. The World tab adds an F9 `MapNode` over 17 region-scoped Shinto shrine
  references plus an eager text table for row navigation. It includes attributed whole-island artwork;
  shrine alignment is unverified, so all markers stay unpositioned and the text list stays available.
  Discovered/completed/rewarded state is Unknown for every entry. The external PowerPyx guide remains linked. Edits invalidate raw block snapshots. Guide views are capped at 200 rows and report
  truncation if a larger pack is used. Guide names are also read-only `ReferenceSpec` fields: their
  pickers browse the complete pinned catalog even when the eager guide page is capped. Guide tables
  use shared category sorting, while wiki IDs remain separate from saved IDs and cannot be committed.
* Opening copies the input; exports and hex pages are isolated copies. No-op exports are byte-identical,
  including unknown data, console metadata and padding. Only the selected document's file is returned;
  sibling saves, backup slots and `param.sfo` are preserved.

## Framework feature adoption

| Feature | Status | Concrete adoption or reason |
| --- | --- | --- |
| F1 SaveDiff | Used | `fixtures.json` records the uncontrolled Tsushima generation pair and selects `suckerpunch.native-v1`; bounded native and F8 view decoding are tested alongside `ByteDiffer`. No fixture embeds save bytes. |
| F2 reference packs | Used | Embedded `suckerpunch.tsushima` v3.0.0 catalogs and four art datasets are loaded through `IReferenceContext`; per-record and UI descriptions carry attribution and the Sources panel exposes each dataset. |
| F3 rich table cells | Used | Inventory, equipment, technique and world tables expose raw IDs, descriptions, mapping-confidence cells, warning tones and F6 action controls. Equipment/technique tables and lazy guides show local attributed ImageNode cells (106 unique icons/thumbnails, 158 catalogue bindings); the SDK has no separate detail-cell node, and actions remain dedicated planned controls rather than blank table cells. |
| F4 reference pickers | Used | Every catalog-backed inventory, equipment and technique ID is a read-only `ReferenceSpec` field with raw-ID fallback, category/description metadata and attribution. |
| F5 structural edits | N/A | No safe add/remove/reorder writer exists: enclosing sizes, references, ownership and acquisition side effects are unproven, so a plugin-local structural substitute would risk data loss. |
| F6 planned actions | Used | Existing resource/ammo target actions provide pure previews, skipped rows, derived ammo/checksum changes, stale-plan rejection and one Core undo/redo group. Query scopes are rejected because the writable inventory is a bounded fixed table and the equipment collection is read-only. |
| F7 document groups | N/A | No controlled capture proves that `memory.dat`, `dis.dat`, `param.sfo` or a backup must change with a supported edit; the safe contract rewrites only the selected file. |
| F8 queryable collections | Used | The per-save equipment identifier view is `equipment/identifiers`, with stable IDs, bounded pages, search, filters and deterministic sorting. Small inventory/technique/world views remain eager because they are below the paging threshold. |
| F9 maps and graphs | Used | Techniques publish a validated read-only `GraphNode` with field targets and a text alternative; World publishes a validated `MapNode` with attributed whole-island art, row targets, Unknown statuses and unpositioned markers because alignment is unverified; the schematic/list fallback remains. |
| F10 exact numbers and nullable/union specs | N/A | Supported edits are fixed-width uint32 quantities and an observed float32 timer; no save field carries an exact JSON number lexeme, nullable value or union that this plugin could preserve. |
| F11 shared codecs | N/A | Tsushima/Yōtei samples are fixed-size plaintext containers; no Kraken, Oodle or SQLite codec is involved. |
| F12 SaveDiff traversal/decoder hooks | Added | `ISaveDecoderProvider` registers SDK-only `suckerpunch.native-v1`; the F1 fixture uses the hook through `HookSaveDecoder`. Separate `PluginViewDecoder` tests traverse every F8 equipment row across small pages and verify incomplete-input limits. |
| F13 inspect actions/picker filtering/duration | Added | The float32 timer declares Duration/seconds. Inventory, equipment, techniques and all six guide catalogs use browse-only read-only references with shared category filters and declared sorting; guide tables use catalog category ordering. Inspection actions are N/A: existing actions change quantities and there is no message-only inspection operation to opt in. |

The merged F4/F6 parameter fix (`63580dc`) is N/A here: both planned actions accept only a
bounded integer target quantity, with no `EnumSpec` ID fallback or reference replacement parameter.

The explicit F12 hook emits stable native paths for four header words, inferred difficulty/play
seconds, all 48 existing Tsushima quantities and mirrored ammo counts. It reuses the validated
container/inventory/progress parsers, stops when the sink declines a value, honors file/depth limits,
and retains evidence warnings. It deliberately omits labels, offsets, reference-guide selections,
account data and unknown payloads. Yōtei exposes only four raw header words with an unknown-gameplay/
integrity warning; this hook does not claim a complete payload decode. The F8 view decoder remains
available as `--decoder suckerpunch` for view-oriented inspection.

```powershell
dotnet run --project tools/SaveToolkit.SaveDiff -- list-decoders
dotnet run --project tools/SaveToolkit.SaveDiff -- compare before.zip after.zip --decoder suckerpunch.native-v1
dotnet run --project tools/SaveToolkit.SaveDiff -- verify plugins/SuckerPunch/fixtures.json
```

`CanEdit` is per document: a validated Tsushima container with the recognised inventory is writable;
containers without its anchor can be inspected without edits. Yōtei and empty placeholders cannot edit.
An ambiguous anchor or a malformed anchored table is rejected with `SaveFormatException`.

## File roles and sample coverage

`dis.dat` contains individual slots; `memory.dat` contains state with the same Tsushima payload layout.
These are fixed-size decrypted files, with zero padding after the declared content in the supplied dumps.
No additional encryption or compression layer was found; runtime plugin code performs no file/network I/O.

| Title | File size | Supplied files | Distinct byte contents |
| --- | ---: | ---: | ---: |
| PPSA02225 | 555,008 | 25 | 12 |
| PPSA26344 | 616,448 | 8 | 3 (two non-empty, one all-zero pattern) |

The Tsushima archive includes primary/backup twins and two `sce_sdmemory` generations. The older
`memory.dat` is **v47**; the remaining content uses v48/v49. PS5 samples themselves contain v49, so
upstream descriptions of v49 as PC-only must not be used to classify these dumps' platform.

## Tsushima container and writes

All header words are little-endian uint32:

| Offset | Meaning | Confidence |
| ---: | --- | --- |
| 0x00 | Magic `0x14E` | Verified |
| 0x04 | Content end offset / content size | Verified |
| 0x08 | Additive checksum | Verified |
| 0x0C | Format version (47, 48, 49 observed) | Verified as stored version |

```text
checksum = (sum(bytes[0x10:contentSize]) + contentSize + 0x14E + version) modulo 2^32
```

The range is half-open; trailing padding is excluded and preserved even if non-zero. Header values and
content bytes are not re-encoded. After edits, only selected count words/the play-time float and the checksum at 0x08 change.
`Serialize()` validates the emitted container, inventory structure and recognised progress block before
returning it. Invalid batches leave every byte unchanged. Undo replays the primary quantity values and
recomputes the checksum, restoring the original bytes. Timer undo/redo likewise restores the exact float bits.

## Inventory structure and anchor evidence

Apollo's CUSA11456 patch and the SaveWizard archive use this sequence:

1. `80010015 ...`: find the 21-byte pattern `11 00 00 00` + ASCII `LegendsInvestment`.
2. `8801000F ...`: search forward from that pointer for `0B 00 00 00` + ASCII `ArrowNormal`.
3. `92000000 0000000F`: advance past the length-prefixed name.
4. `28000004` / `28000008`: write little-endian uint32 counts at +4 and +8 from the data start.

This interpretation was checked against Apollo's patch-engine source: type 8 searches, subtype 8
starts at the current pointer, type 9/2 advances it, and type 2/8 writes a pointer-relative uint32.
Type 4 multi-writes combine a starting value, repetition count, address stride and value increment;
they are not executed by this plugin. This is a targeted parser, not a cheat-code interpreter.

In every Tsushima payload the unique equipment-name anchor is followed, within 512 bytes, by:

```text
uint32 recordCount = 48
repeat recordCount times:
    uint32 nameByteLength
    ASCII  name[nameByteLength]       // no terminator
    uint32 word0                     // ownership/upgrade bits inferred: 0, 1, 3 observed
    uint32 word1                     // quantity
    uint32 word2                     // matching quantity for ammo; zero for resources observed
    uint32 word3                     // unknown
    uint32 word4                     // unknown, often 0xFFFFFFFF or small integers
```

Names are bounded to 64 ASCII identifier characters and unique. The parser requires the first
`ArrowNormal`, the trailing `NGPFlowers`, and named `Supplies`/`Flowers` records. All 48 records are
traversed with bounds checks; record sizes, flags and unknown words are preserved. No new items are
inserted. Ammo words 1 and 2 match in every supplied record; the plugin refuses to edit a mismatch.

The PS4 unlock anchors `5F502EE24A20`, `5F502EE24B20`, and rank/stance anchor
`FFFFFFFF000A000000` do **not** match these samples. Their relative unlock offsets are not applied.
The alternative Google Docs code mirror and Apollo patch were checked; no matching rank anchor emerged.

### Slot evidence

Values below are direct byte readings, **not independently observed in-game balances**. They are
asserted by format tests, while conformance and edit/difference tests cover all snapshots and backups.
The Supplies column includes its absolute count offset only to document the evidence; code uses anchors.

| Slot | Supplies (offset) | Bamboo (`IngredCoreBamboo`) | Ore (`IngredCoreOre`) | Flowers | Normal arrows |
| --- | --- | ---: | ---: | ---: | ---: |
| User0 | 199 (`0x4BFCC`) | 46 | 30 | 19 | 0, locked |
| User1 | 85 (`0x4C20C`) | 67 | 59 | 25 | 6 |
| User2 | 89 (`0x4C210`) | 79 | 59 | 28 | 0 |
| User3 | 156 (`0x4C209`) | 86 | 59 | 30 | 8 |
| User4 | 1339 (`0x4C487`) | 152 | 97 | 47 | 6 |
| User5 | 1357 (`0x4C492`) | 152 | 98 | 47 | 6 |
| User6 | 1540 (`0x4C4C8`) | 156 | 98 | 47 | 8 |
| User7 | 1855 (`0x4C6D9`) | 192 | 123 | 56 | 8 |
| User8 / first memory | 8250 (`0x566FD`) | 339 | 500 | 197 | 11 |
| User9 | 7499 (`0x562D1`) | 278 | 493 | 173 | 12 |
| Auto0 | 7605 (`0x5650C`) | 288 | 500 | 179 | 12 |
| second memory, v47 | 0 (`0x4B995`) | 0 | 0 | 0 | 0, locked |

### Difficulty and time correlation

A second structure contains four length-prefixed names, each followed by a float32:
`Easy`, `Medium`, `Hard`, `Nightmare`. The parser checks the complete ordered sequence, rather than
accepting a lone `Easy` string. Within the preceding 80 bytes, a length-prefixed current difficulty is
followed immediately by the length-prefixed string `Ship`. Its `Easy` / `Medium` value agrees with all
slot subtitles (`Einfach` / `Mittel`). Difficulty remains read-only because changing the string's length
would move data and may also require changing other difficulty state.

The v48/v49 float32 at **table start + 231** correlates exactly with subtitle minutes:
User0 `9576.2958984375` seconds → `02:39`, User1 `15127.5546875` → `04:12`, User6
`40487.40234375` → `11:14`, User9 `185140.953125` → `51:25`, Auto0 `186975.4375` → `51:56`,
first memory/User8 `198318.828125` → `55:05`. The automated test compares every non-empty subtitle.
These correlations establish an **Inferred** play timer, not a confirmed field name from game code.
The four per-difficulty counters grow plausibly but their sum is lower than total play time, so they
are described only as raw float counters; their exact accounting semantics are unknown.

## Yōtei research status

All-zero `dis.dat` placeholders remain opaque. Two non-empty samples have these words:

| File | 0x00 | 0x04 | 0x08 | 0x0C |
| --- | ---: | ---: | ---: | ---: |
| memory.dat | `0x150` | 13 | 411619 | 29902195 |
| AutoE1/dis.dat | `0x150` | 16 | 411397 | 30157398 |

Earlier format research found a promising additive integrity relationship and two counted name tables. The complete
header and writable gameplay semantics have not yet been established; Yōtei remains read-only because
no safe gameplay field is located, not because console validation is required before enabling writes.

### Earlier integrity and structure follow-up

Both non-empty files satisfy this **hypothesis**, independently recomputed in research tests:

```text
candidate checksum at 0x0C = sum(bytes[0x10:word_at_0x08]) + word_at_0x08 + 0x150
```

The apparent size at 0x08 equals the last non-zero byte's end offset in both files. The word at 0x04
(13 / 16) is **not added** by this matching formula. Its meaning is unknown; it must not yet be called
a confirmed version. Searches over start offsets 0, 4, 8, 12, 16, 20, both declared and fixed ends,
with/without magic, size and word1 addends compared byte sums, aligned 16/32-bit word sums in both
endiannesses, CRC32 and Adler32. Only the byte sum from 0x10 plus magic and size matched both files.

However, **both range ends match**: every byte after the apparent content end is zero. A whole-file
checksum and a content-only checksum therefore remain indistinguishable. Unknown coverage would require
an explicit **game may reject the edited save** warning on any future writer; it does not by itself
prevent experimental writes under the owner addendum. Its opaque parser's integrity requirements are
not tightened from this hypothesis. A sample with non-zero padding, a proven implementation, or a
controlled game round trip would settle the range.

Each non-empty payload also contains two `uint32 48` + length-prefixed `ArrowNormal` tables. Offsets:
memory `0x62F17` / `0x640F5`; AutoE1 `0x62E39` / `0x64017`. Their 48 names match, including `Mon`
and `Flowers`, but their layouts differ: the first table has 21 bytes after each name (17 zero bytes
then four opaque bytes); the second has 16 zero bytes. All candidate quantity data are zero in both
files. There is no `LegendsInvestment` anchor. This cannot establish which record is the authoritative
inventory or where a currency count belongs. The Tsushima parser is deliberately not reused here.

`YoteiResearchTests` records both header vectors, the ambiguous checksum ranges, both record layouts,
and read-only byte-identical exports for all eight Yōtei files. No Yōtei field meaning or write support
is claimed. The write round deliberately does not turn zero schema-like bytes into guessed counters.

## Research log and sources

Earlier format research, 2026-10-28: reviewed the converter, both SaveWizard code lists, Apollo's patch and its actual
code interpreter before implementation. Compared all 25 Tsushima files / 12 distinct payloads, including
the older memory snapshot; rejected obsolete binary anchors, parsed named inventory, correlated
subtitles, and tested fixed-size edits. Searches for Yōtei save editors, checksums and `memory.dat`
research did not produce a usable primary-source parser. Downloaded references were read as text and
never executed. Only algorithms and patch semantics were independently implemented; no substantial
third-party code was copied or ported.

Write-round recheck, 2026-10-28: reread [KOVRlN technical notes at 519bb466](https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/519bb46620ef20a4424f1ef899d11455f4381044/TECHNICAL.md)
and [Apollo's patch at 273f032b](https://github.com/bucanero/apollo-patches/blob/273f032b4c31c017fed5c1588f209b8faf87ba6c/PS4/CUSA11456.savepatch).
The checksum and paired ammo-write facts remain the basis of the existing writer. The timer is an
independent inference from the supplied PS5 correlations, not a field documented by either tool.
A fresh GitHub-focused Yōtei `memory.dat`/checksum/editor search yielded no usable primary-source
quantity parser; no third-party code or new reference dataset was imported in the write round.

* [KOVRlN technical notes](https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/main/TECHNICAL.md):
  Tsushima header and additive checksum. Independently verified against every supplied Tsushima file.
* [KOVRlN converter](https://github.com/KOVRlN/ghostoftsushima-save-converter): cross-platform format lead;
  upstream version/platform assumptions are not treated as proof for PS5.
* [SaveWizard Tsushima codes](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/main/SaveWizard-Codes/Ghost%20of%20Tsushima%20Codes%20%5BSaveWizard%5D.txt):
  named ammo anchors, relative count writes and obsolete unlock/rank patterns.
* [Alternative Google Docs mirror](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/main/SaveWizard-Codes/Ghost%20of%20Tsushima%20Codes%20Alt%20(Google%20Docs)%20%5BSaveWizard%5D.txt):
  cross-check of older binary-anchor unlock codes; no direct addresses were reused.
* [Apollo CUSA11456 patch](https://github.com/bucanero/apollo-patches/blob/main/PS4/CUSA11456.savepatch):
  independent archive of named ammo refill codes; also labels itself untested.
* [Apollo patch interpreter](https://github.com/bucanero/apollo-lib/blob/main/source/patches.c): authoritative
  interpretation of search, pointer advance, little-endian pointer-relative writes and multi-writes.
* [Apollo code-format reference](https://bucanero.github.io/apollo-lib/savewizard.html): syntax overview;
  actual interpreter source was used where descriptions were ambiguous.

## Sources and attribution

| Dataset | Entries | Pinned source | License |
| --- | ---: | --- | --- |
| `suckerpunch_items` | 48 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Resources?oldid=29221) | CC-BY-SA-3.0 |
| `suckerpunch_equipment` | 122 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Sword_Kits?oldid=32356) | CC-BY-SA-3.0 |
| `suckerpunch_techniques` | 6 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Techniques?oldid=29271) | CC-BY-SA-3.0 |
| `suckerpunch_resource_guide` | 12 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Resources?oldid=29221) | CC-BY-SA-3.0 |
| `suckerpunch_charm_guide` | 79 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Charms?oldid=32085) | CC-BY-SA-3.0 |
| `suckerpunch_armor_guide` | 15 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Armor?oldid=29366) | CC-BY-SA-3.0 |
| `suckerpunch_sword_guide` | 41 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Sword_Kits?oldid=32356) | CC-BY-SA-3.0 |
| `suckerpunch_technique_guide` | 18 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Techniques?oldid=29271) | CC-BY-SA-3.0 |
| `suckerpunch_world_guide` | 17 | [Ghost Franchise Wiki contributors](https://ghostfranchise.fandom.com/wiki/Shinto_Shrines?oldid=29432) | CC-BY-SA-3.0 |
| `suckerpunch_techniques_art` | 18 icons / 34,818 bytes | [Pinned Techniques page](https://ghostfranchise.fandom.com/wiki/Techniques?oldid=29271), individual files in [asset sidecar](SaveToolkit.Plugins.SuckerPunch/ReferencePacks/suckerpunch.tsushima/assets.source.md) | SIE rights reserved; owner decision 2026-10-29 |
| `suckerpunch_charms_art` | 32 images / 64,167 bytes | [Pinned Charms page](https://ghostfranchise.fandom.com/wiki/Charms?oldid=32085), individual files in asset sidecar | SIE rights reserved; owner decision 2026-10-29 |
| `suckerpunch_gear_art` | 56 thumbnails / 61,612 bytes | [Armor](https://ghostfranchise.fandom.com/wiki/Armor?oldid=29366) and [Sword Kits](https://ghostfranchise.fandom.com/wiki/Sword_Kits?oldid=32356), individual files in asset sidecar | SIE rights reserved; owner decision 2026-10-29 |
| `suckerpunch_map_art` | 1 background / 78,140 bytes | [Map of Tsushima](https://ghostfranchise.fandom.com/wiki/File:Map_of_Tsushima.png), retrieved 2026-10-29 | SIE rights reserved; owner decision 2026-10-29 |
| Official armor descriptions (part of `suckerpunch_armor_guide`) | 14 descriptions | [Pinned per-article revisions in asset sidecar](SaveToolkit.Plugins.SuckerPunch/ReferencePacks/suckerpunch.tsushima/assets.source.md#official-descriptions) | CC BY-SA 3.0 wiki adaptations; game text credited to SIE / Sucker Punch |
| External collectible map link (no imported data) | 17 linked markers | [PowerPyx Tsushima guide](https://www.powerpyx.com/ghost-of-tsushima-wiki-strategy-guide/) | Link only; licence not asserted |

The nine text catalogues and four art datasets belong to embedded pack `suckerpunch.tsushima`
v3.0.0. Original wiki retrieval was 2026-10-28; artwork and game-description retrieval was 2026-10-29. Wiki-derived data and the adapted mappings retain
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) share-alike. Each dataset has a
`.source.md` sidecar, manifest attribution, per-record pinned source, and UI attribution; upstream
URLs are also in `Info.References`. The inventory dataset additionally uses
[Arrows, oldid 31796](https://ghostfranchise.fandom.com/wiki/Arrows?oldid=31796). Source changes and
contributor attribution are detailed in the pack's `SOURCES.md`. Names, gameplay facts and acquisition
information were initially extracted/shortened. The asset round adds 157 catalogue records with game
effect/flavor descriptions, omits button instructions and retains the existing factual acquisition
summaries. All 106 icons/thumbnails are at most 64x64 and are deduplicated; the whole-island image is
1152x2048 WebP without crop, rotation or projection. **Total new embedded binary assets: 238,737 bytes.**
There are 158 icon bindings: 18 technique guides, 4 Inferred saved stance mappings, 79 charm guides,
15 armor guides, 41 sword-kit guides and the existing Inferred Storm of Clan Sakai mapping.
Minor charms reuse game category symbols; nine major charms without individual art use their major
category symbol. Appearance thumbnails do not identify a saved armor dye/tier or establish ownership.
Ambiguous numbered charms/armor slots stay raw/Unknown with no guessed icon.

Art is credited to **Sony Interactive Entertainment / Sucker Punch Productions** and hosted by
**Ghost Franchise Wiki contributors**. The F2 licence field is
`Rights reserved by SIE; used with attribution per owner decision 2026-10-29`; SIE abbreviates Sony
Interactive Entertainment to fit the framework's 100-character licence-label cap. This records the owner's
reuse policy, not a new publisher licence. Full holder names, individual source/download URLs, downloaded
content hashes, retrieval date, processing and bindings are in `assets.source.md`, pack attribution,
`Info.References`, field descriptions, table attribution cells and the Sources panel. Wiki text
adaptations retain CC BY-SA 3.0; quoted game text is credited separately in dataset modification notices.
No game-state coordinates are invented: image bounds are pixel bounds only, alignment remains
unverified, all 17 shrine markers are unpositioned, and missing art/provenance retains the list.
No Yōtei imagery is attached to the opaque reader because no gameplay catalogue exists there.

### Mapping policy and evidence

There are **19 Inferred friendly-name mappings** across the 176 raw-key entries:

* Nine resources: `IngredCoreBamboo`, `IngredUpgradeGold`, `IngredUpgradeSteel`, `IngredCoreLinen`,
  `IngredUpgradeLeather`, `IngredUpgradeSilk`, `IngredUpgradeYew`, `Supplies`, `Flowers`. The names
  match explicit resource words; Yew → Yew Wood is a lexical inference. The resource list supplies
  category/vendor facts, not the raw-ID join. No ore → iron, Hide → Predator Hides, or wax-tree →
  Wax Wood assumption is shipped.
* Five arrows: `ArrowNormal`, `ArrowFire`, `ArrowHeavy`, `ArrowExplosive`, `ArrowPiercing`, joining
  the named Apollo refill anchors/sample records to wiki arrow labels. Piercing Arrow is explicitly
  Legends-only in its description; string presence does not establish campaign availability.
* Four stances: `StoneStance`, `WaterStance`, `WindStance`, `MoonStance`, matching the wiki names
  after inserting a space. These strings do not prove stance unlocks, points or active selection.
* `SwordKatanaSakaiStorm` → The Storm of Clan Sakai: the katana/Sakai/Storm words match the wiki's
  default kit. This does not prove a live owned/equipped cosmetic record.

Each mapping includes a record-level `mappingEvidence` and `mappingConfidence`. All remaining
157 raw mappings are **Unknown**. Quantity confidence and edit safety are independent of name
confidence. In particular, a wiki list's order is never used to assign `AccMinorCharmNN` or
`ArmorAccN` names. Rechecking all 12 distinct Tsushima payloads found the same numbered charm and
stance identifiers across versions 47/48/49, consistent with definitions/registries being mixed into
the save. No controlled observation proves their role as owned, equipped or unlocked state.

The six additional guide datasets have **182 named reference entries** (12 resources, 79 charms,
15 armor sets, 41 sword kits, 18 selected techniques, 17 shrines). Their `wiki:` keys are a separate,
explicitly unjoined reference namespace. They are shown as text, not save bindings or replacement
choices. Armor/charm ownership, technique unlocks, and world discovery/completion/rewards remain
Unknown. The historical Archery Master's Attire is marked removed in patch 2.04. The shrine reference
separates Izuhara (7), Toyotama (6), Kamiagata (3), and Iki expansion (1), without claiming a
save-specific denominator or completed count. No Yōtei entries are imported into Tsushima's pack.

## Licence exclusions

| Source / revision | Terms found | Status and alternative | Remaining requirement |
| --- | --- | --- | --- |
| [PowerPyx Tsushima guide](https://www.powerpyx.com/ghost-of-tsushima-wiki-strategy-guide/), historical check 2026-10-28 | No separate media licence recorded | Resolved: publisher-asset refusal superseded by owner decision 2026-10-29. Attributed wiki island art and game icons imported instead; PowerPyx remains an optional source link. | Marker alignment and native saved-state joins still need evidence; these are not licence refusals. |

### Remaining work and owner captures

Phase 1's attributed naming/readability and target-quantity preview are implemented. Phase 2 still
lacks live equipment/character joins. Phase 3's equipment/technique/character edits and Phase 4's
saved world-state reads require actual field/relationship definitions; the World reference is not a completion tracker.
The existing corpus contains no annotated single-action captures. No new gameplay state semantics
were inferred merely from different slot balances. The timer and expanded quantity action above are
enabled independently of owner console evidence. No console-tested feature or Stable maturity is claimed.

For each capture, use one disposable slot and retain **all participating files**; record title, PS5
patch/build, region, mode (campaign/Legends), NG/NG+, DLC and locale. First capture an unchanged
load/save control. Then capture separate before/after pairs for:

1. **Resource labels/caps:** +1 Supplies, pick one flower, sell one Predator Hide, sell one iron,
   one yew wood and one wax wood; fire/pick up one arrow; upgrade one pouch/quiver. Photograph the
   relevant menu balance and upgrade tier. This resolves raw Hide/Ore/WaxTree joins and capacities.
2. **Equipment:** equip one already-owned armor, sword kit and charm separately; switch one enabled
   per-armor loadout; acquire a second copy of one minor charm; buy one dye; upgrade one existing
   armor/weapon tier. Record exact item names, slot/ownership counts and both loadouts before/after.
3. **Character/techniques:** earn one technique point, spend one point on a named technique, buy one
   prerequisite chain, unlock one stance, finish one mythic tale, gain one legend rank, take damage
   then heal. Record displayed points/rank/health/Resolve and technique state at every step.
4. **World:** for one named shrine, fox den, hot spring, haiku, bamboo strike, pillar and quest,
   capture discovery separately from completion and reward. Record region/location, journal state,
   awarded item/health/Resolve/charm-slot change and coordinates if available.
5. **Console validation:** restore an unchanged export first. For a small supported quantity edit,
   load, observe the exact balance, consume/buy one item, resave, reload and export. Record any
   clamping/normalization and compare every changed span; repeat separately for NG+/Iki/Legends
   before claiming support in those modes.
6. **Yōtei:** non-zero Mon/resource/ammo single-action pairs, manual/autosave/memory role controls,
   and natural non-zero trailing bytes or an owner-controlled integrity/load/resave experiment.
   Identify a structurally safe authoritative quantity field; integrity uncertainty may retain a warning.
7. **Play time:** on a disposable v48/v49 slot, change the timer by one minute within its displayed
   bound. Load, check displayed time, resave, reload and export. Compare the timer, per-difficulty
   counters, all participating files and `param.sfo SUBTITLE`; record normalization or rejection.

## Validation and maintenance

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch.Tests/SaveToolkit.Plugins.SuckerPunch.Tests.csproj
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

Sample-dependent tests use `SampleSaves.Load` and skip if archives are absent. A skipped run is not
round validation. With the supplied artifacts, round 1's baseline was **48 passed, 0 skipped**.
Round-2 final checks on 2026-10-28: **85 passed, 0 failed, 0 skipped** with the artifacts variable
exported in the test command; the full API build passed with **0 warnings and 0 errors**.
Write-round checks on 2026-10-28: **105 passed, 0 failed, 0 skipped**, including conformance and
F1 `ByteDiffer` assertions restricting new edits to their exact intended byte ranges. The full API
build passed with **0 warnings and 0 errors**. All non-generated C# files remain below 300 physical lines.
Coverage includes all 25 Tsushima / 8 Yōtei
payload entries, byte-identical no-op exports, exact changed count/checksum spans, mirrored ammo,
locked/malformed/ambiguous records, rollback/undo, non-zero padding, isolated returned memory,
pack validity/attribution, unjoined guide state, F6 purity/scope/stale/forged plans, and Core one-step
undo/redo plus semantic review. Production code depends only on the SDK and performs no I/O.
New C# files stay below 300 physical lines; no hooks, build targets or repository tooling were added.
Completion-round checks on 2026-10-28: **111 passed, 0 failed, 0 skipped** with the artifacts variable
exported in the invocation. The API build passed with **0 warnings and 0 errors**. F1 fixture/decoder,
F8 query-page/filter/sort/session, and F9 graph/map validation tests cover the new presentation paths;
all existing exact-byte and no-op checks remain green.

F12/F13 follow-up checks on 2026-10-28: **126 passed, 0 failed, 0 skipped** with the artifacts
variable exported in the invocation. The API build passed with **0 warnings and 0 errors**.
`savediff list-decoders` lists `suckerpunch.native-v1`; CLI fixture verification passes **1 fixture,
0 failed, 0 skipped** through the registered hook. Native decoding covers all 25 Tsushima / 8 Yōtei
entries, exact timer/ammo/checksum changes, determinism, corruption/foreign rejection and sink/file/
depth bounds. F8 decoding traverses every observed equipment row across seven-row pages and reports
query caps. Guide picker tests cover all declared sort keys in both directions, categories, search,
paging, attribution, rejected writes and unchanged bytes/history/revision. All six guide catalogs
retain explicit Unknown save state. Every non-generated C# file remains below 300 physical lines.

The follow-up rechecked the existing [KOVRlN technical notes](https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/main/TECHNICAL.md)
and [Apollo ammo anchors](https://github.com/bucanero/apollo-patches/blob/main/PS4/CUSA11456.savepatch)
on 2026-10-28. The hook reuses existing validated parsers; no new format hypothesis, reference dataset
or upstream implementation code was added. Existing attribution remains intact.

Asset-round checks on 2026-10-29: **140 passed, 0 failed, 0 skipped** with the artifact path above;
the full API build passes with **0 warnings and 0 errors**. Tests validate all 107 asset hashes and
types, deduplication, every catalogue icon, 64-pixel icon bounds through the shared loader, cached
asset-object reuse across pinned documents, visible attribution, source title isolation, byte-identical
exports, F9 bounds and missing-art/list fallback. New C# files remain below 300 physical lines.

A standalone TEMP stopwatch measured the largest Tsushima content (`sce_sdmemory/memory.dat`,
555,008 file bytes, 380,388 declared bytes), after sample import and before any F2 load. Debug build,
same harness/machine, current-thread allocated bytes; single cold-process timings are approximate:

| Operation | Before assets | After assets |
| --- | ---: | ---: |
| Cold F2 store + Open + BuildView | 94.847 ms / 4,507,432 bytes | 127.997 ms / 14,089,144 bytes |
| F2 store alone | 65.117 ms / 3,158,440 bytes | 87.030 ms / 12,724,480 bytes |
| Cached Open + BuildView, mean of 20 | 5.834 ms / 1,320,304 bytes | 6.905 ms / 1,335,952 bytes |

The shared F2 store eagerly validates/caches packs once at startup; each document pins that store,
and image requests reuse its AssetContent objects. Plugin code performs no image decode or I/O;
guide tables expand lazily. The added cold-load allocations belong primarily to one-time pack
validation, not each open. Browser image fetching/decoding happens when the local image is displayed.
No shared loader changes were made. Image preparation and benchmarking were outside the repository;
all committed changes are inside `plugins/SuckerPunch/`.

## Open questions

* Console-load the edited quantities; only a human can promote maturity to Stable.
* Confirm individual resource meanings and game capacity limits, plus the flag bits and other raw words.
* Identify chapter/quest state and rank/technique records without relying on unmatched PS4 offsets.
* Locate the v47 play timer and determine the accounting of per-difficulty float counters and subtitle normalization.
* Finish the Yōtei header interpretation and validate non-zero gameplay counts before exposing edits.

## SDK requests

None for this round: F2/F4 references and F6 planning are used directly, and F8 serves the per-save
equipment identifier collection. The smaller inventory, technique and world lists remain bounded eager
views; larger imported guides are bounded to one 200-row page. Equipment/progression/world blockers
require sample evidence rather than new SDK substitutes.
The float-duration display request is resolved by F13 (`FloatFormat.Duration`, seconds). F12 supplies
the SDK-only decoder registration; no Testing dependency or plugin-local traversal workaround is needed.
A future metadata-edit contract/policy could clarify
whether changing play time or difficulty should update `param.sfo SUBTITLE`; this plugin leaves it intact.

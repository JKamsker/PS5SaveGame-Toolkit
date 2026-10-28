# Sucker Punch save plugin (`suckerpunch`)

Titles: **PPSA02225** (Ghost of Tsushima, Director's Cut) and **PPSA26344** (Ghost of Yōtei).
Version 0.2.0 is **Experimental**: Tsushima inventory edits pass conformance and exact byte-difference
tests, but have not been tested in-game. Yōtei documents remain **read-only**. No sample bytes,
extracted saves, or third-party implementation code are included in this plugin.

## Supported behavior

* Tsushima: edit quantities in existing unlocked resource and ammunition records. Ammunition edits
  update both count words targeted by the Apollo / Save Wizard refill codes. The checksum is repaired
  immediately after an atomic edit batch, exposed as a derived change, and checked again during export.
* Supplies, flowers, hides and internal `Ingred...` resource identifiers are shown without inventing
  translations such as `IngredCoreOre` = iron. Resource quantity meanings are **Inferred**. Ammo count
  meanings are **Verified** against matching source anchors and both count copies in all samples.
* Difficulty and v48/v49 play time are **Inferred**, read-only fields. Every slot's whole play-time
  minutes and difficulty match its `param.sfo SUBTITLE`; changing these would also require deciding
  how to update console metadata. The v47 timer layout is different, so no timer is published for it.
* Locked records, unmatched ammo copies, unmapped item kinds and existing quantities above 32767 are
  inspection-only. The editable range 0–32767 follows the upstream refill value, **not a proven game
  capacity**. The UI warns that quantities can be clamped. Ownership/upgrade flags are never changed.
* Tabs contain overview statistics, a 48-row inventory table, lazy Blocks sections and paged raw hex.
  Raw record words have unknown semantics and are read-only. Edits invalidate the block snapshots.
* Opening copies the input; exports and hex pages are isolated copies. No-op exports are byte-identical,
  including unknown data, console metadata and padding. Only the selected document's file is returned;
  sibling saves, backup slots and `param.sfo` are preserved.

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
content bytes are not re-encoded. After edits, only count words and the checksum at 0x08 change.
`Serialize()` validates the emitted container, inventory structure and recognised progress block before
returning it. Invalid batches leave every byte unchanged. Undo replays the primary quantity values and
recomputes the checksum, restoring the original bytes.

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

Round 2 found a promising additive integrity relationship and two counted name tables. The complete
header and writable gameplay semantics have not yet been established; Yōtei remains read-only.
See the research follow-up below for the tested relationship and its limits.

## Research log and sources

Round 2, 2026-10-28: reviewed the converter, both SaveWizard code lists, Apollo's patch and its actual
code interpreter before implementation. Compared all 25 Tsushima files / 12 distinct payloads, including
the older memory snapshot; rejected obsolete binary anchors, parsed named inventory, correlated
subtitles, and tested fixed-size edits. Searches for Yōtei save editors, checksums and `memory.dat`
research did not produce a usable primary-source parser. Downloaded references were read as text and
never executed. Only algorithms and patch semantics were independently implemented; no substantial
third-party code was copied or ported.

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

## Validation and maintenance

```powershell
dotnet test --project plugins/SuckerPunch/SaveToolkit.Plugins.SuckerPunch.Tests
pwsh -NoProfile -File plugins/SuckerPunch/scripts/check-source-length.ps1
```

Sample-dependent tests use `SampleSaves.Load`, so missing archives skip those cases. The synthetic checksum
vector runs without samples. Conformance explicitly chooses a supplies edit and selects all distinct
payloads. Additional tests cover exact byte differences across all 25 Tsushima files, ammo copies,
checksum changes, byte-identical undo, atomic rejection, corrupt headers, malformed inventory with repaired
checksums, non-zero padding, and isolated returned memory. No console load test has been performed.

The plugin-local source-length script warns above 300 lines and rejects above 399, satisfying the task's
strict `<400` limit. Its staged mode is wired to `plugins/SuckerPunch/.githooks/pre-commit`; use it with
`git -c core.hooksPath=plugins/SuckerPunch/.githooks commit ...` without changing repository-wide hook
configuration. No CI configuration exists in this worktree; a future pipeline can run the same script.

## Open questions

* Console-load the edited quantities; only a human can promote maturity to Stable.
* Confirm individual resource meanings and game capacity limits, plus the flag bits and other raw words.
* Identify chapter/quest state and rank/technique records without relying on unmatched PS4 offsets.
* Locate the v47 play timer and determine the accounting of per-difficulty float counters.
* Finish the Yōtei header interpretation and validate non-zero gameplay counts before exposing edits.

## SDK requests

None required for inventory edits. Float timers have no Duration display equivalent to `IntegerSpec`,
so the read-only timer is presented in seconds. A future metadata-edit contract/policy could clarify
whether changing play time or difficulty should update `param.sfo SUBTITLE`; this plugin leaves it intact.

# Elden Ring (`eldenring`)

**Experimental**, with bounded held-rune editing for the sampled character/profile versions **150/90**.
No console reload has been tested. Other versions that pass structural and mirror checks open
read-only. Unrecognised layouts and inconsistent profiles fail with `SaveFormatException`.

## Titles and files

| PS5 retail title | Region | Sample coverage |
| --- | --- | --- |
| PPSA03169 | Japan | One primary allocation and its identical console backup |
| PPSA04608 | Asia | Title identity verified; no sample |
| PPSA04609 | Europe | Title identity verified; no sample |
| PPSA04610 | United States | Title identity verified; no sample |

IDs were checked in PROSPEROPatches; JP identity also appears in the supplied format notes.
Regional layout compatibility is **inferred**, not console-tested. Network Test IDs, including
PPSA04616, PPSA04646 and PPSA05603, are deliberately excluded.

The game file is `sce_sdmemory/memory.dat`: the importer represents it as directory
`sdimg_sce_sdmemory` and file `memory.dat`. A nested `sce_sdmemory/memory.dat` path is also accepted.
Console backups open independently. System files and account identifiers are not interpreted.
Export returns only the opened file; backup mirroring is framework-owned.

## Verified layout

Offsets are hexadecimal; integers are little-endian. Supplied size: **28,967,040 bytes**
(`0x1BA0080`). No PC BND4 header exists at offset zero.

| Offset / size | Role |
| --- | --- |
| `0 / 70` | PS header: magic `2C9C01CB`, sentinel `7F7F7F7F` at `08`. Other words are opaque. |
| `70 + index * 280000 / 280000` | Ten character blocks, indices 0–9; no MD5 prefix. |
| `1900070 / 60000` | User data 10: version, opaque account/settings, menu, active flags and summaries. |
| `1960070 / 240010` | User data 11 / regulation allocation, completely preserved. |
| `1BA0080..EOF` | Any appended bytes, preserved without interpretation. |

Character prefix: version at `+0`, map bytes at `+4`, elapsed milliseconds (`u64`) at `+8`, then
item-handle map at `+20`. Walk 5,120 variable-size entries (5,118 for versions <=81): eight-byte
header; nonzero weapon IDs with top nibble 0 add 13 bytes; armour IDs with top nibble 1 add eight.
Other entries add none. This locates PlayerGameData without scanning names or guessing a fixed
stat offset. The legacy item count is sourced but has only synthetic coverage.

Relative to PlayerGameData: eight `u32` attributes at `+34` (Vigor, Mind, Endurance, Strength,
Dexterity, Intelligence, Faith, Arcane), level `+60`, held runes `+64`, lifetime rune total `+68`,
UTF-16LE name `+94` (16 code units plus terminator).

The menu header is at `19001BC` (`user+14C`); its second `u32` gives the bounded data length.
Ten active bytes follow its eight-byte header and payload. In the sample: flags at `19019C4`,
summaries at `19019CE`. Summaries are `24C` bytes each: name `+0`, level `+22`, whole seconds `+26`,
lifetime rune total `+2A`. Inactive summaries may contain stale/invalid text and are not decoded.
Active flags, rather than residual slot bytes, determine character presence.

Active slots must match summaries on name, level, elapsed whole seconds and rune history.
Attributes must be 1–99 and satisfy **level = sum(attributes) − 79**. Held runes must not exceed
999,999,999 or the lifetime total. Saves outside these supported invariants are rejected;
this plugin does not repair corrupt/stale profiles.

## Sample evidence and confidence

Research date: 2026-10-28. One primary and one identical backup exist; there are **no independent
changed snapshots**. Backup consistency is verified, temporal consistency is not.

* **Verified against sources and bytes:** ten boundaries, one active character, slot 0 `Ploxi`,
  character/profile versions 150/90, PlayerGameData at slot-relative `A28F` (absolute `A2FF`),
  level 12, held runes 1,147, lifetime total 4,427, elapsed 4,597,996 ms / 4,597 profile seconds.
* **Verified attributes:** 13 / 13 / 11 / 11 / 16 / 10 / 8 / 9; sum minus 79 equals 12.
  Curated fields use `Confidence.Verified` because cited meanings and sample checks agree.
* **Inferred:** regional compatibility and prefix layouts on other versions that pass validation.
  Unsampled format versions remain read-only.
* **Unknown:** remaining header words, inactive residue, most inventory/flags/regulation data.
  Raw version fields use `Confidence.Unknown`; no game patch number is invented.

## Integrity, crypto and writes

The outer decrypted allocation and curated fields are plaintext. No additional decryption or
compression is applied. Embedded regulation is opaque and untouched; its own crypto/integrity
properties are not established here.

Evidence for **no per-character or profile MD5 on PlayStation**:

1. ClayAmore's `PSSave::write` directly joins the preserved 70-byte header, ten common `SaveSlot`
   outputs, PS `UserData10` and `UserData11`. Its PC wrapper computes/prefixes MD5; PS omits it.
   The PS profile writer also writes directly without a hash.
2. Hapfel1's independent `recalculate_checksums()` explicitly returns immediately for PS saves,
   with a comment confirming no per-slot or user-data-10 checksums.
3. The sample follows those checksum-free boundaries exactly: slot 0 starts with version 150,
   a timer and valid item map; all active character/profile mirrors agree.

This supports experimental writes, not in-game validation. Held runes are the **only writable
field**, limited to `min(existing lifetime rune total, 999999999)`. Rune-history editing is not
implemented; both history copies stay unchanged. The field warns about this restriction.
Level/attributes remain read-only pending class minima and dependent HP/FP/stamina validation;
there are no derived edits. Rune edits patch exactly four bytes. `BoundSaveDocument` provides
atomic batches. `Serialize()` re-parses, validates and returns an isolated copy. Unknown data,
inactive slots, header, regulation and appended bytes remain byte-identical.

## Views and validation

Tabs contain a ten-row profile table, headline facts, lazy curated character sections and lazy
paged hex for each slot/header/profile/regulation/tail. Hex reads return copies, cap requests at
64 KiB and stop at block boundaries. Opening visits at most 51,200 item headers rather than
building a full inventory tree or scanning the allocation for strings.

Validation on 2026-10-28:

* API build: successful, zero warnings/errors.
* Plugin: **64 passed**, including all six SDK conformance checks, known names/stats, every primary
  and backup record, meaningful rune edit → serialize → reopen, exact four-byte write scope,
  session undo/redo, version gates, UTF-16, corrupt inputs, bounded blobs and a <2-second sample
  open/initial-view check.
* Core framework: **43 passed**.
* Empty sample directory: **56 passed, 8 skipped**, zero failures. Sample-dependent tests use
  `SampleSaves`; no sample fixtures or extracted files are included.

Commands: `dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj`,
`dotnet test --project plugins/EldenRing/SaveToolkit.Plugins.EldenRing.Tests`,
`dotnet test --project tests/SaveToolkit.Core.Tests`.

## Sources and licences

Downloaded code was inspected, never executed.

* [ClayAmore/ER-Save-Editor](https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df),
  inspected commit `014107f0ca1cff867b5f7565cb79d6684f9b35df`, dual MIT/Apache-2.0. Format facts and
  traversal algorithms were adapted; the MIT option and notice are retained in
  [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
  [PS header](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/playstation/save_header.rs),
  [PS reader/writer](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/playstation/ps_save.rs),
  [item map and PlayerGameData](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/save_slot.rs),
  [PS user data](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/playstation/user_data_10.rs),
  [summaries](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/user_data_10.rs),
  [PC MD5](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/pc/save_slot.rs),
  [level/dependent stats](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/vm/vm.rs).
* [Hapfel1/er-save-manager](https://github.com/Hapfel1/er-save-manager/blob/5974934537feeace982721f82f0fbe69c20c208d/src/er_save_manager/parser/save.py),
  inspected commit `5974934537feeace982721f82f0fbe69c20c208d`: PS layout/checksum corroboration.
  [Character prefix](https://github.com/Hapfel1/er-save-manager/blob/5974934537feeace982721f82f0fbe69c20c208d/src/er_save_manager/parser/user_data_x.py)
  names timer units/legacy item count;
  [profile fields](https://github.com/Hapfel1/er-save-manager/blob/5974934537feeace982721f82f0fbe69c20c208d/src/er_save_manager/parser/user_data_10.py)
  name seconds/rune history. Its current licence is **restrictive/source-available**; no code
  from it is copied, translated or included. Layout facts are independently implemented and
  verified against bytes and ClayAmore's permissive reference.
* [ClayAmore/EldenRingSaveTemplate](https://github.com/ClayAmore/EldenRingSaveTemplate): additional
  structure reference found during research; no implementation copied from it.
* [Souls Modding Wiki, SL2](https://soulsmodding.com/doku.php?id=format:sl2): its documented
  DSR/BND4 AES wrapper does not describe this Elden Ring PS allocation and was not reused.
* Title metadata: [JP](https://prosperopatches.com/PPSA03169),
  [Asia](https://prosperopatches.com/PPSA04608), [EU](https://prosperopatches.com/PPSA04609),
  [US](https://prosperopatches.com/PPSA04610). Excluded Network Tests:
  [PPSA04616](https://prosperopatches.com/PPSA04616), [PPSA05603](https://prosperopatches.com/PPSA05603).
* Starting evidence: [PPSA03169 reconnaissance](../../docs/formats/PPSA03169.md) and
  [method and limits](../../docs/formats/README.md).

## Open questions

Console reload of edited PS5 output; independent changed snapshots; regional/later-version samples;
class-aware attribute edits and derived state; inventory editing; opaque regulation format.
Strict profile mirror checks may reject recoverable saves with stale summaries; repair is out of scope.

## SDK requests

None blocking this scope. Bound fields, lazy sections and paged blobs are sufficient.

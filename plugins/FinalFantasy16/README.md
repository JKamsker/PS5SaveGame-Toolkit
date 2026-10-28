# Final Fantasy XVI (`ffxvi`)

Supports **PPSA10665** (retail PS5) and **PPSA13803** (PS5 demo), using the folder title ID.
Some supplied retail `param.sfo` files contain unrelated title IDs; those are not used as schema evidence.

**Maturity: Experimental.** Existing main-inventory stacks, including gil, ability points and renown,
can be patched without resizing or rebuilding the file. Format round trips pass; no modified save has
been tested in-game. Level/XP and play time are displayed read-only. System saves and unsupported
block/allocator variants have read-only exploration and byte-identical serialization.

## File roles and platform boundary

| File / directory | Role |
| --- | --- |
| `FAITHDATAAUTO/game-save-data00` through `09` | Autosave ring |
| `FAITHDATAnnnn/game-save-data` | Manual/special gameplay state |
| `FAITHDATASYSTEM/system-save-data` | System/settings |

Roles follow names and directory evidence. The plugin claims only those basenames under the two
supported title IDs and requires the matching gameplay/system header version. It never writes sibling
files, imports an autosave into another slot, or converts demo saves into retail saves.

The supplied **PS5 inner files are plaintext binary**. Nenkai's PC tool handles an outer PNG `faTh`
chunk, XOR and dictionary-zlib wrapper. None of those PC layers is added to PS5 output. This plugin
does not accept PC PNGs, XML-mode saves, or encrypted console save containers.

## Wire layout

All integers are little-endian. The inner schema is a stream of typed, versioned blocks containing
fixed structs and allocator arrays, rather than a field-tagged object graph or an NXD database.

| Offset | Type | Meaning |
| --- | --- | --- |
| `0x00` | u32 | Header size, exactly `0x10` |
| `0x04` | u32 | CRC-32/ISO-HDLC over every byte from `0x10` to EOF |
| `0x08` | u64 | Container version: gameplay `0x785930BD`, system `0x0C044C6D` |
| `0x10` | blocks | Type u32, declared size u32, schema version u64, then payload |

A block's **payload length is declared size minus `0x18`**, while its on-disk header is only `0x10`.
Thus the next block starts at **current offset + declared size - 8**. This size bias is verified by
walking every record to its final four-byte zero type terminator. Sizes are at least `0x18` and
eight-byte aligned. Duplicate types are real (for example type 44 occurs twice) and are retained by
position rather than collapsed into a dictionary. All unknown blocks and any bytes after the zero
terminator are preserved.

The parser validates the header, CRC, block bounds, alignment, progress, terminator and recognised
inventory allocator bounds. Limits are 64 MiB and 4,096 blocks. Unknown block versions/sizes remain
raw; they do not inherit curated offsets from a newer schema. Corrupt supported input throws
`SaveFormatException` with an explanatory message.

CRC uses reflected polynomial `0xEDB88320`, initial value `0xFFFFFFFF`, and final complement. The
stored CRC is little-endian. It matches **72/72 ZIP records**, including repeated paths: 60 retail and
12 demo. Writes patch the original byte array, update CRC, and `Serialize()` recomputes CRC and
re-parses both the container and supported allocator before returning an independent output buffer.
No other integrity algorithm has been substantiated; CRC correctness does not prove in-game acceptance.

## Curated schemas and confidence

Offsets below are relative to a block's payload, not absolute file offsets.

| Block | Version / supported payload sizes | Fields |
| --- | --- | --- |
| 1, PlayerData | `202211161`; 272 bytes in demo/older retail, 1,536 bytes in newer retail | i32 level at `+0`, XP at `+4`, child level at `+8`, child XP at `+12` |
| 6, ItemData | `2021110401`; 36,448 bytes in both titles | Main allocator at `+0`, secondary allocator at `+33,624` |
| 7, UserData | `2021061001`; 104 bytes in both titles | i32 play time at `+0`, seconds |

Retail accepts both observed player layouts. Demo only applies the 272-byte player schema; it can
still explore an unfamiliar block without applying the extended retail schema. Layout selection is
per block, because retail samples themselves include demo-era saves. The newer retail player suffix,
extra endgame blocks and changed telemetry/UI arrays are preserved without interpretation.

System settings also differ: the demo has 8 blocks / 276 bytes; retail has 9 blocks / 1,076 bytes,
including a keybind block and an enlarged pad-config block. Settings are structurally explored only.

The main inventory has 1,200 fixed records of 16 bytes: i32 item ID, i32 stack, and two preserved i32
words (the upstream template labels one as a new-item flag). Records are followed by 1,200 i32
allocator indices, u64 initialized count, i32 next index, four preserved bytes, 1,200 u64 keys,
i32 size, and four preserved bytes. The secondary allocator has the same shape with capacity 100.

Editing is enabled only for the observed dense arrangement (`initialized == size == next index`,
keys packing the slot twice as low/high u32, positive item IDs). Other allocator arrangements remain
read-only. Bounds outside capacity are rejected. Free-list indices, keys, IDs, flags and padding are
never rewritten; zero stacks with an allocated ID remain visible. Item insertion/removal is unsupported.
Secondary-inventory semantics and its relationship to equipment/shortcuts are unconfirmed, so it is
never edited or presented as another ordinary inventory.

**Verified** labels mean the names/layout match Nenkai's template and sample bytes, not that an edit
has been tested on a console. Resource names use the published item IDs: 1 = Gil, 2 = Renown,
3 = Ability Points, stored as a small embedded reference resource. Other rows retain numeric IDs:
their stack layout is verified, but their item names are **unknown**. Level/XP stay read-only because
growth curves, stat dependencies and progression triggers are not mapped.

Play time's field name comes from the template. Seconds are corroborated independently: across all
11 demo gameplay records, the u32 saved-time value at UserData `+88` minus play time is the same
`1700236450`; every inter-snapshot difference matches elapsed timestamp seconds. The meaning and
epoch of the other time fields are not exposed. Gameplay stack limits are unknown: edits are limited
to non-negative i32 values and carry a warning about game-specific caps and progression effects.

## Explorer and preservation

The view uses tabs for overview, inventory, structure and hex. The block directory preserves every
block, and payloads expand lazily through at most 64 children per expansion and 128 raw words per
leaf table. Raw words are explicitly unsigned little-endian interpretations with **unknown meaning**;
they are not advertised as recovered primitive field types. Hex reads are bounded to 64 KiB per
request. Large sparse blocks do not produce eager field trees. System/empty saves disable editing.

## Sample evidence and tests

Values below refer to the **first ZIP occurrence** of each path; archive occurrence order is not
assumed to be chronological.

| Title / path | Level | XP | Gil | AP | Play time (s) |
| --- | ---: | ---: | ---: | ---: | ---: |
| PPSA10665 / `sdimg_FAITHDATAAUTO/game-save-data00` | 42 | 13,891 | 104,594 | 5,942 | 597,253 |
| PPSA10665 / `sdimg_FAITHDATA0001/game-save-data` | 10 | 0 | 16 | 222 | 5,735 |
| PPSA10665 / `sdimg_FAITHDATA0004/game-save-data` | 19 | 189 | 14,397 | 2,364 | 194,436 |
| PPSA13803 / `sdimg_FAITHDATAAUTO/game-save-data00` | 10 | 0 | 16 | 234 | 88,270 |

All records pass CRC validation and byte-identical no-op serialization. All **60 populated gameplay
records** (49 retail, 11 demo) pass gil edits, CRC recomputation, byte preservation outside the four
stack bytes and four CRC bytes, and reopen/serialize checks. The second retail autosave-ring
occurrence contains empty initialized state; it stays read-only rather than receiving invented items.
The conformance suite covers all distinct sample contents, not only the first six files.

Additional tests cover standard CRC vectors, bad checksums and malformed block sizes, valid-CRC
structural corruption, invalid allocator bounds, unknown versions, duplicate blocks, trailing bytes,
variant gating, atomic rejection, isolation, undo including CRC, hex bounds, and exhaustive bounded
exploration of a synthetic 150,000-word block. Sample-dependent tests skip when archives are absent;
synthetic format/explorer tests still run. No sample bytes or extracted data are checked in.

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
dotnet test --project plugins/FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
```

## Open questions

- In-game validation of edited PS5 saves, especially resource caps and progression effects.
- Full schemas for newer player palettes, settings, abilities, quests, equipment and DLC records.
- Secondary inventory semantics and non-dense allocator states.
- Item/equipment names beyond the three substantiated resources; no names are guessed.
- Any additional semantic integrity requirements or external metadata maintained by the game.

## Sources

Research checked 2026-10-28. Downloaded material was inspected as text and never executed.

- [Nenkai's save-file research](https://nenkai.github.io/ffxvi-modding/resources/other/save_files/):
  primary references to binary templates and the optional PC XML serialization path.
- [FaithSaveFile_Sub.bt, pinned revision](https://github.com/Nenkai/010GameTemplates/blob/f31430e32f099d66cc22d609a7926988e041c409/Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt):
  typed block IDs, field names, fixed-array layouts and payload-size convention. MIT, copyright Nenkai.
  Only the substantiated subset is implemented; offsets and framing were independently checked on PS5 bytes.
- [Published item IDs](https://nenkai.github.io/ffxvi-modding/resources/ids/item_ids/):
  item IDs 1, 2 and 3 and their resource names. The entire table was not imported.
- [FF16Tools FaithSaveFile.cs](https://github.com/Nenkai/FF16Tools/blob/master/FF16Tools.Files/Save/FaithSaveFile.cs):
  CRC coverage and PC outer PNG/XOR/dictionary-zlib handling; those PC wrappers are intentionally absent here.
- [bbfox's original PC cheat-table thread](https://opencheattables.com/viewtopic.php?t=1129):
  corroborates XP, gil/AP and stack editing as research targets. Runtime pointer offsets were not used
  as PS5 save offsets, and no cheat-table scripts were run.
- Local reconnaissance: [retail](../../docs/formats/PPSA10665.md), [demo](../../docs/formats/PPSA13803.md),
  and [method/limits](../../docs/formats/README.md). Archive evidence was rechecked by this plugin's tests.

The parser/patching code is independently written from wire-layout facts; the relevant upstream MIT
notice is retained in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). No runtime NuGet dependency was added.

## SDK requests

None. The existing view, binding, blob and serialization contracts cover this implementation.

# The Witcher 3: Wild Hunt – Complete Edition (`witcher3`)

Title: **PPSA04018**. Maturity: **ReadOnly**. Validates SNFH/LZ4 and SAV3 directories and provides
a lazy variable explorer. Player progression layouts and write integrity remain unverified for PS5;
no edits or recompression are offered.

## File roles

- `CheckPt.*.sav`, `Manual.*.sav`: gameplay; claimed with matching title, `.sav` extension and SNFHFZLC
  signature. Other correctly framed `.sav` slot names are accepted too.
- Same-stem `.req`: required content list; not claimed or modified.
- `user.settings`: INI-style configuration; not claimed or modified.
- System files, JSON companions and console backup siblings are never rewritten as a side effect.

All processing uses SDK-supplied bytes without filesystem/network I/O.

## Verified container

Words are little-endian. `SNFH` is at 0, `FZLC` at 4, int32 chunk count at 8, int32 header/data
size at 12. Descriptor triples begin at `0x10`: stored length, expanded length, optional absolute
end offset (zero allowed). Raw LZ4 blocks follow sequentially at the declared data start, without
4ZLX/LZ4 frame wrappers. Nonzero end offsets must match block ends. Every decode must produce the
exact declared expanded length. Reserved header bytes and any trailer remain in the original output.

All samples use one block and a **3,084-byte (`0xC0C`) header**. Expanded data starts with `SAV3`.
The supplied decrypted files need no further encryption.

Limits: 128 MiB input, 64 MiB expanded data, 4096 chunks, 200,000 indexed/explorer entries,
128 nesting levels and a uint16-sized name dictionary. Invalid bounds/lengths raise `SaveFormatException`.

## SAV3 layout and explorer

| Region | Layout |
| --- | --- |
| Header | `SAV3`, three int32 version words; samples 64 / 23 / 163, exact meanings unconfirmed |
| Final 6 expanded bytes | int32 virtual variable-table offset, `SE` |
| Variable table | int32 count, then virtual int32 offset + int32 span length per entry |
| 10 bytes before table | virtual NM offset, virtual RB offset, `SC` |
| NM | `NM`, `MANU`, int32 name count, opaque int32, byte-length ASCII names, opaque int32, `ENOD` |
| RB | `RB`, int32 count, opaque uint16 + virtual int32 offset per entry |

**Virtual offsets include the SNFH header** although they address expanded data: subtract the declared
header size to index the SAV3 byte array. Ignoring this fails on every sample. Names are one-based.
Indexed spans are sorted and nested by containment; crossings/duplicate offsets are rejected.
The name table is itself indexed.

`BS` holds a uint16 name index; its full containing span comes from the variable table. `VL` / `OP`
hold uint16 name/type indices followed by typed bytes. `SS` holds int32 archive length and a separate,
non-indexed stream. Within these streams the reader handles:

- `SXAP`: magic plus three version words (16 bytes).
- `BLCK`: magic, uint16 name index, int32 child-data length, nested token sequence.
- `PORP` / `AVAL`: magic, uint16 name/type indices, int32 value length, value bytes.

These boundaries are checked structurally, not found by searching arbitrary data for magic.
Unsupported tokens such as ROTS/SBDF, complex handles/arrays and custom object encodings remain bounded
opaque spans. Decoding stops at an unsupported remainder of its parent instead of inventing boundaries.
No SAVY signature was found in the samples; it is not imposed as an expected inner signature.

Upstream calls the RB uint16 a size, but sample values resemble root name indices. This plugin keeps
that word opaque and validates only its referenced offsets; it does not use it as a byte length.

## Fields, confidence and views

Overview includes structural counts, raw version words, and early stored `path`, `description` and
`playthroughId` values when encodings match. Version-word meanings are **Unknown**. The tree shows
stored names/types and read-only scalars for booleans, signed/unsigned 8/16/32/64-bit integers,
finite float/double values, CName, GUID and directly validated short ASCII String/StringAnsi encodings.
Unsupported encodings or lengths remain byte views.

Typed fields are **Inferred** for gameplay meaning: stored names/types do not prove their owner,
effects, units or safe ranges. Seeing `Crowns`, `level` or `W3LevelManager` in the dictionary does not
establish player money/level offsets. Player level, XP, crowns/orens and skill points are not claimed
as decoded player fields.

Tabs provide overview, variable tree, MANU dictionary and container details. Lists page 128 entries;
nodes have lazy children and paged original bytes. Original file, expanded payload and every token
remain inspectable. Chunk tables are bounded to 4096 rows. Value IDs use stable payload offsets.

## Integrity and writing boundary

The cited reader and samples establish framing/length dependencies, not all next-generation internal
integrity or semantic dependencies. No save checksum algorithm is claimed; fields such as `magic_number`
are not treated as proven checksums. Length checks cannot detect every payload mutation.

`CanEdit` is false, all field bindings are read-only, and serialization returns a copy of the complete
original file. A future writer must establish the player schema, all affected sizes/offsets/integrity,
payload preservation and edited round trips before Experimental maturity. In-game testing is required
for Stable. This implementation deliberately does not recompress partially understood saves.

## Samples and tests

Four distinct primary saves plus their four byte-identical backup twins are tested:

| Save | Expanded bytes | Indexed entries | Names |
| --- | ---: | ---: | ---: |
| `CheckPt.53db8.7e73c800.313c5b3.sav` | 653,545 | 16,082 | 1,980 |
| `CheckPt.8559a.7e73c800.317a3e7.sav` | 702,856 | 17,399 | 2,053 |
| `CheckPt.8559a.7e73c800.31c45eb.sav` | 703,834 | 17,375 | 2,079 |
| `Manual.8559a.7e73c800.31c94f5.sav` | 724,174 | 18,445 | 2,106 |

All contain early path `game\witcher3.redgame`. Approximately 19,505–21,894 indexed/embedded explorer
nodes are built per distinct save; views remain lazy. File order does not prove chronology or broad
version compatibility.

Tests cover all gameplay entries/backups; conformance/no-op identity; known dimensions/path; every lazy
section and value with bounded responses; rejected edits; companion exclusion; synthetic two-chunk
decoding with both end-offset conventions; virtual offsets; nested BLCK/AVAL/PORP values; padding/
trailer preservation; bad LZ4; truncations; invalid counts/lengths/name indices; crossing spans;
corrupted directories/footer markers. Synthetic fixtures contain no sample data. Missing sample
archives skip via `SampleSaves`; synthetic tests remain runnable.

```powershell
dotnet test --project plugins/Witcher3/SaveToolkit.Plugins.Witcher3.Tests
```

Validation: **25 passed**, no sample skips. API build: zero warnings/errors. Core: **42 passed**.
Observed open + initial view: approximately 11–30 ms on the development machine; not a portable benchmark.

## Open problems

Decode actual PS5 progression/inventory objects, especially custom handle/array bodies; understand
ROTS/SBDF without signature-scanning guesses; establish other string encodings, RB semantics and
integrity dependencies. A preserving writer and successful in-game test remain future work.

## Sources and licences

Research/sample verification: 2026-10-28. Upstream source was read as text in scratch storage;
no downloaded editor/script was run. This is independently written bounded format code.

- [W3SavegameEditor at cff9051](https://github.com/Atvaark/W3SavegameEditor/tree/cff905189d565606aed9058b2804eb4c5ee9f540/W3SavegameEditor.Core):
  `ChunkedLz4/ChunkedLz4FileHeader.cs`, `ChunkedLz4FileTable.cs`, `Lz4Chunk.cs` for framing;
  `Savegame/SavegameFile.cs` for footer/name/index tables and virtual positioning;
  `Savegame/VariableParsers/` for BS/VL/OP/SS/SXAP/BLCK/PORP/AVAL and primitive encoding facts.
  MIT; notice in [W3SavegameEditor-LICENSE.txt](W3SavegameEditor-LICENSE.txt). Its older PC coverage
  does not establish PS5 writing compatibility; no upstream object parser is run or bundled.
- [LZ4 block specification](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md): raw block
  semantics; no frame or dictionary is added.
- [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4): managed raw codec,
  version 1.3.8 from the existing central package list, MIT.
- [Repository reconnaissance](../../docs/formats/PPSA04018.md): initial PS5 dimensions,
  independently rechecked against every supplied gameplay record.

## SDK requests

None. Existing lazy sections, read-only fields, tables and paged blobs are sufficient.

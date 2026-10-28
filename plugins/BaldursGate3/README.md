# Baldur's Gate 3 (`bg3`)

Title: **PPSA18463**. Maturity: **Experimental**, version 0.2.0.

Opens decrypted BG3 save packages and profile resources, lists package entries with
stored/expanded sizes and compression, and exposes every resource node and attribute
through a lazy explorer. Includes embedded WebP thumbnails, decoded scalar values,
vectors/matrices, translation handles, and paged raw bytes for opaque or large values.
LSX exploration includes XML wrapper elements, metadata attributes and text.
Round 2 adds validated LSPK v18 checksums, preservation-first LSF/LSPK writers,
primitive editing and a sourced Party summary. No in-game write test has occurred.

## Supported scope and preservation

- LSPK v18 reading/writing, independent entries in a single archive part, with a
  mandatory matching header digest. Legacy v15/v16 remain read-only; their CRCs
  and digests are not claimed to be verified.
- LSOF v5/v6/v7 reading/writing, compact metadata formats 0/2 and extended format 1,
  including keys. Edits target existing primitive attributes, not tree structure.
- Uncompressed, zlib, LZ4 blocks/frames, and zstd resource data; package payloads use
  the per-entry codec. The file list is always an LZ4 block.
- LSX XML, including UTF-8/BOM and UTF-16 little/big endian with BOM.
  DTDs and external entities are prohibited.
- No-op `Serialize()` returns a fresh copy of the **original file**, preserving
  unrecognized metadata, padding, trailing data, compression and XML spelling.
  This is tested for all twelve claimed primary/backup files, and independently
  through the section rebuilder for all fourteen embedded/standalone LSF copies.
- Integers, finite floats/doubles, booleans and ordinary UTF-8 string types can be
  edited. GUIDs, vectors/matrices, translation structures, ScratchBuffers, LSX,
  JSON and story data stay read-only. The UUID-only analytics resource has no
  editable primitive. No file/network I/O or external executable is used.

Solid packages, multipart packages, deletion entries and older versions are rejected
with `SaveFormatException`. Solid LZ4 uses offsets into a shared frame rather than
independent payloads; this path has no supplied fixture and is intentionally unsupported.
Files from other title IDs are not claimed. Standalone thumbnails use the framework's
normal raw viewer; the thumbnail inside an opened package is displayed as an image.

## Sample evidence and file roles

The supplied archive has one snapshot (generation 0) and byte-identical console backup
twins, rather than several appended snapshots. No samples or extracted data are included
in this plugin. Sample tests skip via `SampleSaves.Load` when the archive is absent.

`Save0001/AutoSave_0.lsv` is 8,073,350 bytes, LSPK v18, flags 0, one part, seven
zstd-compressed entries in this file-list order:

| Entry | Stored bytes | Expanded bytes | Resource nodes / attributes / roots |
| --- | ---: | ---: | --- |
| `meta.lsf` | 1,283 | 1,516 | 15 / 39 / 1 |
| `StorySave.bin` | 6,754,260 | 33,628,992 | Opaque Osiris story data |
| `Globals.lsf` | 933,769 | 2,410,069 | 149,174 / 189,357 / 30 |
| `SaveInfo.json` | 523 | 1,559 | Raw JSON payload |
| `AutoSave_0.WebP` | 75,678 | 75,666 | RIFF/WebP thumbnail |
| `LevelCache/TUT_Avernus_C.lsf` | 287,642 | 467,459 | 13,981 / 25,867 / 27 |
| `LevelCache/SYS_CC_I.lsf` | 19,593 | 25,350 | 330 / 780 / 27 |

Every embedded resource is LSOF v7 with compact metadata format 0.
The profile files are LSOF v7 with extended metadata format 1:

| File | Nodes / attributes | Role |
| --- | --- | --- |
| `analyticsUserData.lsf` | 1 / 1 | UUID-only profile analytics resource; read-only |
| `profile8.lsf` | 17 / 19 | Profile resource; gameplay meaning not inferred |
| `config.lsf` | 4 / 10 | Configuration resource with a nonempty key table |
| `graphicSettings.lsx` | 14 / 29 XML elements/attributes | Graphics settings XML |
| `modsettings.lsx` | 14 / 26 XML elements/attributes | Module settings XML |

Verified sample values in `meta.lsf`: `Level = TUT_Avernus_C`, raw `Difficulty = 2`,
raw `OriginalPlatform = 3`. Numeric enum meanings are deliberately not assigned.
`Globals.lsf` has a `NewAge` root with a `ScratchBuffer` named `NewAge` of 2,618,936
bytes beginning `LSMF`. It is available in the raw explorer without being decoded.

The Party tab reads `SaveInfo.json / Active Party / Characters`: the supplied sample
contains three rows at level 1, with current/total XP `(30,30)`, `(30,30)`, `(0,0)`.
Origin strings are shown as origins rather than invented localised names. The
`meta.lsf` leader display-name primitive is also exposed (metadata only; it does
not rename a live character). Summary meanings use `Confidence.Inferred`, backed
by the cited community parser/editor documentation and exact sample keys. Level
and XP summaries are read-only. Gold quantities and localised companion names are
unavailable; no validated mapping for them is implemented. The metadata has two
`CharacterMetaData` nodes, so those nodes are not assumed to map one-to-one to the
three JSON rows. Editing primitive metadata does not synchronise live ECS state.

## Binary layout

All numeric fields below are little-endian. These layouts follow LSLib; see sources.

### LSPK

| Absolute header offset | v18 field |
| --- | --- |
| 0 | `LSPK` magic (4 bytes) |
| 4 | Version (u32) |
| 8 | File-list offset (u64) |
| 16 | File-list stored size (u32), including 8-byte preamble |
| 20 / 21 | Package flags / priority (u8 each) |
| 22 | MD5 over expanded payloads in physical order, each digest byte +1 (16 bytes) |
| 38 | Archive part count (u16); payloads begin at 40 or later |

The file list starts with an i32 entry count and u32 compressed size, followed by an
LZ4 block. Each v18 row is 272 bytes: 256-byte NUL-terminated UTF-8 name, 48-bit payload
offset, u8 archive part, u8 compression flags, u32 stored size, u32 expanded size.
The compression method is the low nibble: 0 none, 1 zlib, 2 LZ4, 3 zstd. The high
nibble denotes compression level; it does not change the decoder. For method 0 the
stored size is the actual size even when the expanded-size field is zero.

v16 uses the same header with 296-byte rows (64-bit offset/sizes and 32-bit
part/flags/CRC/reserved fields). v15 uses those rows with a 38-byte header and
implicit single part. Legacy row CRCs are not verified. Ranges, overlaps, counts,
unique names, compression results and resource references are validated.

### LSOF / LSF

`LSOF`, u32 version and u64 packed engine version occupy the first 16 bytes. v6/v7
metadata consists of five `(u32 expanded, u32 stored)` pairs: strings, keys, nodes,
attributes, values; then a compression byte, three reserved bytes, and a u32 metadata
format. Payload starts at byte 64. v5 omits the keys pair and starts payload at 56.
The **physical** section order is strings, nodes, attributes, values, keys. A zero
stored size means raw expanded bytes, not a missing section when expanded size is nonzero.

The string section contains u32 bucket count, then each bucket's u16 string count
and `(u16 byte length, UTF-8 bytes)` strings. Name references select bucket in the
high 16 bits and chain offset in the low 16 bits.

Compact nodes are `(u32 name, i32 firstAttribute, i32 parent)`; compact attributes are
`(u32 name, u32 lengthAndType, i32 ownerTag)`. The low six bits select the attribute
type; upper bits give its value length. Owner tags group attribute chains and are
**not** assumed to equal node indices. Values are consecutive in compact attribute order.
Extended nodes are `(name, parent, nextSibling, firstAttribute)` and attributes are
`(name, lengthAndType, nextAttribute, valueOffset)`, each 16 bytes. Key rows are
`(u32 nodeIndex, u32 nameReference)`. `-1` ends reference chains.

All 34 LSLib attribute type IDs are recognized. Numeric primitives keep signedness;
non-finite floats display as text with a warning. Strings are validated NUL-terminated
UTF-8 (including LSLib's WString variants). GUID display uses LSLib's default swapped
last four 16-bit groups. Translated strings expose their handle/version; localisation
is not resolved. Scratch buffers and formatted translation payloads remain raw bytes.
Matrices display their stored column-major scalar sequence.

## Integrity and writing

No additional encryption was found or applied: the provided input is already decrypted.
Codec framing/length checks are enforced, including zlib/zstd integrity checks provided
by their decoders when present. They do **not** authenticate the package or its raw data.

The v18 digest is reproduced and verified: MD5 of concatenated expanded entries in
increasing payload-offset order, then each digest byte +1 modulo 256. The sample
matches `6D98C6D4C2FF81E7C7F43AAA881D66DA`. File-list order and ordinal filename
order fail. Case-insensitive name order matches the same physical sequence on this
single independent sample, so those hypotheses cannot yet be distinguished. Both
orders remain satisfied by the writer's supported edits, which preserve names and
physical order. Zero/stale/nonmatching v18 digests are rejected; the plugin does not
assume the game ignores the hash. [INTEGRITY.md](INTEGRITY.md) records all 844
experiments, exact physical order, limitations and upstream issue/commit research.

The LSF writer retains the original header, names, node order, owner tags, links,
keys, unused value bytes, trailing bytes and unchanged compressed sections. It
patches attribute lengths and rebuilds the value section, updating all extended
value offsets after string growth/shrinkage. Overlapping extended attribute value
ranges are readable but edits are rejected rather than rewriting aliased data.
The rebuilder retains raw sections under compressed headers and block/frame LZ4
encodings. No-op and undo to original values return exact original bytes, including
noncanonical true bytes and additional trailing string NULs.

The LSPK writer preserves file-list order and physical payload order independently.
It retains filename bytes/padding, flags/priority, unknown gap/trailing bytes and
every untouched entry's **original compressed bytes**. Only modified entries are
recompressed. Offsets, sizes and digest are recomputed, and the LZ4 file list is
rebuilt. Added alignment padding uses LSLib's 64-byte header-relative alignment and
`0xAD` fill. No-op package writing is byte-identical; edited output is not expected
to match the old archive byte layout, but reopens to exactly the intended expanded
entries. All package entries and resource tables are re-parsed inside `Serialize()`.
The writers additionally compare every reopened attribute/payload to its expected
bytes before returning output.

Original codec and level flags are retained. LSLib's fast/default/max mappings are
used for recompression: zlib Fastest/Optimal/SmallestSize, LZ4 fast/10/12, zstd
3/9/22. Unknown high-nibble levels use the reference default while retaining the
flag byte. Unchanged sections/entries are never passed through a compressor.

The digest does not cover filenames, container metadata or padding and is not
authentication. Standalone LSFs have structural checks but no discovered content
checksum. NewAge/LSMF and Osiris internal formats/integrity are not decoded or
modified. Community load failures after ECS changes are not evidence about the
primitive-only writer; PS5 compatibility still needs human testing. Maturity stays
Experimental until that test, even though the byte/checksum/conformance tests pass.

## Confidence and remaining limits

Verified: magic/version/layout matches, sample counts, actual primitive values, codec
output sizes, XML structure, the LSMF signature, byte-identical no-op preservation,
the supplied package digest and preservation of every unedited compressed payload.
Raw fields carry `Confidence.Unknown` because their **gameplay meaning** is unvalidated.
Curated Party metadata uses `Confidence.Inferred`. Live character names, level/XP,
gold, abilities and inventory relationships inside LSMF need a separate validated
decoder. No live stat or gold editor, guessed item names, tree insertion/deletion,
multipart/solid writer, or console compatibility is claimed.

Other limits: 256 MiB input, 256 MiB total expanded package payloads, 256 MiB expanded
resource sections per document, one million resource nodes/attributes, XML depth 256,
formatted translation depth 32. Editable text is limited to one million UTF-16
units and rejects embedded NULs/invalid UTF-8. The optional Party JSON reader caps
input at 1 MiB, depth at 32 and rows at 128; unsupported JSON remains available raw.
Explorer collections page in groups of 128 with
hierarchical page links for very large lists. Binary attributes above 4 KiB use a raw
view; XML values above 4,096 characters use a bounded preview. Views are lazy; models and compressed resource tables validate
during opening. Opening and initial view construction took approximately 0.4 s
on the development machine; this is an observation, not a timing assertion in CI.

## Validation

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
dotnet test --project plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
```

Tests cover the shared conformance suite, every sample resource and no-op copy,
backup equality, 844 digest hypotheses, known MD5 vectors, synthetic version/codec/
layout fixtures, string growth/shrinkage, unordered extended offsets and gaps,
preservation of untouched compressed sections and entries, atomic rollback/undo,
UTF-8/UUID/scalar vectors, malformed/truncated data, entity rejection, meaningful
leader-name edits in conformance, thumbnails/blob ranges, and 20,000-entry/child
paging. Synthetic tests run without samples.

Validation on .NET SDK 10.0.400: API build passed with zero warnings/errors; all 120
plugin tests and all 43 framework tests passed. With an empty
`SAVETOOLKIT_ARTIFACTS` directory, 103 plugin tests pass and 17 sample-dependent
tests skip. The source-size test
runs with the plugin tests; all source files are below 300 physical lines. No plugin
hooks, scripts, build targets or repository tooling are installed.

## SDK requests

None. Existing bindings support atomic edits, rollback, lazy raw fields and undo.
The current shared conformance suite also checks no-op output for read-only files.

## Sources and licensing

Primary reference: [Norbyte/LSLib](https://github.com/Norbyte/lslib), MIT, inspected at
revision `6f5f6987d10d8485876b8368431c948e4dea4f83`. Adapted format algorithms and type
IDs retain the upstream licence in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
Downloaded source was inspected as text; no upstream executable was run.

- [PackageFormat.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageFormat.cs): headers, row layouts, flags.
- [PackageReader.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageReader.cs): file lists, version dispatch, solid/multipart behavior.
- [PackageCommon.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageCommon.cs): payload selection and deletion markers.
- [PackageWriter.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageWriter.cs) and [PackageVersion.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Enums/PackageVersion.cs): MD5+1 algorithm, physical/build-input order and header-relative 64-byte padding. See [integrity research](INTEGRITY.md) for history and issue sources.
- [LSFCommon.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSF/LSFCommon.cs) and [LSFReader.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSF/LSFReader.cs): section order, layouts, attribute chains, keys, strings/translations.
- [LSFVersion.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Enums/LSFVersion.cs): BG3 extended header, node-key and patch version numbering.
- [LSFWriter.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSF/LSFWriter.cs): section compression/order and attribute offsets. Our writer patches original tables instead of rebuilding names and trees.
- [Compression.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Compression.cs) and [compression flags](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Enums/Compression.cs): codec dispatch and framing.
- [NodeAttribute.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/NodeAttribute.cs) and [BinUtils.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/BinUtils.cs): type IDs, scalar/vector layout, GUID display.
- [LSXReader.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSX/LSXReader.cs): XML resource structure; our explorer retains all elements generically.
- [clemarescx/bg3d](https://github.com/clemarescx/bg3d): inspector precedent and NewAge opaque-blob context.
- [Hawk-on field mapping](https://github.com/Hawk-on/bg3-save-editor/blob/master/FIELD_MAPPING.md): SaveInfo party/level/XP context and distinction between template amounts and runtime gold. No runtime-gold control adopted.
- [Wahkah/BG3SE research](https://github.com/Wahkah/BG3SE/blob/main/README.md): independent write-integrity concerns and LSMF context; no decoder code copied and no gameplay mappings adopted.
- [Serofix-code/bg3-save-editor](https://github.com/Serofix-code/bg3-save-editor): reported repack warnings and metadata versus live-state distinction.
- [BG3 format research](https://github.com/DanFinchUK/bg3-savefile-parser/blob/master/FORMAT.md): leader-name metadata, JSON party/level/XP summary and distinction from live LSMF state. Curated summaries are Inferred, not verified live stat controls.
- [K4os LZ4 1.3.8](https://www.nuget.org/packages/K4os.Compression.LZ4.Streams/1.3.8) and [ZstdSharp.Port 0.8.8](https://www.nuget.org/packages/ZstdSharp.Port/0.8.8): managed MIT codec dependencies; see notices.

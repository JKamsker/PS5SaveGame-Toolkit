# Baldur's Gate 3 (`bg3`)

Title: **PPSA18463**. Maturity: **ReadOnly**, version 0.1.0.

Opens decrypted BG3 save packages and profile resources, lists package entries with
stored/expanded sizes and compression, and exposes every resource node and attribute
through a lazy explorer. Includes embedded WebP thumbnails, decoded scalar values,
vectors/matrices, translation handles, and paged raw bytes for opaque or large values.
LSX exploration includes XML wrapper elements, metadata attributes and text.

## Supported scope and preservation

- LSPK v15/v16/v18, independent entries in a single archive part.
- LSOF v5/v6/v7, compact metadata formats 0/2 and extended format 1, including keys.
- Uncompressed, zlib, LZ4 blocks/frames, and zstd resource data; package payloads use
  the per-entry codec. The file list is always an LZ4 block.
- LSX XML, including UTF-8/BOM and UTF-16 little/big endian with BOM.
  DTDs and external entities are prohibited.
- No-op `Serialize()` returns a fresh copy of the **original file**, preserving
  unrecognized metadata, padding, trailing data, compression and XML spelling.
  This is explicitly tested for all twelve claimed primary/backup files even though
  the shared conformance suite skips serialization checks for ReadOnly plugins.
- All fields reject edits. No writer, recompression level, or console compatibility
  is claimed. No file system/network access or external executable is used by the plugin.

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
| `analyticsUserData.lsf` | 1 / 1 | Profile analytics resource; inner string is not interpreted |
| `profile8.lsf` | 17 / 19 | Profile resource; gameplay meaning not inferred |
| `config.lsf` | 4 / 10 | Configuration resource with a nonempty key table |
| `graphicSettings.lsx` | 14 / 29 XML elements/attributes | Graphics settings XML |
| `modsettings.lsx` | 14 / 26 XML elements/attributes | Module settings XML |

Verified sample values in `meta.lsf`: `Level = TUT_Avernus_C`, raw `Difficulty = 2`,
raw `OriginalPlatform = 3`. Numeric enum meanings are deliberately not assigned.
`Globals.lsf` has a `NewAge` root with a `ScratchBuffer` named `NewAge` of 2,618,936
bytes beginning `LSMF`. It is available in the raw explorer without being decoded.

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
| 22 | Unverified header digest (16 bytes) |
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

## Integrity, writing, confidence and open problems

No additional encryption was found or applied: the provided input is already decrypted.
Codec framing/length checks are enforced, including zlib/zstd integrity checks provided
by their decoders when present. They do **not** authenticate the package or its raw data.

LSLib's reference package writer computes MD5 over concatenated expanded entries,
then increments each digest byte. On this PS5 sample, **neither file-list order nor
ordinal filename order matches the header digest**. A regression test records this
finding. The digest is preserved and displayed as unverified, never reported as valid.
The correct PS5 digest/integrity algorithm remains unknown. Structural corruption and
broken compressed streams are rejected; arbitrary content changes that leave a valid
structure may be undetectable.

ReadOnly is deliberate: implementing a writer before resolving integrity would violate
the toolkit's integrity rule. Community editor research also reports load/warning
problems after edits, but those PC findings have not been independently reproduced on
PS5. No in-game writing verification has occurred. An LSF/LSPK writer, meaningful edit
conformance tests, and per-entry unchanged-content proof remain future work.

Verified: magic/version/layout matches, sample counts, actual primitive values, codec
output sizes, XML structure, the LSMF signature and byte-identical no-op preservation.
Raw fields carry `Confidence.Unknown` because their **gameplay meaning** is unvalidated.
There are no inferred gameplay controls, guessed item names, or curated Party tab.
Live character names, level/XP, gold, abilities and inventory relationships inside
LSMF require a separate validated decoder; LSF primitive editing alone cannot supply it.

Other limits: 256 MiB input, 256 MiB total expanded package payloads, 256 MiB expanded
resource sections per document, one million resource nodes/attributes, XML depth 256,
formatted translation depth 32. Explorer collections page in groups of 128 with
hierarchical page links for very large lists. Binary attributes above 4 KiB use a raw
view; XML values above 4,096 characters use a bounded preview. Views are lazy; models and compressed resource tables validate
during opening. Diagnostic parsing of the supplied package took approximately 0.5 s
on the development machine; this is an observation, not a timing assertion in CI.

## Validation and source-size guard

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
dotnet test --project plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
pwsh -NoProfile -File plugins/BaldursGate3/scripts/check-source-size.ps1
```

Tests cover the shared conformance suite, every sample resource and no-op copy,
backup equality, reference digest mismatch, synthetic version/codec/layout fixtures,
UTF-8/UUID/scalar vectors, malformed/truncated data, entity rejection, read-only edits,
thumbnails/blob ranges, and 20,000-entry/child paging. Synthetic tests run without samples.

Validation on .NET SDK 10.0.400: API build passed with zero warnings/errors; 44 plugin
tests and 42 framework tests passed. With an empty artifacts directory, 33 plugin
tests passed and 11 sample-dependent tests skipped. The source-size guard passed.

The source-size test runs with the plugin test project in CI. The optional scoped hook
warns above 300 physical lines and rejects 400 or more, honoring the stricter plugin
rule. Run it without changing configuration shared with other workers:

```powershell
git -c core.hooksPath=plugins/BaldursGate3/.githooks commit ...
```

## SDK requests

No contract change is required. Test-kit improvement: the shared conformance suite
currently skips byte-identical no-op checks for ReadOnly plugins. Those plugins can
and should still prove preservation; this plugin supplies that test itself.

## Sources and licensing

Primary reference: [Norbyte/LSLib](https://github.com/Norbyte/lslib), MIT, inspected at
revision `6f5f6987d10d8485876b8368431c948e4dea4f83`. Adapted format algorithms and type
IDs retain the upstream licence in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
Downloaded source was inspected as text; no upstream executable was run.

- [PackageFormat.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageFormat.cs): headers, row layouts, flags.
- [PackageReader.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageReader.cs): file lists, version dispatch, solid/multipart behavior.
- [PackageCommon.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageCommon.cs): payload selection and deletion markers.
- [PackageWriter.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageWriter.cs): digest algorithm tested against the sample; no writer ported.
- [LSFCommon.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSF/LSFCommon.cs) and [LSFReader.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSF/LSFReader.cs): section order, layouts, attribute chains, keys, strings/translations.
- [LSFVersion.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Enums/LSFVersion.cs): BG3 extended header, node-key and patch version numbering.
- [Compression.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Compression.cs) and [compression flags](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Enums/Compression.cs): codec dispatch and framing.
- [NodeAttribute.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/NodeAttribute.cs) and [BinUtils.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/BinUtils.cs): type IDs, scalar/vector layout, GUID display.
- [LSXReader.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSX/LSXReader.cs): XML resource structure; our explorer retains all elements generically.
- [clemarescx/bg3d](https://github.com/clemarescx/bg3d): inspector precedent and NewAge opaque-blob context.
- [Hawk-on field mapping](https://github.com/Hawk-on/bg3-save-editor/blob/master/FIELD_MAPPING.md): exploratory gold/character leads, not accepted as validated field mappings.
- [Wahkah/BG3SE research](https://github.com/Wahkah/BG3SE/blob/main/README.md): independent write-integrity concerns and LSMF context; no decoder code copied and no gameplay mappings adopted.
- [Serofix-code/bg3-save-editor](https://github.com/Serofix-code/bg3-save-editor): reported repack warnings and metadata versus live-state distinction.
- [BG3 format research](https://github.com/DanFinchUK/bg3-savefile-parser/blob/master/FORMAT.md): cross-check of LSMF scope; not a basis for PS5 party controls.
- [K4os LZ4 1.3.8](https://www.nuget.org/packages/K4os.Compression.LZ4.Streams/1.3.8) and [ZstdSharp.Port 0.8.8](https://www.nuget.org/packages/ZstdSharp.Port/0.8.8): managed MIT codec dependencies; see notices.

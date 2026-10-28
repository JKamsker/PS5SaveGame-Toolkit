# Unreal Engine GVAS (`engine.unreal-gvas`)

Experimental editor for decrypted Unreal SaveGame properties. The reusable parser
is in `SaveToolkit.Plugins.UnrealGvas.Gvas`, without UI types, filesystem/network
access, additional packages, or native DLLs.

## Sample coverage and file roles

Observations apply to the supplied archives, not every release of each title.
Filenames alone never establish support. Probe returns `Engine` for raw GVAS or
GVAS magic obtained by decompressing a supported envelope.

| Title | Observed format and roles | Result |
| --- | --- | --- |
| PPSA01603, Hogwarts Legacy | `ue4savegame.ps5.sav`: plain GVAS, UE 4.27.2, package 524, 72 custom versions. `SaveGameList` contains slot metadata; `SavedUserOptions` an opaque settings array; `HL0000`–`HL0014` contain metadata and compressed database arrays. 34 files, 17 distinct payloads. | All round-trip exactly. Primitive metadata editable; embedded byte arrays read-only. |
| PPSA02199, STAR WARS Jedi: Fallen Order | `ue4savegame.dpx.sav`: plain GVAS, UE 4.25.0, package 518, 56 custom versions. `GameUserSettings` has settings/structs. `SaveGame00`/`Backup00` have zlib actor records and `Crc32`. 6 files, 3 distinct payloads. | All round-trip exactly. Settings editable. Gameplay/backup documents read-only: checksum coverage unknown. |
| PPSA10695, Atomic Heart | Starts `01 00 00 00`, then a UE-style header and `/Script/SaveExtension.SlotInfo`, followed by custom framing. No GVAS magic anywhere. | Unclaimed; custom serializer needs its own plugin. |
| PPSA01802 | Versioned binary with region names including `W0_Ruins_Region`. No GVAS magic. | Unclaimed; unknown format, no established compression wrapper. |
| PPSA02343 | UTF-8 JSON with a `Profile` object. | Unclaimed; not GVAS. |
| PPSA16064 | Character file is JSON with `SerializeMeta`. Global settings appear to be JSON XOR 1 (inferred from the opening bytes; not implemented). Neither contains GVAS magic. | Unclaimed; not GVAS. |

All 40 claimed files, including backup twins, are tested byte-for-byte. Conformance
also covers all 20 distinct payloads, exceeding the default six-file cap. Regression
tests ensure that the other four titles' filenames never cause false matches.

## Layout and reader/writer

* Little-endian GVAS header: magic; save version 1–3; UE4 package version; UE5 package
  version for save version 3; engine major/minor/patch/build and FString branch.
  Modern headers contain custom-version format 3 and GUID/int32 entries. All header
  bytes are preserved. A save-class FString precedes the `None`-terminated properties.
* Legacy tags contain FString name/type, **uint32 payload size and uint32 static
  array index** (not a uint64 size), type metadata, and optional property GUID.
  Boolean payload size is zero: its value is in the tag. UE5.4+ uses recursive
  complete type names and flag bits; bool edits preserve all other flags.
* FStrings have positive UTF-8 byte counts or negative UTF-16LE code-unit counts,
  including NUL; empty strings may use length zero. Original encoding, indices,
  GUIDs, unknown payloads and trailing bytes remain byte-identical without edits.
  Changed non-ASCII strings use UTF-16LE.
* Supported primitives: bool; signed/unsigned 8/16/32/64-bit integers; float/double;
  string/name; enum and byte-enum strings; object/interface paths. FText base and
  culture-invariant source strings are editable, retaining flags/namespace/key.
  Other histories and non-finite floats remain opaque.
* Tagged custom structs, struct arrays with inner tags, and primitive arrays/maps/
  sets are traversed. Known structs: Vector, Vector2D, Vector4, Rotator (Pitch/Yaw/
  Roll), Quat, LinearColor, Color, Guid, DateTime, Timespan, SoftObjectPath and
  SoftClassPath. UE5 coordinates use doubles; UE4 uses singles; LinearColor always
  uses singles. SoftObjectProperty supports older two-string and UE5 package-version
  >=1007 three-string layouts.
* Byte arrays are opaque nodes. Maps/sets with removed entries or unspecified
  struct schemas, and unsupported native values, remain opaque. A custom struct is
  interpreted as tagged properties only when its complete bounded payload parses.
  Unrecognized tag extensions/version layouts throw SaveFormatException when safe
  payload boundaries cannot be established.
* Edits replace scalar spans in the original bytes. Every enclosing property size
  and struct-array inner size is adjusted by the length delta. Serialize reparses
  the result and compares the entire decoded tree and values before returning.
  Undo restores byte-identical output. No decrypted sample data is embedded.

## Compression, SQLite and checksums

The two claimed title sets have no outer wrapper. Plain RFC1950 zlib and the
documented 16-byte Astroneer envelope can additionally be identified and opened
**read-only**. Original compressed bytes are returned unchanged, with declared
length and Adler-32 checked. Recompression is deferred until game-specific window
requirements are verified against real wrapped PS5 samples; wrapper tests here are
synthetic. Dictionaries and trailing wrapper extensions are unsupported.

Hogwarts `RawDatabaseImage` and `RawExclusiveImage` are byte arrays beginning with
UE chunk magic `C1 83 2A 9E 00 00 00 00`; the first compressed stream in each begins
`8C 06`, verified in HL0010. Upstream tools identify this as Oodle Kraken carrying
SQLite databases. **Database contents have not been inspected or edited here**;
there is no managed Oodle implementation. The arrays and their internal framing
remain untouched. `RawMiniMapImage` and settings arrays are likewise read-only.

Jedi actor data has zlib framing, alongside explicit `UncompressedActorRecordsSize`
and `Crc32` properties. Standard CRC32 over compressed/uncompressed actor data did
not match the supplied gameplay records. Coverage/algorithm is unknown, so the
entire gameplay document is read-only. More generally, visible property names
containing CRC/checksum/signature disable writes. This conservative guard cannot
detect integrity metadata hidden in opaque game payloads. GVAS has no standard
container checksum. No embedded encryption was found in the claimed containers.

Outer Oodle/unknown envelopes remain unsupported. Without verified inner GVAS,
Probe declines them instead of inferring format from a filename.

## Views, confidence and limits

Overview shows header facts and writability. Properties uses lazy sections with
100-node pages; Search contains paged tables of top-level primitives. Top-level
fields appear only in Search to satisfy global value-ID uniqueness; nested fields
appear in the explorer. IDs use ordinal paths, stable across length changes.
Opaque values expose paged read-only hex.

`Verified` means the serialized name/storage type or sourced struct component
layout is known. Game effects, valid combinations, and useful ranges remain
unknown and fields carry that warning. Layouts absent from the real samples are
source-backed and synthetically tested, not console-verified. SQLite interpretation
comes from upstream research; contents are not independently confirmed.

Limits: 128 MiB unwrapped data; 100,000 decoded nodes; 64 nested parsing levels;
1,048,576 FString units. Byte-array lengths use the file bound, not the node bound.
Sample open-time tests enforce the approximately two-second target.

## Validation and maturity

**Experimental**: meaningful edits pass conformance, with integer/float/bool and
length-changing Unicode strings surviving reopen. No console reload has been
performed. This does not establish Stable or gameplay compatibility.

Tests cover every supplied GVAS snapshot/twin, known Hogwarts/Jedi metadata and
compression signatures, scalar widths, UE5 coordinates and complete-type tags,
nested struct-array/map/set size changes, byte-exact undo, atomic rejection,
bounded views/hex, truncations/corruption, and randomized foreign/mutated input.
Sample-dependent tests skip through SampleSaves when archives are absent.

Verified locally: API build completed with zero warnings/errors; plugin tests
32 passed, zero skipped with samples present; framework tests 42 passed. With an
empty sample directory, 20 synthetic/metadata tests pass and 12 sample tests skip.

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
dotnet test --project plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
pwsh -File plugins/UnrealGvas/scripts/check-csharp-size.ps1
```

Plugin-scoped MSBuild targets enforce C# file sizes during production/test builds:
warning above 300 lines, rejection above 500. No current file exceeds 300 lines.
The optional committed `hooks/pre-commit` runs the same check and can be chained
from the owner's existing hook. Shared hooks/CI files were not changed from this
isolated worktree.

## Open problems

1. Resolve Jedi gameplay checksum coverage before enabling gameplay writes.
2. Obtain managed, permissively licensed Oodle support and validate Hogwarts
   database consistency before offering database edits.
3. Verify compression rewriting against actual wrapped PS5 samples.
4. Add game schemas for opaque map/set structs, native values, complex FText and
   version extensions when samples become available.
5. Research Atomic Heart and the other custom serializers separately.

## Sources

Production C# independently implements the documented algorithms. No substantial
upstream code was copied and no downloaded program was executed.

* https://github.com/trumank/uesave (formerly trumank/uesave-rs; MIT), particularly
  https://github.com/trumank/uesave/blob/master/uesave/src/lib.rs — primary reference
  for Header, PropertyTagFull, VersionInfo, struct arrays, SoftObjectPath, FText,
  native struct layouts and opaque fallback.
* https://github.com/13xforever/gvas-converter — C# alternative surveyed before
  choosing uesave as the primary layout reference.
* https://github.com/turtle-insect/GvasViewer — C# alternative surveyed; native
  Oodle DLL requirement informed the decision to keep this plugin managed-only.
* https://github.com/topche-katt/hlsavetool — Hogwarts byte-array/SQLite/Oodle flow.
* https://github.com/producedbytmsh/hl-savetools — RawDatabaseImage/RawExclusiveImage
  UE chunk headers and `8C 06` Kraken framing; only outer signatures verified here.
* https://gist.github.com/MartyMacGyver/ebeb8f803ef66be87c7c7d95d000ab42 — Astroneer
  wrapper magic, declared size, zlib and reported 4 KiB compression-window constraint.
* https://www.rfc-editor.org/rfc/rfc1950 — zlib header and Adler-32 definition,
  independently implemented and checked against .NET-generated compressed streams.

## SDK requests

* Allow read-only document download separately from editing: CanEdit=false
  currently disables export in the UI as well.
* A field-reference node could show one value in both Search and the tree while
  retaining global value-ID uniqueness. Tree entries currently refer to Search.
* Conformance selection could distinguish recognized-but-unsupported wrappers
  from openable matches; this plugin currently selects only openable formats.

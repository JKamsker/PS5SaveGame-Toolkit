# Unreal Engine GVAS (`engine.unreal-gvas`)

Experimental editor for decrypted Unreal SaveGame properties. The reusable parser
is in `SaveToolkit.Plugins.UnrealGvas.Gvas`, without UI types, filesystem/network
access, additional packages, or native DLLs. Hogwarts database inspection uses
the shared [Codecs](../../src/SaveToolkit.Codecs/README.md) library, including its
bundled native in-memory SQLite dependency.

Round-2 non-Hogwarts work adds F8 named/nested property views, attributed storage
catalogs, corrected Jedi actor decoding and extended checksum evidence. See
[round-2 evidence and exact owner checklist](ROUND2.md). Existing-value writes are
enabled in this round with explicit rejection warnings; no bulk action is exposed.

## Framework feature adoption

| feature | status | where / reason |
| --- | --- | --- |
| F1 SaveDiff | Used | Hogwarts decoder hook and adjacent table/row evidence; corruption and no-op fixtures cover GVAS and database framing. |
| F2 reference packs | Used | `unrealgvas.ps5`, `unrealgvas.storage` and `hogwarts.legilimens` are embedded, pinned and attributed in the Sources panel. |
| F3 rich table cells | Used | F8 rows expose live field cells, text, confidence, descriptions, warnings and provenance; image-cell rendering remains host-owned. |
| F4 `ReferenceSpec` pickers | Used | Exact table/column-qualified Hogwarts IDs, title captions and catalogue examples open the F13 browse-only picker with attribution, category filters, declared sorting and no save commit. |
| F5 structural edits | Added | Fully parsed primitive array/map/set nodes expose a bounded remove-last action with snapshot undo/redo, reparsing and size validation; struct/native/opaque containers remain read-only. |
| F6 planned bulk actions | N/A | No bounded bulk or preset has proven gameplay semantics; adding one would hide unknown checksum/relationship effects. |
| F7 document groups | N/A | No enabled edit safely owns a coupled companion; Hogwarts primary/backup and the two database images remain independent until owner captures establish roles. |
| F8 queryable collections | Used | Generic nested properties, Jedi actor observations, Sackboy projections and all Hogwarts tables use bounded search/filter/sort/paging. |
| F9 maps/graphs | Added | Hogwarts Spatial tab adds an attributed schematic collectible/location map and a catalogue-domain graph, both with text alternatives and unknown states. |
| F10 exact numbers and nullable/union specs | Used | GVAS widths are preserved through `Int128`/exact scalar bindings; nullable database values remain text-only when their semantics are unknown. |
| F11 shared codecs | Used | Hogwarts routes Kraken framing and in-memory SQLite through `SaveToolkit.Codecs`; no local codec or filesystem I/O is present. |
| F12 SaveDiff F8 traversal / decoder hook | Used | Shared `PluginViewDecoder` pages generic/named F8 views; `UnrealGvasPlugin.Decoders` registers the production SDK-only `unrealgvas.hogwarts.rows` evidence hook. |
| F13 non-mutating inspection / catalogue browsing | Added | Attributed catalogue tabs and `Mutates = false` coverage/provenance actions work on writable and read-only documents; Hogwarts uses shared category filtering and Key/Name/Category sorting. |
| F13 float duration hint | N/A | No exposed float timer has verified units; serialized time-like names alone do not justify a duration conversion. |
| F14 empty inventory slots | Added this round | Each independent Hogwarts `InventoryDynamic` F8 table hides confirmed NULL item/variation + integer-zero slots before paging, with matching metadata, stable tuple keys and correct hidden counts. Named zero-count and unknown records remain visible; Jedi/Sackboy projections have no confirmed vacancy rule. |
| F4 in F6 planned parameters (`63580dc`) | N/A | No planned action or enum parameter fallback is published; catalogue inspection actions have no parameters and cannot mutate saved IDs. |

## F14 inventory presentation

The table audit covered all eager schema/property/actor summaries, generic and
title-specific F8 projections, and all Hogwarts SQLite tables. No inventory uses
"Slots N–M" sections. Existing lazy raw-property pages and the bounded Jedi actor
summary are diagnostics; their complete searchable F8 indexes remain available.

Hogwarts retains **one `InventoryDynamic` table per independent database image**.
Both its collection metadata and view declare the `ItemID` empty-slot label column.
Only rows with SQL NULL `ItemID`, SQL NULL `Variation` and an integer `Count` of zero
are marked empty. The shared browser hides them by default and displays **Empty
slot** when shown, retaining the original cells and editable count in details.
Queries apply search/column filters, count matching hidden slots, filter visibility,
then sort/page. The native decoder explicitly includes all rows, as does shared
SaveDiff view traversal. No record or slot is deleted or reordered in the save.

The 15 distinct supplied gameplay saves contain 30 independently decoded images:
44,572 inventory rows, including 21,387 rows with the exact vacant shape and 30
named zero-count records that remain visible. The SQL NULL exclusion in the
[pinned HLSGE-derived editor inventory queries](https://github.com/falker47/HogwartsLegacy-SaveEditor/blob/349c3f0b75e4d158e5b7d7920bded999b518de36/HLSE-src/src/client/resources/saveGameDB.ts)
corroborates the missing-item interpretation; the complete three-part rule is
verified against the PS5 bytes. Gameplay meaning remains Inferred. `GearVariation`
equal to the string `None` does not prove an empty equipped slot: all 540 observed
equipped records retain gear IDs. No empty rule is imposed on those records,
gear instances, catalogue examples, Jedi actors or Sackboy property projections.

Tests cover direct and host queries, full-list paging/search/filter/sort, default
and explicit visibility, hidden counts, original slot keys, visible-slot writes,
empty-count visibility changes and undo, both diff decoders, and F6 selection over
unloaded pages. The F6 planner is a **test-only adapter**; no production bulk action
is added. Count edits refresh slot markers and page selections. Original tuple
identities and byte-identical no-op serialization are unchanged. Research and
licence details are in the [slot-rule source note](SaveToolkit.Plugins.UnrealGvas/Hogwarts/InventorySlots.source.md).
Validation: all 139 plugin tests passed (10 new regressions), with zero skips;
the API build passed with zero warnings/errors. All changes stay in this plugin.

## F12/F13 adoption

The **Catalogues** tab (generic GVAS, Jedi and Sackboy) and **Collectible catalogue**
tab (Hogwarts) publish read-only `ReferenceSpec` examples. They are explicitly
catalogue examples, not saved values, ownership or completion evidence. The shared
F13 picker provides bounded search, categories, paging, declared Key/Name/Category
sorting and attribution. Existing read-only saved reference fields use the same
browse-only picker; neither path writes an ID.

Each loaded catalogue also publishes a `Mutates = false` inspection action that
returns bounded coverage/category/sort metadata and provenance. Core runs it on an
isolated current-save copy and verifies no byte/value/history/revision changes.
Missing packs omit these examples/actions; raw saved IDs remain preserved.

The former test-only Hogwarts F1 decoder now lives in the production plugin as an
SDK-only `ISaveDecoderHook`, retaining its ID and schema/count/hashed-row semantics.
The existing adjacent-snapshot evidence tests use the shared `HookSaveDecoder`
adapter. The hook stops when the sink refuses output, honors file/depth limits,
and caps traversed rows at 100,000 across both images, reporting truncation.
Ambiguous modeled tables retain content-hash multisets with multiplicities and
warnings; unmodeled tables retain schema/count coverage. Raw cell/account values
are never emitted. Select it explicitly:

```powershell
dotnet run --project tools/SaveToolkit.SaveDiff -- list-decoders
dotnet run --project tools/SaveToolkit.SaveDiff -- compare before.zip after.zip --decoder unrealgvas.hogwarts.rows
```

Shared F12 view traversal is also verified against 205 generic F8 rows over multiple
pages, including truncation at the query-row cap. It describes rendered storage;
the native Hogwarts hook remains the row-identity evidence mode.

## Enabled writes and their evidence

| write | confidence | evidence | integrity status | owner validation needed |
| --- | --- | --- | --- | --- |
| Jedi gameplay and Sackboy General existing scalar fields (checksum fields remain read-only) | Inferred | Field names/types and bounded scalar spans match the supplied PS5 captures; edits reopen byte-exactly | CRC/checksum coverage is unproven; the game may reject or overwrite the save | [Issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): controlled before/after captures and console load/save/reload for each title |
| Hogwarts existing integer, real and text cells in either decoded database image | Inferred | Shared `SaveToolkit.Codecs` SQLite image, integrity check, stored Kraken frame rebuild, fresh GVAS parse and independent SQLite reopen | SQLite `PRAGMA integrity_check` is required; stored-block game acceptance is unproven and output is capped at 16 MiB | [Issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): unchanged reconstruction, per-event image/row mapping, slot allocation and console reload |

All enabled writes are Experimental. Warnings are shown beside each field and returned
in the edit outcome. Hogwarts does not edit identity columns, blobs, catalog reference
cells, metadata-only files or malformed/partial database pairs.

Compressed outer envelopes, including Hogwarts zlib and Astroneer wrappers, remain
read-only until envelope recompression is verified. Database inspection is still
available and serialization preserves the exact imported wrapper.
Jedi's `UncompressedActorRecordsSize` also remains read-only: changing this paired
length without rebuilding the preserved compressed actor stream invalidates it.

## Sample coverage and file roles

Observations apply to the supplied archives, not every release of each title.
Filenames or GVAS magic alone never establish support. Probe returns `Engine`
only after validating the supported header and first root property name, either
directly or inside a supported compression envelope. The shared header reader
checks save/engine versions, FString lengths, encodings and NUL terminators,
custom-version format/count/table length, save-class string and UE5 root flags.
It stops before property tags/payloads, skips custom-version entries without
modeling them, and uses the full parser's limits rather than a smaller probe cap.
Files above the same 128 MiB input limit are unclaimed. Full property and wrapper
integrity validation remains the responsibility of `Open`.

| Title | Observed format and roles | Result |
| --- | --- | --- |
| PPSA01603, Hogwarts Legacy | `ue4savegame.ps5.sav`: plain GVAS, UE 4.27.2, package 524, 72 custom versions. `SaveGameList` contains slot metadata; `SavedUserOptions` an opaque settings array; `HL0000`–`HL0014` contain metadata and two Kraken/SQLite arrays. 34 files, 17 distinct payloads. | All round-trip exactly. Existing database cells can be edited through the bounded codec path; metadata and malformed pairs remain read-only. |
| PPSA02199, STAR WARS Jedi: Fallen Order | `ue4savegame.dpx.sav`: plain GVAS, UE 4.25.0, package 518, 56 custom versions. `GameUserSettings` has settings/structs. `SaveGame00`/`Backup00` have zlib actor records and `Crc32`. 6 files, 3 distinct payloads. | All round-trip exactly. Existing gameplay scalar fields are editable with an unproven-checksum warning; `Crc32` and compressed actor payloads remain read-only. |
| PPSA01288, Sackboy: A Big Adventure | `General`: UE 4.25.1 tagged progression, including `CollectabellsStored` and `Checksum`. `LevelImages_*` / `CostumeThumbnails_*`: GVAS image sets. 20 files, 6 distinct payloads. | All round-trip exactly. Existing General scalar fields are editable with an unproven-checksum warning; `Checksum` and opaque image payloads remain read-only. |
| PPSA01463 / PPSA01493, Gearbox variants | 24 files start with GVAS magic but have header/length layouts incompatible with this reader. Includes character `1` and profile files, with primary/backup snapshots. | Unclaimed after header validation; use the higher-priority Gearbox plugin. Regression for [#36 finding F](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/36). |
| PPSA10695, Atomic Heart | Starts `01 00 00 00`, then a UE-style header and `/Script/SaveExtension.SlotInfo`, followed by custom framing. No GVAS magic anywhere. | Unclaimed here; see the separate [AtomicHeart plugin](../AtomicHeart/README.md), which validates framing and SHA-1. |
| PPSA01802 | Versioned binary with region names including `W0_Ruins_Region`. No GVAS magic. | Unclaimed; unknown format, no established compression wrapper. |
| PPSA02343 | UTF-8 JSON with a `Profile` object. | Unclaimed; not GVAS. |
| PPSA16064 | Minecraft Dungeons II: Character is plain JSON with `SerializeMeta`; Global settings are byte-minus-one JSON, **not XOR 1**. Neither contains GVAS magic. | Unclaimed here; see [XorJson](../XorJson/README.md) for Global settings. |

All 60 claimed files, including backup twins, are tested byte-for-byte. Conformance
also covers all 26 distinct payloads, exceeding the default six-file cap. Regression
tests ensure that the other four titles' filenames never cause false matches.
A corpus-wide regression probes and attempts to open every file in every available
sample archive, asserting both that every match opens and that every openable file
is claimed (60 matches in the supplied 33-title corpus). Dedicated tests reject all
24 Gearbox variants. Synthetic tests cover legacy UE4 and modern UE5 headers,
UTF-8/UTF-16, maximum supported lengths/counts, truncated/malformed headers, all
three envelope forms, and short non-seekable reads that stop before property tags.

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
  and struct-array inner size is adjusted by the length delta. Hogwarts database
  writes replace only the two modeled array payloads and preserve surrounding GVAS
  bytes. Serialize reparses the result and compares the decoded tree and values
  before returning. Undo restores byte-identical output. No decrypted sample data
  is embedded.

## Compression, SQLite and checksums

The three claimed title sets have no outer wrapper. Plain RFC1950 zlib and the
documented 16-byte Astroneer envelope can additionally be identified and opened
**read-only**. Original compressed bytes are returned unchanged, with declared
length and Adler-32 checked. Recompression is deferred until game-specific window
requirements are verified against real wrapped PS5 samples; wrapper tests here are
synthetic. Dictionaries and trailing wrapper extensions are unsupported.

Hogwarts `RawDatabaseImage` and `RawExclusiveImage` are byte arrays beginning with
UE chunk magic `C1 83 2A 9E 00 00 00 00`; the first compressed stream in each begins
`8C 06`, verified in HL0010. The shared managed Kraken decoder now exposes both
SQLite images through the shared in-memory helper, with separate integrity checks.
All 30 images across 15 distinct gameplay snapshots decode and serialize without
changes byte-identically. On an edit, the write path is SQLite `Serialize()` (which
also runs `PRAGMA integrity_check`), then stored Kraken blocks, then a size-adjusted
GVAS replacement. The result is parsed again, both arrays are independently walked,
and both images pass a fresh SQLite integrity check before export. Stored-block
acceptance is unproven; a conservative 16 MiB slot budget rejects larger output.
`RawMiniMapImage` and settings arrays are likewise read-only. See [Hogwarts evidence
and owner checklist](HOGWARTS.md) for framing, row identities, F1 diffs, write-probe
results and remaining console gates.

Jedi actor data has zlib framing, alongside explicit `UncompressedActorRecordsSize`
and `Crc32` properties. Standard IEEE CRC32, CRC32C, FNV-1a, Adler32 and byte-sum
candidate vectors over the known whole-file/zeroed-field range do not match the
supplied gameplay records; independent check vectors are in the plugin tests.
Coverage/algorithm is unknown, so the known `Crc32`/`Checksum` fields remain read-only
while other existing scalar fields are enabled with an explicit game-rejection
warning. More generally, unknown documents with visible CRC/checksum/signature fields
remain read-only. This conservative guard cannot detect integrity metadata hidden in
opaque game payloads. GVAS has no standard container checksum. No embedded encryption
was found in the claimed containers.

Outer Oodle/unknown envelopes remain unsupported. Without verified inner GVAS,
Probe declines them instead of inferring format from a filename.

## Views, confidence and limits

Overview shows header facts, writability and the selected version/class/property
signature schema. Sackboy General and image sets expose read-only schema tables
with attributed catalog references. F8 indexes all nested generic properties
without lazy expansion, with text search, exact filters and deterministic text
sort/paging. Sackboy adds Progression, Costumes and emotes, World and levels, and
Settings groups. Jedi adds Journey state, Settings and Actors, separating tagged
data from unverified FString observations. Actor decoding starts at the complete
ActorRecords FString length; it is not an outer array-count prefix. Properties
uses lazy sections with 100-node pages; Search contains top-level primitives.
The Schema actor summary is bounded to 100 rows; full actor collections use F8.
Hogwarts adds independent
Character, Inventory, Progression and Collections F8 tables for each image; every
other table is available lazily. The pinned MIT Legilimens F2 catalog supplies 699
collectible location records, with exact table/column-qualified F4 reference fields.
Its Spatial tab adds a link-first schematic map of attributed regions plus a catalogue
domain graph. Positions are plugin-authored grouping coordinates, not game coordinates;
all marker and graph states remain Unknown, and fast travel is an unjoined external
domain. `SpatialViewValidation` checks bounds, links, attribution and acyclicity.
Hogwarts metadata and malformed/partial database pairs remain read-only. Existing
integer, real and text cells in a complete pair are editable with Inferred confidence;
identity columns, blobs and catalog reference cells stay read-only. Top-level fields
appear only in Search to satisfy global value-ID uniqueness; nested fields appear in
the explorer. IDs use ordinal paths, stable across length changes.
Opaque values expose paged read-only hex.

`Verified` means the serialized name/storage type or sourced struct component
layout is known. Game effects, valid combinations, and useful ranges remain
unknown and fields carry that warning. Layouts absent from the real samples are
source-backed and synthetically tested, not console-verified. Hogwarts table/column
storage and exact catalog-ID matches are checked against actual PS5 bytes; labels,
state interpretation, image relationships and game effects remain Inferred/Unknown.

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

The Hogwarts round adds all-image integrity/no-op checks, F1 row diff evidence,
F8 host registration/query bounds, F2/F4 attribution and candidate resolution,
corruption isolation, and test-only stored-frame GVAS reconstruction. Sample
tests require the artifacts environment variable in the same command as the run.
Validated for the completion round: **85 passed, 0 failed, 0 skipped**, including
Jedi/Sackboy warned scalar edits, Hogwarts SQLite/stored-block edit/reopen, all
Hogwarts regressions, conformance, query bounds/identities, F5 primitive array/map/set
remove/reopen/undo, corruption tests and F9 spatial validation; API build: **0 warnings, 0 errors**.

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet test --project plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

No plugin build hooks, git hooks or size-check scripts are installed. All C# files
added in this round remain below 300 lines; repository policy is enforced in review.

## Open problems

1. Resolve Jedi/Sackboy checksum coverage and validate their gameplay effects on console.
2. Validate Hogwarts database roles, effective gameplay limits, stored-output slot
   allocation and console load/save/reload behavior before relying on database edits.
3. Verify compression rewriting against actual wrapped PS5 samples.
4. Add game schemas for opaque map/set structs, native values, complex FText and
   version extensions when samples become available.
5. Solve Sackboy General checksum coverage; decode image texture layouts before
   providing image previews. Atomic Heart now has its own read-only plugin.
6. Generic structural insertion, reorder and semantic item creation remain disabled
   until stable identities, required defaults and game relationships are proven. The
   bounded primitive-container remove-last action is the only enabled structural edit.

## Historical overlay handoff (2026-10-28)

The schema selector matches positive save-class, engine-build and property-signature
evidence before adding a title tab; unmatched files retain the generic view. The
Jedi actor view first attempts the normal legacy tag reader and falls back to a
bounded serialized-FString index for custom actor structs, preserving unknown bytes
and never enabling edits. The Sackboy tab surfaces the verified storage names and
values, while its checksum and image metadata remain explicitly unknown or inferred.

The first overlay handoff introduced `unrealgvas.ps5` F2 v1.0.0; round 2 extends it
to v1.1.0, title-scoped to PPSA01288 and PPSA02199. It repeats attribution for each dataset and carries
no limits, icons, localization or private sample bytes. The UI attaches a
`ReferenceSpec` to each read-only catalog property so F4 can resolve the raw name
without changing the stored value.

### Hogwarts follow-up after #28

The merged shared APIs now provide Kraken and in-memory SQLite. A composed
`HogwartsDocument` adds the named views without changing the generic parser or
Jedi/Sackboy functionality. No codec or SQLite implementation is duplicated here.
See [HOGWARTS.md](HOGWARTS.md) for the complete current boundary and evidence.

## Round 2 evidence (2026-10-28)

The newer non-Hogwarts roadmap round is documented in [ROUND2.md](ROUND2.md).
Its coverage matrix adds 12 Sackboy / 25 Jedi named ranges and 592 derived-seed
CRC hypotheses, with independent Python cross-checks. Both distinct Sackboy
snapshots store `2E0726BB`; invariant ranges are explicitly inconclusive.
The older observations below remain valid but are not checksum-repair proof.

Sackboy General has two distinct supplied snapshots: both `CollectabellsStored`
and `TotalCollectabellsCollected` read 445 in one and 322 in the other. These are verified
serialized names and values, not in-game validated edit targets. `Checksum` remains
read-only; other existing General scalars are enabled with the write-round warning.

All companion files use `/Script/GingerBread.GingerbreadSaveImageSet`, including
an empty `LevelImages_05`. Nonempty image sets have a tagged `Data` struct with
an opaque Name-to-struct map. The bytes contain `SurfaceWidth`, `PixelFormat`,
`Image`, and Unreal chunk headers. `LevelImages_04` has a zlib stream at `0x6E4`:
12,144 compressed bytes decode to 131,072 bytes. No PNG or JPEG signatures were
found across all four distinct companion payloads. The source below identifies
PC images as compressed, swizzled raw texture pixels; matching PS5 raw pixel
layout remains **inferred**. The asset round adds only separately sourced, downscaled Sackboy costume reference thumbnails; the save's opaque image payloads remain untouched.

Jedi gameplay/backup actor arrays are valid zlib, declaring and producing
2,580,619 / 2,471,665 bytes. Their decompressed data begins with the tagged
`ActorRecords` property. Stored CRCs are `253EB7D0` / `3A3A9C96`; IEEE CRC32 of
the decompressed streams is `19B1D711` / `6A4BFDA3`, and of the compressed streams
is `97B6B113` / `AD68AFA4`. None match.

Further scratch experiments covered top-level tag/payload boundaries; whole
file/headerless property streams with CRC original/zeroed/omitted; compressed
array payload with and without its count; decompressed actor substitution with
old/new/omitted counts; full `ActorRecords` tagged-stream substitution with
size-property included/omitted; terminal padding/None removal; and all 1,023
nonempty ordered subsets of the ten top-level property payloads, compressed and
expanded. Two later batches alone checked 2,334 + 708 buffers per distinct
sample, using external zlib seeds 0 / FFFFFFFF and both result complements.
No match was found. Committed tests reproduce the principal ranges and also
reject CRC32C variants, with independent standard check vectors. This does not
rule out other seeds, coverage, serialization order, or a non-CRC algorithm.
Gameplay scalar writes are enabled with warnings; no checksum-repair claim is made
and the explicit integrity fields remain read-only.

## Sources

Production C# independently implements the documented algorithms. No substantial
upstream code was copied and no downloaded program was executed.

* https://github.com/trumank/uesave (formerly trumank/uesave-rs; MIT), particularly
  https://github.com/trumank/uesave/blob/master/uesave/src/lib.rs — primary reference
  for Header, PropertyTagFull, VersionInfo, struct arrays, SoftObjectPath, FText,
  native struct layouts and opaque fallback.
* https://github.com/13xforever/gvas-converter — C# alternative surveyed before
  choosing uesave as the primary layout reference.
* https://github.com/turtle-insect/GvasViewer — C# alternative surveyed; no native
  proprietary Oodle DLL is shipped. Shared SQLite uses bundled native e_sqlite3.
* https://github.com/topche-katt/hlsavetool — Hogwarts byte-array/SQLite/Oodle flow.
* https://github.com/producedbytmsh/hl-savetools — RawDatabaseImage/RawExclusiveImage
  UE chunk headers and `8C 06` Kraken framing; decoded PS5 schema is now inspected
  independently rather than inheriting PC column names or mirror/write assumptions.
* https://gist.github.com/MartyMacGyver/ebeb8f803ef66be87c7c7d95d000ab42 — Astroneer
  wrapper magic, declared size, zlib and reported 4 KiB compression-window constraint.
* https://www.rfc-editor.org/rfc/rfc1950 — zlib header and Adler-32 definition,
  independently implemented and checked against .NET-generated compressed streams.

* https://peteronprogramming.wordpress.com/2024/01/06/reverse-engineering-the-end-of-level-photos-in-sackboy-a-big-adventure/ — primary author research on GVAS image sets, chunked zlib and swizzled raw pixels; no code copied. PS5 zlib framing checked independently above.
* https://github.com/MsKrypt1/SackboyWorldUnlocker — surveyed PS4 General tool; no code copied (no explicit licence established).
* https://prosperopatches.com/PPSA01288 — Sackboy title-id mapping.
* https://www.save-editor.com/tools/pc_game_save_converter_title_StarWars_Jedi_FallenOrder.html — converter surveyed; publishes no usable checksum implementation. No data uploaded or code executed.

## Sources and attribution

| Dataset / implementation | Pinned source, author, license | Changes and display provenance |
| --- | --- | --- |
| Hogwarts vacant inventory slot presentation | [HLSGE-derived `saveGameDB.ts`, revision 349c3f0](https://github.com/falker47/HogwartsLegacy-SaveEditor/blob/349c3f0b75e4d158e5b7d7920bded999b518de36/HLSE-src/src/client/resources/saveGameDB.ts), HLSGE contributors / falker47; embedded package declares `UNLICENSED`: No licence published; reused with attribution per owner decision 2026-10-28; retrieved 2026-10-29 | Researched non-NULL item filtering; independently implemented a stricter PS5-observed NULL item/variation + integer-zero rule. No upstream code or dataset copied. Original rule/implementation GPL-3.0-or-later; [source note](SaveToolkit.Plugins.UnrealGvas/Hogwarts/InventorySlots.source.md), `Info.References` and inventory table descriptions retain attribution. |
| GVAS header layout and probe validation | [uesave `Header::read` and `Root::read`, revision 1917a22](https://github.com/trumank/uesave/blob/1917a22ab69e9a4045e613d9d7f39a49fe00bd46/uesave/src/lib.rs), Truman Kilen / contributors; MIT; rechecked 2026-10-29 | Independently implemented shared C# header reader; no code copied. Header/string limits match this plugin's full parser. Actual PPSA01463/PPSA01493 header rejection and all 60 openable samples verified separately. |
| Hogwarts 699 collectible IDs, type/region/index and guide links | [Legilimens 02525dde](https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/tree/02525dde0fd9bc0be72982478680377dccb41246), Malin001 and contributors; [MIT](https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/blob/02525dde0fd9bc0be72982478680377dccb41246/LICENSE); retrieved 2026-10-28 | Projected to F2 `hogwarts.legilimens` v1.1.0; labels combine type/region/index, table/column-qualified exact joins, no icons/video media in this dataset. License and [source sidecar](SaveToolkit.Plugins.UnrealGvas/ReferencePacks/hogwarts.legilimens/collectibles.source.md) accompany data; manifest hash, `Info.References`, Sources panel and each F4 field carry attribution. MIT retained; no CC BY-SA data imported. |
| Hogwarts gear descriptions and icons | [HogwartsLegacy.GamesRef.com gear database](https://hogwartslegacy.gamesref.com/gear), [EIP.GG gear icon mirror](https://cdn.eip.gg/hogwarts-legacy/icons/gear/); retrieved 2026-10-29 | Eight short game UI descriptions and eight 64x64 WebP gear icons. Friendly names and icon joins are Inferred and never asserted as PS5 saved-ID matches. Rights reserved by Warner Bros. Games / Portkey Games; used with attribution per owner decision 2026-10-29. [Asset sidecar](SaveToolkit.Plugins.UnrealGvas/ReferencePacks/hogwarts.legilimens/assets.source.md) and manifest carry attribution. |
| Hogwarts Spatial background | [Plugin source](https://github.com/JKamsker/PS5SaveGame-Toolkit), PS5 Save Game Toolkit contributors; GPL-3.0-or-later; retrieved 2026-10-29 | Original 800x800 Highlands schematic aligned to the existing 0-100 marker grid. No publisher map pixels or external coordinates are embedded. |
| Hogwarts framing and schema research | [hl-savetools e568dedc](https://github.com/producedbytmsh/hl-savetools/tree/e568dedcda2611f54cc793e6d0237c749ae25094), producedbytmsh; MIT; retrieved 2026-10-28 | Independently implemented bounded framing; PS5 lengths/schema checked. No inherited PC mirror, state/reward or no-checksum claims. Navigation descriptions cite research and label meaning Inferred. |
| Shared Kraken / SQLite | [SaveToolkit.Codecs](../../src/SaveToolkit.Codecs/README.md), toolkit contributors and attributed upstream authors; GPL-3.0-or-later decoder, MIT Microsoft.Data.Sqlite, Apache-2.0 SQLitePCLRaw, public-domain SQLite | Referenced, not ported/duplicated. Root LICENSE/THIRD-PARTY-NOTICES retain shared dependencies' notices. In-memory native SQLite only; no proprietary decoder. |
| PS5 schema captions / F1 identity evidence | Toolkit contributors, GPL-3.0-or-later; privately retained sample observations, 2026-10-28 | Original navigation text and storage observations, no extracted sample records committed. Table descriptions cite provenance and expose schema fingerprints; raw labels remain when catalog joins fail. |
| Sackboy progression/costume/world and Jedi actor/journey captions | [Toolkit original evidence](https://github.com/JKamsker/PS5SaveGame-Toolkit), contributors; GPL-3.0-or-later, 2026-10-28 | `unrealgvas.ps5` v1.2.0 has 24 Sackboy and 12 Jedi entries plus four attributed costume icon references. Original captions inferred from observed serialized names; costume thumbnails are reference-only and have no saved-ID join. [Sidecar](SaveToolkit.Plugins.UnrealGvas/ReferencePacks/unrealgvas.ps5/SOURCES.md), dataset manifest, [costume sidecar](SaveToolkit.Plugins.UnrealGvas/ReferencePacks/unrealgvas.ps5/costumes.source.md), F4 descriptions, per-F8-row source and Sources panel carry provenance. |
| Jedi settings storage captions | [Toolkit original evidence](https://github.com/JKamsker/PS5SaveGame-Toolkit), contributors; GPL-3.0-or-later, 2026-10-28 | Six original display/input/override captions in `unrealgvas.ps5` v1.1.0, read-only named projections over canonical raw fields. Same sidecar/manifest/UI provenance; effective ranges and gameplay effects unverified. |
| Generic serialized GVAS type captions | [uesave 1917a22](https://github.com/trumank/uesave/tree/1917a22ab69e9a4045e613d9d7f39a49fe00bd46), Truman Kilen / contributors; MIT upstream reference, GPL-3.0-or-later original captions; 2026-10-28 | `unrealgvas.storage` v1.0.0: 17 type identifiers and independently authored explanations; no implementation port. Full [MIT notice](SaveToolkit.Plugins.UnrealGvas/ReferencePacks/unrealgvas.storage/LICENSE.txt), [source sidecar](SaveToolkit.Plugins.UnrealGvas/ReferencePacks/unrealgvas.storage/property-types.source.md), normalized hashes, Info.References, per-row attribution and Sources panel. Applies to generic GVAS without a known title. |
| External gameplay guides / unclear-license schema leads | [Imagisphere levels](https://wiki.imagisphere.me/Category:Sackboy:_A_Big_Adventure_Levels), [Imagisphere shop](https://wiki.imagisphere.me/Zom_Zom%27s_Shop_%28Sackboy%3A_A_Big_Adventure%29), [PowerPyx Jedi guide](https://www.powerpyx.com/star-wars-jedi-fallen-order-all-collectible-locations-guide/), [SackboyWorldUnlocker](https://github.com/MsKrypt1/SackboyWorldUnlocker), [GingerBread scaffold](https://github.com/LBPHaxMods/GingerBread/tree/2cf1351e8992ca07d7d7474be454c6476f8ebde2); respective authors, research only | Rule 9 external-link route: source/details links only. No unsupported saved-ID joins are asserted. New publisher/community art is limited to the attributed, owner-permitted asset datasets described above. No new CC BY-SA adaptation bundled. |
| Sackboy image metadata catalog | [Peter on Programming](https://peteronprogramming.wordpress.com/2024/01/06/reverse-engineering-the-end-of-level-photos-in-sackboy-a-big-adventure/); [MIT extractor 3b26966](https://github.com/Donpedro13/GingerBreadImageExtractor/tree/3b26966a04a2b6e20d61962ef6fcb84242bb7a1f), original PS5 evidence, 2026-10-28 | Existing field-name-only dataset unchanged; metadata evidence is separate from unverified PS5 pixels/swizzle. |
| Hogwarts spatial navigation links | [Legilimens 02525dde](https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/tree/02525dde0fd9bc0be72982478680377dccb41246), MIT; [MapGenie external map](https://mapgenie.io/hogwarts-legacy/maps/hogwarts), link-only, retrieved 2026-10-28 | Region positions remain original schematic coordinates and are aligned to the embedded schematic background. MapGenie remains an external navigation link; no third-party coordinates or publisher map art is embedded. |

## Licence exclusions

Resolved in the 2026-10-29 owner decision: publisher game assets and all-rights-reserved community references used by the curated Hogwarts gear and Sackboy thumbnail datasets. Each asset dataset records the holder, source, attribution and owner-policy basis; no unlisted publisher or community media is embedded.

## SDK requests

No new SDK request is required for this round. The implementation uses the existing
`IReferenceContext`, embedded-pack loader, `ReferenceSpec`, lazy sections and bounded
F8 collections. Any future eligible bulk action must use F6 `IActionPlanningDocument`
and `SupportsPlanning`, including complete F8 query scopes; none is enabled by
unproven checksums or guessed gameplay bounds. The former read-only-download request is stale: the current API already
permits original-file download when editing is disabled. A canonical field-reference
node and richer image cells remain useful future framework improvements, but this
plugin does not duplicate them locally. Hogwarts uses F8 and the shared Codecs
reference with no new required contract. The former read-only F4 browse request is
resolved by F13 (#33): the shared picker remains searchable, filterable and sortable
while selection/commit is disabled. F12 (#32) resolves native decoder discovery through
`ISaveDecoderProvider`; no test-only decoder or plugin-side catalogue index is needed.

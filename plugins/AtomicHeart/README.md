# Atomic Heart (`atomic-heart`)

**ReadOnly** inspector for decrypted PPSA10695 SaveExtension saves. Two supplied
primary/backup files contain one identical 126,214-byte payload. No game data,
identifiers or extracted records are embedded in the plugin or tests.

## Format and file roles

`sdimg_9/ue4savegame.dpx.sav` is a slot containing uncompressed metadata and
compressed gameplay records. It is not GVAS and is not claimed by UnrealGvas.
This plugin depends only on the SDK and .NET, with no project-level parser reuse,
extra packages, native libraries, filesystem or network access.

The SaveExtension 1.4 source establishes tag/version/header, class/byte-array
pairs, and tagged object serialization. Atomic Heart has two observed extensions:
an algorithm FString replacing upstream's compression bool, and a SHA-1 trailer.
All numbers below were checked against the supplied bytes.

| Region | Layout / observed value |
| --- | --- |
| Header | LE int32 tag 1, save version 2, package version 522; engine 4.27.2, FString branch, custom-version format 3 and GUID/int32 version entries |
| SlotInfo | FString `/Script/SaveExtension.SlotInfo`; int32 byte count 1,061; payload at 1,192 |
| Metadata | UE tagged properties terminated by FString `None` and four zero bytes |
| SlotData | FString `/Script/SaveExtension.SlotData`; FString `Oodle`; int32 compressed byte-array length 123,892; payload at 2,302 |
| Chunk frames | 14 independently framed blocks, each six LE uint64 values: `9E2A83C1`, 131072, compressed size, plain size, repeated compressed size, repeated plain size; then compressed bytes |
| Trailer | Last 20 bytes: **SHA1(file[0..length-20])**, verified exactly |

Each observed compressed block begins `8C 06`. Thirteen blocks declare 131,072
plain bytes and the last declares 47,455, totaling 1,751,391 bytes. These are
**declared lengths**, not independently decompressed results. The `Oodle` label,
chunk lengths and signatures are verified; identifying the codec as Kraken from
`8C 06` remains inferred. No embedded encryption was found in the outer framing.

## Views, confidence and preservation

Overview shows engine, checksum and compression facts. Slot metadata expands
lazily in pages of at most 100 nodes. Its primitive fields are read-only:
`Filename=9`, `Version=2`, `ThumbnailId=2`, `SubLevel=Vavilov_Entrance`, and
`PlayedTime=SlotPlayedTime=34646201172` ticks. Timespan/DateTime values retain
their exact int64 100 ns tick representation. Native Transform payloads remain
opaque hex. Gameplay has a paged chunk table and bounded, paged original bytes.

Confidence.Verified refers to serialized names/types, not gameplay semantics.
No inferred inventory or player-state fields are presented. Open verifies the
full-file SHA-1, header, lengths, metadata termination, chunk size consistency,
and signatures. It does **not** decode or semantically validate Oodle records.
Serialize re-runs these checks and returns a fresh byte-identical copy; all edits
are rejected. SHA-1 is required for file compatibility, not authentication.

Limits: 128 MiB file/declared plain data, 4,096 custom versions, 1,024 chunks/metadata
properties, 1,048,576 FString code units. Unknown algorithm/header/signature
variants are rejected with SaveFormatException instead of guessed.

## Validation and open problems

14 tests pass with supplied samples, including the read-only conformance suite,
all primary/backup no-op round trips, known metadata and 14-frame layout,
opening under two seconds, the SHA-1 `abc` vector, every synthetic truncation and
single-byte corruption, invalid sizes with recomputed hashes, and bounded blobs.
Sample-dependent tests skip through SampleSaves when the archive is absent.
Empty metadata views and excessive chunk counts are covered. Verified with an
empty sample directory: 10 pass, 4 skip, none fail.

ReadOnly is intentional: the compressed SlotData records are not understood and
there is no managed Oodle decoder here. Safe metadata edits might be possible
with SHA-1 recomputation, but relationships to the compressed records have not
been established. A console reload has not been tested. Broader Atomic Heart
versions, other SaveExtension titles, and trailer variants are unsupported.

## Sources

* https://github.com/PipeRift/SaveExtension/blob/1.4/Source/SaveExtension/Private/FileAdapter.cpp — primary framing/object serializer reference, tag 1, version 2, Info/Data arrays. Tag 1.4 resolves to commit `c40f37a078ce8b9b2b3a811e47729e6ca2d11d48`; source read, never executed.
* https://github.com/PipeRift/SaveExtension/blob/1.4/Source/SaveExtension/Public/SlotInfo.h — upstream slot metadata definitions. Atomic Heart's extra properties are confirmed from its tags.
* https://github.com/PipeRift/SaveExtension/blob/main/Source/SaveExtension/Private/SEFileHelpers.cpp — modern framing surveyed; retains bool compression and differs from Atomic Heart. Upstream is Apache-2.0; no substantial code copied.
* https://www.rfc-editor.org/rfc/rfc3174 — SHA-1 and the standard `abc` test vector; hashing uses .NET's managed API.
* https://github.com/producedbytmsh/hl-savetools — comparative Unreal chunk / `8C 06` research, not proof of decoded Atomic Heart contents.

SHA-1 coverage, compression-name FString and exact sample offsets are independent
measurements, not claims made by upstream SaveExtension.

## SDK requests

Allow byte-identical download/export for CanEdit=false documents. The current UI
also disables export when editing is disabled. No SDK changes are needed to open
or inspect these saves.

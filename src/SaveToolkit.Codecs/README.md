# Shared save codecs (F11 / #28)

`SaveToolkit.Codecs` provides managed, synchronous, byte-oriented APIs without file/network I/O.
Its only native dependency is the SQLite `e_sqlite3` bundle that `Microsoft.Data.Sqlite` ships. Plugins may reference
this library alongside `SaveToolkit.Sdk`; no existing plugin or SDK interface changed.

## Kraken

```csharp
byte[] plain = OodleCodec.Decompress(compressedSpan, declaredOutputLength, maxBytes: 16 * 1024 * 1024);
byte[] stream = OodleCodec.EncodeStored(plain);
```

For a batch of independent streams, own the destination and reuse one workspace:

```csharp
using var workspace = new OodleWorkspace();
byte[] plain = new byte[totalOutputLength]; // Bound the aggregate length before allocating.
// Repeat for each frame, advancing outputOffset by declaredOutputLength:
OodleCodec.Decompress(compressedSpan, plain.AsSpan(outputOffset, declaredOutputLength),
    workspace, maxBytes: 16 * 1024 * 1024);
```

The destination span declares the exact output length and needs no padding. The workspace owns
pooled padded input/output staging buffers and lazily rented entropy scratch, retains them across
calls, and clears/returns them on disposal. Use one workspace per batch without concurrent or
reentrant calls; those calls and disposal during decoding are rejected. Dispose after the batch
to release retained peak buffer capacity. Each call resets active buffers (including padding) to
zero and creates a fresh work/nesting budget and stream state. Logical checked view extents stay
at the requested padded lengths even when pool rentals are larger. Decoding copies into the
destination only after complete validation, so failed calls leave it unchanged and overlapping
input/output is supported. The allocating overload delegates to this path with a temporary
workspace and returns its own exact-length array. Small stage tables still allocate per decode;
this API does not promise zero allocations or remove the final staging-to-destination copy.

The complete **Kraken (decoder 6)** path from the pinned GPL C# port is adapted here, including
raw, Huffman, RLE, tANS, recursive/multi-array entropy and delta/raw literals. The chosen source
does not implement Mermaid/Selkie, Leviathan, LZNA or Bitknit: those decoder IDs are rejected.
Quantum checksums and the newer excess-byte control form are also explicitly rejected, matching
the supported scope of the source without silently ignoring integrity. Errors use
`InvalidDataException`; invalid caller limits use `ArgumentOutOfRangeException`.

Both input and declared output are bounded before allocation: 64 MiB by default, maximum caller
limit 256 MiB; stored header overhead is allowed on input. Exact input consumption and output size
are required. Entropy/LZ decoding lazily prepares scratch of 0x6C000 bytes plus 64 bytes of owned
padding once per stream, reusing the workspace rental; stored and memset quanta do not rent it. Each byte/word access uses a
checked managed view. Entropy nesting is capped at 16; work is capped at the lesser of 256 million
operations and 1 million + 64 times the sum of input/output sizes. No unsafe code, native decoder,
reflection or static mutable decoder state is used. Typed table views currently require little endian.
Small, fixed format tables are allocated per stage; padding used by upstream word-copy loops
never becomes caller-visible output. Multi-array interval lengths are preflighted so both directions'
consumed bits fit in their declared byte span before padded word lookahead. Work/size bounds can
reject otherwise valid extreme streams.

`EncodeStored` emits Oodle block-level uncompressed Kraken streams (`CC 06`, at most 256 KiB per
block), rather than compressing. It round-trips through the decoder. Unreal's six-u64 framing,
container sizes/checksums and game-specific prefixes belong to consumers. This is format-valid
emission, **not PS5 reload validation**: no edited game save was written or tested on a console.
Preserve original compressed bytes for no-op plugin serialization. Corrupt bytes that still form a
valid stream cannot be detected without an independent enclosing checksum or semantic validation.

The code is composed by stage, rather than partial classes. Original algorithm names and bit-field
constants intentionally follow upstream for auditability. `BufferView<T>` implements only checked
managed byte offsets, typed accesses and slices; `Unsafe.SizeOf<T>()` obtains a struct's size without
unsafe code. The pinned source was read before adapting/executing it.

## SQLite images

```csharp
using SaveToolkit.Codecs.Sqlite;
using var database = SqliteImage.Open(databaseBytes, new SqliteLimits { MaxDatabaseBytes = 16 * 1024 * 1024 });
IReadOnlyList<string> tables = database.ListTables();
SqliteResult page = database.Query("SELECT * FROM items WHERE id=$id LIMIT 50",
    new Dictionary<string, object?> { ["$id"] = 1L });
database.Execute("UPDATE items SET quantity=$count WHERE id=$id",
    new Dictionary<string, object?> { ["$count"] = 42L, ["$id"] = 1L });
byte[] edited = database.Serialize();
```

`SqliteImage.Create()` also creates a new in-memory image. Instances own private unpooled
`Microsoft.Data.Sqlite` connections and must be disposed; use one per document, without concurrent
calls. All imported bytes go through `SQLitePCL.raw.sqlite3_deserialize(connection.Handle, ...)`.
SQLite uses a fixed-capacity `sqlite3_malloc64` buffer (no RESIZEABLE flag), freed after the connection
closes. New/imported databases have `max_page_count` set. Serialization uses
`sqlite3_serialize` and always frees its native copy. `PRAGMA integrity_check` must produce exactly
one `ok` row on open, after edits, and before serialization. WAL images are rejected because their
separate WAL is absent. Header/page alignment and image length are checked before deserializing.

SQL is trusted plugin code; use parameters for game/user values. No connection/handle escapes the
wrapper. Query permits reads only. Execute runs batches atomically and rolls back failures.
An authorizer denies ATTACH/DETACH, external PRAGMAs, extension loading, virtual tables, caller
transactions and non-main schemas. Temporary storage and journals remain in memory; extensions
are disabled and trusted_schema is off. Native APIs' internal schema/PRAGMA operations are allowed
only during private library calls. There are no temporary files, uploads or filesystem fallbacks.

Defaults: image 64 MiB (hard max 256 MiB), 1,000 result rows, 8 MiB materialized results, 1 MiB
per SQLite value/row or parameter, SQL 64 KiB, 10 million VM steps (progress checked every 1,000),
256 columns/parameters, expression depth 64, compound selects 32, trigger depth 16. Limits are
per instance/operation, not a process-wide memory quota; SQLite also allocates bounded-value
intermediates and its own page cache. Callers should page results and limit concurrent documents.
Image-opening failures use `InvalidDataException`, retaining native SQLite failures as inner exceptions.
SQL errors/interruption during operations use `SqliteException`; wrapper bounds/integrity errors use
`InvalidDataException`. This helper does not infer how two game database images relate or mirror them.
Each standalone `ValidateIntegrity()` call starts a fresh VM budget, including after an interrupted
query. Integrity checks inside opening, edits and serialization share the enclosing operation's budget.

## Evidence and tests

`tests/SaveToolkit.Codecs.Tests` includes stored/memset/RLE/Huffman vectors, stored round-trips
across quantum boundaries, 3,000 seeded malformed streams, recursive-depth rejection, 100 mutations
and systematic truncations of an actual compressed chunk, and SQLite edits/rollback/bounds/fuzz.

The correctness review for #40 adds every-prefix checks for the known stored/memset/RLE/Huffman
vectors, authored recursive/multi-array and raw/delta LZ vectors, 1,280 stage-targeted entropy
corruptions, independently inspected stored framing at 128/256 KiB boundaries, and complete stored
round trips of the decoded sample payloads below. Multi-array tests cover nonzero asymmetric bit
streams in combined/separate index modes, plus truncations with repaired enclosing sizes. SQLite
tests cover repeated/interrupted-operation integrity checks, empty-image round trips, native SQL
limits, UTF-8/result/parameter limits, created/imported image growth and rollback, every incomplete
header prefix and page-boundary truncations. These are bounded deterministic cases, not an
exhaustive malformed-stream proof or a proprietary-decoder cross-check.

Allocation regressions use synchronous `GC.GetAllocatedBytesForCurrentThread` deltas after warmup,
with input, destination and workspace setup outside the measured interval. Stored and RLE streams
must stay under 4 KiB of overhead per decode; the allocating stored API allows its result array
plus 4 KiB. The 14 AtomicHeart and 35/33 Hogwarts frame batches allow at most 64 KiB per frame
for the remaining small stage tables, excluding caller-owned output and retained pooled buffers.
There are no wall-clock assertions. Tests also pin buffer extents after casts/cursor arithmetic,
fresh zeroed buffers/budgets, destination guards/overlap, failure recovery and workspace lifecycle.

The #40 performance review measured the 14 AtomicHeart streams on Windows/.NET 10.0.11,
SDK 10.0.400, Release with tiered compilation disabled, five warmups and 21 synchronous samples.
Median allocated bytes fell from **10,467,272** at baseline `6866a37` to **2,386,576** through the
existing allocating API, or **632,416** with a reused workspace and caller-owned aggregate output
(about 94% less than baseline). Input ZIP reads, frame parsing, aggregate output allocation and
workspace setup/warmup are excluded; this is managed allocation, not retained heap or native memory.
The workspace retains peak pool rental capacities until disposal, which can exceed logical padded
lengths; small entropy-stage tables and the final output copy remain. Clearing reused/returned
buffers also costs work. No latency or complete plugin Open speedup is claimed. The reproduction
harness and before/after logs are retained at `D:/Temp/saveeditor/review-results/perf-codecs/`.

Read-only ZIP tests use **only** `SAVETOOLKIT_ARTIFACTS` and skip if samples are absent:

- AtomicHeart `PPSA10695`, `ue4savegame.dpx.sav`: framing begins at 2,302; 14 chunks decode to
  1,751,391 bytes, ending before the 20-byte SHA-1 trailer.
- Hogwarts `PPSA01603`, `sdimg_HL0010/ue4savegame.ps5.sav`: 35 + 33 chunks decode to
  4,534,280 + 4,231,176 bytes. The two little-endian length prefixes precede SQLite at offset 8.
  Both databases pass independent SQLite integrity checking, list tables including
  `MiscDataDynamic`, read a row, and serialize/reopen. Their relationship is not assumed.

These lengths/framing agree with the independent research in [AtomicHeart](../../docs/plans/AtomicHeart.md),
[UnrealGvas](../../docs/plans/UnrealGvas.md) and [SAMPLES](../../docs/SAMPLES.md). No private sample bytes
or decoded sample data are checked in. No independent proprietary decoder was run; SQLite's B-tree
integrity validation is the additional independent oracle for the decoded Hogwarts databases.

## Sources and licences

Retrieved 2026-10-28. See root [LICENSE](../../LICENSE), [THIRD-PARTY-NOTICES](../../THIRD-PARTY-NOTICES).

- [TekkenMusicModManager Kraken.cs, 8737709](https://github.com/AaryanKh/TekkenMusicModManager/blob/8737709e840c74efc935e23ac0d33cdb6aaef8f5/src/Tmm.Core/Pak/Kraken.cs), GPL-3.0-or-later: ported and hardened as described above.
- [Powzix ooz kraken.cpp, 0503806](https://github.com/powzix/ooz/blob/05038060aa68f9187ae9923b2388ca8db40e58d1/kraken.cpp), GPL-3.0-or-later: original decoder/layout reference.
- [Microsoft.Data.Sqlite interoperability](https://learn.microsoft.com/dotnet/standard/data/sqlite/interop) and [bundled versions](https://learn.microsoft.com/dotnet/standard/data/sqlite/custom-versions): connection handle/raw API and e_sqlite3 bundle.
- [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw), Apache-2.0: raw API, native bundle; dependency unchanged.
- SQLite [deserialize](https://sqlite.org/c3ref/deserialize.html), [serialize](https://sqlite.org/c3ref/serialize.html), [authorization](https://sqlite.org/c3ref/set_authorizer.html), [limits](https://sqlite.org/c3ref/limit.html): native memory/ownership and SQL bounds.

The repository is GPL-3.0-or-later. The imported GPL source, MIT Microsoft.Data.Sqlite,
Apache-2.0 SQLitePCLRaw and public-domain SQLite are compatible with GPLv3. GPL-2.0-only would
be incompatible with this combination; none was imported. Third-party game-data rights remain separate.

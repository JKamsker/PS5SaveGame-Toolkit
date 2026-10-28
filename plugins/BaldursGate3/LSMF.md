# LSMF directory foundation and live-storage extension

The original 0.3.0 foundation below is an independently implemented **directory and EntityId reader**, not a
complete live gameplay decoder. The plugin extracts only an attribute named
`NewAge`, of typed `ScratchBuffer`, under a `NewAge` LSF node. It does not scan
compressed package bytes for a coincidental magic string. Unsupported/corrupt
optional NewAge inspection produces a warning and leaves the raw explorer and
exact no-op export available.

Version 0.6.0 extends this foundation with independently measured PS5 live storage, fixed-width writers, F5/F6 inventory actions and F9 spell presence graphs. [NEWAGE.md](NEWAGE.md) supersedes the historical exclusions below and records current bounds, unsupported joins and exact owner checks.

## Implementation provenance

Research lead: DanFinchUK/bg3-savefile-parser,
[`9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/FORMAT.md`](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/FORMAT.md),
retrieved 2026-10-28. That project's code licence was not established. **No parser
implementation, algorithmic solver or downloaded executable was copied or run.**
Only documented format facts were compared with actual PS5 bytes. New C# code was
written independently; existing MIT-attributed LSLib UUID decoding is reused.

Independent verification used a locally authored Python reader in the external
scratch directory, inspected before use and run with `python -I`. It independently
decodes the LSPK file list, zstd payload, LSF LZ4 sections and typed attribute.
Neither that script nor extracted sample bytes are committed. The committed
synthetic vectors are handwritten, not downloaded or extracted save fragments.
The automated `SampleLsmfTests` compares the C# result with the independent census
and fingerprint below. This is cross-implementation structural validation of one
snapshot, not console validation of gameplay semantics.

## Supported measured layout

All integers are little-endian. Only prefix `4c534d4601010008` is supported.

| Header offset | Interpretation in this sample |
| --- | --- |
| 0–7 | Signature/version prefix |
| 8–15 | Unknown opaque value; never labelled a checksum or edited |
| 16 | u64 arena-relative directory pointer; absolute directory = value + 48 |
| 24 | u64 directory-region byte size, **including descriptor table**, not merely names |
| 32 | u32 descriptor-table offset relative to the directory |
| 36 | u16 dynamic descriptor count |
| 38–47 | Preserved opaque header bytes |

Each 48-byte descriptor stores `(u64 nameOffset, u64 nameLength, u64 typeHash,
u32 elementBytes, u32 flags, u64 nominalRows, u64 arenaRelativeDataOffset)`.
Names occupy the region before the descriptor table; they are length-delimited
UTF-8, with no assumed separator. The actual nominal data range starts at
`48 + arenaRelativeDataOffset`. The observed first EntityId descriptor's offset
is 40, so its first GUID starts at absolute byte 88. The upstream prose's claim
that this descriptor offset is absolute is not used for this PS5 snapshot.

The supplied snapshot has absolute directory offset 2,592,720, directory size
26,216, descriptor relative offset 11,096 and 315 descriptors. Thus the descriptor
table consumes the final 15,120 directory bytes. Fixed 355-entry readers and
readers treating all 26,216 bytes as names would be incorrect here.

| Component | Nominal element bytes | Nominal rows |
| --- | ---: | ---: |
| `core.v0.EntityId` | 16 | 9,453 |
| `game.stats.v3.StatsComponent` | 36 | 256 |
| `game.stats.v0.HealthComponent` | 32 | 380 |
| `game.inventory.v0.ContainerSlotData` | 16 | 638 |

EntityId contains 1,984 distinct GUIDs, 1,460 repeated GUID groups and up to 99
occurrences of one GUID. Occurrence keys include the GUID plus occurrence ordinal;
the view also shows the source row. Duplicate GUIDs remain separate. Neither a
GUID match nor the row order establishes a unique body/stats/spell/party identity.

Whole NewAge size: 2,618,936 bytes. SHA-256:
`fb308dc5c5af0e97e11c41e8212677e43a33c4350627b889ebf27df2c71708cc`.
The UI exposes the whole-buffer fingerprint in addition to per-component nominal
fingerprints. This accounts for unknown bytes outside nominal component ranges;
it is not evidence of an in-game integrity mechanism.

## Bounds, preservation and deliberate exclusions

- Blob ≤64 MiB; ≤4,096 descriptors; ≤512 UTF-8 bytes/name; ≤1 MiB/nominal element;
  ≤1,000,000 nominal rows; EntityId inspection ≤100,000 occurrences.
- All header, name and nominal ranges are checked before slicing/allocation.
  Directory/table alignment, invalid UTF-8, duplicate names, nonempty zero-width
  arrays, arithmetic overflow and truncation are rejected by the reader.
- `ArenaRange` accepts an explicitly arena-relative pointer. `EntityRow` accepts
  an explicitly absolute EntityId pointer and rejects interior/sentinel pointers.
  These are bounded primitives, not a guessed universal pointer classifier.
- Unknown flags, type hashes, component versions, arena bytes, header bytes,
  directory padding and trailing bytes remain unchanged in the original buffer.
- Views are lazy and hierarchically paged, with at most 128 rows or page links
  per expansion. Initial view creation does not decode EntityId or build item rows.
- In the original 0.3.0 subset, ownerlist-region discovery, packed Stats/Health streams, archetype shifts,
  heaps/strings/enums, Creator→Entity joins, stack groups, nested containment,
  cycles and gameplay fields were **not interpreted**. Untrusted pointer graphs
  are not traversed. No generic descriptor-row or ownerlist-position rule is used.
- In 0.3.0 NewAge had no mutation path. The 0.6.0 internal path is documented in NEWAGE.md;
  exact no-op and primitive edit/undo preservation tests still pass.

The F1 `PluginViewDecoder` can consume these bound read-only fields without a new
runtime dependency. Component paths use component names; entity paths use GUID
occurrence provenance. Whole-buffer changes remain visible even if every nominal
fingerprint stays unchanged. Duplicate occurrence ordinals are provenance within
a snapshot, not proof of stable cross-generation identity for repeated entities.

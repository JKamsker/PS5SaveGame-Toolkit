# Malformed-input correctness review (#40)

Branch: `review/robust-witcher3`. Review date: 2026-10-29. Started from the merged performance and
consistency work. This review concerns correctness and bounded parsing, not security. All changes
are inside `plugins/Witcher3/`; no samples were modified on disk or committed.

## Findings and fixes

| Severity | Finding | Reproduction and fix |
| --- | --- | --- |
| medium | Expanded output allocated before LZ4 rejection | A two-byte invalid block declaring 32 MiB allocated 33,555,048 managed bytes. Preflight every compressed chunk before allocating the combined output. Two allocation regressions cover the first and a later malformed chunk. |
| medium | Indexed children bypassed SS validation | A child index suppressed both the SS length check and embedded parsing, accepting corrupt SS/BLCK lengths. Reject indexed children in the documented separate SS stream and always validate its length. |
| medium | Index spans overlapped directory metadata | Entries pointing into RB, NM or MANU interiors, or crossing RB, opened as opaque game variables. Bound variable data before RB and permit only the complete MANU span as the metadata exception. |
| medium | Indexed BLCK/PORP/AVAL lengths ignored | Three indexed tokens declaring five bytes in a four-byte span opened successfully. Validate all recognized length-bearing tokens against their complete spans during shared node creation. |
| low | Embedded depth reset after indexed ancestry | Three indexed/embedded splits accepted depth 129. Carry ancestry into embedded parsing; total depth 128 opens and 129 rejects, including an empty archive at the boundary. |

The original eleven regressions all failed before the first fixes; three additional indexed-token
regressions failed before their fix. No confirmed findings are deferred. No high findings were found.

## Bounds and compatibility

The supported limits remain 128 MiB stored input, 64 MiB expanded payload, 4,096 chunks,
200,000 indexed/explorer nodes, 128 total tree levels and 65,535 names. Length accumulators check
remaining output before addition. LZ4 references must refer to earlier output; terminal sequences
obey the upstream block rules. Validation scans encoded sequences without output allocation or
decompression; K4os still performs actual decoding and checks its returned size. There is an extra
linear scan of compressed bytes; this review makes no latency improvement claim.

Chunk-table validation finishes before stream preflight, and every stream is checked before the
payload allocation. Indexed traversal is iterative and bounded by entry count/depth. Embedded
parsing advances by a positive recognized header size or consumes the entire unknown remainder.
Name counts and byte lengths remain bounded and printable ASCII is required. Known malformed
framing, ranges and lengths throw the documented `SaveFormatException` with a nonempty diagnostic;
no blanket catch converts indexing/null-reference failures into format errors.

The supplied files contain **SAV3**, a one-based **MANU** dictionary referenced by CName values,
and **SS/SXAP** archives. The requested CNAMES/SAVY areas were reviewed through these actual
structures; no unsupported signature or new format variant is claimed.

Opaque reserved header bytes, raw-storage chunks, either chunk-end convention, partial/absent opaque
container trailers, unknown bounded tokens and unsupported scalar encodings remain compatible.
Independent optional ability/Gwent/recipe/facts/journal/map readers retain their documented warning
fallback. Arbitrary byte changes cannot all be detected: no researched checksum exists here and
unknown bytes deliberately remain opaque. Experimental maturity, console/dependency warnings and
write scopes are unchanged. Existing write/reopen/undo and byte-identical no-op tests pass.

## Verification

- Baseline Release plugin suite with external samples: **139 passed / 0 failed / 0 skipped**.
- Final Release suite: **234 passed / 0 failed / 0 skipped**, including **95 added cases**.
- New synthetic cases with `SAVETOOLKIT_ARTIFACTS` unset: **93 passed / 0 failed / 0 skipped**.
- Full Release API build with `--warnaserror`: **0 warnings / 0 errors**.
- New synthetic coverage: raw/extended/overlapping LZ4 vectors and malformed sequences; allocation
  rejection; every descriptor field; every physical prefix before full chunk data; MANU counts,
  lengths, ASCII and terminators; virtual pointers; index spans; every recognized embedded header
  truncation; nested lengths; total depth; node budget; progress and opaque compatibility controls.
- Two seeded mutation cases exercise **512** independent stored/expanded synthetic mutations.
  Each either rejects with a clear format error or opens/builds its view/collections and preserves
  the full mutated input on no-op serialization.
- Two sample cases exercise all **eight** save/backup records: **112 physical prefixes** and
  **176 structural mutations** across directories, names, tokens and references. All changes are
  in-memory copies, independently wrapped as raw chunks when expanded bytes are mutated.
- Existing conformance, sample byte identity, writes, undo/history, planned actions and performance
  allocation regressions are included in the final suite.

TRX reports and API build output are kept outside the source tree on D: during the review.

## Research and attribution

Rechecked the already credited [Atvaark layout at cff9051](https://github.com/Atvaark/W3SavegameEditor/tree/cff905189d565606aed9058b2804eb4c5ee9f540/W3SavegameEditor.Core),
including MANU strings, variable parsing and chunk framing; existing MIT notice is retained.
The new validation helper is independently written from Yann Collet's
[LZ4 block specification at 3dad297](https://github.com/lz4/lz4/blob/3dad297255098df1a8b0111402bfbdbd2cd0161f/doc/lz4_Block_format.md),
consulted 2026-10-29. No downloaded implementation was executed or copied, and no specification
prose was copied. The existing K4os package remains the decoder. Format sources are also linked
from the plugin README and `Info.References`.

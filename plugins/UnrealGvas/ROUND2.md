# Jedi, Sackboy and generic GVAS — round 2

Evidence date: 2026-10-28. Issue [#24](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/24).
Hogwarts implementation and its owner gates remain in [HOGWARTS.md](HOGWARTS.md).

## Delivered views and identities

The non-Hogwarts document composes the original lossless scalar editor with F8
read-only collections. No original value is copied into a second editable binding.
The all-properties collection searches descendants without expanding lazy sections;
rows expose serialized paths, storage types, current scalar/payload summaries,
evidence, descriptions and complete attribution/external-source links. Text cells
are shortened for the response budget; queries still search the complete scalar,
raw property name, and retained bounded path. Filters are exact, case-insensitive
text comparisons. Sorting uses ordinal text, with ordinal identity as a stable
tie-break; numeric columns are deliberately not represented as gameplay numbers.

Canonical raw row keys are `property/<ordinal>/...`, matching the existing raw
bindings. Duplicate names keep separate rows and schema references; no last-name
match silently chooses a value. Keys survive scalar length changes, reopen and
undo, but are not suitable for structural changes or cross-generation identity.
Actor keys use their own `actors/` and `actor-strings/` namespaces, are read-only,
and do not pretend to be editable outer-GVAS bindings or persistent actor GUIDs.

Named groups select exact observed container names, not similarity:

- Sackboy: Progression, Costumes and emotes, World and levels, Settings.
- Jedi gameplay: Journey state and Actors, with tagged properties and string
  observations in separate collections.
- Jedi settings: Display/input/saved overrides; existing raw scalar edits retain
  their Experimental status, with no new gameplay-safe limits or enum claims.
- Other GVAS: all nested properties, plus the existing lazy tree/primitive editor.

Title captions require the observed save class and UE/package version. Unmatched
versions retain the generic view and existing integrity guards. Hogwarts keeps
its own query interface, fallback behavior, codec/database ownership and guards.

## Jedi decoding correction and bounds

The decoded streams begin with the FString for `ActorRecords`: its leading Int32
is the string length, **not** an outer array count. Round 1 skipped that length,
causing a fallback scan. Both supplied primary/backup streams now parse from byte
zero with the existing headerless parser, exposing more than 1,000 tagged nodes
each. This is serialized storage proof, not identification of currency/skills,
actor roles, prerequisites, or game effects.

Inflation stops at the declared byte length with a one-byte overrun probe; the
128 MiB decoded limit, exact outer byte-array count, decoded length, and zlib
Adler trailer are checked. A malformed stream disables the actor overlay while
preserving its outer save bytes and read-only guard. Unsupported custom structs
stay opaque; any fallback tag scan is labelled Inferred. Its node budget is shared
across recursion. A separate scan retains at most 2,000 printable ASCII FString
observations, including duplicates at distinct offsets, with no property-boundary
or gameplay interpretation. Other encodings/late observations may be absent.

The Schema actor summary is limited to 100 rows; large lists use F8, capped at 200
rows per request. Wide-cell and real-sample tests verify the host's 2 MiB response
budget. Raw values and undecoded bytes remain available in the original tree/hex.

## Checksum evidence and coverage

`IntegrityCoverageTests` reproduces a matrix of named ranges, default alternatives
and derived-seed CRC hypotheses. It writes result-only JSON to TEMP, without save
contents. Each title contributes **two distinct payloads**; archive twins add no
independent evidence and these snapshots are not controlled before/after pairs.

Sackboy General stores **2E0726BB in both distinct snapshots**, even though both
currency fields are 445 in one and 322 in the other, and many other bytes differ.
This neither proves that the field is dispensable nor establishes what it covers.
A schema/version fingerprint or another serialization path remains possible.

The committed matrix uses 12 Sackboy ranges (11 changing) and 25 Jedi ranges
(19 changing). It covers original/zeroed/omitted-value/omitted-tag files and
headerless property streams, bytes before/after the value, ordered top-level
payload concatenation, and individual root payloads. Jedi adds zlib, raw deflate,
decoded bytes, decoded bytes excluding the first Int32 (an explicit alternative,
not the actual parse start), and a decoded-array substitution. Tag starts are
located from serialized name/type pairs, not magic sample offsets. Substitution
is a research buffer, not a valid writer or a recompressed save.

For each common range the matrix checks:

- FNV-1a and Adler-32 with their standard parameters.
- Reflected and normal IEEE/Castagnoli CRCs, initial states 0/FFFFFFFF,
  XOR-out 0/FFFFFFFF, and ordinary/byte-swapped stored result interpretations.
- An arbitrary initial CRC state derived by reversing the first capture, then
  applied unchanged to the second capture for each fixed output convention.

There are 192 Sackboy and 400 Jedi derived-seed hypotheses. No **changing-input**
range fits both captures. Sixteen Sackboy hypotheses fit the invariant trailing
range and unchanged stored field by construction; they are explicitly
inconclusive. None of the Jedi hypotheses fit both captures. This does not rule
out other coverage, polynomial/reflection combinations, serialization, nonconstant
seeds, an arbitrary unknown XOR-out, or a different algorithm.

Standard checks use `123456789`: IEEE CRC32 CBF43926, CRC32C E3069283,
CRC32/MPEG-2 0376E6E7, FNV-1a BB86B11C and Adler-32 091E01DE. Reverse-state tests
include empty/all-byte inputs and arbitrary initial states. An independent scratch
Python `-I` probe recomputed zeroed-file vectors for all four distinct saves using
standard-library zlib/FNV and bit-step CRC reversal; results agree with C#'s
byte-table inversion. No downloaded implementation was executed. Reference
definitions: [CRC RevEng](https://reveng.sourceforge.io/crc-catalogue/17plus.htm),
[RFC 1950](https://www.rfc-editor.org/rfc/rfc1950).

Existing Jedi gameplay and Sackboy scalar writers are enabled with a warning;
explicit `Crc32`/`Checksum` fields and opaque actor/image payloads remain read-only.
Corruption tests reject actor decoding after trailer/count/declared-size damage;
they do **not** claim to validate the unresolved gameplay checksum. Original-file
passthrough still works for inspection.

## Catalogs and rights

`unrealgvas.ps5` v1.1.0 contains 24 Sackboy field captions, 12 Jedi actor/journey
field captions, six Jedi settings captions and the four existing image-metadata
entries. Captions describe observed serialized names. Their apparent meanings are
Inferred, with no asset IDs, legal caps, quest prerequisites or saved-ID-to-wiki
joins invented. The image dataset remains unchanged.

`unrealgvas.storage` v1.0.0 adds 17 generic serialized-type descriptions based on
pinned MIT uesave `1917a22ab69e9a4045e613d9d7f39a49fe00bd46`, with its complete MIT
notice retained. Descriptions are original prose; no implementation was ported.
Both packs have normalized-file hashes, dataset attribution, source sidecars,
Info.References, per-row/field provenance and host Sources-panel registration.

Imagisphere, PowerPyx, SackboyWorldUnlocker and the GingerBread reflected scaffold
remain research/external links where reuse rights or saved-ID joins are unclear.
No wiki text, datamine content, screenshots, icons or game assets were imported.
No new CC BY-SA adaptation was made, so no unpinned wiki revision is bundled.
See the README attribution table and adjacent resource source files.

## Writes and F6 gate

No new structural operations, presets or bulk actions are enabled. Existing scalar
writes retain byte-identical no-op and undo/reopen behavior but do not prove game
effects or checksum repair. Once a future bulk action has verified targets, bounds,
dependencies, integrity coverage and console evidence, it must implement
`IActionPlanningDocument` with
`ActionNode.SupportsPlanning`, use F6 preview/apply and F8 query scopes for
select-all, expose all derived changes, and undo as one atomic step. There is no
plugin-local preview/apply substitute or direct bulk-action bypass.

## Exact owner capture / validation checklist

1. Record title/region, game build, DLC, slot and visible values/action notes for
   every capture. Keep existing captures. Preserve all companions and independent
   primary/backup files; do not mirror different states because of their names.
2. For **each** Jedi and Sackboy slot, capture an untouched export, a load/re-save
   with no intentional progression change, and a second unchanged re-save. At
   least three states are needed to distinguish constant metadata from changing
   integrity and nondeterministic churn.
3. Sackboy: make separate before/after pairs for earning an exact Collectabells
   amount, spending an exact amount on one named costume piece, equipping that
   piece/tint, binding one emote, entering/unlocking one world, and collecting one
   identified orb/prize. Record both displayed current and lifetime currency
   where available. Retain General, image/thumbnail and every related companion.
4. Specifically compare Checksum after each Sackboy event and across different
   game builds/new-slot creation. Its identical 2E0726BB value in the supplied
   snapshots must not be treated as proof of a safe bypass.
5. Jedi: separate before/after pairs for gaining an exact XP amount, spending one
   skill point, collecting one identified chest/stim, changing one cosmetic,
   meditating, and moving to one named planet/area. Retain primary and Backup00
   independently and note which meditation/save event updated each.
6. For game-ID catalogs, supply version-matched, rights-cleared internal IDs and
   labels or a user-owned extraction pack. Match exact path/type/identity in the
   saves; ambiguous rows stay raw. Record prerequisites, ownership and effective
   limits separately from integer storage widths.
7. Sackboy photo work still needs one captured photo plus the matching in-game
   screenshot, dimensions/orientation/colour notes, and its image-set companion.
   Pixel format/swizzle/channel order must be proven before a preview is claimed.
8. Before any future write, reproduce integrity across all unchanged captures
   and controlled pairs using an independent implementation, prove coverage by
   targeted mutations, validate a byte-identical no-op and exact undo/reopen.
   Unknown build/coverage retains full-document read-only mode.
9. On a backed-up spare slot, validate unchanged reconstruction first; then one
   authorized single edit at a time: import, load, observe intended value and
   side effects, save in game, exit, reload again, verify, re-export/revalidate and
   restore if needed. Only the owner can mark a capability Stable.
10. Any later bulk preset additionally needs F6 preview of every target/derived
    change, explicit skipped targets, atomic rejection and one-step exact undo;
    large selections must cover the complete F8 query scope.

No new required SDK contract was identified. The existing optional request for
non-mutating F4 catalog browsing on read-only fields remains applicable. A future
canonical field-reference node would let a curated view navigate directly to the
existing raw binding; this round exposes the exact identity as read-only text.

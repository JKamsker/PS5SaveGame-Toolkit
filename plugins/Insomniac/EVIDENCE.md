# Read-only roadmap evidence (#20)

Validated 2026-10-28 against the private PPSA01474 archive. No private save or
extracted payload is committed. Historical integrity research remains in
[RESEARCH.md](RESEARCH.md); this document records the new structural observations.

## Corpus and DDL

- All 76 game records retain byte-identical no-op serialization: 36 primary gameplay
  records, two preferences and 38 identical backup twins. Duplicate ZIP paths remain
  distinct generations. The two campaign IDs are used only inside private comparisons.
- Twelve gameplay DDL roots and the preference root are decoded recursively. Segment
  roots have a four-byte prefix; objectives have eight. Prefix bytes are preserved.
- Names/hash words, string bounds/UTF-8, stored numeric types, object sizes, array
  counts and parent bounds are checked. Nested map keys are actual stored field names,
  including hexadecimal identities. No schema ordinal is substituted for a key.
- An unaligned nested object's descriptor/value boundary aligns to four bytes in
  the file; scalar fields then remain packed. The activity `DerivedSaveData` is a
  concrete example. Synthetic tests cover this independently of sample bytes.
- Reader budgets per section: depth 32, 32,768 objects, 65,536 fields, 524,288 values,
  four MiB decoded text, 1,024 fields per object and 65,536 bytes per string. The
  largest fog section needs more than the old 4,096-value root budget. Views expand
  arrays in pages of 100 and keep raw bytes available.
- The representative late save has 15 map-area keys, 47 award records and ten
  objective graph keys. Map dimensions/bounds are stored observations. Set-bit counts
  include padding bits and are not an exploration percentage. Award codenames are
  neither mission labels nor proof of platform trophy status.

## Inventory framing

The `00D0A9DF` section is traversed sequentially, not split at marker occurrences:

1. LE u32 container tag and LE u32 declared payload length.
2. LE u32 `0x494E5654` (`TVNI` bytes), followed by LE u32 record count.
3. Exactly that many records: LE u32 schema type, LE u32 payload length, bounded bytes.
4. Remaining container state is retained opaque. Two observed tag families have a
   separate four-byte zero suffix outside their declared payload length.

Every boundary reaches EOF exactly across all 36 primary gameplay records. There
are 27 containers in each file, not 27 weapons. Container tags repeat; UI identities
use tag plus occurrence. Containers are not assigned to Ratchet, Rivet, equipped,
owned or shared state without controlled evidence.

| Container tag | Opaque bytes inside declared payload | External suffix |
| --- | ---: | ---: |
| `78CB152E` | 4 | 0 |
| `62D731C8` | 218 | 0 |
| `37475015` | 0 | 4 zero bytes |
| `272E811F` | 0 | 0 |
| `C7D08B36` | 23, 298 or 309 | 0 |
| `31D06004` | 0 | 0 |
| `4BF3942C` | 0 | 4 zero bytes |

The tail sizes are observations, not parser constants or edit rules. Unknown record
types and unfamiliar presence masks retain bounded opaque payloads.

## Item type and field candidates

All five record type IDs match IDs and seeded name hashes in Rivet's CC0 schema:

| Stored type | Schema name | Measured payload | Prefix bytes |
| --- | --- | ---: | --- |
| `4E5CE3CB` | GameItemPrius | 23 | `0F` |
| `388B19BC` | GadgetSwingshotItemPrius | 23 | `0F` |
| `A89E9C3C` | WeaponItemPrius | 119 | `FF 3F` |
| `2F1E071F` | MeleeWeaponItemPrius | 119 | `FF 3F` |
| `F6AD1389` | VanityItemPrius | 460 | `FF 1F` |

Matching schema fields suggest presence bits, eight-byte GUID/config tokens, LE u32
item count and type-specific state. Tokens are displayed in stored byte order;
numeric asset-ID normalization and config-path hashing are not established.
Instance keys use the exact GUID candidate plus duplicate occurrence within a
container. Unknown layouts use an explicitly positional opaque key.

For the fixed 119-byte weapon layout, payload offsets 24/28/32 give BE u32 XP and
BE f32 checkpoint/loaded-ammo candidates. These differ from LE framing/counts.
The fields match schema order and vary plausibly across saves; values alone are not
HUD confirmation. All remain **Inferred**, with no level conversion, cap, refill,
purchase or ownership behavior. Non-finite candidate values suppress interpretation
and retain the record instead of rejecting an unproven semantic layout.

## F1 differential checks

`RiftEvidenceDecoder` in the test project is an F1 adapter. It emits
campaign counters, stored wire kinds, inventory identities, payload fingerprints,
state candidates and world collection sizes. It omits account/system metadata and
avoids materializing every fog byte as a semantic value.

- `DifferentialEvidenceTests.SameCampaignSnapshotsKeepIdentityWhileWeaponStateCandidatesChange`
  compares snapshot `108976056` with the late autosave after first checking campaign
  equality. Career bolts change 363925 → 977095; at least ten weapon candidate paths
  match by identity, and XP/ammo candidates change. Both trees and the diff are complete.
- The generation test uses F1 `SaveSetDiffer` on ZIP generations 0 → 1 of the autosave,
  observes 977095 → 23444 career bolts and collapses only byte-identical backup twins.
  This second comparison spans generations and is not claimed to isolate one action.
- Neither comparison is a one-variable experiment. They establish framing, persistent
  identity candidates and changing state, not item names, legal bounds or causality.

Run the tests using the README command with `SAVETOOLKIT_ARTIFACTS` set in the same
shell invocation. Fixture expectations contain gameplay facts only; no save bytes,
account IDs or archive contents are stored in the repository.

## Source and confidence boundary

Schema facts: [Neptuwunium / Rivet, a631c96e](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json),
[CC0 sidecar](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json.license).
Section strategies were cross-checked with the pinned
[SaveData research](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet/Data/SaveData.cs).
No upstream implementation was copied or executed. New inventory framing and mixed
byte-order candidates were independently tested against the PS5 corpus.

Complete integrity coverage, config/localization joins, ownership, legal limits,
collectible locations and console acceptance remain open. There are no writable
bindings or mutation actions, and maturity remains ReadOnly.

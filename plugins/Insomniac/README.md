# Ratchet & Clank: Rift Apart plugin (`insomniac`)

This plugin targets **PPSA01474** (Ratchet & Clank: Rift Apart) and opens the decrypted
`i29-s.save` gameplay file and `i29-userprefs.save` preferences file. Version 0.1.0 is
**ReadOnly**: it is an evidence-preserving DAT1 explorer, not a gameplay editor. The plugin does
not read files, use network access, or depend on the Core/API projects.

## What is implemented

The reader validates and preserves the complete DAT1 envelope:

| Offset | Encoding | Meaning | Confidence |
| ---: | --- | --- | --- |
| `0x00` | `u32 LE` | Magic `0x44415431` (`1TAD`) | Verified in all 76 supplied records |
| `0x04` | `u32 LE` | Header word `8` | Verified in all 76 records; semantic name unknown |
| `0x08` | `u32 LE` | Total file size | Verified equal to EOF in all records |
| `0x0C` | `u16 LE` | Section descriptor count | Verified (`19` gameplay, `1` preferences) |
| `0x0E` | `u16 LE` | Auxiliary-record count | Verified (`0` in the supplied records) |
| `0x10` | `u32,u32,u32 LE` | Section tag, absolute offset, byte size | Verified by bounds checks |
| after descriptors | `8 * auxiliary count` | Auxiliary records, retained opaque | Unknown |

The parser rejects a wrong magic or header word, a size/count overflow, a truncated descriptor
table, out-of-range descriptors, and overlapping sections. It retains descriptor order, all bytes
between the descriptor table and the first section, section padding, section order, and every
unknown byte. `Serialize()` reparses its own output before returning it; no-op output is byte
identical. Hex pages are copies, so mutating a returned page cannot mutate the document.

The view has an overview tab with validated header fields, a section descriptor table, lazy per-
section explorers (hex plus bounded printable-ASCII hints), and a paged raw-file hex view. Tags are
shown as raw `0x........` values. Printable runs are deliberately labelled untyped; they are not
treated as field names or editable values.

## Research status

[ALERT](https://github.com/Tkachov/ALERT/blob/main/dat1lib/types/dat1.py) documents the same DAT1
header, descriptor triplets, auxiliary records, string-table region and offset-preserving section
layout for Insomniac assets. Its [format wiki](https://github.com/Tkachov/ALERT/wiki/Formats) says
DAT1 sections are arbitrary per-tag payloads and describes DDL as a separate serialized-data
format. ALERT is GPL-3.0; this plugin independently implements only the small framing reader and
does not copy code. [RiftRipper](https://github.com/VELD-Dev/riftripper) is a C# level editor for
Rift Apart and related Insomniac games, but its compatibility list marks PS5 support as unknown and
does not document these save sections. Its code was read as reference only.

The supplied PS5 corpus contains 36 gameplay paths plus two preference records and matching backup
twins. Gameplay section tags are stable across snapshots, while offsets and sizes move as progress
changes. The data contains readable DDL-like names such as `OBJ_ZURK_ARENA_COMPLETE_SILVER_CUP`,
`Platinum_Trophy`, and `configs/hero/hero_clankpack.config`; no source or hash-name table maps those
strings to a save schema. Attempts to match tags to common CRC32/FNV name hashes did not establish a
unique mapping. No checksum, hash, signature, compression marker, encryption layer, or stable scalar
offset was proven by the snapshots.

Public cheat listings for this title mention candidate concepts such as career bolts, raritanium,
weapon XP, health/ammunition flags, and challenge mode ([NinjaSaves](https://ninjasaves.com/cheats/ratchet-clank-rift-apart-ppsa01474)).
That is evidence that such game concepts exist, not evidence of their binary offsets, types, ranges,
or integrity dependencies. They are intentionally not exposed as fields. A controlled save pair or a
licensed save parser is required before enabling edits.

## Unknowns and open work

* Identify the 19 gameplay section schemas and the one preference schema, including DDL type tables.
* Determine whether any section or outer file has a checksum/signature and its exact coverage.
* Prove meanings and ranges for bolts, raritanium, play time, difficulty, inventory, and unlock flags.
* Establish whether PS5 saves use the same DDL versions as PC assets and validate an edited file in-game.
* Add fixed-size, integrity-aware edits only after those questions are answered. Until then the
  maturity must remain `ReadOnly`.

## SDK requests

None. The existing SDK supports the bounded lazy section explorer and read-only hex blobs required
for this research-stage plugin.

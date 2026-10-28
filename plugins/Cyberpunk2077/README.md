# Cyberpunk 2077 (`cyberpunk2077`)

Title: **PPSA04027**. Maturity: **Experimental**. Only player money is editable, for the
sample-verified header combination **save 265 / game 2210 / archive 195**. Other CSAV versions
retain the container/node/fact explorer but have no inventory editing. No console load has been
tested; only a human can promote this plugin to Stable.

## File roles and scope

- `sav.dat`: gameplay CSAV; claimed with matching title, filename and VASC/FZLC signatures.
- `settings.json`, `metadata.9.json`: owned by GenericJson; never claimed or rewritten here.
- `user.gls`: separate global/profile format; not claimed or modified.
- `screenshot.png`, system files and backup siblings: never rewritten as a side effect.

The plugin performs no filesystem/network I/O. All parsing and writing use the SDK's supplied bytes.

## Verified container layout

All integer words are little-endian. Magic strings show their on-disk byte order.

| Region | Layout |
| --- | --- |
| `0x00..0x18` | `VASC`, save version at 4, game version at 8, opaque clock/date/header bytes, archive version at 21 |
| `0x19` | `FZLC`, int32 chunk count |
| `0x21` | Descriptor triples: absolute file offset, stored size, expanded size (12 bytes per chunk) |
| First descriptor offset | Data begins after reserved table space; sample offset `0xC21` / 3105 |
| Compressed chunk | `4ZLX`, repeated int32 expanded size, raw LZ4 block; stored size includes the 8-byte wrapper |
| Uncompressed chunk | No wrapper; stored and expanded sizes must agree |
| Node directory | `EDON`, packed count, then packed ASCII name + next ID + child ID + virtual offset + size |
| Final 8 bytes | int32 directory **file** offset, `ENOD` |

Expanded node offsets use a virtual file with the same header/data start: subtract the first chunk's
file offset to index the concatenated payload. Each node starts with its int32 ID; parent sizes include
children. IDs, ordered ranges, containment, sibling chains, child links, chunk ranges, wrapper sizes
and exact LZ4 output lengths are checked.

Packed integers have six initial magnitude bits, sign/continuation bits, then seven magnitude bits per
subsequent byte. Names use negative byte lengths. Positive/UTF-16 name encoding is unsupported.
Unknown padding, gaps, directory extensions and opaque node bodies are retained.

Limits: 128 MiB input, 64 MiB total expanded data, 4096 chunks, 100,000 nodes, 128 nesting levels,
1024 bytes per node name. Invalid/oversized input raises `SaveFormatException`.

## Inventory, fields and confidence

The verified inventory schema has an int32 sub-inventory count after its node ID, then uint64 container
ID and int32 item count. Each item has matching 16-byte item-info prefixes before and inside its
`itemData` node: uint64 TweakDB ID, uint32 seed, byte structure, uint16 unique counter, byte flags.
After the node ID and prefix are byte flags, uint32 creation time and optional uint32 quantity.
Quantity is at node-relative offset 25. Extended item bodies remain opaque.

The prefix length matters: old MIT CyberCAT uses a 15-byte variant. Its item parser cannot be applied
literally to this sample. Current WolvenKit's wire layout and the actual PS5 bytes agree on 16 bytes.

`Items.money` resolves to **0x0000000BF5E188EC**: CRC32/IEEE of its UTF-8 name in the low word, name
length 11 in the next byte. The sample's unique container-1 money item is node 39, quantity **300**.
This meaning is **Verified** by the hash match, structure and upstream money editor. Other IDs remain
hexadecimal; no names are invented. Quantity encoding is Verified; other ownership is not interpreted.

`FactsTable` contains packed count, a uint32 hash plane, then a uint32 value plane. Count and node
size must agree exactly. Hashes stay unresolved; fact meanings are **Unknown**, all read-only.
Attributes/perk points, level and street cred/XP are not decoded from scriptable/stat systems.
They remain inspectable through named nodes and raw bytes.

## Writing, compression and integrity

Only the unique quantity-only `Items.money` record in container 1 is writable. It is a fixed-size patch;
items are never inserted and unknown bodies are never reserialized. The editor cap is 100,000,000,
or the original value if larger: an editor policy, **not** a proven game limit. Missing/ambiguous money,
unverified item shapes and unsupported versions disable editing.

No-op serialization returns the original bytes, including after undo. Only changed chunks are
re-encoded with K4os raw LZ4 and identical expanded chunk boundaries. Unchanged compressed chunks,
reserved bytes, gaps and directory bytes remain identical. Descriptor file offsets/stored lengths
and the final directory pointer are updated; expanded sizes and virtual node offsets stay unchanged.
Serialization re-decompresses, compares the complete expanded payload, and revalidates the node tree,
inventory and fact tables before returning bytes.

The supplied decrypted file needs no further encryption. The cited CSAV readers/writers define no
container checksum/signature; there is no known container checksum to recompute. TweakDB CRCs identify
names, not save integrity. Unknown internal bytes are preserved. Structural correctness and edit/reopen
preservation are tested; game acceptance and undocumented semantic dependencies remain untested.
This is why maturity remains Experimental.

## Views and tests

Overview, inventory, facts, node tree and container tabs provide stats, 128-entry item/fact pages,
lazy named children and paged original/payload/node bytes. Chunk tables are bounded to 4096 rows.
Value IDs stay stable across edits.

Sample evidence: **one primary record and its identical backup**, 1,302,721 stored bytes, 22 chunks,
5,607,178 expanded bytes, 159 nodes and 22 items. This is one distinct save, not a multi-session corpus.
Every supplied gameplay entry, including backups, is tested.

Tests cover conformance with an explicit money edit; known sample values; full lazy traversal; atomic
rejection; version fallback; CRC32 and literal/overlap LZ4 vectors; compressed/uncompressed synthetic
containers; padding/gap preservation; all truncations of a small fixture; invalid offsets/counts/links;
bad LZ4 and excessive expanded sizes. Edited payload is compared against an independent four-byte
patch, unchanged compressed chunks/directory bytes are checked, output is reopened, and undo must
restore the exact original. Sample tests skip via `SampleSaves` when absent; synthetic tests still run.

```powershell
dotnet test --project plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests
```

Validation: **26 passed**, no sample skips. API build: zero warnings/errors. Core: **42 passed**.
Observed open + initial view: approximately 5–17 ms on the development machine; not a portable benchmark.

## Open problems

Decode scriptable progression/stat packages, extended items and attachments; verify other regions and
schema versions; establish global/profile integrity separately. A human must test loading edited saves
on the console before any Stable designation. These capabilities are not claimed by this release.

## Sources and licences

Research/sample verification: 2026-10-28. Downloaded source was read as text in scratch storage;
no upstream application/script was executed. This is independently written format code. No GPL source
is copied or linked into the plugin. The managed K4os package is its only third-party runtime dependency.

- [CyberCAT at 50db326](https://github.com/WolvenKit/CyberCAT/tree/50db3263a94247531853ba13c2e200f2e784ba8d75/CyberCAT.Core):
  `Classes/CompressionHelper.cs` for FZLC/4ZLX and rebuilding, `Classes/SaveFile.cs` for directory/footer
  offsets and links, `ExtensionMethods.cs` for packed integers/names, `Classes/Parsers/FactsTableParser.cs`
  for fact planes. MIT; notice retained in [CyberCAT-LICENSE.txt](CyberCAT-LICENSE.txt).
- [WolvenKit at 65ea74b](https://github.com/WolvenKit/WolvenKit/tree/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save):
  `Helper/InventoryHelper.cs`, `Classes/ItemData.cs`, `Classes/ItemInfo.cs` for current item wire facts;
  `IO/CyberpunkSaveReader.cs` / `CyberpunkSaveWriter.cs` for a second framing reference.
  GPL-3.0; format reference only, no source copied.
- [CP2077SaveEditor inventory control at c1fd003](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Views/Controls/InventoryControl.cs):
  confirms `Items.money` quantity in player container 1. MIT; no source copied.
- [PixelRick CyberpunkSaveEditor](https://github.com/PixelRick/CyberpunkSaveEditor): alternate tree/
  scriptable-system research lead; no parser copied or schemas inferred from its feature list.
- [LZ4 block specification](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md): block grammar
  underlying literal and overlap vectors.
- [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4): managed raw codec,
  version 1.3.8 from the existing central package list, MIT.
- [Repository reconnaissance](../../docs/formats/PPSA04027.md): initial sample dimensions, independently
  rechecked against every supplied gameplay entry.

## SDK requests

None. Existing lazy sections, tables, bound fields and paged blobs cover this implementation.

# Ragnarök PS5 research log

2026-10-28. Sources are linked in the [README](README.md#sources). Measurements read the main archive
in place, preserving each `ZipInfo` occurrence. Exploration buffers and reference text stayed in OS
temporary storage outside the repository. No account metadata or credentials were needed.

## Existing knowledge and transfers

Searches covered 2018 PC/PS4 editors, Ragnarök PC editors, GOWRSAVE format/010 templates,
CRC/xxHash/Murmur/FNV leads, Lua modding, WAD extractors and SaveWizard resource codes.

- The open 2018 editor uses 20 slots inside `memory.dat`, with a fixed slot-relative inventory
  address. It corroborates 16-byte ID/quantity/metadata rows and the shared Hacksilver/EconomyXP IDs.
  Its fixed offsets were not transferred.
- Continue?'s PC reader identifies description `50..18F`, time `190`, counter `1BC` and wallet marker
  `5F2243C695480000`. Its revision differs from PS5. Those offsets and counted rows were checked
  independently on this corpus. Its filesystem scanning and other statistics were not ported.
- Codes Prince has separate commercial PC/console products. Their feature lists establish that tools
  exist, not their algorithms or compatibility here. No executable was downloaded/run.
- GOWTool/smpack describe asset containers. WAD/WTOC headers are not this save's header; their
  compression and hashes do not establish save compression or hashes.
- No open complete Ragnarök save grammar, integrity implementation or save-specific key was
  established. Lua runtime inventory mods do not prove serialization rules.

## Decoding the codes

Type 8 searches literal bytes. For the source's Hacksilver search, the eight-byte ID is
`9E 52 40 D2 74 06 E8 5A`; the following zeros are code padding. Type 2 with the pointer modifier
and offset eight writes a numeric u32 at search-pointer + 8, stored little-endian. Search bytes
must not themselves be swapped as a numeric value. In an LE-u64 UI the same ID is `5AE80674D240529E`.

Type A writes literal bytes; type 9 manipulates pointers. The chest recipe has conditionals and
12-byte item payload writes. This neither proves a universal chest grammar nor that the commercial
service leaves integrity untouched. No write code is executed by this plugin.

## Corpus and correlation

76 primary gameplay records, two preferences, and 78 identical backup twins. Eleven repeated primary
paths changed. Archive order is not guaranteed chronological: SAVE0 goes from late to early gameplay.

| Literal search ID / resource | Occurrences per primary gameplay record | Observation |
| --- | --- | --- |
| `9E5240D27406E85A` Hacksilver | 3 in 23; 2 in 43; absent in 10 | Offsets 88,822–550,579; adjacent words disagree across matches. |
| `7E3FBE3EB167B14F` Rawhide | 1 in 60; 2 in 6; absent in 10 | +8 words include 0–52. |
| `888DE11A0DA6CFDC` Chaos Flame | 1 in 60; absent in 16 | +8 word 0 in 59 occurrences, 1 in one. |
| `29AD79C2750B0E71` Gale Flame | 1 in 17; absent in 59 | All +8 words zero. |
| `CC1B3E5C878842B3` Strength dew | 2 in 25; absent in 51 | One +8 word 1 and another 0 in each matching file. |
| `B0BDD5E2372DB672` chest | 1 in 69; 2 in 3; 3 in 3; 6 in 1 | Some +8 words are implausible counts; not universally parseable chests. |

These are whole-file raw ID matches, not only indexed wallet rows. A first-match editor or summed
inventory display would mix records with different roles.

### Wallet

Exactly one hero-wallet marker occurs in each primary gameplay record, at offsets 86,202–113,796.
Following counts range 0–211; all counted 16-byte rows fit. Indexing follows marker/count, not slot
offset. In generation-zero SAVE0: marker `1BC44`, count 211, rows start `1BC58`, Hacksilver ID
`1BC88`, quantity `1BC90 = 288638`; Rawhide ID `1BCD8`, quantity `1BCE0 = 52`.
Three whole-file Hacksilver matches occur, but only one belongs to this table.

The preceding u32 is 6,536 in that file. Row counting alone does not explain its coverage; further
arrays and chest-looking content intervene. A universal length chain was not established. Indexing
deliberately stops at the counted wallet rows. Unknown metadata and IDs are retained without names.

### Directory

All 76 primary gameplay records have 798 strictly increasing, unique u64 IDs beginning `446` after
the count at `442`. Exactly 798 subsequent u32s contain only 0/2. The next count is 10, followed by
ten IDs all present in the sorted array. Relationships hold in small and large generations. Tests
validate structure without assigning field types, names or nesting.

### Rejected shortcuts

The PS4 difficulty pointer is `2F440` plus two. At `2F442`, 68/76 files have `0000`; others have
literal bytes `99F5` (twice), `0030`, `19AC`, `4204`, `6FE9`, `D8C4` or `E7A2`. Most are outside
the source's enum. Metadata word `1B8` spans 0–10 and was not relabelled as difficulty either.

Header `18` is not file size: first SAVE0 has 2,672,346 there versus 3,215,266 file bytes.
No computed-size or compression claim is made from that correlation.

## Integrity experiments and limits

For each of 78 primary records, candidate ranges started at decimal offsets
`0, 4, 8, 16, 28, 32, 64, 1088, 1090` when in range, ending at EOF or EOF minus four.
Stored u32/u64 candidates were taken at four-byte steps from the first 64 and final 32 bytes,
using only fully fitting values and excluding zero/all-one sentinels.

Gameplay additionally tested the initial ID array, IDs plus paired words, wallet rows alone, and
wallet header plus rows. The three wallet header words and word before its marker were additional
u32 candidates. These are plausible probes, not exhaustive record coverage.

CRC32/IEEE, Adler32, xxHash32, xxHash64, MurmurHash3 x86-32 and the low 64-bit half of MurmurHash3
x64-128 (seed zero where applicable) produced **10,200 computations, no matches**. CRC32/Adler32
also tested additional footer-byte alignments on the first three gameplay records without a match.
Python zlib and isolated reference hash packages in scratch storage were used. No hash package is
a plugin runtime/test dependency.

Untested possibilities include CRC32C, custom seeds, zeroed embedded checksum fields, other endian/
width variants, full nesting, keyed hashes/signatures and compressed coverage. Identical backups
give no checksum equation; broad snapshot changes do not reliably isolate a protected field.
Integrity remains a blocker, not an established absence of checksums.

FNV-1/FNV-1a 64-bit probes of obvious Hacksilver/hero-wallet/chest spellings, with/without a NUL,
did not match these IDs. This small negative experiment does not exclude the FNV family. No
hash-derived name labels are claimed.

## Completion boundary

Delivered: read-only headers, partial directory/wallet index, resource correlation, preferences,
bounded lazy exploration, exact preservation and rejection of known structural corruption. The
SDK did not block implementation. Full nested parsing and safe writing were not established.
Next work needs controlled PS5 save/UI pairs, serialization call tracing or a stronger public
grammar before introducing setters on search matches.

## Round 2 — nested framing, names and unkeyed digests

2026-10-28. Clean-room probes read only gameplay files from the original archive in memory.
No save bytes were copied into the repository. Downloaded source text stayed under the OS temp
directory and was inspected rather than executed. New tests reproduce the measurements.

### Nested records

The u32 preceding the HERO marker is an enclosing byte length. Starting at the marker, repeatedly
consume `u64 containerId; u32 resourceCount, idCount, wordCount`, then `16×resourceCount` bytes,
`8×idCount` bytes and `4×wordCount` bytes. All 76 primary records consume their length exactly,
yielding five containers with the same identities. HERO and the blacksmith chest retain inferred
research labels; the other three containers remain unnamed. SAVE0 generation zero has resource
counts 211/10/8/0/3, 261 hero IDs and 159 index-like words.

A proposed zero-based link from those u32 words to the preceding ID list was falsified by early
saves. The production parser therefore bounds only their count/range and retains raw values.
No gear ownership, equipped slot, skill unlock or resource-row relationship is asserted.

The next section is `u16 length; u32 count; count×22 opaque bytes`, exactly consuming `4+22×count`.
After it, a u16 length encloses `u64 all-ones; u32 1; u32 groupCount` followed by counted groups:
`u64 groupId; u32 count; count×(u64 ID, four raw bytes)`. All records consume this payload exactly.
There are five groups with 28/26/27/23/28 records (132 total) in every sample. Observed four-byte
patterns include `00000100`, `01000100`, `01010100`, `00010100`, `01000000`, `01010000`.
These look like flags but their semantics are unknown; they are not named quest completion bits.
Generation-zero SAVE0 wallet end is 0x1D5CC and flag-group end is 0x1DC50.

### Reference expansion

The pinned GPL-3.0 Continue? reader identifies raven resource hashes `482780EFF18ED57E` and
`28FB73555D08478B` as numeric u64s. Their literal LE bytes occur in counted PS5 hero resource rows
in 40/76 and 56/76 primary saves. Generic raven-item and gift-counter labels remain Inferred.
The PC completion formula was not ported or displayed. Controlled PS5 raven/reward captures are
required before presenting a completed/total count.

The pinned GPL-3.0 Apollo patch adds 36 literal ID/category candidates beyond the original 31
resources. The source does not name these extra items; the new dataset keeps Unknown names rather
than aligning them speculatively to wiki lists. No preset value is treated as a legal maximum.
The new datasets retain their licences, pinned sources and notices, separately from CC BY-SA data.
Fandom HTML/API requests returned HTTP 403, so no new unpinned wiki content was imported. Research
did not establish a permissive, build-matched gear/quest hash crosswalk. No unlicensed runtime
table or 2018 item database was embedded. Source details are in README and the data sidecar.

### Integrity / authentication evidence

`RagnarokAuthentication` adds an original CRC32C implementation using reflected Castagnoli
polynomial 0x82F63B78, all-ones initial/final XOR, verified by `123456789 → E3069283`.
Standard-library MD5/SHA-1/SHA-256/SHA-512 calls are verified against the published `abc` vectors.
MD5/SHA-1 are legacy-identification hypotheses, never authentication mechanisms in this plugin.
Background specifications: [CRC32C / RFC 3720 appendix B](https://www.rfc-editor.org/rfc/rfc3720#appendix-B),
[MD5 / RFC 1321](https://www.rfc-editor.org/rfc/rfc1321),
[SHA / RFC 6234](https://www.rfc-editor.org/rfc/rfc6234). No reference implementation was ported.

Coverage: whole file; file without EOF sentinel; after the initial eight-byte timestamp through
EOF minus four; after outer header through EOF minus four; after metadata through EOF minus four;
metadata alone; directory IDs/paired words/secondary arrays; wallet payload; wallet block including
length; flag-group block including length; hero resource rows (omitted when empty).
CRC32C compares both byte orders; the other digests compare complete canonical digest bytes.
Every possible byte-aligned target is searched, including unaligned targets and the final fitting
offset. The target search is the complete file; a raw match would be a lead, never acceptance.

Across 76 primary generations: **4,956 coverage/encoding comparisons, zero matches** (4,130 digest
calculations; LE CRC32C reuses its canonical calculation). No keyed hypothesis was testable: no
key derivation or save authentication routine is established. These finite negatives do not
exclude truncated digests, custom seeds, zeroed/omitted fields, compressed coverage or keyed
MACs/signatures. F1 reports stable container/resource/flag paths and named digest coverage, and
the UI computes the same evidence lazily. Whole-file hash changes alone do not locate authentication.

The original CRC32/Adler32 helper now omits zero-length spans and correctly excludes the final
sentinel from aligned target comparisons. The historical round-1 20-probe result remains a finite
historical result; the expanded grammar supplies more bounded spans in round 2.

The updated [owner checklist](OWNER_CHECKLIST.md) includes no-action controls, separate equip/upgrade
pairs, raven collection versus reward redemption, exact UI/journal states and redacted algorithm
tracing. The plugin remains ReadOnly with no setters/actions. Complete named gear/quest models,
character-specific XP, completion states and write authentication remain open.

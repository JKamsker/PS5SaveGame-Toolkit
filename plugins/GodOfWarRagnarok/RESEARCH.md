# Ragnarök PS5 research log

The [GOWTool MIT follow-up](GOWTOOL_ASSESSMENT.md) supersedes the earlier unpinned GOWTool
licence status: f2899f6c89cb85bf0f313f8a844fb1884ff23633 includes MIT, copyright (c) 2026 kainotoa,
added by f0e92b0c720fd5abc44023a0d01b55d416516088. Only LICENSE changed from its pre-licence parent.
The pinned source targets 2018 WAD/texture/mesh assets and supplies no new saved-ID mappings,
StdNameHash implementation or save-integrity repair. It is technically inapplicable to new save
features with the available inputs; no code/data port or write/confidence change was made.
The report records primary paths/hashes, aggregate sample checks, 270 passing sample-backed Release
tests and the separate 220-pass/50-skip empty-sample run, plus exact next inputs and maintainer follow-up.

The [2026-10-29 gear/skills/XP discovery report](FIELD_DISCOVERY.md) supplements this historical
log. A newly located public gear sheet yields inferred definition/catalog-level joins across every
primary gameplay save. Core weapon levels, named saved skills, equipped-slot state, XP totals and
dependency/integrity writers remain unresolved. See the README's field-discovery section for current scope.

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

## Write-enable round (2026-10-28)

Owner instruction: enable bounded writes and retain warnings; owner-only captures are tracked in
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31), separate from implementation #19.
The prior evidence gate is superseded for existing decoded counters/time. Authentication is still
unknown: round 2's 4,956 comparisons yielded zero matches. No new algorithm or key is claimed.

Re-read the pinned [Continue? reader, Alexsander720 / contributors, GPL-3.0, 51e19b2a](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py)
and [Apollo PS4 patch, bucanero / contributors, GPL-3.0, 273f032b](https://github.com/bucanero/apollo-patches/blob/273f032b4c31c017fed5c1588f209b8faf87ba6c/PS4/CUSA34384.savepatch)
as text before implementing. The former supplies the PC time/hero-wallet leads; the latter shows
quantity-at-ID+8 conventions, not PS5 authentication or legal maxima. No downloaded code was run,
no new dataset/code port was embedded, and existing data attribution is retained. PS4 preset
quantities and difficulty offsets were not adopted as PS5 limits or setters.

The clean-room writer uses the existing PS5 parser's row boundaries and fixed-width LE u32 patching.
Each counter is bounded to 0..its own import, including a zero-only range for zero imports. Unknown
IDs retain raw labels; XP character ownership and raven reward/completion dependencies are not
invented. F6 previews restore counters after in-session reductions, with skipped rows and explicit
unknown-integrity warnings. Structural, gear, quest, preference and opaque fields have no writer.
Serialize re-parses every edited output; all unrelated bytes are preserved. See README for the
enabled-write evidence table and OWNER_CHECKLIST.md for the exact console trials.

## Unlicensed-source unblock (owner decision 2026-10-28)

The previous research-only/no-licence conclusions above are historical. The owner now permits
attributed factual imports and original reimplementation of community behavior. Audit covered
README, this log, notices, data sidecars and the linked plan's sources; there was no existing
licence-exclusion table. The new README table records resolved exclusions and the continued
proprietary-game-asset exclusion explicitly.

### Source audit and decoding result

- **mooofin runtime table / bundled ID spreadsheet**, revision
  `7b1730217b92d27ab910d6d421efbd503cc109b2`: imported 961 unique resource IDs
  (233 specific source names, 728 explicit Unknowns) plus four skill-tree IDs. The spreadsheet's
  numeric hashes are converted to LE byte keys; one identical duplicate is merged. Pin/hash/author
  details are in [the runtime sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.runtime.en/tables.source.md).
  No CE code, injection, Lua, runtime pointer traversal or unlock transaction was copied/run.
- **ProtoBuffers / xcier 2018 database**, revision
  `9342d5f3967ef74bac1161581dc4eab8bb41760d`: imported all 1,682 unique factual
  name/category/internal-name records, including gear and quest/lore candidates, into a separate
  catalog with `2018 candidate:` names and Unknown confidence. IDs are already literal bytes;
  reversing them again would be incorrect. `safe_edit`, slot offsets and implicit game limits are
  omitted. See [the crosswalk sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.2018.en/tables.source.md).
- **MorseTheCode/GodOfWarTool**, pinned `d8fdda4993d593825b1a2206904ab85c86fd703a`,
  and the earlier kainotoa/GOWTool/smpack leads: asset/WAD/TEXPACK formats do not describe the
  observed save header. They are retained as format research for technical relevance, not refused
  for lacking a licence. No applicable save decoder, reference crosswalk or integrity code was found.
  No game media/text was imported. JeanxPereira/GoWToolkit's earlier educational/research terms
  were not treated as a general redistribution grant; no asset-extractor dependency is required.

All 76 primary saves were parsed through the existing bounded grammar. There are 211 distinct
runtime ID matches in resource rows, 78 with specific source names; 66 primaries have matches,
ten early records have no resources. All 211 late SAVE0 hero rows match the runtime dataset.
The 2018 table matches 16 distinct resource IDs, all also in the runtime source. Both item tables
have **zero** matches in parsed directory IDs, nested-ID arrays or individual grouped-flag IDs.
Thus the imports improve resource/XP labels but do not establish named gear, ownership, equipped
slots or quests. Runtime Valhalla armour rows cannot be treated as equipped armour records.

Four exact group IDs match every primary save: Axe `0000001013865FC6` / 28 rows,
Blades `38EA4A0828266819` / 26, Spear `014BAA9043ACBB11` / 27,
Atreus `07A134D7DF345D66` / 28. The remaining `011CBBEE78698EC9` / 23 stays Unknown;
it is not named Freya by elimination. PC code's runtime 0x101 skill-unlock mask is **not** proof
of saved flag meanings. Wiki skill presentation keys are still separate from unidentified row IDs.

The lookup gives original PS5/raven mappings precedence, then named runtime candidates, community
unknowns, raw runtime entries and finally explicitly cross-game 2018 candidates. Pinned/direct
calls resolve the same names and catalogs. F8 rows, browse-only F4 fields, counter/action labels,
inspection and F12 labels carry provenance; all old fixed-size counter writers, bounds, warnings
and no-op byte preservation remain. No new writer or game limit is inferred from imported facts.

### Integrity re-check of newly available sources

Inspected source text only, not executed:

| File / pinned revision | SHA-256 | Result |
| --- | --- | --- |
| mooofin `gow.CT`, `7b173021` | `365a53fd4e47c70dc612f9583a5893d3f8d2d6deca2e128cdddf32d74294236d` | Locates `StdNameHash` by an AOB prefix, calls runtime wallet/skill APIs; no complete hash body, save serialization, checksum, authentication coverage or key derivation. |
| xcier `app/core/save_file.py`, `9342d5f3` | `4bbd2e9e5acf2dece3e79e9ea659ec432a194606ca3d6d1ecbdab8812dfeb324` | Fixed-slot reads/writes and raw-buffer save, no applicable PS5 authentication repair. |
| xcier `app/core/gow2018_data.py`, `9342d5f3` | `b0e31b50c8de4b39c0928d6520711b6383a3a9584a3a4ff9bd1642f72f3ffede` | Data/category normalization and 2018 constants, no save-integrity algorithm. |
| MorseTheCode `gow_unified_backend.py`, `d8fdda49` | `89f22347111d0ced4b5769ef6f893ffdc6bbadf779ed09afcdf7f9b1007e46fc` | LZ4 frame content checksum flag is for asset compression, not evidence of GOWRSAVE integrity. Texture/file hash fields are identifiers, not save checksums. |

The import supplies name/hash facts, not an implementable integrity algorithm. No new checksum
candidate or arbitrary-key test is claimed; existing finite CRC/digest tests remain regression
evidence only. **Integrity unknown — the game may reject this save.** Controlled PS5 captures,
load/native-resave/reload trials and redacted serialization/integrity traces remain the concrete
owner needs in OWNER_CHECKLIST.md / #31. Named Ragnarök gear/quest crosswalks still need exact
saved-ID joins; author permission is no longer the blocker for community factual tables.

# Baldur's Gate 3 (`bg3`)

Title: **PPSA18463**. Maturity: **Experimental**, version 0.7.0.

Opens decrypted BG3 save packages and profile resources, lists package entries with
stored/expanded sizes and compression, and exposes every resource node and attribute
through a lazy explorer. Includes embedded WebP thumbnails, decoded scalar values,
vectors/matrices, translation handles, and paged raw bytes for opaque or large values.
LSX exploration includes XML wrapper elements, metadata attributes and text.
Round 2 adds validated LSPK v18 checksums, preservation-first LSF/LSPK writers,
primitive editing and a sourced Party summary. No in-game write test has occurred.
Version 0.4.0 adds pinned attributed item, equipment, spell, passive, companion and
camp-supply catalogs, F8 queryable catalog/inventory collections, explicit LSMF live-state
candidate signals, and bounded StorySave database-marker inspection. These are read-only
candidate views. Version 0.5.0 enables existing party-summary number tokens and
explicit LSF quantity candidates with warnings, following the owner's decision
“Enable writes. Keep warnings.” Console validation is tracked in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
See [LSMF.md](LSMF.md) for the independent implementation record and
[EVIDENCE.md](EVIDENCE.md) for phase status and the exact owner capture checklist.

Version 0.6.0 adds bounded live NewAge storage decoding and editing: explicit stack
member quantities, current/temporary HP candidates and effective ability-cache
candidates. F5/F6 plan a two-key container reorder and imported-quantity restoration;
F8 pages live values, container entries, class selections and spell books; F9 shows
spell occurrences with Unknown states and no invented prerequisites. **Source rows
are not party identities.** See [NEWAGE.md](NEWAGE.md) for the PS5-specific layouts,
conservative limits, warnings, exclusions and owner checks.

Round 2 adds software-proven identity and dependency joins. `MemberComponent` body
references are joined through LSF `Creator.Entity` and `Creator.TemplateID`, then
through duplicate-preserving `TemplateComponent` rows to the unique state entity
that has the measured classes/stats/spell families. Health and spell-book epochs are
selected only when their measured row is unique or their storage count gives one
unambiguous winner. Current containers are reached from the party-member entity,
traversed recursively with cycle and depth guards, and each path retains its source
container, slot and item entity. The supplied sample proves 3 party joins and 638
current inventory entries; only entries reached from the party roots receive a body
owner label, while other world/trader/container rows remain explicitly unowned. Gold
template occurrences remain world/trader candidates:
the LSF item node has no unique item-instance bridge, so no player total or gold
distribution writer is exposed.

## Game assets (0.7.0)

The embedded pack `bg3.starter.en` 1.2.0 adds **51 distinct icons on 60 catalog records**:
21 spells, 5 passive/action features, 13 items/supplies and 12 base-class badges.
There are **39 distinct official game-tooltip/flavour-text excerpts on 48 records**;
long descriptions are explicitly marked excerpts. Existing CC BY-SA label attribution
is retained separately from Larian game-asset rights. Corrected source-verified UIDs
include `Target_Bless`, `FeyAncestry`, `Tiefling_HellishResistance`,
`AttackOfOpportunity` and `HumanMilitia`; the Shield entry is specifically the
`Shout_Shield_Wizard` variant. No generic spell/passive writer is enabled.

Catalog pickers and F8 tables resolve local icons. Saved-item tables use the exact
four template GUIDs; live spell details and read-only class UUID fields resolve
only exact catalog keys. Unknown IDs/missing packs keep raw text. Images do not
prove ownership, learned/prepared state or current class authority. Race icons are
not applicable because no race field is decoded/displayed. No map backgrounds are
added: the plugin has no validated discovery/coordinate model.

**New embedded binary assets: 161,455 bytes**, all at most 64×64, below the 4 MB budget.
Processing and visual inspection occurred in a temporary directory. Full per-file
source revisions, original hashes and modification notes are in
[assets.source.md](SaveToolkit.Plugins.BaldursGate3/ReferencePacks/bg3.starter.en/assets.source.md).
The framework validates/caches packs once and shares image content across pinned
documents; the plugin performs no runtime loading, decoding or network access.

An isolated Debug stopwatch run on the largest sample (`AutoSave_0.lsv`, 8,073,350
bytes), excluding sample import and including first pack load, open, root view,
first ten rows of every collection and catalog icon resolution, measured:

| Measurement | Before | After |
| --- | ---: | ---: |
| Pack load | 46.6137 ms | 66.3150 ms |
| First load and view | 669.7487 ms | 810.6028 ms |
| Managed allocated bytes | 238,784,728 | 242,339,240 |

These are single-process observations including JIT, not a latency guarantee.
A second final-code process measured 71.4132 ms for the pack, 1,207.3090 ms for
first load/view and 244,796,760 allocated bytes, showing first-use timing variance.
`AssetLoadMeasurementTests` repeats the operation and optionally writes the result
when `BG3_ASSET_BENCHMARK_OUTPUT` names a scratch output file.

Validation: **214 passed, 0 failed, 0 skipped** with
`SAVETOOLKIT_ARTIFACTS=C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts`.
API build: **0 warnings, 0 errors**. Coverage includes all embedded hashes/types,
asset-ID resolution, deduplication, shared cached content, attributed rich catalog
cells, exact sample class/spell resolution, Unknown graph state, missing-pack/raw-ID
fallbacks, icon-column sorting and byte-identical sample export. Offline image QA
verified every image decodes at at most 64×64; all C# sources remain under 300 lines
(largest: 261). All repository edits are inside this plugin folder.

## Supported scope and preservation

- LSPK v18 reading/writing, independent entries in a single archive part, with a
  mandatory matching header digest. Legacy v15/v16 remain read-only; their CRCs
  and digests are not claimed to be verified.
- LSOF v5/v6/v7 reading/writing, compact metadata formats 0/2 and extended format 1,
  including keys. Edits target existing primitive attributes, not tree structure.
- Uncompressed, zlib, LZ4 blocks/frames, and zstd resource data; package payloads use
  the per-entry codec. The file list is always an LZ4 block.
- LSX XML, including UTF-8/BOM and UTF-16 little/big endian with BOM.
  DTDs and external entities are prohibited.
- No-op `Serialize()` returns a fresh copy of the **original file**, preserving
  unrecognized metadata, padding, trailing data, compression and XML spelling.
  This is tested for all twelve claimed primary/backup files, and independently
  through the section rebuilder for all fourteen embedded/standalone LSF copies.
- Integers, finite floats/doubles, booleans and ordinary UTF-8 string types can be
  edited. GUIDs, vectors/matrices, translation structures, arbitrary ScratchBuffers, LSX,
  arbitrary JSON and story data stay read-only. Existing SaveInfo party level/XP
  number tokens are editable in v18 packages. The UUID-only analytics resource has no
  editable primitive. The internal typed NewAge writer patches only validated
  fixed-width values/map keys. No file/network I/O or external executable is used.

Solid packages, multipart packages, deletion entries and older versions are rejected
with `SaveFormatException`. Solid LZ4 uses offsets into a shared frame rather than
independent payloads; this path has no supplied fixture and is intentionally unsupported.
Files from other title IDs are not claimed. Standalone thumbnails use the framework's
normal raw viewer; the thumbnail inside an opened package is displayed as an image.

## Sample evidence and file roles

The supplied archive has one snapshot (generation 0) and byte-identical console backup
twins, rather than several appended snapshots. No samples or extracted data are included
in this plugin. Sample tests skip via `SampleSaves.Load` when the archive is absent.

`Save0001/AutoSave_0.lsv` is 8,073,350 bytes, LSPK v18, flags 0, one part, seven
zstd-compressed entries in this file-list order:

| Entry | Stored bytes | Expanded bytes | Resource nodes / attributes / roots |
| --- | ---: | ---: | --- |
| `meta.lsf` | 1,283 | 1,516 | 15 / 39 / 1 |
| `StorySave.bin` | 6,754,260 | 33,628,992 | Opaque Osiris story data |
| `Globals.lsf` | 933,769 | 2,410,069 | 149,174 / 189,357 / 30 |
| `SaveInfo.json` | 523 | 1,559 | Raw JSON payload |
| `AutoSave_0.WebP` | 75,678 | 75,666 | RIFF/WebP thumbnail |
| `LevelCache/TUT_Avernus_C.lsf` | 287,642 | 467,459 | 13,981 / 25,867 / 27 |
| `LevelCache/SYS_CC_I.lsf` | 19,593 | 25,350 | 330 / 780 / 27 |

Every embedded resource is LSOF v7 with compact metadata format 0.
The profile files are LSOF v7 with extended metadata format 1:

| File | Nodes / attributes | Role |
| --- | --- | --- |
| `analyticsUserData.lsf` | 1 / 1 | UUID-only profile analytics resource; read-only |
| `profile8.lsf` | 17 / 19 | Profile resource; gameplay meaning not inferred |
| `config.lsf` | 4 / 10 | Configuration resource with a nonempty key table |
| `graphicSettings.lsx` | 14 / 29 XML elements/attributes | Graphics settings XML |
| `modsettings.lsx` | 14 / 26 XML elements/attributes | Module settings XML |

Verified sample values in `meta.lsf`: `Level = TUT_Avernus_C`, raw `Difficulty = 2`,
raw `OriginalPlatform = 3`. Numeric enum meanings are deliberately not assigned.
`Globals.lsf` has a `NewAge` root with a `ScratchBuffer` named `NewAge` of 2,618,936
bytes beginning `LSMF`. The NewAge tab validates its 315-entry directory and
displays nominal component fingerprints and paged EntityId occurrences. There
are 9,453 occurrences but 1,984 distinct GUIDs; duplicates are not collapsed into
an invented unique character. The separate Live NewAge tab decodes supported PS5
storage profiles, ownerlist candidates and bounded heap references. Unsupported
families have explicit diagnostics and remain available raw; ownerlist position is
not promoted into a proven current body/stats/spell/party identity.

The Party tab reads `SaveInfo.json / Active Party / Characters`: the supplied sample
contains three rows at level 1, with current/total XP `(30,30)`, `(30,30)`, `(0,0)`.
Origin strings retain their raw values alongside optional sourced origin labels. The
`meta.lsf` leader display-name primitive is also exposed (metadata only; it does
not rename a live character). Summary meanings use `Confidence.Inferred`, backed
by the cited community parser/editor documentation and exact sample keys. Level
and XP summaries are now editable as metadata, with console warnings. The starter pack names Laezel and DarkUrge as
origins; these are not live character names or party/ECS identity joins. A player
gold total remains unavailable; explicit NewAge stack quantities include all owners. The metadata has two
`CharacterMetaData` nodes, so those nodes are not assumed to map one-to-one to the
three JSON rows. Editing primitive metadata does not synchronise live ECS state.

The Inventory tab browses original `Globals.lsf / Items / Item` occurrences from
**all owners**, including world and trader items. Four exact template GUIDs in the
starter pack label 40 of the sample's 761 occurrences (including twenty healing
potions and four gold-template occurrences); these are not known in-game stack
counts or player inventory totals. Each candidate name carries per-cell source,
licence and raw-ID provenance. Duplicate templates remain separate, unknown IDs
stay raw. If an Item node carries an explicit owner or quantity attribute, it is shown
as an unverified candidate; absent attributes remain `Unknown / unknown`. The same
collection is exposed through the SDK's bounded F8 query contract with stable row keys.
Unambiguous, positive integer quantity candidates use the canonical raw-resource
binding with conservative observed bounds; the current value refreshes after edits
and can be sorted in the queryable table. The original owner/quantity text is
explicitly labelled as the original snapshot. None of the supplied 761 occurrences
has such a quantity control; their live amounts are not guessed from templates.

## Enabled writes and their evidence

All writes remain **Experimental**. Newly writable fields carry `Inferred` or
`Unknown` confidence, a “Not validated on console” description and a visible
warning that the game may reject the edited save. No owner evidence is needed to
use these enabled paths. Metadata changes do not update live NewAge state.

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Existing SaveInfo party `Level` | Inferred | Three real PS5 JSON rows; exact-token edit/reopen, F1 byte diff and session undo/redo. Base-game cap 12 from [Experience](https://bg3.wiki/wiki/Experience); higher existing levels extend only to the observed maximum. | v18 MD5+1 repaired and verified; output JSON re-parsed. Other game consistency checks unknown; save may be rejected or summary overwritten. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): test level metadata independently, inspect save selection and live sheet, resave/reload/redump. |
| Existing SaveInfo party current-level XP | Inferred | Three PS5 tokens (30,30,0). Nonnegative range bounded by the current summary's observed maximum (30 in supplied save); no gameplay limit claimed. | Same verified v18 digest and JSON checks; unknown game consistency checks may reject/overwrite edits. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): change one summary XP token, inspect metadata/live state, resave/reload/redump. |
| Existing SaveInfo party total XP | Inferred | Three PS5 tokens (30,30,0); same conservative observed bounds; persistence, intended-token diff and undo/redo tests. | Same verified v18 digest and JSON checks; unknown game consistency checks may reject/overwrite edits. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): test total XP separately and compare both summary and live XP after resave. |
| Explicit existing LSF Item quantity candidate (`Quantity`, `Amount`, `Count`, `StackAmount`, `StackCount`) | Unknown | Existing candidate-name inspection plus proven primitive writer. Synthetic standalone/package fixtures test bytes, persistence, invalid batches, raw/page binding consistency and session undo/redo. **No supplied item has a direct quantity attribute.** | LSF structure re-parsed; v18 digest repaired and verified when packaged. Cross-representation dependencies/integrity unknown; game may reject edits. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): supply a save with a direct positive integer quantity, identify owner/stack, reduce by one, load/use/trade/resave/reload/redump. |
| NewAge existing positive member quantity | Unknown | All 38 sample groups / 39 members follow explicit group→member/entry references. Multiple contributions are summed; every contribution stays positive. Range: contribution count through imported member total. | Fixed u32 patches; surrounding bytes preserved, output NewAge/LSF/package re-parsed, v18 digest repaired. Inner integrity/dependencies unknown; game may reject edits. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): identify one non-quest stack/owner, reduce by one, use/trade/rest, native resave/reload/redump. |
| NewAge current / temporary HP candidates (2 write kinds) | Unknown | Whole PS5 380-row profile validates. Current HP: 0..stored maximum; temporary HP: 0..max(imported temporary HP, stored temporary maximum). Owner/epoch joins remain unproven. | Same fixed-width preservation and outer digest repair; inner integrity/dependent health state unknown. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): separate damage/heal/temp-HP captures and spare-slot edit checks, including unrelated characters and later combat/rest. |
| NewAge six effective ability-cache candidates | Unknown | Whole PS5 256-row profile validates six i32 values after an opaque 8-byte per-record prefix. Each range is 1..its imported value. These are effective/cache candidates, not base scores or respec. | Same preservation/checks; boosts, progression and cache authority unknown. Game may reject or regenerate values. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): reduce one supported value, inspect sheet/boosts/combat and resave/reload/redump; test each ability separately. |
| F5/F6 swap two existing container map keys | Unknown | Two existing u16 keys in one validated container; membership, dense ordinals, generation words, item pointers and all other bytes are preserved. Scoped memento supports one-step undo/redo. | NewAge structure re-parsed and v18 digest repaired; map-key/grid semantics and external dependencies unconfirmed. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): two ordinary unequipped items in one inventory, observe order/ownership/equipment after swap, undo/redo, resave/reload/redump. |
| F6 restore imported stack quantities | Unknown | Preview includes changed/skipped rows and integrity warnings; explicit, document and Core-resolved F8 query scopes with exclusions. Reuses bounded quantity setters; one history step. | Same quantity integrity limits. No missing stack, gold ownership, template identity or quantity of one is fabricated. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): reduce two existing quantities, restore selected/filtered rows, verify preview and grouped undo plus native resave/reload. |

The round 2 joins do not make three writers safe. Learned-spell rows are empty in
the supplied sample (`LearnedSpells` uses the empty sentinel); prepared-spell
storage is a variable-length `SpellBookPrepares`/string-pool relationship whose
allocation and source/owner semantics are not established. Effective `StatsComponent`
ability values are cache candidates; `CharacterCreationStatsComponent` is present,
but its base-score layout and derived invalidation contract are not established.
The four gold-template occurrences have duplicate Creator template mappings and no
unique LSF item-instance-to-Entity bridge. A controlled learned/prepared edit, a
base-versus-derived capture, or a gold gain/spend pair with visible owner is the
evidence that would unblock each writer. Fixed-width no-op and byte-preservation
checks remain in force for every enabled writer.

Summary edits patch existing UTF-8 number tokens only. Unknown members, string
escapes, whitespace, number spellings and trailing whitespace are retained. Undo
returns the original token spelling and exact original archive. Missing, fractional,
overflowed, negative or ambiguous duplicate-property summaries remain read-only;
unsupported/multiple SaveInfo entries are preserved. Only the first 128 character
rows are exposed. Origins and reference labels remain read-only.

Quantity candidates must have one numeric alias, a positive original integer and
an unshared attribute identity. Their allowed range is 1 through the largest
observed positive quantity of that stored integer type in the document. Zero/removal
semantics are unknown and rejected by the new control. The same canonical binding
and bounds apply in the raw explorer and table. This is not a player-gold total.

Inventory creation/removal/transfers/equipment, base-score/respec/level changes,
spell selection and Osiris quest/approval actions remain unavailable: entity
allocation/relocation, dependency repair and authoritative subsystem joins are
unmodeled. Existing-key reorder does not solve those structures. No generic
pointer/blob edit is exposed. Existing primitives and LeaderName remain enabled.

The Catalogs tab exposes the embedded pack as bounded queryable collections. Raw IDs use
an F4-compatible `ReferenceSpec` with custom values allowed and read-only confidence;
the pack's build, module and load-order are unspecified, so these records are candidate
labels rather than live joins. The original NewAge inspection section reports only component-family
presence (character, health, classes, spellbook, containers and stack candidates). The
Story tab reports bounded byte markers for known Osiris database families, without claiming
tuple values, approval totals, quest completion or romance state.

The framework loads the embedded `bg3.starter.en` pack and injects it through
`Open(file, references)`. `Open(file)` without a context retains raw fallbacks.
The native build is unknown, so build-specific catalogs are refused. Only
build-agnostic references are accepted; no gameplay rules/limits are supplied and no
pack data enters serialized saves. There is no plugin file or network I/O.

## Binary layout

All numeric fields below are little-endian. These layouts follow LSLib; see sources.

### LSPK

| Absolute header offset | v18 field |
| --- | --- |
| 0 | `LSPK` magic (4 bytes) |
| 4 | Version (u32) |
| 8 | File-list offset (u64) |
| 16 | File-list stored size (u32), including 8-byte preamble |
| 20 / 21 | Package flags / priority (u8 each) |
| 22 | MD5 over expanded payloads in physical order, each digest byte +1 (16 bytes) |
| 38 | Archive part count (u16); payloads begin at 40 or later |

The file list starts with an i32 entry count and u32 compressed size, followed by an
LZ4 block. Each v18 row is 272 bytes: 256-byte NUL-terminated UTF-8 name, 48-bit payload
offset, u8 archive part, u8 compression flags, u32 stored size, u32 expanded size.
The compression method is the low nibble: 0 none, 1 zlib, 2 LZ4, 3 zstd. The high
nibble denotes compression level; it does not change the decoder. For method 0 the
stored size is the actual size even when the expanded-size field is zero.

v16 uses the same header with 296-byte rows (64-bit offset/sizes and 32-bit
part/flags/CRC/reserved fields). v15 uses those rows with a 38-byte header and
implicit single part. Legacy row CRCs are not verified. Ranges, overlaps, counts,
unique names, compression results and resource references are validated.

### LSOF / LSF

`LSOF`, u32 version and u64 packed engine version occupy the first 16 bytes. v6/v7
metadata consists of five `(u32 expanded, u32 stored)` pairs: strings, keys, nodes,
attributes, values; then a compression byte, three reserved bytes, and a u32 metadata
format. Payload starts at byte 64. v5 omits the keys pair and starts payload at 56.
The **physical** section order is strings, nodes, attributes, values, keys. A zero
stored size means raw expanded bytes, not a missing section when expanded size is nonzero.

The string section contains u32 bucket count, then each bucket's u16 string count
and `(u16 byte length, UTF-8 bytes)` strings. Name references select bucket in the
high 16 bits and chain offset in the low 16 bits.

Compact nodes are `(u32 name, i32 firstAttribute, i32 parent)`; compact attributes are
`(u32 name, u32 lengthAndType, i32 ownerTag)`. The low six bits select the attribute
type; upper bits give its value length. Owner tags group attribute chains and are
**not** assumed to equal node indices. Values are consecutive in compact attribute order.
Extended nodes are `(name, parent, nextSibling, firstAttribute)` and attributes are
`(name, lengthAndType, nextAttribute, valueOffset)`, each 16 bytes. Key rows are
`(u32 nodeIndex, u32 nameReference)`. `-1` ends reference chains.

All 34 LSLib attribute type IDs are recognized. Numeric primitives keep signedness;
non-finite floats display as text with a warning. Strings are validated NUL-terminated
UTF-8 (including LSLib's WString variants). GUID display uses LSLib's default swapped
last four 16-bit groups. Translated strings expose their handle/version; localisation
is not resolved. Scratch buffers and formatted translation payloads remain raw bytes.
Matrices display their stored column-major scalar sequence.

## Integrity and writing

No additional encryption was found or applied: the provided input is already decrypted.
Codec framing/length checks are enforced, including zlib/zstd integrity checks provided
by their decoders when present. They do **not** authenticate the package or its raw data.

The v18 digest is reproduced and verified: MD5 of concatenated expanded entries in
increasing payload-offset order, then each digest byte +1 modulo 256. The sample
matches `6D98C6D4C2FF81E7C7F43AAA881D66DA`. File-list order and ordinal filename
order fail. Case-insensitive name order matches the same physical sequence on this
single independent sample, so those hypotheses cannot yet be distinguished. Both
orders remain satisfied by the writer's supported edits, which preserve names and
physical order. Zero/stale/nonmatching v18 digests are rejected; the plugin does not
assume the game ignores the hash. [INTEGRITY.md](INTEGRITY.md) records all 844
experiments, exact physical order, limitations and upstream issue/commit research.

The LSF writer retains the original header, names, node order, owner tags, links,
keys, unused value bytes, trailing bytes and unchanged compressed sections. It
patches attribute lengths and rebuilds the value section, updating all extended
value offsets after string growth/shrinkage. Overlapping extended attribute value
ranges are readable but edits are rejected rather than rewriting aliased data.
The rebuilder retains raw sections under compressed headers and block/frame LZ4
encodings. No-op and undo to original values return exact original bytes, including
noncanonical true bytes and additional trailing string NULs.

The LSPK writer preserves file-list order and physical payload order independently.
It retains filename bytes/padding, flags/priority, unknown gap/trailing bytes and
every untouched entry's **original compressed bytes**. Only modified entries are
recompressed. Offsets, sizes and digest are recomputed, and the LZ4 file list is
rebuilt. Added alignment padding uses LSLib's 64-byte header-relative alignment and
`0xAD` fill. No-op package writing is byte-identical; edited output is not expected
to match the old archive byte layout, but reopens to exactly the intended expanded
entries. All package entries and resource tables are re-parsed inside `Serialize()`.
The writers additionally compare every reopened attribute/payload to its expected
bytes before returning output.

Original codec and level flags are retained. LSLib's fast/default/max mappings are
used for recompression: zlib Fastest/Optimal/SmallestSize, LZ4 fast/10/12, zstd
3/9/22. Unknown high-nibble levels use the reference default while retaining the
flag byte. Unchanged sections/entries are never passed through a compressor.

The digest does not cover filenames, container metadata or padding and is not
authentication. Standalone LSFs have structural checks but no discovered content
checksum. Supported NewAge storage is decoded/patched as described in NEWAGE.md;
its inner integrity and authoritative gameplay semantics remain unknown. Osiris
remains read-only. Community load failures after ECS changes are not evidence about the
primitive/summary writer; PS5 compatibility still needs human testing. Maturity stays
Experimental until that test, even though the byte/checksum/conformance tests pass.

## Confidence and remaining limits

Verified: magic/version/layout matches, sample counts, actual primitive values, codec
output sizes, XML structure, the LSMF signature, byte-identical no-op preservation,
the supplied package digest and preservation of every unedited compressed payload.
Raw fields carry `Confidence.Unknown` because their **gameplay meaning** is unvalidated.
Curated Party metadata and exact-reference label candidates use `Confidence.Inferred`;
unresolved names and raw fingerprints use `Unknown`. Live character names, level/XP,
gold ownership, authoritative abilities and native party semantics need
controlled PS5 observations. The software party/body/storage identity joins are
sample-proven, but no player-gold total, base-stat setter, guessed item name, tree insertion/deletion,
multipart/solid writer, or console compatibility is claimed.

Other limits: 256 MiB input, 256 MiB total expanded package payloads, 256 MiB expanded
resource sections per document, one million resource nodes/attributes, XML depth 256,
formatted translation depth 32. Editable text is limited to one million UTF-16
units and rejects embedded NULs/invalid UTF-8. The optional Party JSON reader caps
input at 1 MiB, depth at 32 and rows at 128; unsupported JSON remains available raw.
Explorer collections page in groups of 128 with
hierarchical page links for very large lists. Binary attributes above 4 KiB use a raw
view; XML values above 4,096 characters use a bounded preview. Views are lazy; models and compressed resource tables validate
during opening. Opening and initial view construction took approximately 0.4 s
on the development machine; this is an observation, not a timing assertion in CI.

## Validation

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet test --project plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
```

Tests cover the shared conformance suite, every sample resource and no-op copy,
backup equality, 844 digest hypotheses, known MD5 vectors, synthetic version/codec/
layout fixtures, string growth/shrinkage, unordered extended offsets and gaps,
preservation of untouched compressed sections and entries, atomic rollback/undo,
UTF-8/UUID/scalar vectors, malformed/truncated data, entity rejection, meaningful
leader-name edits in conformance, thumbnails/blob ranges, and 20,000-entry/child
paging. Synthetic tests run without samples.

Version 0.6.0 round 2: **203 passed, 0 failed, 0 skipped** with the real artifacts path,
after rebasing onto main with F12/F13.
API build passed with zero warnings/errors. With an empty artifacts path,
**170 passed, 30 skipped, 0 failed**. F1 fixture verification passed: one baseline,
two unchanged files, an identical backup pair and zero changed bytes. New live
tests cover all six effective abilities, HP bounds, explicit stack contributions,
container-key preservation, typed Creator IDs, malformed references, scoped F6
selection/exclusions, intended byte changes, scalar/structural Core history,
F12 paging/truncation, F13 catalogue sorting, exact unsigned class-level sorting,
and the three-party identity/inventory join census.

Version 0.5.0: **168 passed, 0 failed, 0 skipped** with the real artifacts path;
API build passed with zero warnings/errors. With an empty artifacts path,
**144 passed, 24 skipped, 0 failed**, confirming absent samples skip cleanly.
New tests cover all nine sample summary fields, F1 intended-token differences,
unchanged compressed entries, length-changing/escaped JSON preservation, duplicate
properties, integer bounds, failed mixed batches, canonical quantity bindings,
sample absence of direct quantities and Core undo/redo (multi-row edits are one step).
Quantity write tests use synthetic data because the actual sample has no direct
quantity attribute; this does not establish live NewAge stack behavior.

Validation for 0.3.0 on .NET SDK 10.0.400: API build passed with zero warnings/errors;
all 148 plugin tests pass. F1 `verify plugins/BaldursGate3/fixtures.json` passes,
with one generation, identical package twins and zero changes. With an empty
`SAVETOOLKIT_ARTIFACTS` directory, 128 plugin tests pass and 20 sample-dependent
tests skip. The source-size test
runs with the plugin tests; all source files are below 300 physical lines. No plugin
hooks, scripts, build targets or repository tooling are installed.

## SDK requests

No SDK request blocks this subset. After rebasing onto main, F12's generic plugin
view decoder traverses live F8 fields; regression tests compare an edited quantity
across one-row pages and verify explicit incomplete evidence on budget exhaustion.
There is no separate test-only/native decoder to migrate to the optional hook.
Select this decoder with `savediff compare before.zip after.zip --decoder bg3`.
Direct intended-byte assertions remain additional preservation evidence. F13
provides browse-only pickers for the existing read-only references and standard
catalog filtering/sorting, replacing the plugin's full-catalog sorting workaround.
No message-only inspection action or proven float duration exists to opt in.

## Framework feature adoption

| Feature | Status | Where / concrete limit |
| --- | --- | --- |
| F1 SaveDiff | Used | Pinned baseline `fixtures.json`, original whole/component fingerprints, intended-byte diffs and F12 live-value semantic checks. No fake event/control or churn mask. |
| F2 reference packs | Used / expanded | Embedded catalogs and 51 compact game icons, pinned per-file source revisions, mixed-rights attribution and Sources panel; host cache shares assets across documents. |
| F3 rich cells | Used / expanded | Catalog and saved-item image cells, game descriptions with sources, class badges and spell-detail icons; raw fields and confidence/warnings remain available. |
| F4 ReferenceSpec | Used / expanded | Catalog raw IDs, saved CurrentTemplate, origin IDs, saved spell IDs and class UUIDs; F13 browse-only where replacement needs an unimplemented dependency writer. |
| F5 structural undo | Added this round | Two-existing-container-key reorder with scoped inverse memento, stable row IDs, exact bytes on undo. Allocation/removal/transfers remain unsupported. |
| F6 preview/apply | Added this round | Quantity restoration and structural reorder, skipped rows/warnings, query scopes/exclusions and one-step undo. |
| F7 document groups | N/A | Gameplay/summary resources are already members of one atomically rewritten LSPK. Backup twins are identical archival copies, not a demonstrated coupled gameplay invariant. |
| F8 collections | Used / expanded | Catalogs, saved items, live values, slots, classes and spell books; bounded text/filter/sort pages. |
| F9 graph/map | Added this round | Saved spell presence graphs, text alternatives and field targets, Unknown states, no guessed prerequisite edges. Map N/A: no proven saved discovery/coordinate transform. |
| F10 exact values | Used | IntegerSpec/Int128 transports exact stored u32/i32/u64 values (including class levels). Nullable/union/exact-JSON specs N/A: supported mutable fields have fixed binary types or existing bounded integer JSON tokens. |
| F11 shared codecs | N/A | No Kraken or SQLite payload exists in this supported Larian path. LZ4/zstd/zlib use the existing attributed managed codecs. |
| F12 | Added this round | Generic `bg3` decoder traverses live F8 rows with host budgets; one-row-page changed-quantity and truncation tests. No private decoder needs a separate hook. |
| F13 | Added this round | Browse-only read-only reference fields; host category filtering and declared Key/Name/Category sorting. Inspection actions N/A: both actions mutate storage. Float duration N/A: no proven mutable float timer semantics. |

## Sources and attribution

| Dataset / code | Source and author | Licence / retrieval | Changes and scope |
| --- | --- | --- | --- |
| Summary level cap (12) | [bg3.wiki contributors, Experience](https://bg3.wiki/wiki/Experience) | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), 2026-10-28 | One factual base-game cap; no prose, progression table or new pack copied. Unknown modded levels use the observed maximum. Source is also adjacent to the constant, in Info.References and field descriptions. |
| Summary/quantity write research | [Hawk-on FIELD_MAPPING.md, pinned revision](https://github.com/Hawk-on/bg3-save-editor/blob/6a62466c77ad4939fc5e6fdcbc2e4188efdfcd15/FIELD_MAPPING.md); [Norbyte PackageWriter.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageWriter.cs) | MIT, revisited 2026-10-28 | Confirms metadata/runtime distinction and existing outer hash algorithm. No new upstream implementation copied or executed; live gold heuristics rejected. |
| `bg3_templates` (4 records) | [bg3.wiki contributors](https://bg3.wiki/): [Gold](https://bg3.wiki/w/index.php?title=Gold&oldid=419021), [Potion of Healing](https://bg3.wiki/w/index.php?title=Potion_of_Healing&oldid=421944), [Longsword](https://bg3.wiki/wiki/Longsword), [Shortbow](https://bg3.wiki/w/index.php?title=Shortbow&oldid=423168) | Adaptation [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), 2026-10-28 | Factual labels/technical GUIDs, original uncertainty annotations plus game icons and flavour-text excerpts; no gameplay limits. Exact GUID candidates only. Game material has separate rights below. |
| `bg3_origins` (3 records) | [bg3.wiki contributors](https://bg3.wiki/): [Lae'zel](https://bg3.wiki/w/index.php?title=Lae%27zel&oldid=409907), [Shadowheart](https://bg3.wiki/w/index.php?title=Shadowheart&oldid=438521), [Dark Urge](https://bg3.wiki/w/index.php?title=The_Dark_Urge_%28Origin%29&oldid=434137) | Adaptation [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), 2026-10-28 | Factual labels mapped to SaveInfo Origin keys; original uncertainty annotations. Not live identity/name mappings. |
| `bg3_items` (6 records) | Pinned [Supply Pack](https://bg3.wiki/w/index.php?title=Supply_Pack&oldid=423731), [Camp Supply Sack](https://bg3.wiki/w/index.php?title=Camp_Supply_Sack&oldid=416884), [Potion of Healing](https://bg3.wiki/w/index.php?title=Potion_of_Healing&oldid=421944), Gold, Longsword and Shortbow revisions | Adaptation [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), 2026-10-28 | Factual item labels plus UUID/UID candidates, compact icons and short game flavour-text excerpts; no gameplay limits. Game material has separate rights below. |
| `bg3_equipment` (6 records) | [Equipment](https://bg3.wiki/w/index.php?title=Equipment&oldid=405329), pinned weapon/container and companion pages | Adaptation [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), 2026-10-28 | Equipment/category candidates plus weapon/container icons and game flavour-text excerpts; sets are not item identities and do not receive item icons. |
| `bg3_spells` (21 records) and `bg3_passives` (5 records) | Pinned [Spells](https://bg3.wiki/w/index.php?title=Spells&oldid=433193), [Cantrips](https://bg3.wiki/w/index.php?title=Cantrips&oldid=420539), [Guidance](https://bg3.wiki/w/index.php?title=Guidance&oldid=413520), [Fire Bolt](https://bg3.wiki/w/index.php?title=Fire_Bolt&oldid=373237), [Passive feature](https://bg3.wiki/w/index.php?title=Passive_feature&oldid=421706) | Adaptation [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), 2026-10-28 | Spell/feature page UID candidates with icons and short game-tooltip excerpts; original label adaptation remains CC BY-SA 4.0. Game material has separate rights below. Source/preparation and passive-state semantics remain unknown. |
| `bg3_companions` (7 records) and `bg3_camp_supplies` (8 records) | Pinned [Companions](https://bg3.wiki/w/index.php?title=Companions&oldid=439368), origin pages and [Camp Supplies](https://bg3.wiki/w/index.php?title=Camp_Supplies&oldid=409610) | Adaptation [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), 2026-10-28 | Character UUID/UID and supply-value candidates; supplies now carry icons and game flavour-text excerpts (separate game rights below). No party ownership, approval or inventory totals. |
| `bg3_game_icons` (51 unique assets) and game descriptions (39 distinct excerpts on 48 records) | Larian Studios game assets via [bg3.wiki](https://bg3.wiki/); every page and file revision in [assets.source.md](SaveToolkit.Plugins.BaldursGate3/ReferencePacks/bg3.starter.en/assets.source.md) | Rights reserved by Larian Studios; used with attribution per owner decision 2026-10-29. Retrieved 2026-10-29; [asset licensing](https://bg3.wiki/wiki/Help:Licenses), [Larian policy](https://larian.com/fan-content-policy) | Verified image headers, resized to at most 64×64, WebP or smaller PNG-8, deduplicated images; tooltip/flavour-text excerpts at most 25 words per source page, normalised markup/spacing and retained uncertainty annotations. No legacy wiki editorial prose imported. |
| `bg3_classes` (12 base-class UUIDs) | [DanFinchUK gamedata.json, pinned 9578ff7c](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/data/gamedata.json), class icon files via bg3.wiki | No licence published; reused with attribution per owner decision 2026-10-28. Icons use the separate Larian asset notice above; retrieved 2026-10-29. | Selected GUID/name facts, linked local badges, original uncertainty notes; no implementation copied/executed, no party identity or class-authority inference. |
| Independent LSMF directory/EntityId and PS5 live-storage reader/writer | [DanFinchUK format documentation, pinned revision](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/FORMAT.md), independently measured PS5 bytes | No licence published; reused with attribution per owner decision 2026-10-28 | Behaviour was reimplemented independently; no repository code, executable or save fragment was copied or run. PS5 directory, Creator/template, party, stacks, containers, Health/Stats/classes/spells were measured against bytes. See [LSMF.md](LSMF.md) and [NEWAGE.md](NEWAGE.md). |
| Existing LSPK/LSF/LSX and UUID format ports | [Norbyte/LSLib](https://github.com/Norbyte/lslib/tree/6f5f6987d10d8485876b8368431c948e4dea4f83) | MIT; [licence notice](THIRD-PARTY-NOTICES.md) | Managed preservation-first implementations; format files listed below. |
| Managed LZ4 codec | [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4), Milosz Krajewski | MIT; [notice](THIRD-PARTY-NOTICES.md) | Dependency, not copied source. |
| Managed zstd codec | [ZstdSharp](https://github.com/oleg-st/ZstdSharp), Oleg Stepanischev and contributors | MIT; [notice](THIRD-PARTY-NOTICES.md) | Dependency, not copied source. |

## Licence exclusions

Only the following sources or material were refused. Unlicensed factual research is
permitted by the owner decision dated 2026-10-28; its behaviour is independently
reimplemented and attributed in the table above.

| Source and revision | Licence found (exact) | Why it was refused | What we did instead | What would unblock it |
| --- | --- | --- | --- | --- |
| [bg3.wiki copyright policy and legacy contributions](https://bg3.wiki/wiki/bg3wiki:Copyrights), retrieved 2026-10-28 | CC BY-NC-SA (legacy expressive contributions) | Non-commercial terms remain a refusal for legacy wiki editorial prose; this is separate from directly attributed Larian game assets. | Used attributed CC BY-SA label facts and technical mappings plus separately attributed Larian icons/game-text excerpts; kept legacy wiki editorial content out of the pack. | A source-specific CC BY/CC BY-SA or other compatible relicensing grant covering the exact material. |
| Larian Studios Baldur's Gate 3 art, icons and extracted game text, exact source/file revisions in assets.source.md | Rights reserved by Larian Studios; used with attribution per owner decision 2026-10-29 | **Resolved: imported (owner decision 2026-10-29).** | 51 compact icons and 39 distinct game-description excerpts embedded with attribution. | No remaining publisher-asset policy blocker for this import. |

The starter factual-label adaptation remains **CC BY-SA 4.0**, separately from code and Larian game assets. Retain
its attribution, source links, modification notice and share-alike terms in
redistributed/adapted packs. See the embedded
[data.source.md](SaveToolkit.Plugins.BaldursGate3/ReferencePacks/bg3.starter.en/data.source.md)
and per-dataset `pack.json` attribution. The UI includes provenance on every name
cell, and the framework Sources panel shows the resolved datasets.

The [wiki copyright policy](https://bg3.wiki/wiki/bg3wiki:Copyrights) distinguishes
legacy CC BY-NC-SA content from dual-licensed contributions. This factual-label
subset copies no legacy wiki editorial text. Larian game-text excerpts and icons now have
separate provenance under the 2026-10-29 owner decision in assets.source.md and pack.json.
The remaining NC exclusion does not apply to these separately sourced publisher assets.

Primary reference: [Norbyte/LSLib](https://github.com/Norbyte/lslib), MIT, inspected at
revision `6f5f6987d10d8485876b8368431c948e4dea4f83`. Adapted format algorithms and type
IDs retain the upstream licence in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
Downloaded source was inspected as text; no upstream executable was run.

- [PackageFormat.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageFormat.cs): headers, row layouts, flags.
- [PackageReader.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageReader.cs): file lists, version dispatch, solid/multipart behavior.
- [PackageCommon.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageCommon.cs): payload selection and deletion markers.
- [PackageWriter.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageWriter.cs) and [PackageVersion.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Enums/PackageVersion.cs): MD5+1 algorithm, physical/build-input order and header-relative 64-byte padding. See [integrity research](INTEGRITY.md) for history and issue sources.
- [LSFCommon.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSF/LSFCommon.cs) and [LSFReader.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSF/LSFReader.cs): section order, layouts, attribute chains, keys, strings/translations.
- [LSFVersion.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Enums/LSFVersion.cs): BG3 extended header, node-key and patch version numbering.
- [LSFWriter.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSF/LSFWriter.cs): section compression/order and attribute offsets. Our writer patches original tables instead of rebuilding names and trees.
- [Compression.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Compression.cs) and [compression flags](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Enums/Compression.cs): codec dispatch and framing.
- [NodeAttribute.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/NodeAttribute.cs) and [BinUtils.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/BinUtils.cs): type IDs, scalar/vector layout, GUID display.
- [LSXReader.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSX/LSXReader.cs): XML resource structure; our explorer retains all elements generically.
- [clemarescx/bg3d](https://github.com/clemarescx/bg3d): inspector precedent and NewAge opaque-blob context.
- [Hawk-on field mapping](https://github.com/Hawk-on/bg3-save-editor/blob/master/FIELD_MAPPING.md): SaveInfo party/level/XP context and distinction between template amounts and runtime gold. No runtime-gold control adopted.
- [Wahkah/BG3SE research](https://github.com/Wahkah/BG3SE/blob/main/README.md): independent write-integrity concerns and LSMF context; no decoder code copied and no gameplay mappings adopted.
- [Serofix-code/bg3-save-editor](https://github.com/Serofix-code/bg3-save-editor): reported repack warnings and metadata versus live-state distinction.
- [BG3 format research](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/FORMAT.md): leader-name metadata, JSON party/level/XP summary, ownerlist conventions and distinction from live LSMF state. No licence published; reused with attribution per owner decision 2026-10-28. Curated summaries are Inferred, not verified live stat controls.
- [K4os LZ4 1.3.8](https://www.nuget.org/packages/K4os.Compression.LZ4.Streams/1.3.8) and [ZstdSharp.Port 0.8.8](https://www.nuget.org/packages/ZstdSharp.Port/0.8.8): managed MIT codec dependencies; see notices.

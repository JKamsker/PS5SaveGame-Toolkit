# God of War Ragnarök (`gow-ragnarok`)

**Maturity: Experimental (v0.5.0).** Opens decrypted PS5 gameplay/history saves and the separate preferences record.
Provides metadata, five length-bounded wallet containers, F8 resource/XP/raven counters,
nested gear/skill ID candidates, 132 grouped raw flag records, attributed F2 catalogs,
integrity evidence and lazy raw word/byte views. Existing decoded wallet counters and play time
are editable, with an F6 restore-to-imported-values action. **Integrity unknown — the game may reject
this save.** Integrity dependencies and the complete serialization grammar remain unresolved.
An unsuccessful checksum search does not establish game acceptance. Preferences remain read-only.

## Framework feature adoption

| Feature | Status and evidence |
| --- | --- |
| F1 SaveDiff | **Used**: `RagnarokEvidenceDecoder` is the bounded decoder hook; grammar spans expose stable wallet, flag and integrity paths. |
| F2 reference packs | **Used**: PS5/community/skill packs plus attributed resource, skill, gear, rune and map assets; 961 runtime IDs, four tree groups and 1,682 explicitly cross-game 2018 candidates; direct fallback and Sources-panel attribution are tested. |
| F3 rich table cells | **Used**: confidence badges/warnings, detail fields, attributed icon cells, attribution cells and per-row inspect/restore actions. |
| F4 `ReferenceSpec` pickers | **Used**: catalogue-backed resource IDs in F8 pages and read-only skill-reference fields; unknown IDs remain raw. |
| F5 structural edits | **N/A**: no safe add/remove/reorder grammar or free-slot/ownership invariant is proven. |
| F6 action planning | **Used**: import-bounded restore supports selected/document/query scopes, skipped rows, preview revalidation and grouped undo/redo. |
| F7 document groups | **N/A**: no proven coupled gameplay/preferences write contract; editing both would guess dependencies. |
| F8 queryable tables | **Used**: seven server-backed collections page resources, IDs, words, flags and directory records. |
| F9 graphs/maps | **Added this round**: three attributed weapon skill DAGs and a nine-realm map with a compact Midgard background, external guide links, text alternatives and stable targets. |
| F10 exact/nullable/union values | **N/A**: the proven grammar contains bounded integer words and UTF-8 text only. |
| F11 shared codecs | **N/A**: no Kraken/SQLite payload was detected; opaque bytes are preserved. |
| F12 SaveDiff F8 traversal | **Used**: the production decoder hook is independently bounded by host sink limits. |
| F13 non-mutating inspect/browse | **Used**: resource inspections are `Mutates=false`; reference fields are browse-only and preserve history/bytes. |

The skill dataset is a presentation catalogue, not a saved-state crosswalk. Graph states, saved hashes,
XP ownership and eligibility are `Unknown`; source links, compact thumbnails and attribution appear in
records, details and graph descriptions. Realm markers remain unpositioned external-guide references;
the embedded Midgard tile mosaic is visual orientation only. See the `assets.source.md` sidecars under
`ReferencePacks/` for transformations and attribution.

## Enabled writes and their evidence

The 2026-10-28 owner decision “Enable writes. Keep warnings.” supersedes the earlier evidence gates
in the plan and round-2 handoff. These are **3 write operations/families**, not three individual cells.
Console validation is tracked separately in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Existing wallet u32 counters, including resource, inferred XP, raw raven and unmapped ID rows in decoded containers | Inferred quantity/container role; unknown gameplay semantics retain raw labels | Counted 16-byte rows and exact nested boundaries across 76 primary records; [Continue? reader](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py), existing attributed ID packs; edit/reparse/F1 byte-range tests | **Integrity unknown — the game may reject this save.** No authentication bytes are guessed. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): separate small resource/XP/raven edits, UI effect, normal use/progression, native re-save and reload |
| Metadata play-time u32 at `190` | Inferred seconds; not validated on console | PC reader offset/units plus coherent PS5 corpus; edited round trip, byte-range and Core undo/redo tests | **Integrity unknown — the game may reject this save.** | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): reduce by one second, check displayed time, native re-save/reload and forward play |
| F6 restore selected/all existing wallet counters to imported values | Inferred; import ceiling is not a legal game maximum | Pure preview, old/new counters, skipped rows, warning revalidation, F8 selected/query scopes, atomic apply and Core grouped undo/redo | **Integrity unknown — the game may reject this save.** No derived totals, rewards or dependencies are invented. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): reduce two counters, preview/restore both, verify one-step undo/redo and console persistence |

Every counter's range is **0..its own imported value**, intersected with u32 storage. Legal maxima
are unknown; the import is the conservative observed ceiling, not a promise that every lower value
is gameplay-safe. Zero imports permit only zero. Reductions can be restored within the same document;
reopening an edited file establishes its newly imported values as the ceilings. The restore action
does not grant new rows or raise a counter above its import. Duplicate IDs/containers remain separate.
Individual fields, action descriptions, previews, direct action outcomes and edit/apply outcomes
carry explicit integrity and console/dependency warnings. Canonical counter bindings are separate
from read-only raw diagnostics, so expanding a raw alias cannot replace an editable binding.

Only decoded fixed-size words are patched. IDs, counts, lengths, metadata words, padding, links,
opaque regions and trailing bytes are preserved. Structural inventory edits, gear/equipped references,
quest completion, difficulty and settings writers remain **unimplemented**: their structure/dependency
repair or field meaning/range is not established. They are not waiting solely on console permission.

The wallet length prefix must follow the complete directory/secondary arrays. Counted arrays,
legacy wallet rows and the complete nested wallet/link/flag sequence must end before the separate
EOF sentinel. Overlapping layouts are rejected before editable bindings are exposed; exact adjacent
boundaries remain supported. Malformed-layout and boundary-control tests cover these write guards.

## Titles and file roles

Catalogued Ragnarök PS5 IDs: **PPSA08329, PPSA08330, PPSA08331, PPSA08332, PPSA08333, PPSA08334,
PPSA25043, PPSA25044**. Only **PPSA08330** has sample coverage. Other IDs require the same observed
header/layout; their catalogue identity does not establish save compatibility. No PC/PS4 header is
accepted. The source catalogue for all eight IDs is linked below.

God of War (2018) is excluded: its public PS4 editor uses a multi-slot `memory.dat` and fixed
slot-relative offsets. Shared resource IDs do not establish a shared container. No matching native
PS5 sample or title ID was established.

| File | Role | Observed coverage |
| --- | --- | --- |
| `GOWRSAVE0`–`GOWRSAVE65` | Gameplay/history slots; suffix alone does not establish chronology | 76 primary records, 833,714–3,217,731 bytes |
| `userpreferences` | Separate settings | 2 primary records, exactly 696 bytes |
| `sdimg_sce_bu_*` twins | Console backups, opened independently | All 78 pairs byte-identical |

All **156 archive records**, including duplicate paths and backups, open and serialize byte-identically.
There are 66 distinct gameplay file names. No sample files were copied into the repository.

## Established layout

Little-endian numbers, hexadecimal offsets. Structural constants have unknown semantics unless stated.
These are measured invariants of the supplied revision, not a complete game schema.

| Offset | Layout / interpretation |
| --- | --- |
| `00` | Timestamp-like u32; upper word at `04` is zero in samples. Unix-seconds meaning **Inferred**. |
| `08` | u32 `8` gameplay, `31` preferences. |
| `0C` | u32 `00513040`, supported revision word; exact meaning unknown. |
| `10`, `14` | Gameplay constants `000002B8`, `008C0400`; no size/dimension semantics asserted. |
| `18` | Variable u32, **Unknown**; not the file length. |
| `40`–`43F` | Gameplay metadata; timestamp-like prefix, `35` at `48`, same revision at `4C`. |
| `50`–`18F` | NUL-terminated UTF-8 location/quest text, **Verified** from readable sample content. |
| `190` | u32 play seconds, **Inferred** from the PC reader and coherent PS5 progression. |
| `1BC` | u32 save counter, **Inferred** from the PC reader and snapshot progression. |
| `440` | u16 `61C0`, observed directory marker; meaning unknown. |
| `442` | u32 ID count: 798 in all 76 primary gameplay records. |
| `446` | That many strictly increasing, unique u64 IDs; no name-hash algorithm established. |
| after IDs | Same number of u32 words, only `0`/`2` observed; correspondence to IDs **Inferred**. |
| after words | u32 count then u64 IDs: 10 in every record, each also in the sorted array. |
| variable | Exactly one u64 `00004895C643225F`, the PC reader's hero-wallet ID. |
| wallet `+08` | u32 row count, 0–211 in this corpus. |
| wallet `+0C`, `+10` | Counts of subsequent u64 IDs and u32 index-like words (roles **Unknown**). |
| wallet `+14` | Counted 16-byte rows: u64 ID, u32 inferred quantity, u32 unknown metadata. |
| EOF `-04` | Gameplay `FFFFFFFF`, observed sentinel, **not a checksum**. |

Round 2 establishes the following additional framing across **all 76 primary gameplay records**:

- The u32 immediately before the hero marker is the byte length of a block containing five
  consecutive containers. Each is `u64 ID; u32 resourceCount, idCount, wordCount;`
  `resourceCount × (u64 ID, u32 quantity, u32 metadata); idCount × u64; wordCount × u32`.
  Every block is consumed exactly. The index-like words are kept raw: early saves disprove a
  zero-based reference into the adjacent ID list. No equipped/owned relationship is inferred.
- Next is `u16 byteLength; u32 count; count × 22 opaque bytes`. Its length is exactly `4 + 22×count`.
- Next is `u16 byteLength; u64 FFFFFFFFFFFFFFFF; u32 1; u32 groupCount`, followed by
  `groupCount × (u64 group ID; u32 count; count × (u64 ID; four raw bytes))`.
  All samples have five groups and 132 rows (28/26/27/23/28); the payload consumes its length exactly.
  Their quest/skill/collectible meanings remain **Unknown**.

In generation-zero SAVE0, the wallet block ends at `1D5CC` and the grouped flags end at `1DC50`.
The five resource counts are 211/10/8/0/3; the hero has 261 subsequent IDs and 159 index-like words.
Only HERO and the blacksmith-chest role have research labels, both **Inferred**. Other containers
keep their raw IDs. These invariants establish structure, not a complete serialization grammar.
A zero enclosing-length word retains the legacy hero-only fallback (used by minimal fixtures);
it does not claim nested structures or authentication.

## Meaning and confidence

The embedded `ReferencePacks/gow-ragnarok.ps5.en` pack is version `1.0.0` and contains 31 factual
hash/name/category records. It records `limit: null` deliberately: no source or controlled capture
proves a legal game maximum. The old `Resources/ResourceNames.json` remains a direct-call fallback
for legacy anchor scanning. Direct name lookups now load the original F2 pack as their fallback,
so all 31 names/categories agree with pinned calls. XP candidates are named from the newly imported
PC runtime table, with PS5 ownership still **Inferred**. No network or filesystem access is performed by the plugin.

The unlicensed-source round adds **2,647 reference records** in two attributed packs:
961 runtime IDs (233 specific names, 728 explicit Unknowns), four PC skill-tree group IDs and
1,682 2018 ID/name/category/internal-name candidates. The latter always carry `2018 candidate:`
and Unknown confidence; they are not proof of Ragnarök gear or quest identity. Across all 76 primary
saves, 211 distinct runtime IDs match counted resource rows (78 specifically named); all 16 matching
2018 IDs are already in the runtime table. Neither item table matches any parsed directory,
nested-ID or individual flag ID. Four group IDs match every save: Axe/Blades/Spear/Atreus,
with 28/26/27/28 rows; the fifth group and every individual skill/flag bit remain Unknown.
Runtime names reach F8, browse-only F4, counter/inspection/F6 labels and attributed F12 labels;
group names/provenance reach grouped flags and F12. Names do not alter enabled write ranges or warnings.

Ragnarök name-to-ID correspondences are substantiated by the source and exact sample matches where
present. Wallet ownership, quantities, time, counter and timestamp meaning stay **Inferred** because
they have not been compared with the PS5 UI. Unmapped IDs, words outside wallet rows, raw settings
and raw word interpretations are **Unknown**. All occurrences remain separate; they are never summed.
An absent ID does not imply zero inventory. Missing Valhalla IDs do not establish Valhalla support.

The PS4 difficulty offset `2F442` is not portable: 8/76 primary gameplay records contain values outside
its documented enum. No named difficulty field is published.

Preferences receive exact-length/header validation and byte pages covering all 696 bytes. Individual
audio, controller, language and accessibility meanings are not guessed. The two primary records
differ at 29 positions; settings ranges and integrity remain unknown.

## Integrity, compression and preservation

Readable metadata and matching resource records establish accessible plaintext structures, not the
absence of embedded compression/encryption. Reconnaissance found no recognized framed codec or
successful initial zlib candidate. No decryption key or compression transform is applied.

Standard CRC32/IEEE and Adler32, plus seed-zero xxHash32/64 and MurmurHash3 candidates were checked against header/footer
words and selected directory/wallet spans: no match. Round 2 adds CRC32C (canonical and LE),
full MD5, SHA-1, SHA-256 and SHA-512 digests over 11 explicitly named ranges (10 when the hero
resource list is empty): whole file; without EOF sentinel; after timestamp; after outer header;
after metadata; metadata alone; directory arrays; wallet block with/without its length;
grouped flag block with length; hero resources alone. Full digest bytes are searched at **every
byte alignment**, including the last possible target. Across 76 primary records there are
**4,956 coverage/encoding comparisons, 0 matches**. This does not rule out truncated digests,
zeroed/omitted embedded fields, custom seeds, compressed coverage or a keyed MAC/signature.
No key or authentication routine was found; arbitrary guessed-key HMAC tests are not evidence.
The lazy Integrity tab and F1 hook expose reproducible digests/ranges without making acceptance claims.

Completion-round research added reflected CRC32-IEEE and CRC32C range algebra over every primary and
backup record, testing whole/no-footer/after-time/after-header/after-metadata coverage, embedded
header/footer words, both byte orders, zeroed and omitted fields, standard XOR-outs and custom-seed
recovery. It also tested MD5/SHA-1/SHA-256/SHA-512 at 4/8/full digest widths for unchanged, zeroed,
omitted, reversed and prefix/suffix encodings at candidate header/footer fields. The sample-backed
results are **15,616 CRC field/range comparisons and 81,216 embedded digest comparisons with zero
matches**, and no custom seed shared by three independent primary generations. This expands the earlier
4,956 whole-digest comparisons; it still cannot rule out keyed MACs/signatures, truncated fields outside
the tested locations, custom coverage, compression or external authentication. No checksum is recomputed.
Coverage/seed searches were finite. See the
[research log](RESEARCH.md) for methods and negative results.

The parser rejects unsupported headers, known malformed metadata, unsorted IDs, missing secondary
IDs, invalid array counts, missing/duplicate wallet markers and missing observed EOF sentinels.
**It cannot detect arbitrary corruption in opaque data or authenticate a save.**

`RagnarokGrammar` is a clean-room F1 evidence harness. It emits observed header/metadata/array/wallet
spans, preserved opaque gaps, stable directory/wallet hash candidates, fitting opaque u32 length
candidates and recognized compression signatures. Length and compression candidates are explicitly
marked unproven; the harness never traverses them or rewrites them. `RagnarokDiffDecoder` feeds those
values to `SaveSetDiffer` for generation-to-generation semantic reports.

`CanEdit` is true for gameplay and false for preferences. `BoundSaveDocument` rejects invalid edits
atomically, with plugin-side integer/type/range validation even for direct SDK callers. Unknown actions
are rejected; direct restore invocation changes nothing and directs the caller to F6 preview/apply.
`Serialize()` re-parses the current buffer and throws if the output violates the supported grammar,
then returns an isolated copy for the same path. This grammar check is not authentication validation.
Without edits the output remains byte-identical. Unknown bytes/padding/tails are preserved; blob pages
also return copies. Core reopens output before export.

## Bounds and tests

Open reads one file, never all 66 slots or siblings. Files are capped at 32 MiB, array counts at
65,536, resource occurrences at 4,096. No large views are built during Open. The seven F8 collections (`resources`, `xp`, `collectibles`, `nested-ids`, `index-words`,
`flags`, `directory`) search/filter/sort the complete indexed set, with stable document row IDs,
exact filters, numeric quantity sorting and at most 200 rows per page. They use offset paging;
unsupported cursors and columns are rejected. Collection cells are bound only for the requested page. Small diagnostic/secondary tables and legacy helper expansions use lazy 256-row pages. Raw words use 1 KiB pages under 64 KiB lazy groups, with final unaligned bytes
shown individually. Preferences use 256-byte pages. Hex reads are capped at 64 KiB.

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/GodOfWarRagnarok/SaveToolkit.Plugins.GodOfWarRagnarok.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
```

**Unlicensed-source round: 86 plugin tests passed, 0 failed, 0 skipped; API build: 0 warnings/errors.**
The artifact environment variable is exported in the same test command. Coverage includes all 156
records and byte-identical serialization, all 76 primary nested grammars and serialize/reopen byte
identity, nested length/count/identity corruption, F8 global search/filter/numeric sort/bindings,
attributed pack validation/direct-call fallback, F1 generation comparison, published digest vectors,
F9 graph/map validation, F12 decoder bounds and F13 non-mutating inspection. Tests skip when samples
are absent; such a run is not validation.
Conformance now exercises real edits. Additional coverage edits every positive decoded counter over
all 76 primary records, checks serialize/reopen values and F1 diffs restricted to intended u32 ranges,
rejects invalid types/ranges and mixed batches, and verifies Core scalar/grouped undo/redo, F6 stale
or tampered previews, skipped rows, empty selections and F8 query scopes. A synthetic positive
non-hero-container fixture tests that writer/history path without claiming console evidence. A
synthetic wallet-marker collision proves Serialize rejects output that cannot be re-parsed.
No console test is claimed.

The new tests verify all 2,647 imported records through the real F2 loader, pinned/direct name and
catalog agreement, ID byte order, full-corpus resource/group joins and negative gear/quest joins,
read-only/Unknown cross-game F4 behavior and attributed F12 labels. Hash-checked regeneration is
byte-identical for all five new pack JSON files. All plugin C# files remain below 300 lines.

Known generation-zero `GOWRSAVE0`: 3,215,266 bytes, play-time word 138,041, counter 1,797,
Hacksilver wallet word 288,638 at `1BC90`, Rawhide 52, Chaos Flame 0, XP-ID word 84,725.
Its next archive occurrence is 834,621 bytes, play-time word 568, counter 1 and an empty hero wallet.
These are byte-level regression facts, not proof of player-facing meanings.

## Open work

- Recover full nested serialization/length/type rules, including regions surrounding wallets.
- Determine all integrity dependencies, coverage and seeds; replace the unknown-integrity warning
  only when the actual algorithm and acceptance are demonstrated.
- Validate quantities, XP role and time units against PS5 UI or controlled saves.
- Locate difficulty through structure; map preferences with one-setting-at-a-time captures.
- Obtain other regional/revision and Valhalla samples; test in-game before any Stable designation.

## Sources

Research date: 2026-10-28. References were read as text; no downloaded editor/trainer was executed.
No third-party implementation was copied. Existing community datasets retain their upstream GPL-3.0
licensing; newly unblocked factual tables carry the exact owner-decision licence statement,
not an invented upstream grant. See pinned revisions, hashes, sidecars and plugin-local notices.

| Source | Used for |
| --- | --- |
| [Local reconnaissance](../../docs/formats/PPSA08330.md), [method](../../docs/formats/README.md) | Corpus, headers, compression-search limits. |
| [SaveWizard codes, inspected revision](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/4c32596efde54af7bec390b298265f09c929d94d/SaveWizard-Codes/GOW%20Ragnarok%20%5BSaveWizard%5D.txt) | Resource IDs, chest anchor, PS4 difficulty lead; whole cheats not copied. |
| [Apollo SaveWizard reference](https://bucanero.github.io/apollo-lib/savewizard.html) | Types 8 search, 2 pointer-relative u32 write, 9 pointer and A literal write. |
| [Apollo CUSA34384 patch](https://github.com/bucanero/apollo-patches/blob/main/PS4/CUSA34384.savepatch) | Independent code/difficulty context; no integrity algorithm supplied. |
| [Continue? PC reader, inspected revision](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py) | Wallet ID, counted rows, metadata/time/counter. PC revision is `00660404`; transferred ideas checked on all PS5 samples. |
| [2018 layout data](https://github.com/xcier/GoW-2018-PS4-editor/blob/main/app/core/gow2018_data.py), [item data](https://github.com/xcier/GoW-2018-PS4-editor/blob/main/app/resources/database/gow2018_items.json) | XP/EconomyXP ID and reason to exclude the 2018 container. |
| [Codes Prince console editor](https://codesprince.com/2022/12/06/ps4-ps5-save-editor-for-gow-ragnarok/), [PC editor](https://codesprince.com/2024/09/24/pc-editor-for-gow-ragnarok/) | Separate platform/version support; no public parser/integrity algorithm obtained. |
| [GOWTool](https://github.com/kainotoa/GOWTool), [smpack](https://github.com/iArtorias/smpack) | Asset/WAD leads; no proof that asset hashing or WTOC/LZ4 applies to saves. |
| [Regional catalogue](https://sharpemu.app/game/PPSA08329/), [PPSA08329](https://prosperopatches.com/PPSA08329), [PPSA08330](https://prosperopatches.com/PPSA08330), [PPSA08331](https://prosperopatches.com/PPSA08331), [PPSA08332](https://prosperopatches.com/PPSA08332), [PPSA08334](https://prosperopatches.com/PPSA08334) | Title identity/IDs; not save compatibility. |
| [Leviathan Axe](https://godofwar.fandom.com/wiki/Leviathan_Axe), [Blades of Chaos](https://godofwar.fandom.com/wiki/Blades_of_Chaos), [Draupnir Spear](https://godofwar.fandom.com/wiki/Draupnir_Spear), [Skill Labors](https://godofwar.fandom.com/wiki/Skill_Labors_%28Ragnar%C3%B6k%29) | CC-BY-SA skill names, branches, explicitly listed costs/levels and prerequisite facts; transcribed into the separately attributed skill pack with five downscaled showcase thumbnails and unknown saved state. |
| [PlayStation combat guide](https://www.playstation.com/en-us/games/god-of-war-ragnarok/god-of-war-combat-refresher-course/) and [God of War Wiki gear pages](https://godofwar.fandom.com/wiki/Berserker_Set) | Short weapon/gear descriptions and six compact gear/rune/map catalogue records; no saved ownership or equipped-state join is asserted. |
| [MapGenie community maps](https://mapgenie.io/god-of-war-ragnarok/maps/midgard) | Four z9 tiles stitched into a 512x512 WebP Midgard background. Coordinates remain link-only and unasserted. |

## Sources and attribution

The embedded pack's dataset is attributed in `ReferencePacks/gow-ragnarok.ps5.en/pack.json` and in
each record's `source` field. Names and broad categories are adapted from [God of War Wiki resource
entries](https://godofwar.fandom.com/wiki/Resources/GOW_%28Ragnar%C3%B6k%29), licensed CC-BY-SA-3.0
under [Fandom's licensing terms](https://www.fandom.com/licensing). Hash bytes were normalized from
the cited community resource table and checked against PPSA08330 occurrences; no trainer code, game
art, or copied prose is embedded in the resource table. The pack preserves share-alike attribution and states unknown
legal limits instead of inventing ranges. The fallback JSON uses the same 31 factual identities
and is covered by the same attribution.

The additional `gow-ragnarok.community.en` v1.0.0 pack adds **38 records** to the original 31:
36 PS4 community resource/key-item ID candidates (names deliberately **Unknown**) and two
raven-related counter identities (PC-to-PS5 role **Inferred**). Thus the three catalogs contain 69
records; the separate XP fallback remains an original observation with an inferred role. The new
collectible hashes occur in 40/76 and 56/76 primary PS5 hero wallets respectively. No derived raven
completion formula or legal maximum is displayed. Missing records remain raw IDs.

| Dataset / role | Pinned source and author | Licence / changes |
| --- | --- | --- |
| Original 31 resource names/categories and fallback | [God of War Wiki contributors](https://godofwar.fandom.com/wiki/Resources/GOW_%28Ragnar%C3%B6k%29), retrieved 2026-10-28; existing dataset retained | [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/); normalized names/categories; IDs cross-checked against cited community resources; no new wiki text imported in round 2. |
| 36 additional ID candidates | [Apollo patch contributors / bucanero, 273f032b](https://github.com/bucanero/apollo-patches/blob/273f032b4c31c017fed5c1588f209b8faf87ba6c/PS4/CUSA34384.savepatch) | [GPL-3.0](https://www.gnu.org/licenses/gpl-3.0.html); extracted unique literal search IDs and section categories, omitted writes and preset quantities; no invented item names. |
| Two raven-related counters | [Alexsander720 / Continue? contributors, 51e19b2a](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py) | [GPL-3.0](https://www.gnu.org/licenses/gpl-3.0.html); factual IDs/roles converted to LE byte keys, added PS5 occurrence evidence; no code/formula ported. |
| Hero / blacksmith-chest research labels | Continue? revision above and [SaveWizard-Resources contributors, 4c32596e](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/4c32596efde54af7bec390b298265f09c929d94d/SaveWizard-Codes/GOW%20Ragnarok%20%5BSaveWizard%5D.txt) | [GPL-3.0](https://www.gnu.org/licenses/gpl-3.0.html); factual labels only, both inferred; source displayed in container table description. |
| Nested framing and generic XP candidate | Toolkit contributors, [issue #19](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/19) | Project GPL-3.0-or-later; original byte observations, no third-party implementation copied. |
| Weapon skill references and thumbnails | [Skill pack sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.skills.en/skills.source.md) and pinned wiki pages above | CC BY-SA 3.0/share-alike for factual text; five downscaled visual thumbnails use the owner-decision rights-reserved attribution; presentation keys are not save hashes. |
| Gear, rune and map visual catalogue | [Asset pack sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.assets.en/assets.source.md), PlayStation combat guide, God of War Wiki and MapGenie | Rights reserved by the named holders; used with attribution per owner decision 2026-10-29. Six records; no saved ownership or coordinate join. |
| 961 runtime identities | [mooofin / community spreadsheet contributors, 7b173021](https://github.com/mooofin/God-of-War-Ragnarok/blob/7b1730217b92d27ab910d6d421efbd503cc109b2/item%23.zip) | No licence published; reused with attribution per owner decision 2026-10-28. Factual names/categories/notes; numeric u64 converted to literal LE keys; one identical duplicate merged. [Sidecar and hashes](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.runtime.en/tables.source.md). |
| Four skill-tree group IDs | [mooofin, 7b173021](https://github.com/mooofin/God-of-War-Ragnarok/blob/7b1730217b92d27ab910d6d421efbd503cc109b2/gow.CT) | No licence published; reused with attribution per owner decision 2026-10-28. Extracted factual GetSkillTree arguments; no Lua/assembler port. Group roles inferred; individual saved skill IDs/bits unknown. |
| 1,682 2018 crosswalk candidates | [ProtoBuffers / xcier / God of War IDs spreadsheet contributors, 9342d5f3](https://github.com/xcier/GoW-2018-PS4-editor/blob/9342d5f3967ef74bac1161581dc4eab8bb41760d/app/resources/database/gow2018_items.json) | No licence published; reused with attribution per owner decision 2026-10-28. IDs/names/categories/internal filenames only; explicit cross-game Unknown labels; safe-edit flags/offsets omitted. [Sidecar and hash](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.2018.en/tables.source.md). |

The new community pack has a [data sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.community.en/community.source.md), and the asset packs have
[`assets.source.md` sidecars](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.assets.en/assets.source.md), plus the existing
[GPL text](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.community.en/GPL-3.0.txt).
[THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md) is plugin-local to respect this worker's scope.
Attribution reaches `Info.References`, pinned F2 Sources, every F8 source cell, icon descriptions,
legacy wallet/anchor tables, container descriptions and direct callers. The selected wiki image files
and short descriptions are embedded only in the bounded asset packs above; no bulk wiki dump or
dialogue extraction was imported.
Runtime and 2018 factual tables are now imported under the owner decision. No build-matched named
Ragnarök gear/quest join was found in the samples; this is an evidence limitation, not a licence gate.
Asset tools remain format research because WAD/TEXPACK layouts do not establish a save grammar.
Their LZ4 checksum does not establish save integrity; no newly inspected source supplies a save
authentication routine or complete StdNameHash algorithm. See the latest [research log](RESEARCH.md).

## Licence exclusions

| Source (revision) | Licence found | Reason / disposition | What would unblock it |
| --- | --- | --- | --- |
| mooofin runtime table and spreadsheet, `7b1730217b92d27ab910d6d421efbd503cc109b2` (links above) | None published | **Resolved:** factual tables imported with attribution under owner decision 2026-10-28; no code copied | Already resolved |
| ProtoBuffers / xcier 2018 crosswalk, `9342d5f3967ef74bac1161581dc4eab8bb41760d` (link above) | None published | **Resolved:** facts imported in a separate Unknown cross-game catalog; no code copied | Already resolved; PS5 gear/quest joins still need evidence |
| Proprietary game art, textures, icons, screenshots and extracted dialogue/text | Rights reserved by the named holders | **Resolved: imported (owner decision 2026-10-29)** as a bounded, attributed asset round; no bulk extraction, dialogue dump or save-coordinate join | Already resolved for this round; future additions still require the same attribution and size budget |

## Owner capture and validation checklist

See [OWNER_CHECKLIST.md](OWNER_CHECKLIST.md) for exact captures, evidence needed for gear/quest
names, keyed authentication investigation and enabled-write trials tracked on
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31). Full named gear/quests,
character-specific XP and derived collectible completion remain open. Console captures no longer
gate the enabled bounded writes. Only the owner may approve Stable.

## SDK requests

None blocking. F1/F2/F8 support the model and evidence, and F6 supports planned restoration.
F5/F7 are available but no safe structural or linked multi-file writer is established for this format.

# ASTRO's PLAYROOM (`astro-playroom`)

Title: **PPSA01325**. Maturity: **Experimental**, with edits passing conformance and format tests;
no edited save has been loaded on a console. Only a human should promote this to Stable.

## Files and supported layout

The game document is `memory.dat` in `sce_sdmemory` (archive directory `sdimg_sce_sdmemory`),
with an identical console backup under `sdimg_sce_bu_sdmemory` in each supplied generation.
The plugin returns only its own file. The framework's default export mirrors matching primary
files to their same-generation backups; it does not require plugin-specific backup writes.
System files are untouched. The plugin has no filesystem/network access and depends only on the SDK.

| Range | Observed role |
| --- | --- |
| `0x000–0x3FF` | 1,024 zero bytes; nonzero variants are rejected |
| `0x400–0x401` | `0D 0A` (CRLF), part of the text, **not a version/length word** |
| `0x402…` | UTF-8 XML-like `<PreInstall>` tree; numeric tag names are legal |
| After `</PreInstall>` | Opaque suffix, if any; preserved without interpretation |
| First NUL from `0x400` | Allocation text terminator |
| Terminator through `0xFFFFF` | All zero in both snapshots; nonzero padding variants are rejected |

Exactly **1,048,576 bytes** are required. There is no game-layer encryption or compression in this
observed allocation. Platform save decryption/encryption is outside this plugin.

The tokenizer records source byte spans for elements, quoted attributes and non-whitespace text
segments. It accepts numeric names, repeated siblings, comments, self-closing elements, mixed
content, UTF-8 values and single/double-quoted attributes. It does not expand entities or use XML
libraries. Entity spellings are shown literally and preserved. DTDs, processing instructions and
CDATA are unsupported and rejected inside the root rather than rewritten incorrectly.

Raw sections load lazily and page at 64 immediate entries plus a continuation. Parsing is bounded
to 64 levels, 4,096 elements and 4,096 values. Collections over 100 records use server-backed F8
tables (200 rows maximum per request); F9 maps page at 64 markers. Value IDs
use tag names and sibling occurrence numbers, never byte offsets, so length-changing edits retain
their IDs. Empty elements remain visible as tree sections without inventing a text value.

## Editing and confidence

- **Verified name matches:** `Common/NumCoin` (coins) and `Common/PlayCount` (play count).
  Observed values are 0/1 in generation 0 and 1749/2 in generation 1 respectively. What event
  increments PlayCount, and the game's actual coin limit, have not been established. The editable
  range 0…2,147,483,647 is a conservative signed-storage bound, not a verified gameplay limit.
- **Inferred effects, literal saved names:** boolean fields in `Progress` and `World`, including boss,
  visited, clear and map-notification flags. `Artifact`, `Puzzle` and `Prop` expose `IsGot` and
  existing `IsCinematicsPlayed` fields in Raw (props have no cinematic field in these samples).
  The catalogue adds 50 candidate artifact aliases without claiming verified saved-ID mappings.
  These names strongly suggest collection/progression semantics, but linked counters, notification
  behavior, cinematics and trophy effects have not been tested in-game.
- **Unknown:** other primitives, mixed text and all attributes are read-only in Raw. Leaderboard
  numeric IDs, scores/units, prompts and `UdsWorkaround` counters are preserved, not remapped.
- **Special bots:** no `Armadillo` or dedicated collected-bot fields occur in either supplied
  generation. The UI reports this absence. Newer community unlock flags are not inserted into an
  older save. Trophy-action names containing `BOT` are not treated as collected-bot flags.

## Named catalogue and schema report (roadmap Phase 1)

The Collectibles tab has separate Artifact, Puzzle and Prop sections. Each shows actual saved-record
and ownership totals, mapped/unmapped counts, F3 confidence badges, searchable raw keys, candidate
world/category and status filters, and expandable provenance/flag details. Catalogue labels/statuses
remain read-only; reviewed ownership actions are described below. Individual flag controls remain in Raw.
Mapped means an **Inferred candidate identity or internal-stage alias**. All 50 artifacts have
candidate names and all 96 puzzles have candidate internal-stage names, but physical piece locations
remain unresolved; prop identities remain **0/18**. Raw keys are always retained.
`ds3` explicitly records the DualShock 3/SIXAXIS naming discrepancy between public sources.

The English F2 pack supplies these 164 records and 22 candidate world/stage aliases, with per-record
source links. Puzzle aliases include an Inferred `stageKey` for their internal prefix (for example,
`gpu_forest` → Renderforest); this is a candidate stage name only and never a physical piece location.
Most artifact aliases also carry an Inferred candidate `worldKey`; the final 1994 Throwback aliases
remain category-only because no matching saved world key is present in the observed schema.
It is selected through the host's pinned `IReferenceContext`; missing packs fall back to raw keys. No
runtime network/filesystem access or embedded game artwork is introduced.
Names require the exact schema fingerprint and a build-agnostic pack because the samples' patch
version is unknown. A changed schema or duplicate saved key cannot silently inherit a mapping.

The Schema tab reports structure/key SHA-256, element/value counts, all top-level groups (including
unknown ones), suffix length and special-bot availability. The fingerprint excludes values,
whitespace, offsets and opaque suffix; it includes empty elements, attributes and sibling occurrences.
It is **not** a claimed patch/version detector. See [EVIDENCE.md](EVIDENCE.md).

Several puzzle prefixes contain five/three entries rather than four/four. Those exact groups survive
unchanged, with no guessed physical piece order or mural position. After a raw edit,
undo or redo the plugin requests a view refresh so ownership filters, section totals and detail text
reflect current values. F6 planning exposes reviewed coin and collect-existing actions, with explicit
old/new values, skipped rows and warnings before apply. Each apply is one undo/redo group.

## Framework feature adoption

| Feature | Status | Concrete use or reason |
| --- | --- | --- |
| F1 SaveDiff | Used | `fixtures.json`, PluginViewDecoder and sample tests reproduce all 94 observed primitive transitions, byte changes and identical backups. Independent text oracles cover ownership output. |
| F2 reference packs | Used | Offline schema-qualified English catalogue, per-record/dataset attribution, Sources panel and pinned context; no plugin I/O. |
| F3 rich cells | Used | Read-only name/confidence fields, provenance/details and selected-row planned action. No game images: no cleared icon dataset was established. |
| F4 reference picker | N/A (framework blocker) | Adoption attempted and Core eligibility verified, but F6's browser dialog disables ReferenceSpec parameters: see SDK requests below. Ship a small attributed EnumSpec world-choice fallback. Saved ownership/progress fields are booleans, not replaceable IDs. |
| F5 structural undo | N/A | Writer patches existing primitive spans only. Record creation/removal/reorder lacks a known game grammar/dependency repair; this round adds no structural writer. Existing batches use ordinary grouped value history. |
| F6 planned actions / #27 | Added this round | Existing coins/collect-all plus candidate-world ownership and bounded gatcha-budget top-up presets. Core resolves all-matching F8 scopes and exclusions; preview/apply/one-step undo/redo are tested. |
| F7 document groups | N/A | One gameplay allocation; no distinct coupled file/invariant identified. Identical backups are handled by same-generation framework export mirroring, not an independently editable partner document. |
| F8 queryable tables | Added this round | Combined 164-record collection, whole-save search/exact filters/deterministic sort/paging, stable keys and rich cells. Individual groups switch to F8 above 100 records. |
| F9 maps / graphs | Added this round | Lazy level schematic plus no-coordinate collectible maps and accessible marker lists; every supported marker links to an existing field/row. No prerequisite graph: no skill/perk tree or proven prerequisite edge exists in this format. |
| F10 exact values | Used | SDK integer transport preserves exact signed values with `IntegerSpec`/`FieldValue.Integer`; boolean flags use BoolSpec. There are no JSON numeric lexemes, nullable values or union discriminators requiring the additional specs. |
| F11 shared codecs | N/A | The observed 1 MiB payload is uncompressed readable UTF-8; no Kraken/Oodle/SQLite content or local codec exists to replace. |
| F12 | Pending #32 | SaveDiff F8 traversal/decoder hooks were not on main at round start; retain observed eager group coverage and existing F1 tests. No local traversal substitute. |
| F13 | Pending #33 | Inspect actions, browse-only picker, catalogue filters/sort and float duration were not on main at round start; no local substitutes. |

## Spatial navigation and candidate-world presets

The Maps tab uses F9: an **original list-order level schematic**, with no game art, route, adjacency
or prerequisite claim; and separate artifact/puzzle/prop lists with **no physical coordinates**.
Marker Found/Missing means only the associated saved boolean. Missing/ambiguous ownership is Unknown.
Every map has a text alternative and an always-available searchable/filterable marker list. Original
schematic provenance and CC BY-SA catalogue attribution are shown beside the map/in marker details.
PowerPyx is an attributed external guide link; no guide prose, media or coordinates are imported.
Unknown oversized paths remain available in Raw instead of violating F9 publication limits.
For groups above 100 rows, map navigation opens the exact lazy Raw ownership field rather than an
unloaded F8 row. Maps/counts/query statuses refresh after edits, planned actions and undo/redo.

The All saved collectibles section queries the complete save, including unloaded rows. Search across
names/raw keys, combine exact status/world/category filters, sort, then select all matching with
explicit exclusions before reviewing the ownership action. Small per-type tables remain convenient
for the observed 50/96/18 records. All joins use the same duplicate-safe, schema-qualified catalogue.

`collectibles/collect-world` offers an attributed candidate-world choice and F6 preview (EnumSpec
fallback pending the F4/F6 browser fix below). It changes
only existing missing ownership fields whose catalogue world matches (or whose inferred stage has
that parent world); explicit table/query selections intersect that set. Unmapped/outside-world
records and already-owned rows appear as skips. No cinematic, notification, clear/reward/UDS/trophy
state is derived. This is an ownership-only preset, **not a complete-world guarantee**. Memory Meadow
has 9 artifact + 16 internal-prefix puzzle records in both supplied snapshots; all 25 are initially
missing and are tested through exact-byte oracle, serialization/reopen and grouped undo/redo.

`common/top-up-gatcha` computes max(current coins, attempts × 100); it never repeatedly adds a budget
or reduces a higher balance. Gage/PowerPyx reports the factual 100-coin cost (external research link,
no guide prose/media copied). Attempts are restricted to **0–17**, keeping the calculated target at
most 1700 within the sample-observed 0–1749 range. This is a conservative experimental policy, not
a proven coin cap; actual cost/acceptance still need #31 validation. Random prizes cannot guarantee
completion. The F6 preview/outcome retains integrity/console warnings and the exact coin change.

## Enabled writes and their evidence

The owner enabled gated writes on 2026-10-28: **"Enable writes. Keep warnings."** Controlled captures
and console acceptance are tracked in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
The plugin remains **Experimental**. No edited save has been validated on console. Integrity is
unverified: no game checksum/signature algorithm is known, so **the game may reject an edited save**.
Serialization reparses the output and checks all primitive values; this does not prove game acceptance.

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| `collectibles/collect-all`: set missing existing `Artifact/*/IsGot`, `Puzzle/*/IsGot`, `Prop/*/IsGot` to true, document-wide, selected rows or all matching query rows | Inferred | Existing boolean spans; mixed-event samples show 3/9/1 ownership transitions. Tests apply 164 flags in generation 0 and 151 in generation 1, reopen, verify exact untouched bytes with F1, and undo/redo the whole batch. Query exclusions are tested. | Unknown game integrity; output reparsed, all other primitives and opaque suffix preserved. Game may reject edits. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): independent artifact/puzzle/prop grants and full/selected/query batches; museum, notifications, cinematic/reward behavior, resave and second reload. |
| **Added this round:** `collectibles/collect-world`, existing mapped ownership only | Inferred | Same existing-span writer; schema-qualified world/stage joins. Tests grant only the 25 Memory Meadow records and preserve every unrelated byte; query/explicit exclusions and grouped undo/redo covered. | Same unknown game integrity; no derived dependency repair. Game may reject edits. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): verify candidate-world membership and selected/query preset effects, museum/puzzles, rewards/cinematics/notifications, gameplay/resave/second reload. |
| **Added this round:** `common/top-up-gatcha`, existing coin span only | Inferred | Gage/PowerPyx reports 100 coins per attempt; independent bounded arithmetic for 0–17 attempts. All four samples verify exact unrelated-byte preservation/reopen; Core one-step undo/redo and invalid/overflow-sized request rejection pass. | Same unknown integrity; only NumCoin changes, higher balances preserved. Game may reject edits. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): spare-slot top-up, actual per-attempt cost, spend/resave/reload, cap/clamping and unrelated flags. |
| Existing individual ownership/cinematic/Progress/World booleans | Inferred | Literal saved names and booleans; flags vary independently between samples. No dependency relationship is proven. | Same unverified integrity; no inserted fields or guessed repairs. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): each flag's effect, dependencies and persistence. |
| Existing `Common/NumCoin` field and F6 `common/set-coins` | Verified name; Inferred gameplay range/effects | Observed 0/1749, preserving serializer tests. Bounds 0…2,147,483,647 are signed-storage policy; only 0…1749 is observed, not a proven game cap. | Same unverified integrity; reparsed after edit. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): gain/spend, cap/clamp, independent edit and persistence. |
| Existing `Common/PlayCount` | Verified name; Unknown increment semantics | Observed 1/2; nonnegative signed-storage bound, not a verified gameplay limit. | Same unverified integrity; reparsed after edit. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): event semantics, accepted values and persistence. |

Use **Collect existing records (review before apply)** below the tables for every existing
collectible. To collect a subset, select rows in one table (including through its category/status
filters), open any row's **Record details**, then use its action to preview that table's selection.
Hidden selected rows remain included; an empty selection changes nothing. Plans label each raw
group/key and old/new `IsGot`, skip already-owned or missing/ambiguous/nonboolean ownership fields,
and reject unknown/duplicate selection keys, unexpected parameters and unresolved/foreign query scopes.
The action also works without a coin field or reference pack; it never relies on candidate aliases.

Only missing `IsGot` becomes true. There are **no derived writes**: cinematic, notification,
Progress/World, UDS, coins and play-count values remain unchanged. The preview/outcome warns that
pickup dependencies and rewards are unverified and makes no completion or trophy guarantee.
Individual grant/revoke/cinematic edits remain available in Raw with warnings. Special-bot insertion,
record add/remove and cross-title transactions remain unsupported: the newer structures/record grammar
are unknown, so creating them would risk corrupting unknown data. There was no prepared F5/F7 write
to enable; identical same-generation backups continue to use framework export mirroring.

Only existing supported integer/boolean text spans are writable. Every other byte, including
indentation, quotes, comments, unknown values and the opaque suffix, is copied verbatim. If the
replacement length changes, everything through the original NUL shifts together; remaining capacity
is zero-filled. There is no identified length field to update. Batched edits validate total capacity
including the NUL before any state change. Serialization reparses its output and compares every
original primitive's value. Undo back to an original value restores its exact original spelling
(including leading zeros). No-op output is byte-identical on all four records.

## Snapshot and integrity evidence

Research date: **2026-10-28**. Source: main-repository `PPSA01325_saves_dec.zip`; duplicate ZIP records
remain distinct generations, not four independent saves. Only `memory.dat` was examined; no account
metadata or credentials were read. Scratch extraction was outside the repository.

Both primary records and both backup twins have **785 elements / 508 primitive text values**,
with no attributes. Each primary/backup pair is byte-identical. The two primary generations differ
at 27,999 common offsets, first `0x428`, last `0x7773`.

| Observation | Generation 0 | Generation 1 |
| --- | ---: | ---: |
| Root end, exclusive | `0x7776` | `0x7729` |
| Text NUL offset | `0x7776` | `0x7776` |
| Opaque suffix length | 0 | 77 bytes |
| `NumCoin` / `PlayCount` | 0 / 1 | 1749 / 2 |
| True `Artifact/*/IsGot` | 0 of 50 | 3 of 50 |
| True `Puzzle/*/IsGot` | 0 of 96 | 9 of 96 |
| True `Prop/*/IsGot` | 0 of 18 | 1 of 18 |
| CRC32 over `[0x400, NUL)` | `D15E8648` | `910A25C2` |
| Adler-32 over `[0x400, NUL)` | `ACF2293B` | `C4242ACD` |
| Byte sum over `[0x400, NUL)` | `0023272D` | `002328BF` |

The second suffix consists of fragments such as `ll`, `all`, `nstall` and `>`; **stale overwrite
residue is inferred**, not a defined format feature. The root became 77 bytes shorter but the first
NUL did not move. Tokenization therefore stops at the balanced root, not at that NUL; suffix bytes
remain opaque. Their byte preservation is tested through shorter/longer edits.

No integrity field was identified. Every byte before `0x400` is zero in both generations, and the
following two bytes are CRLF while the text changes. The research tests check all unaligned 32-bit
header words in both endiannesses against CRC32/ISO-HDLC, Adler-32, byte sum, text lengths and end
offsets; lengths are also checked as 16-bit words. Candidate ranges start at `0x400` or `0x402` and
end at the root end, the first NUL, or immediately after the NUL. None match. There are no named
checksum/hash/length/version fields in the observed tree. Hash-vector tests verify the candidate
algorithms independently; they are test helpers, not production save checksums.

**Inference:** the observed game payload has no integrity/length metadata to repair. This plus
community reports of direct text editing supports Experimental writing for this layout. It is not
proof against an undocumented algorithm, a relationship among gameplay flags, or integrity changes
in newer versions. Syntactically valid value corruption cannot be detected without a known checksum.

F1 now reproduces all **94 primitive transitions**, 27,999 changed byte positions and the identical
backup twins through [fixtures.json](fixtures.json). These are unlabeled, mixed-event generations:
their order does not establish gameplay chronology or any dependency. Observed ownership changes
cover three artifacts, nine puzzles and one prop. `ds2/IsGot` changes while its cinematic flag stays
false; `hula_hoop` has no cinematic field at all. Automatically setting all cinematic flags would
contradict this evidence. No control-pair churn masks or unsupported gameplay causes are invented.

## Validation

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/AstroPlayroom/SaveToolkit.Plugins.AstroPlayroom.Tests
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project tests/SaveToolkit.Core.Tests -- --filter-class '*Export*'
```

Completion-round validation (2026-10-28): **71 plugin tests passed, 0 failed, 0 skipped**; **11 Core
export tests passed, 0 failed, 0 skipped**; API build **0 warnings/0 errors**. Adds F9 exact marker
coverage/states/targets/attribution, 550-record lazy-map/F8 fallback bounds, nonboolean/duplicate-field
robustness, whole-query selection/exclusions, world-preset exact-byte oracle and grouped undo/redo.
Budget tests cover all sample records, exact coin-only bytes, zero/enough-balance no-ops, invalid and
Int128-overflow-sized inputs, no repeated award, reparse and one-step history.
The F4/F6 browser blocker below was found with the real frontend and recorded centrally; the small
EnumSpec fallback retains working world selection. Earlier validation follows for historical context.

Focused browser verification used this branch's v0.4.0 API and the existing production frontend,
without rebuilding or changing web files. Confirmed 51 schematic world-flag markers; external guide
host/attribution and `noopener noreferrer`; artifact text-list jump opens Collectibles and focuses
the exact Artifact/ps1 row without editing. The world preset previews 25 changes/139 skips; apply
and one-step undo/redo refresh 13/164 ↔ 38/164 totals. F8 searches all rows and reports 16 Memory
Meadow puzzle records; all-query selection across 164 records with one exclusion previews 150
changes/13 skips on generation 1. No console/network errors or external resource requests were
observed. Temporary browser tab/server were closed; only memory.dat entries were imported.

Write-enable round validation (2026-10-28): **57 plugin tests passed, 0 failed, 0 skipped**; **11 Core
session/export tests passed, 0 failed, 0 skipped**; API build succeeded with **0 warnings/0 errors**.
New write tests cover all four records, exact ownership-only output including necessary text
displacement/zero fill, F1 byte/semantic diffs, per-type selection, skipped rows, invalid boolean
values/parameters/scopes, stale/tampered plan rejection and one-step bulk undo/redo with live counts.
Focused browser validation against this branch's API and the existing production UI verified the
generation-1 preview (151 changes/13 skips and all warnings), apply to 164/164, one-step undo/redo,
then an Artifact/ps1 selected-row preview/apply (one change, unchanged false cinematic flag).
The export plan staged the primary plus its matching same-generation backup. No browser console
errors occurred. This round did not rebuild or modify the frontend.
Earlier roadmap validation covered 44 tests; round 2 covered 48. The following UI observations are
from the earlier roadmap round, not a new production web build:
New tests cover pack validity/attribution and exact key coverage, both generations and both twins,
schema/value separation, unknown-schema/build fallback, duplicate/ambiguous records, 5/3 puzzle
groups, F1's complete observed changes, and actual Core session edit/undo/redo view refresh.
The production web build also passed. A local browser check with generation 1 verified all three
catalogue sections, unique saved-key control labels, the three-owned-artifact filter, combined
search/missing filtering, read-only details with confidence/provenance, two attributed Sources
datasets and the schema's 77-byte suffix report; no console/network errors occurred.

The plugin suite covers all four sample records and both distinct generations, no-op identity,
known values, meaningful coin edits, boolean and integer length changes, exact byte preservation,
opaque suffixes, undo, multiple edits, session isolation, exact capacity and atomic overflow rejection,
numeric tags, attributes, comments, UTF-8, duplicate sibling IDs, lazy paging, malformed tags/encoding,
missing terminators, unsupported padding, oversize files and parser limits. Sample tests use
`SampleSaves` and skip when the archive is absent. Open/main-view timings in the initial test run
were 1.4–20.4 ms per sample; these are local observations, not a benchmark guarantee.

## Sources and attribution

| Dataset / research | Source and authors | Licence / use and changes |
| --- | --- | --- |
| Artifact candidate names/categories (`astro_collectibles`) | [Artifacts](https://astrosplayroom.fandom.com/wiki/Artifacts), Astro's Playroom Wiki contributors; selection/joins/annotations by Toolkit contributors; retrieved 2026-10-28 | [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/); factual names/worlds only, inferred exact-key joins; no game flavor text or media. |
| Puzzle stage aliases (`astro_collectibles`) | Original Toolkit aliases plus inferred internal-prefix stage joins; [Puzzle Pieces](https://astrosplayroom.fandom.com/wiki/Puzzle_Pieces), wiki contributors, provides stage context; retrieved 2026-10-28 | Pack adaptation remains CC BY-SA 3.0; `stageKey` is Inferred from saved prefixes; no physical piece names, locations or cross-game rules imported. |
| Prop raw-key aliases (`astro_collectibles`) | Original Toolkit aliases; [PlayStation Labo](https://astrosplayroom.fandom.com/wiki/PlayStation_Labo), wiki contributors, provides context only; retrieved 2026-10-28 | Pack adaptation remains CC BY-SA 3.0; readable literal keys only, no prize identity claim. |
| World/stage candidates (`astro_worlds`) | [Artefacts](https://astro-bot-series.fandom.com/wiki/Artefacts), Astro Bot Wiki contributors; [CPU Plaza](https://astrosplayroom.fandom.com/wiki/CPU_Plaza), Astro's Playroom Wiki contributors; retrieved 2026-10-28 | CC BY-SA 3.0; selected names/order, original inferred raw-path joins, no walkthrough prose or art. |
| Licence provenance | [Fandom Help:Licensing](https://community.fandom.com/wiki/Help:Licensing), Fandom contributors; retrieved 2026-10-28 | Research only: default text licence is CC BY-SA 3.0; media rights are separate. |
| Gameplay guide comparison / gatcha arithmetic | [PowerPyx collectible guide](https://www.powerpyx.com/astros-playroom-all-collectible-locations-artefacts-puzzle-pieces/), Gage, 2020-11-17, rechecked 2026-10-28 | External research, no reuse grant established; counts/naming discrepancy checked and factual 100-coin cost independently represented. Original bounded arithmetic, no guide prose/images embedded; `AstroGatchaPlanning.source.md` records scope. |
| Original schematic positions | [Toolkit contributors](https://github.com/JKamsker/PS5SaveGame-Toolkit/tree/main/plugins/AstroPlayroom), created 2026-10-28 | [GPL-3.0-or-later](https://www.gnu.org/licenses/gpl-3.0.html), original eight-column list layout; no physical coordinates/adjacency or game media. Sidecar: `AstroSpatialViews.source.md`; full provenance in MapNode and Info.References. Existing catalogue names retain CC BY-SA 3.0. |

Per-record source URLs, retrieval/access limitations, modification notices and share-alike terms are
also in the pack's `data.source.md`/`pack.json` and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md),
`Info.References`, catalogue descriptions and the Sources panel. Live wiki revision fetches were
blocked (402/403/robots); search-indexed article text and licence footers were checked. No oldid is
fabricated: pack version/file hashes pin the selected data and `sourceVersion` states that limitation.

Additional format research (no implementation copied):

- [Repository format reconnaissance](../../docs/formats/PPSA01325.md): initial layout, sample counts,
  numeric-tag warning and starting community reference; all byte claims rechecked against the archive.
- [PROSPEROPatches title record](https://prosperopatches.com/PPSA01325): title-ID/name correspondence.
- [Original PS5_Jailbreak editing thread](https://www.reddit.com/r/PS5_Jailbreak/comments/1wemi58/astros_playroom_special_bots_dont_show_up/):
  first-person reports of editing newer `memory.dat` Common/Armadillo flags with a text editor,
  updating the backup, and successfully reloading. Anecdotal supporting evidence only; none of
  those flags or proposed values were copied into these older samples.
  The write-round web search rechecked this first-person report on 2026-10-28; it supplies no
  collectible dependency or checksum algorithm. No new external data/code was embedded this round.
- [zlib manual, checksum functions](https://zlib.net/manual.html#Checksum): candidate CRC32 and
  Adler-32 conventions for the integrity investigation; also cross-checked with Python standard
  library `zlib` in scratch analysis. No zlib package is needed by the plugin.
- [RFC 1952](https://www.rfc-editor.org/rfc/rfc1952): CRC32 polynomial specification used for the
  independent, bitwise test helper. No substantial external code was copied or executed.

Searches included ASTRO's PLAYROOM save editors, GitHub parsers/templates, `memory.dat` header,
`PreInstall`, `NumCoin`, coins/collectibles, special bots, CRC/Adler/checksums and PS5_Jailbreak
threads. No reusable game-specific parser or documented integrity algorithm was established.
Search hits for Astroneer parsers and other games' `memory.dat` formats were excluded.

## Open questions

Console validation remains required, especially coin limits and consistency between collection,
cinematic, notification and `UdsWorkaround` flags. Newer special-bot schemas need actual samples
and a fresh integrity investigation before curated support. The opaque suffix's purpose is unknown.

## Remaining phases and exact owner checklist

Phase 1 and the implementable Phase 3/5 work are complete: named catalogue, candidate-stage/world
joins, completion summaries, selected/query/document ownership planning, candidate-world ownership
preset, bounded gatcha-budget preset, original F9 schematic/navigation, F8 bounds and release hardening. Phase 2's controlled mapping
and console acceptance remain tracked in #31; they do not gate existing-span writes. Phase 4 exposes
schema diagnostics/absence only; bot support requires newer structures. Physical puzzle-location
mapping, proven gameplay dependency/completion guarantees, coin-cap validation and human acceptance remain
owner-blocked. Maturity stays Experimental. F4/F6 browser compatibility needs the central fix below;
F12/F13 remain pending #32/#33, with no plugin-local substitute.

| Requirements | Completion disposition |
| --- | --- |
| AP-R1, AP-R4, AP-R5, AP-R6 | Owner-blocked (#31): controlled pickup/clear/coin/bot/update/transfer captures and console persistence; no guessed missing-state insertion. |
| AP-R2, AP-R3 | Done: F1 fixtures, attributed schema-qualified catalogue and candidate aliases; owner still verifies physical locations/identities. |
| AP-R7, AP-R17 | Rights route resolved: original schematic, cleared CC BY-SA names and link-only guide; catalogue screenshots/locations remain owner-blocked. |
| AP-R8–AP-R13 | Done with existing F3/F6/F8/F9: rich details, world/status filtering, live refresh, named changes/skips/warnings and Core all-query scopes. F4 parameter compatibility uses documented fallback. |
| AP-R14, AP-R15 | F2 pinned context used; additional optional image/localization ingestion not pursued because there is no cleared icon/localization dataset to ingest. |
| AP-R16 | Done: preservation, malformed schemas, per-action atomicity/undo, independent byte oracle, F1, conformance and Core export regression checks. |
| AP-R18 | Done: original level schematic and externally linked collectible lists; physical coordinates stay absent pending owner mapping. No invented prerequisites/graph edges. |

For each pair, record title, exact game build, region, slot, DLC, date, before/after displayed values
and the one action taken. Export decrypted before/after archives (including primary and backup) to
the private artifacts folder; never include account identifiers in manifests or screenshots.

1. Capture a save/load/resave control pair without gameplay, once for each build, to isolate churn.
2. Capture exactly one known coin gain, one 100-coin gatcha spend and boundary/clamping behavior;
   photograph displayed coin values. Test a small independent coin edit on a spare save.
3. Capture one named artifact pickup, one prop/gatcha prize, one normal stage clear and one boss
   clear separately; include the catalogue before/after and any notification/cinematic behavior.
4. Capture one puzzle at each ambiguous boundary: Renderforest/Teraflop Treetops,
   Raytrace Ruins/Mt. Motherboard, Bot Beach/Springy Spa and Gusty Gateway/Fastlane Fields. Record
   the exact physical piece/location and the UI's piece count. Do not use guide ordinal as save ID.
5. Provide catalogue screenshots to verify all 50 artifact aliases, especially `ds3` (DualShock 3
   versus SIXAXIS). Identify each of the 18 prop scenes/prizes independently if desired.
6. Supply old/new update pairs from the same profile and newer-build pairs before/after event
   eligibility, each of four bot discoveries/rescues and each added-artifact acquisition. For
   transfer research, provide both games before/after the actual transfer with offline/online context.
7. For each enabled write: load the spare edited save on PS5, confirm the intended effect,
   perform ordinary gameplay, resave, export again, reload a second time and record persistence
   plus any unintended linked effects. Only the owner may approve Stable or safe bulk dependencies.
8. For the F6 coin action, capture one save with the displayed coin value before and after one known
   coin gain or spend, then compare `Common/NumCoin` and every changed primitive/byte range. Repeat
   the same event on a second slot and reload after ordinary gameplay; record cap/clamp behavior.
9. For the enabled artifact batch, capture exactly one named artifact pickup per pair. Record the exact
   `Artifact/<id>/IsGot` and `IsCinematicsPlayed` values, `Progress` notifications, `UdsWorkaround`,
   `NumCoin` and `PlayCount`, plus all unrelated changed paths. Repeat one pickup on a separate slot.
10. For the enabled puzzle batch, repeat item 9 for one piece in each boundary pair
    (Renderforest/Teraflop Treetops, Raytrace Ruins/Mt. Motherboard, Bot Beach/Springy Spa and
    Gusty Gateway/Fastlane Fields). Include the physical location and in-game piece count; do not
    use the saved ordinal as a location claim.
11. For the enabled prop batch and future per-world dependency research, capture exactly one gatcha prize and one stage clear,
    respectively, with the same full linked-field list. Capture a boss clear separately. Existing
    ownership batches are enabled with warnings; future derived changes require their own evidence.
12. On separate spare saves, apply one artifact, one puzzle and one prop grant, then a selected-row
    batch and the document-wide collect-existing batch. Record in-game museum/puzzle/prize status,
    notifications, cinematics/rewards and any side effects. Play normally, resave, export primary+
    backup, and reload a second time. Diff all primitive paths, suffix and byte ranges; compare with
    a no-action control. Report rejection as well as successful persistence in #31.
13. Preview Memory Meadow's candidate-world preset (9 artifact + 16 internal-prefix puzzle records
    in this schema), verify every included/skipped record against the museum and stage counts, then
    repeat with a filtered all-matching selection excluding one record. Confirm cinematic, clear,
    notification, reward and UDS behavior, play normally, resave/re-export primary+backup and load a
    second time. Report normalization/rejection/unrelated effects alongside successful persistence.
14. On a spare low-balance slot, preview one gatcha attempt (target at least 100 coins) and separately
    17 attempts (target at least 1700). Confirm the actual cost per roll, random-prize behavior and
    no balance reduction when already above the target. Spend normally, resave/re-export, reload a
    second time and record persistence, cap/clamping or rejection. Do not interpret the budget as a
    guaranteed collectible count.

## SDK requests

**F4 reference picker in an F6 planned action is disabled in the browser.** Reproduced against main
and its existing production frontend on 2026-10-28. Minimal reproduction: publish an ActionNode with
`SupportsPlanning = true` and parameter `new FieldNode("world", "World", new ReferenceSpec("astro_worlds"),
FieldValue.Of("World/COOLING_SYSTEM"))`; open the action dialog. The trigger shows the raw ID and is
disabled, so it cannot select another candidate. Core `SearchReferences("world", ..., actionId: ...)`
works with attribution/eligibility and rejects bad IDs; this is a browser binding problem.
`web/src/components/document/review/PlannedActionButton.tsx` passes neither `valueId={p.valueId}` nor
`actionId={node.actionId}` to FieldControl; ReferenceControl disables when `valueId` is absent. Legacy
ActionButton already passes both. The orchestrator should add those props and a planned-reference
browser regression test centrally. This plugin uses a bounded EnumSpec fallback; no framework file
was edited. Once the central fix lands, replace that small fallback with ReferenceSpec and its
eligibility restriction to worlds with existing mapped records.

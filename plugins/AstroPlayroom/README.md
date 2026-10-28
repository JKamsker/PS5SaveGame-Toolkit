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
to 64 levels, 4,096 elements and 4,096 values; each curated table stays below 5,000 rows. Value IDs
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
world/category and status filters, and expandable provenance/flag details. Names/statuses in this
read-first catalogue cannot edit a save. Existing experimental flag controls remain in Raw.
Mapped means an **Inferred candidate identity** for artifacts; puzzle mapped counts mean physical
locations (currently **0/96**), and prop identities remain **0/18**. All 50 artifacts have candidate
names; 96 puzzles and 18 props retain original raw-key aliases, with Unknown mapping confidence.
`ds3` explicitly records the DualShock 3/SIXAXIS naming discrepancy between public sources.

The English F2 pack supplies these 164 records and 22 candidate world/stage aliases, with per-record
source links. It is selected through the host's pinned `IReferenceContext`; missing packs fall back
to raw keys. No runtime network/filesystem access or embedded game artwork is introduced.
Names require the exact schema fingerprint and a build-agnostic pack because the samples' patch
version is unknown. A changed schema or duplicate saved key cannot silently inherit a mapping.

The Schema tab reports structure/key SHA-256, element/value counts, all top-level groups (including
unknown ones), suffix length and special-bot availability. The fingerprint excludes values,
whitespace, offsets and opaque suffix; it includes empty elements, attributes and sibling occurrences.
It is **not** a claimed patch/version detector. See [EVIDENCE.md](EVIDENCE.md).

Several puzzle prefixes contain five/three entries rather than four/four. Those exact groups survive
unchanged, with no guessed physical piece order, stage location or mural position. After a raw edit,
undo or redo the plugin requests a view refresh so ownership filters, section totals and detail text
reflect current values. No new writing capability, grant/revoke preset or collect-all action is added.

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

Roadmap round validation (2026-10-28): **44 plugin tests passed, 0 failed, 0 skipped**; **11 Core
session/export tests passed, 0 failed, 0 skipped**; API build succeeded with **0 warnings/0 errors**.
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
| Puzzle raw-key aliases (`astro_collectibles`) | Original Toolkit aliases; [Puzzle Pieces](https://astrosplayroom.fandom.com/wiki/Puzzle_Pieces), wiki contributors, provides context only; retrieved 2026-10-28 | Pack adaptation remains CC BY-SA 3.0; no physical piece names, locations or cross-game rules imported. |
| Prop raw-key aliases (`astro_collectibles`) | Original Toolkit aliases; [PlayStation Labo](https://astrosplayroom.fandom.com/wiki/PlayStation_Labo), wiki contributors, provides context only; retrieved 2026-10-28 | Pack adaptation remains CC BY-SA 3.0; readable literal keys only, no prize identity claim. |
| World/stage candidates (`astro_worlds`) | [Artefacts](https://astro-bot-series.fandom.com/wiki/Artefacts), Astro Bot Wiki contributors; [CPU Plaza](https://astrosplayroom.fandom.com/wiki/CPU_Plaza), Astro's Playroom Wiki contributors; retrieved 2026-10-28 | CC BY-SA 3.0; selected names/order, original inferred raw-path joins, no walkthrough prose or art. |
| Licence provenance | [Fandom Help:Licensing](https://community.fandom.com/wiki/Help:Licensing), Fandom contributors; retrieved 2026-10-28 | Research only: default text licence is CC BY-SA 3.0; media rights are separate. |
| Gameplay guide comparison | [PowerPyx collectible guide](https://www.powerpyx.com/astros-playroom-all-collectible-locations-artefacts-puzzle-pieces/) | Research only, no reuse grant established; counts/naming discrepancy checked, no prose/images embedded. |

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

Phase 1 is complete. Phase 2 has reproducible F1 observational evidence but still needs controlled
captures and console testing. Phase 3's read-only F3 browser is implemented; selected-entry/per-world
actions and F6 collect-all previews remain blocked by the dependency evidence below. Phase 4 exposes
schema diagnostics/absence only; bot support needs newer saves. Phase 5's completion presets, diagram
and console acceptance remain open. Maturity stays Experimental. No further SDK request is needed:
F1/F2/F3/F6 already provide the required framework capabilities; the blocker is evidence.

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
7. For each proposed new write: load the spare edited save on PS5, confirm the intended effect,
   perform ordinary gameplay, resave, export again, reload a second time and record persistence
   plus any unintended linked effects. Only the owner may approve Stable or safe bulk dependencies.

## SDK requests

None. Existing lazy sections, bindings, byte outputs and framework backup mirroring are sufficient.

# Gear, skills and XP field discovery (#40, 2026-10-29)

Scope: all 156 PPSA08330 archive records through the plugin parser, including 76 primary
gameplay records, their 76 byte-identical backups and four preference records. No sample
bytes or decoded record dumps belong in this checkout. Research scratch, test extraction,
build products and reports use `D:/Temp/saveeditor/test-scratch/research-godofwar-gear`.
Original archives are read-only at the supplied external artifacts path.

## Result and confidence

| Field | Result | Remaining evidence for writes |
| --- | --- | --- |
| Gear instances and armor/attachment levels | New read-only F8 collection and F12 evidence: exact definition-hash joins, names and **catalog** levels, Inferred. Unknown definitions/levels stay raw/Unknown. | Controlled single-upgrade pairs, actual per-item/mode bounds, costs, dependency writers and integrity repair. |
| Core Axe/Blades/Spear upgrade levels | Some definition names join; a complete core-weapon level crosswalk is absent. No inferred numeric core-weapon level is published. | Build-matched definition-to-level mapping and capture pairs. Do not reinterpret the instance index as a level. |
| Enchantments / rune slots | Named enchantment definitions join; parent indices and six-byte subrecord counts are raw read-only evidence. | Amulet repair, equip/unequip and runic-upgrade pairs; slot-index/unlocked-state mapping, threshold and set-bonus dependencies. |
| Individual skill unlocks | 132 stable IDs in five groups; four attributed group roles remain Inferred. Individual names and unlock semantics stay Unknown. | Exact saved hash-to-skill crosswalk plus a one-buy/upgrade/refund pair with XP cost, prerequisite, weapon-level and story gates. |
| Available / total XP | Existing character-labelled resource candidates are retained. Neither spendable-vs-total semantics nor a lifetime XP field is established. | PS5 UI values plus one purchase/earn pair for each character; distinguish wallet duplicates, separate resource IDs and spent XP. |

No additional writes: dependency repair and save integrity are unknown. A source-level label or
a base-game cap does not satisfy those gates. Existing import-bounded wallet/time writes retain
their prior owner-authorized behavior; this task does not add a gear/skill grant or XP formula.

## Differential method and positive evidence

1. Decode every sample with `RagnarokRecordIndex`, retaining wallet, directory and grouped-flag
   framing. Compare stable instance/group/row identities, not file offsets or slot-name chronology.
   Archive generations are occurrences, not guaranteed chronological progress or controlled edits.
2. Read the public gear CSV as data. Normalize all 2,092 distinct numeric u64 IDs, then scan every
   byte alignment in each primary gameplay file. This gives 9,058 hits / 143 distinct IDs;
   arbitrary occurrences are leads only, never owned/equipped records.
3. Infer a bounded block ending **exactly** at the known wallet block's u32 length prefix.
   Search no further back than a u16 length permits, and never cross the decoded directory.
   Accept only one candidate satisfying all measured row/count/identity/parent checks.
   Zero or multiple candidates leave the entire region opaque, without failing existing formats.
4. The block is `u16 byteLength; u32 recordCount; records; opaque remainder`. Each record is
   `u64 definitionHash; u32 instanceIndex; u32 parentIndex; u32 subrecordCount; subrecordCount × 6 bytes`.
   Counts/records must fit the enclosing block. Instance indices start at zero and strictly increase;
   a parent is `FFFFFFFF` or another instance in this same block. Parent role remains Unknown.
   There is no new parser for, or write into, the opaque remainder.

All 76 primaries yield a unique block. Counts are 28–172, totaling **7,443 rows**, with 182 distinct
instance indices and 251 distinct definition hashes. Of these definitions, **141** join the new
public catalog (4,015 row occurrences), **118** have specific candidate names, and **57** have
numeric source levels (1,534 row occurrences). There are 110 unmatched definitions. Backups give
the same results. Reference keys are literal LE bytes; numeric hashes printed here are u64 values.

33 instance indices have more than one definition across the corpus; 149 have only one observed
definition. These are differences, not proof of a single causal upgrade transaction. For example,
instance 5 has `73169D00521722E5` and `73169D0052172AC7`, source-labelled Wraps of Survival at
levels 1 and 3. The instance word remains 5. Thus treating `hash+8` as an upgrade word is disproven.
Some core-weapon variants do not occur in the source and others have no level; no interpolation
of their hashes is used. The reference pack's 81 presentation skill keys still are not save hashes.

The late generation-zero SAVE0 block starts at `1A19F`, has byteLength 6,815 and 172 rows;
its record section ends at `1AF39`, leaving 3,335 opaque bytes before wallet framing at `1BC40`.
The early generation-one SAVE0 block starts at `14827`, has byteLength 2,189 and 29 rows.
These offsets are sample evidence, not constants in the implementation.

Only instance 81 has counted six-byte subrecords: 2/4/6 in 16/14/14 primary saves respectively.
All other records have zero. The parent word is `FFFFFFFF` in 7,275 rows and 81 in 168 rows.
Named children include enchantment candidates, corroborating an amulet relationship without
establishing equip position, repair state or an unlocked-slot count. No equipped label is published.

The read-only `gear` collection uses instance-based row keys, global search/filter/sort and
page-only field registration. Numeric catalog levels sort numerically, including NG+ fractional
labels. Both direct SDK callers and pinned hosts retain attribution. F12 adds four paths per
instance: definition, catalog level, raw parent and raw subrecord count. No saved skill name is guessed.

## Negative results: skills and XP

Every primary contains the same 132 skill/flag IDs in the same five group layouts. 56 rows have
one observed state, 68 have two and eight have three. Across 10,032 row observations the raw
four-byte states are `00000100` (5,808), `01000100` (2,292), `01010100` (767), `00010100` (671),
`01000000` (336) and `01010000` (158). Four exact group hashes join the existing PC tree pack;
the fifth and individual skill meanings remain Unknown. These finite state sets do not label bits.

The PC CE table walks runtime pointers with 0x18-byte stride, obtains names through a live pointer
and changes a u16 using `0x101`. Saved rows instead have an eight-byte ID and four bytes, at
12-byte stride. The source does not supply the individual saved-ID crosswalk, transaction costs
or save persistence rules. Matching a group hash and seeing `0101` cannot transfer that live mask.

XP candidates in the hero wallet occur in 66 primaries for Kratos `9C7CF61C839B9A8A` and Atreus
`A1D028F9B467E25C`, and 44 for Freya `E577D004DA4F5243`. The other attributed Atreus candidate,
`AC98E8B5155F2C73`, is absent from **decoded wallet resource rows**. Ten early primaries have no
XP resource rows; absence is not a zero balance. The three observed IDs also occur separately in
other wallets, with five nonzero non-hero occurrences in the corpus. They are never summed.
The source's playing-as contexts do not establish ownership of these PS5 containers.

The known late SAVE0 hero words are 84,725 / 43,577 / 37,708 for those three candidates.
Their observed ranges are 100–111,293 / 754–43,577 / 219–37,708. These are observations, not
legal bounds, a spendable-XP claim, or a lifetime total. Neither the sources nor these uncontrolled
snapshots isolate skill spending from runic/rage/relic spending, earned XP or story changes.
No available-plus-spent formula, reconstructed total or character level is published.

## Dependencies and next captures

The developer's launch guide establishes base equipment level 9, XP spending on runic attacks,
rage and relic upgrades, skill-use labors, matching armor perks and amulet/realm-set effects.
The NG+ announcement adds equipment conversion rewards, enchantment stat thresholds and XP
upgrade paths for Skill Mod tokens. These are concrete dependencies, not save-layout facts.
The game may recompute power/stats/perks from equipment and enchantments; no load/resave trace
here establishes which are recomputed or which cached values require patching.

Capture separately: no-action control; one armor upgrade; one core-weapon upgrade; one amulet
repair; one enchantment equip/unequip; one runic upgrade; one named skill buy/upgrade/refund with
XP before/after for its character. Retain exact build/mode, UI levels/stats/XP and native re-save.
Resolve definition variants, parent/slot encodings, derived caches, costs/gates and integrity before
any writer. Warning text does not replace these missing joins or dependency writers.

## Source register and licence decisions

All URLs below were accessed **2026-10-29**. Downloaded source was read as text/data only;
no downloaded editor, CE/Lua/assembler, browser script or datamine executable was executed.
Only public gear facts were embedded, using an original hash-checked importer.

| Source / author | Evidence and disposition |
| --- | --- |
| [Gear sheet](https://docs.google.com/spreadsheets/d/17JWS2EyV68tRAajJIungRvhL5mmePIyGUNqekWu1lBc/edit), cfemen-linked community contributors | New factual hash/name/type/level dataset; CSV SHA-256 and transformation in [sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.gear.en/gear.source.md). No licence published; reused with attribution per owner decision 2026-10-28. No legal maxima inferred. |
| [Resource sheet](https://docs.google.com/spreadsheets/d/1ERFISPdTQvIuRMyHuPhastH0AjYfmPegGhsHlvxJWF4/edit), community contributors | Independent reread of four XP names/contexts; CSV SHA-256 `34782f736759f6d64714e5a5b8770f610590fcd0d83f883e8bb77dc1e6f840af`. Same facts already in runtime pack; no second import or ownership claim. No licence published. |
| [mooofin CE table, 7b173021](https://github.com/mooofin/God-of-War-Ragnarok/blob/7b1730217b92d27ab910d6d421efbd503cc109b2/gow.CT) | Runtime tree lookup / pointer stride / unlock-mask hypothesis. Rechecked SHA-256 `365a53fd4e47c70dc612f9583a5893d3f8d2d6deca2e128cdddf32d74294236d`. No code port or execution; prior attributed group facts retained. No licence published. |
| [Continue? PC reader, 51e19b2a](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py), Alexsander720 / contributors | GPL-3.0. Rechecked wallet/count/time context and platform revision difference; no gear parser or new integrity repair obtained. No code copied. |
| [Console editor](https://codesprince.com/2022/12/06/ps4-ps5-save-editor-for-gow-ragnarok/), KG971 / Codes Prince | Feature claims include gear, nine amulet slots, skills and NG+; not an implementation, schema or per-item bounds. No data/code embedded. No applicable source licence found; no licence refusal. |
| [Launch progression guide](https://blog.playstation.com/?p=371846), Anthony DiMento / Santa Monica Studio | Primary developer evidence for gameplay dependencies above. Facts paraphrased only; no prose/assets imported. Rights reserved by Sony Interactive Entertainment / Santa Monica Studio. |
| [NG+ announcement](https://blog.playstation.com/2023/04/05/god-of-war-ragnarok-new-game-plus-is-available-now/), Grace Orlady / Santa Monica Studio | Primary evidence for conversion, thresholds and Skill Mod Plus dependencies; no build-matched save schema or universal maximum. No prose/assets imported. Rights reserved by Sony Interactive Entertainment / Santa Monica Studio. |
| [Combat guide](https://www.playstation.com/en-us/games/god-of-war-ragnarok/god-of-war-combat-refresher-course/), PlayStation / Santa Monica Studio | Rechecked equipped runic-attack context. Existing separately attributed guide assets retained; no new import. |

The gear sheet export was available through a direct HTTPS request even though the browsing tool
could not open Google Sheets. No immutable revision ID was returned, hence the pinned export hash.
A search-indexed [archive of cfemen's post](https://pawchive.pw/patreon/user/28139295/post/112592922)
led to the public sheet; its trainer and access links were not used. The primary
[Patreon page](https://www.patreon.com/posts/god-of-war-112592922) was unavailable through the browsing
tool. Technical conclusions use the public sheet bytes, original CE source and supplied saves.
No new licence refusal arose. New UI names/levels, the pack manifest, README, plugin references
and plugin-local third-party notices all retain attribution under rule 9.

## Regression evidence and fingerprints

Tests preceded production exposure. Independent research decoding captured 76 SHA-256
fingerprints of selected gear/XP/flag values; tests check each primary and backup against its
expected generation/slot fingerprint, without committing sample bytes or extracted records.
All 156 records retain byte-identical no-op serialization. Synthetic cases cover variable record
sizes, exact named joins, unknown IDs, duplicate indices, missing parents, excessive counts,
ambiguous candidates, read-only edit rejection, numeric sorting, pagination and F12 output.

The original five SDK-03 record/metadata fingerprints are unchanged. Four gameplay protocol
fingerprints intentionally change for the eighth collection and Gear tab; both preferences
fingerprints are unchanged. Two internal index fingerprints change solely for the additive empty
`Gear` property on legacy fixtures. Original decoder fingerprints and allocation limits are retained.
The eager sample-node ceiling rises from 100 to 101 for the new collection shell. CI gains a
synthetic gear mutation seed; sample-backed fuzz includes two preferences variants and gameplay.

Validation: **263 passed, 0 failed, 0 skipped** in Release with samples. Mutation fuzz completed
**800 cases** (one synthetic gear seed and three real seeds, 200 each), no failures or known failures.
An additional sample-free run passed **5,000 synthetic mutations**, with no failures or skips.
The Release API build passed with **0 warnings and 0 errors**. All outputs used the task's D: scratch.

The requested Core `EmbeddedPackSnapshotTests` run fails at its existing count pin (39 versus 40),
before emitting its hash. A D:-only, throwaway probe using that test's exact pack ordering,
`ReferencePackStore`, record serialization, asset bytes and provenance hashing computes:
`e7cca4235e1740fce5036e4dd76fac49dbf868c2c6e37a9743647d55971d08ac` for **40 packs**.
Excluding the new gear pack reproduces the existing 39-pack hash exactly:
`19ff03f234988519f8758116484ea69d63ac69e58e73baf1f148c770704dfd0b`.
No `tests/` file was edited; the orchestrator must re-pin both count and aggregate hash.

## Suggested EDIT_COVERAGE.md wording (maintainer scope)

- **GW04: R — I gear-instance framing and catalog levels.** A unique bounded block in all 76
  primary saves contains 7,443 records; 141 definition hashes join the attributed public gear pack,
  including 57 numeric catalog levels. Core-weapon numeric levels, equip/slot semantics, mode-specific
  maxima, costs, stat caches and integrity repair remain unresolved. Require separate acquire/equip/
  upgrade/amulet-repair captures and dependency writers before edits.
- **GW03 remains R:** four tree-group roles join, but 132 individual saved IDs and six raw states
  still lack a named unlock/cost crosswalk. Require a controlled buy/upgrade/refund and character XP pair.
- **GW01 remains E for existing counters:** explicitly retain candidate XP roles; neither spendable
  versus total semantics nor a lifetime XP counter is established; duplicate wallets are never summed.
- Replace the blanket missing-join wording for Ragnarök in the deliberately-not-shortlisted paragraph
  with: “Ragnarök now has inferred gear definition/catalog-level joins; core weapon levels, equipped
  slots, skill identities, XP transaction semantics and dependency/integrity writers remain missing.”

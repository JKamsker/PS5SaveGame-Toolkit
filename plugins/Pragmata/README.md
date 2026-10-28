# Pragmata (`pragmata`)

PS5 title **PPSA02530**. Maturity: **Experimental**. All supplied samples pass
integrity, parsing, editing and byte-for-byte reversal checks. No console load
test has been performed; only a human should promote this plugin to Stable.

## What works

- Gameplay overview: slot, console-local save date, hours played, cleared flag,
  difficulty hash and editable fixed-size header fields.
- Inventory: existing item IDs in hex and decimal, int32 quantities, equipped
  marker, and an F6 preview/apply quantity action for selected or all existing rows. F2 reference packs
  supply attributed names/descriptions/icons through writable F4 reference fields
  and F3 detail cells. The embedded item catalog labels all 90 IDs unmapped.
- Bounded Gadgets, Loadout, Progress, Quests and World existing-value editors, grouped by
  hash-confirmed section classes. F8 queries search/filter/sort evidence arrays
  before paging; the largest sampled world list has 6,988–6,989 records. Stored
  record counts are not completion totals. Details retain confidence, source attribution
  and console/integrity warnings. Strings, structs and collection structure remain read-only.
- Six observed inventory IDs cross-reference an MIT PC mission dataset's
  `OpenConditionItemID` fields. This **Inferred** role is shown separately from
  the still-Unknown player-facing names and limits; no PS5 unlock behavior is claimed.
- A developer-only F1 decoder and differential harness, plus a versioned section/ID
  manifest. Canonical paths align reordered sections and uniquely keyed records.
  See [evidence and exact owner capture checklist](evidence/README.md).
- Synchronized playtime and elapsed/demo/pause editing, with every derived value
  reported in `EditOutcome.Changes` for framework undo/redo.
- System/settings overview with localized `Title`, `SubTitle`, `Detail` strings.
- A lazy explorer of the complete typed tree. Supported primitives are editable
  by exact hash/index path; strings, structs, array sizes and container metadata
  are read-only. Non-finite floats are displayed as text.
- Fixed-size patching preserves unknown fields, padding, unused encrypted
  capacity, ElGamal pairs, unknown metadata bytes and the RSA/key trailer.

The slot rail labels gameplay saves by the normalized directory name:
`SAVESERVICE-LINE-0-<n>Slot` or `SAVESERVICE-LINE-0-0` becomes
“Gameplay save, slot N”. `data00-1.bin` is labelled “System data”; unrecognized
directory names fall back to “Gameplay save”. Probe still reads only the 16-byte
DSSS header and never decrypts. Backup twins use the same normalized names.
After opening, gameplay titles include the parsed slot and inferred playtime in
hours, e.g. “Pragmata · Slot 0 · 25.7 h”.

Each tab starts with one experimental notice. Field descriptions retain
provenance (`path · u32 @0x50`, with plaintext offsets), source attribution,
editor bounds and console/integrity warnings; confidence describes unverified
gameplay meaning. Inventory-specific risks and clock guidance remain in
their tabs. The overview integrity stat reads “Verified”, with hash block counts
and embedded key/IV verification details in its hint.

No new NuGet dependencies. Production code depends only on `SaveToolkit.Sdk`
and .NET libraries, and does no filesystem/network I/O. Embedded identifier data
is immutable and shared; mutable state is isolated per document.

## Enabled writes and their evidence

The 2026-10-28 owner decision **"Enable writes. Keep warnings."** supersedes the
earlier console-evidence gate. Version 0.4.0 enables six prepared write surfaces:
item-ID references and the five family tabs below. This edits existing primitive
storage only, using the same fixed-size patcher as Raw. Names identify stored
fields, not verified gameplay effects. Every writable field describes its source,
console status and integrity limitation; F6 preview/apply outcomes retain warnings.

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Inventory item-ID replacement through F4 | Unknown; imported names at most Inferred | Exact uint32 storage; 90 observed IDs; edit/reopen and plaintext-byte isolation in all 11 gameplay files | CityHash64/Murmur3 repaired; RSA trailer unverified | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): replace one existing ID; inspect equipped state, quantities, unlocks and sibling files after reload |
| Gadgets / shortcut existing primitives | Unknown | Hash-confirmed identifiers/types; all-sample F1 preservation and undo/redo | Same fixed-size repair; RSA unverified | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one gadget/shortcut change; verify equip/use behavior |
| Loadout existing primitives (weapons, enhancements, presets, character) | Unknown | Same; no invented enum meanings or dependency repairs | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one weapon/perk/preset value; verify ownership, effects and dependent state |
| Progress existing primitives | Unknown | Same; record counts are not completion percentages | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one chapter/event/campaign/difficulty value with an exact field path and visible result |
| Quests existing primitives | Unknown | Same; objective identifiers do not prove quest semantics | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one objective state; verify prerequisites and downstream quest behavior |
| World existing primitives | Unknown | Same; fixed physical row identities and bounded F8 details | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one checkpoint/gimmick/prop/environment value; verify travel, interactions and dependent state |
| Existing quantity controls and F6 selected/all quantity action (retained) | Inferred | Independent golden hashes; all-sample F1 quantity-only deltas; atomic planned apply and group undo | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): small quantity change; preview, apply, undo/redo, export/re-import and console load |
| Existing header/clock/raw primitive edits (retained) | Inferred for named fields; Unknown otherwise | 12 no-op identities; primitive/clock edit/reopen/reverse and derived-clock undo tests | Same | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): unchanged control first, then one small edit per family |

**The game may reject edited saves.** RSA trailer verification/regeneration is
not implemented. Serialization preserves that trailer and every unknown byte,
repairs the two known hash layers, decrypts its own result and fully re-parses it.
Successful parsing cannot establish console acceptance or repair unknown gameplay
dependencies. Item-ID replacement changes only the selected stored ID; it does
not create a record or update equipment, ownership, unlocks or other references.

New numeric family controls and their Raw aliases use immutable editor bounds
computed when the document opens: match containing class/array, field hash,
primitive type and width; take the observed minimum/maximum in that save, include
0 and 1, and retain the native type bounds. The added 0/1 endpoints are an explicit
editor policy, not observations or claimed legal enum states. Boolean fields
accept booleans only; floats must be finite and fit their stored precision.
Reference IDs use exact decimal uint32 text, with the same observed bounds;
custom IDs are permitted within those bounds even without a reference pack.
Imported names or limits never override this policy. Invalid batches and alias
conflicts reject atomically. Existing quantity, header, clock and system/raw
metadata controls retain their documented storage bounds and clock invariants.

Structural add/remove/reorder, string/struct edits and resizing remain disabled.
There is no typed structural writer: byte-identical fixed-size no-ops do not
prove that unknown alignment, padding, counts and metadata survive rebuilding a
different layout. No prepared cross-file transaction or proven sibling dependency
exists to enable through F7. Existing operations emit only their owned file;
no legal-max or progression preset is fabricated. Maturity stays Experimental.

For each enabled surface, use a spare slot: record build/region/DLC, path and
displayed old/new values; capture an unchanged load/save/reload control; make
one small change; export/re-import; load, inspect side effects, re-save, reload,
re-export and diff. Keep system/gameplay siblings in the capture. The detailed
[capture checklist](evidence/README.md) and [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31)
track evidence separately; writes no longer wait for those captures.

## Files and sample coverage

| File | Role in supplied samples |
| --- | --- |
| `data00-1.bin` | System/settings, 10,484 plaintext bytes, four roots |
| `data000.bin` | Gameplay slot 0; not proven to be an autosave |
| `data001Slot.bin` … `data010Slot.bin` | Gameplay slots 1–10 |
| `sce_sys/*`, keystone and other metadata | Not claimed or modified |

The archive contains 12 distinct game files, 503 Mandarin blocks and 519,872
class records. All plaintexts parse exactly. Every gameplay file contains 27
section records and 90 inventory entries. Section order varies: inventory and
clock sections are located by class hash, never fixed indices. Importer snapshot
generations and backup twins remain independent; siblings are not rewritten.

## DSSS/Mandarin format

Integers are little-endian. Only DSSS v2, flags `0x10` (Mandarin without
compression), is supported. “Decrypted PS5 save” still contains this game layer.

| Offset | Contents |
| --- | --- |
| `0x00` | `DSSS`, uint32 version 2, uint32 flags `0x10`, four preserved padding bytes |
| `0x10` | Mandarin blocks |
| `F - 0x8c` | Preserved 128-byte RSA/key trailer |
| `F - 12` | uint64 exact plaintext length |
| `F - 4` | MurmurHash3 x86-32 of `[0, F-4)`, seed `0xffffffff` |

Mandarin uses public PS5 key input `526253637` and encryption seed
`0x7da24a9e1479f3d7`. Generate **all** `ceil(length / 0x4000)` potential
capacities first: `((state & 7) + 1) * 0x4000`. Generator steps add
`0x9e3779b97f4a7c15`, then perform the SplitMix64 mixing operations. The **mixed
output becomes the next state**, unlike conventional SplitMix64. After the
capacities, add the bitwise complement of the 64-bit key input.

Each actual block uses 16 outputs for low-byte AES key bytes and next-byte IV
bytes, then 528 outputs for the metadata XOR mask. A block contains:

- Four 128-byte ElGamal-style key/IV metadata pairs.
- A masked uint64 CityHash64 of **valid plaintext bytes**, at `+0x200`.
- Eight preserved unknown bytes at `+0x208`.
- AES-128-OFB ciphertext at `+0x210`, occupying its generated capacity.

ElGamal metadata is decoded with the reference modulus and complemented key
using `BigInteger.ModPow`; recovered key/IV words must match. This checks
consistency, not cryptographic authenticity. RSA trailer validation/generation
is not implemented. Portable AES-OFB ECB-encrypts the previous feedback block
and XORs the stream; NIST vectors cover full/partial blocks and in-place use.

Serialization starts from original ciphertext, XORs only
`old plaintext XOR new plaintext` into changed blocks, repairs their masked
CityHash64 values, then repairs outer Murmur3. It never resizes or regenerates
keys. **Every `Serialize()`** decrypts the output, verifies both hash layers and
embedded key/IV, compares all plaintext, and fully re-parses it, including no-ops.

## Typed stream and bounds

Roots have a native hash followed by field-count/class-hash records. Fields have
name hashes and signed type tags: array `-1`, enum `1`, bool `2`, signed/unsigned
integers `3–10`, floats `11–12`, char integers `13–14`, counted UTF-16LE string
`15`, opaque struct `16`, class `17`. Class arrays may contain an `0xffeeffee`
marker and per-element hashes, preserved verbatim. Unsupported tags are rejected.

Alignment includes the removed 16-byte DSSS header:
`aligned = ((offset + 16 + size - 1) & ~(size - 1)) - 16`.
The bit-mask rule also applies to non-power-of-two struct sizes such as 24.
Origin zero fails on several supplied slots.

The parser rejects truncation, duplicate field/root hashes, invalid sizes/counts,
incomplete streams, nesting beyond 100, arrays above 1,000,000 elements, trees
above 2,000,000 nodes, and plaintext above 64 MiB. These are editor support
bounds, not claimed game limits. Lazy expansions and inventory pages contain at
most 200 entries; very large arrays use recursive range sections. String/struct
previews are bounded without altering their stored bytes.

Primary value IDs are exact paths, e.g.
`7c416083/7c018857[6]/3185b7a5[5]/d18b252d`. Raw controls prefix that path with
`raw/`, avoiding duplicate IDs across views. Both aliases share one primitive;
changes report every exposed alias. Contradictory alias edits reject atomically.

## Clock decisions

All 11 gameplay samples satisfy:

`_SavePlayActualTime = _GameElapsedTime - _DemoSpendingTime - _PauseSpendingTime`.

Microsecond units are inferred. Timer controls use Duration, 1,000,000 units per
second, and `Confidence.Inferred`; their names are independently hash-confirmed.

- Playtime edits derive elapsed = playtime + demo + pause, preserving demo/pause.
- Elapsed, demo or pause edits recompute playtime.
- A playtime + demo/pause batch derives elapsed unless elapsed is also supplied;
  when both are supplied, the whole relation must agree.
- Overflow, underflow and conflicting batches reject all edits, including any
  unrelated fields. All derived values are reported and undoable.

Save-date ticks remain editable raw int64 values. The overview interprets valid
ticks as .NET ticks with an unknown timezone; invalid ticks are displayed
explicitly. No UTC conversion is made.

## Verified versus inferred and unknown

`Resources/HashNames.json` embeds **408 identifiers** selected from the pinned
MIT RE_RSZ schema. Every entry reproduces its
MurmurHash3 over UTF-8 with seed `0xffffffff`, checked on resource load and in
tests. Five names from the earlier unlicensed dump are no longer distributed:
`Slot`, `Detail`, `_IsFirstGetWeapon`, `_IsFirstGetActiveSkill` and
`_AcquireContentsHashes`. Their fields remain available by raw hash. Class hashes
`00000000` and `6cb10f10` remain unnamed; unmatched native root hashes remain numeric.

Upstream dumps resolve additional names beyond the earlier map, including
`_ApplicationVersion`, `_SaveSlotIconHash`, `_SaveSlotStageHash`,
`_SaveSlotDescriptionHash`, `_SavePlayActualTime`, `_LapCount`, `_MainSlot`,
`_Gadgets` and `_ShortcutSlot`. These are verified **identifier matches**, not
proof of legal values or game behavior. `_MainSlot` is not labelled money,
`_LapCount` is not equated to NG+, and difficulty/inheritance IDs have no invented
friendly labels. Writable controls use Inferred for named raw fields and Unknown
for unmapped fields/family semantics. Identifier provenance remains hash-verified;
it does not confer Verified gameplay confidence. Experimental console/integrity
warnings appear in every field description and at the top of each tab.

Item names, ID meanings, legal stack limits, unlock dependencies, many field
semantics, unknown metadata and console compatibility remain unverified. The
reviewed enum dump did not yield a confirmed item dictionary. Quantities retain
the full signed int32 storage range with a warning; bulk N is nonnegative int32.
The F6 action changes existing entries, including zero-count entries, without
adding records or setting unlock flags. The preview lists raw-ID old/new values,
aliases, skips and warnings. Explicit empty selection changes nothing; unknown,
duplicate or non-inventory keys and duplicate item IDs reject the entire action.
Direct invocation is rejected. Unexposed target quantity pages must be expanded
before planning so the complete apply remains one undo step. No new editable
gameplay family, legal-max preset or structural operation is introduced in roadmap round 2.

Regenerate the pack from a reviewed local copy of the pinned MIT schema:

```powershell
python -I plugins/Pragmata/scripts/build-reference-pack.py --rsz <MIT-rszpragmata.json> --missions <MIT-missionuserdata.user.3.json>
```

The generator validates source SHA-256
`67e5ed72e0e9efff0287214ad36737a8959d799aa1510b6b76591731a21f4df2`,
also validates the pinned mission-data SHA-256 (see the pack sidecar), and reads
only the reviewed JSON inputs, current known-hash subset and evidence manifest,
and does not download/execute upstream code or read saves. HashNames is the
reviewed observed-hash allowlist; adding new hashes requires reviewing that subset.

The embedded `pragmata.evidence.en` v1.1.0 pack contains `pragmata-identifiers`
(408 MIT names), `pragmata-sections` (27 MIT-derived group labels) and
`pragmata-items` (90 CC0 original unmapped-ID descriptions) and `pragmata-item-evidence`
(six MIT mission-opening-condition cross-references). User packs can supply
names/descriptions/icons under another pack ID; item keys are invariant decimal
uint32 strings. Identifier and section keys are eight-digit lower-case hex.
See [pack provenance and import contract](SaveToolkit.Plugins.Pragmata/ReferencePacks/pragmata.evidence.en/data.source.md).
F4 item references allow experimental existing-ID replacements, imported labels are at most Inferred, and
reference limits do not become edit caps. No artwork or copyrighted game descriptions
are embedded. Counts in the new family tabs denote records, not completion totals.
Evidence row keys use fixed plaintext offsets, which remain stable under this
size-preserving editor and across page/filter/sort changes. They identify physical
records, not proven gameplay identities. Search covers raw IDs, types and up to
12 direct stored fields per record; nested arrays are queried separately, and text
payloads remain in details. GUIDs are fingerprinted in query summaries. Sorting is
ordinal text with a stable row-key tie-break; queries use offsets, not cursors.

## Validation and development

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/Pragmata/SaveToolkit.Plugins.Pragmata.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
pwsh -NoProfile -File plugins/Pragmata/scripts/check-code-size.ps1
```

Conformance covers all 12 distinct files with meaningful quantity edits and a
system boolean edit. Format tests check the research map's root/section values,
classes, offsets, array counts, all 990 inventory entries and exact consumption;
all no-op bytes; quantity edit/reopen/reversal in every gameplay file; and 11
whole-output SHA-256 golden digests independently generated by the Python
toolkit. Item `2227368435` in `data000.bin` starts at 2575 and plaintext `0x0a9bcc`.

Tests also cover ciphertext damage with repaired outer hashes in every file,
embedded metadata damage, malformed inputs, origin-16 struct alignment, 18
CityHash64/Murmur3 vectors across length branches, NIST AES-OFB vectors, clock
invariants, atomic rejection, raw aliases, float reversal, bounded million-entry
paging, and framework action/clock undo/redo. Sample tests skip through
`SampleSaves` when the archive is absent. No sample payload bytes are committed;
the existing research map is linked into tests and new fixtures hold only digests.

A local Release measurement of open plus initial view was approximately 36–185 ms
per supplied file, including integrity checks and parsing. This is hardware
dependent and is not a timing assertion in CI.

Round 2 tests also check cheap probe labels, malformed directory-name fallbacks,
backup/snapshot labels against parsed slot values, one experimental notice per
tab, compact field and lazy Raw provenance, integrity hints, and current playtime
titles under a locale that normally uses a decimal comma.

Historical Round 2 validation: API build succeeded with zero warnings/errors;
**74 plugin tests passed**, **43 Core tests passed**. An independent run with an
empty sample directory passed 44 pure tests and skipped all 30 sample-based tests.
The previous identifier-generation script reproduced 413 entries; this round
replaces that data route with the pinned MIT-only subset.

Roadmap validation: **89 plugin tests passed, 0 failed, 0 skipped** with the sample
environment exported in the test command. API and research builds passed with zero
warnings/errors. New checks cover all-sample decoder completeness and unique paths,
section/inventory reorder invariance, exact quantity delta, duplicate-key fallback,
decode limits, manifest coverage, pack hashes/licensing, user-pack override and
provenance, read-only batch rejection, bounded family views, and framework undo/redo
of raw item-ID edits with read-only reference aliases.

Roadmap round 2: **99 plugin tests passed, 0 failed, 0 skipped** with the artifact
environment in the test command; API build passed with zero warnings/errors.
Added all-sample query/read-only/no-op checks, complete pagination of the large
world collection, exact filters, deterministic sort and reorder-stable row keys,
query-bound rejection, attributed mission-ID cross-references, F6 pure previews,
selection/skip/preflight/stale/tampered-plan rejection and one-step framework undo.
F1 verifies the bulk operation in all eleven gameplay saves: only inventory quantity
semantic paths and plaintext storage change; reopening and reversal preserve exact
original bytes. These are synthetic preservation experiments, not causal in-game
captures. Detailed remaining requirements: [round-2 research and evidence](evidence/round2.md).

Write-enable validation: **108 passed, 0 failed, 0 skipped**, using all 12 distinct
files with the artifact environment in the same test command. New coverage applies
one existing primitive per family in all 11 gameplay saves, verifies complete F1
deltas (including deliberate identity-key changes), confines plaintext-byte deltas
to requested fields, reopens values and exercises framework group undo/redo.
F4 item replacements receive the same all-sample byte/reopen/undo coverage;
malformed IDs, invalid types/ranges and conflicting aliases reject atomically.
Field/action descriptions and F6 plan/apply warnings explicitly cover console and
RSA limitations. API build passes with zero warnings/errors. No new dataset,
algorithm port or dependency was added; the original attribution remains intact.

Builds run the plugin-local code-size check: warn above 300 C# lines and fail
above 500 unless a reviewed justification exists in `code-size-exemptions.json`.
All current C# files are below 300 lines. The same pre-commit hook can be used
without changing shared worktree configuration:

```powershell
git -c core.hooksPath=plugins/Pragmata/.githooks -c user.name="Codex Worker" -c user.email="codex@localhost" commit ...
```

## SDK requests

Optional: publish field specs independently of eager/lazy view nodes. Core undo
filters changes against specs already indexed from exposed views. All 90 sample
quantities are eager. For inventories above 200, F6 preflight now rejects unexposed
target quantity pages instead of applying without undo. Internal bindings alone
cannot safely promise framework undo. No framework change was required.

F1's stock CLI resolves plugin IDs through `PluginViewDecoder`; it has no external
decoder-assembly registration option. The plugin-local research executable consumes
`ISaveDecoder` directly through the shared F1 engine, so there is no production plugin
dependency on Testing and no duplicate diff engine. Optional future request: discover
external decoder hooks in the stock CLI. F2/F3/F4/F5/F6/F8 required no framework changes.

## Sources and attribution

| Dataset / code | Source, author, license | Retrieved / transformations |
| --- | --- | --- |
| HashNames; `pragmata-identifiers`; section names / declared types | [Pinned RE_RSZ schema](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/rszpragmata.json), alphazolam / praydog, [MIT](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/LICENSE) | 2026-10-28; observed hash subset; name hashing, group labels, type cross-index. Full notice in THIRD_PARTY_NOTICES.txt. |
| Item ID placeholders / sample observation statistics | [Issue #21](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21), Save Toolkit contributors, [CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/) for original descriptions / computed facts | 2026-10-28; no gameplay name mapping; GUIDs fingerprinted; no sample payloads. |
| `pragmata-item-evidence`: six inventory/mission opening-condition links | [Pinned mission JSON](https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/data/json/natives/stm/singletonuserdata/missionuserdata.user.3.json), Egg Targaryen / dzxrly; [MIT](https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/LICENSE) | 2026-10-28; exact numeric `OpenConditionItemID`/mission-ID pairs cross-indexed with observed PS5 item IDs. Inferred role; names/limits Unknown. Full notice embedded beside the dataset. |
| CityHash64 algorithm adaptation | [Google CityHash](https://github.com/google/cityhash), Google, MIT | Existing port; full notice in THIRD_PARTY_NOTICES.txt. |
| AES-OFB vectors | [NIST SP 800-38A](https://csrc.nist.gov/pubs/sp/800/38/a/final), NIST, US government publication | Existing F.4.1 test vectors. |
| DSSS / Mandarin / typed-stream observations | [Supplied verified research](../../docs/research/pragmata/README.txt), owner-supplied format evidence | Existing independently implemented crypto/format; no upstream editor code embedded. |

Research-only sources (no unlicensed datasets embedded):

- [neuro-pragmata runtime inventory/object-name resolvers](https://github.com/vaultic13/neuro-pragmata/tree/ae2ee572410e353ceae19bdc23f83f71687267dd/autorun/pragmata/bindings), Vaultic,
  [MIT](https://github.com/vaultic13/neuro-pragmata/blob/ae2ee572410e353ceae19bdc23f83f71687267dd/LICENSE), retrieved 2026-10-28:
  identifies `app.ObjectIDs`, `getKindHash` and localized `GuiDataManager.getItemData`
  as owner capture leads; no runtime table, code or localized text is copied.
- [Pragmata Wiki item category](https://pragmata.fandom.com/wiki/Category%3AItems)
  and [Lunafilament](https://pragmata.fandom.com/wiki/Lunafilament), community CC BY-SA
  text, retrieved 2026-10-28: no save-ID linkage found; no article text or media embedded.

- [Verified format](../../docs/research/pragmata/FORMAT.txt),
  [research README](../../docs/research/pragmata/README.txt), `pragmata_save.py`,
  `hashes.py`, `selftest.py`, `verified_save_map.json`: primary byte-verified
  format, alignment, crypto, patch strategy, expected values and tests.
- [REE Save Editor README](https://github.com/kvasszn/ree-save-editor/blob/main/README.md):
  independently reproduced PS5 key and region hash, game support context.
- [mandarin.rs](https://github.com/kvasszn/ree-save-editor/blob/main/src/save/crypto/mandarin.rs),
  [util.rs](https://github.com/kvasszn/ree-save-editor/blob/main/src/save/crypto/util.rs),
  [game.rs](https://github.com/kvasszn/ree-save-editor/blob/main/src/save/game.rs):
  capacities, mixed-state PRNG call sequence, AES-OFB, ElGamal and public constants.
  Consulted as algorithms; no substantial Rust code copied.
- [types.rs](https://github.com/kvasszn/ree-save-editor/blob/main/src/save/types.rs),
  [type_map.rs](https://github.com/kvasszn/ree-save-editor/blob/main/src/sdk/type_map.rs):
  type tags, sized alignment, class arrays and name hashing. The supplied research
  confirms origin 16 against the PS5 samples.
- [rszpragmata.json](https://github.com/kvasszn/ree-save-editor/blob/main/assets/pragmata/rszpragmata.json),
  [strings_pragmata.txt](https://github.com/kvasszn/ree-save-editor/blob/main/assets/pragmata/strings_pragmata.txt),
  [enumspragmata.json](https://github.com/kvasszn/ree-save-editor/blob/main/assets/pragmata/enumspragmata.json):
  earlier identifier candidates and enum research. Replaced by the MIT RE_RSZ subset;
  no data from these unlicensed dumps is newly redistributed.
- [Google CityHash](https://github.com/google/cityhash/blob/master/src/city.cc):
  CityHash64 algorithm, adapted under MIT; see [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
- [NIST SP 800-38A](https://csrc.nist.gov/pubs/sp/800/38/a/final), Appendix F.4.1:
  independent AES-128-OFB test vectors.
- [REasy releases](https://github.com/seifhassine/REasy/releases) and
  [REFramework nightly](https://github.com/praydog/REFramework-nightly/releases):
  further Pragmata dump/modding research leads. No code/data from these projects
  is embedded and no verified item dictionary was obtained from them.

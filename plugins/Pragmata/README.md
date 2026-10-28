# Pragmata (`pragmata`)

PS5 title **PPSA02530**. Maturity: **Experimental**. All supplied samples pass
integrity, parsing, editing and byte-for-byte reversal checks. No console load
test has been performed; only a human should promote this plugin to Stable.

## What works

- Gameplay overview: slot, console-local save date, hours played, cleared flag,
  difficulty hash and editable fixed-size header fields.
- Inventory: existing item IDs in hex and decimal, int32 quantities, equipped
  marker, and an atomic “Set quantity of all items to N” action. F2 reference packs
  supply attributed names/descriptions/icons through read-only F4 reference fields
  and F3 detail cells. The embedded item catalog labels all 90 IDs unmapped.
- Bounded read-only Gadgets, Loadout, Progress and World tabs, grouped by the
  hash-confirmed section classes. Stored record counts are not completion totals.
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

Each tab starts with one experimental notice. Field descriptions retain compact
provenance (`path · u32 @0x50`, with plaintext offsets) and source attribution; confidence conveys name
verification separately. Inventory-specific risks and clock guidance remain in
their tabs. The overview integrity stat reads “Verified”, with hash block counts
and embedded key/IV verification details in its hint.

No new NuGet dependencies. Production code depends only on `SaveToolkit.Sdk`
and .NET libraries, and does no filesystem/network I/O. Embedded identifier data
is immutable and shared; mutable state is isolated per document.

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
friendly labels. Other field controls use Verified for confirmed names and
Unknown for unnamed hashes; the experimental game-effects warning appears once
at the top of each tab, rather than on every field.

Item names, ID meanings, legal stack limits, unlock dependencies, many field
semantics, unknown metadata and console compatibility remain unverified. The
reviewed enum dump did not yield a confirmed item dictionary. Quantities retain
the full signed int32 storage range with a warning; bulk N is nonnegative int32.
The bulk action changes existing entries, including zero-count entries, without
adding records or setting unlock flags.

Regenerate the pack from a reviewed local copy of the pinned MIT schema:

```powershell
python -I plugins/Pragmata/scripts/build-reference-pack.py --rsz <MIT-rszpragmata.json>
```

The generator validates source SHA-256
`67e5ed72e0e9efff0287214ad36737a8959d799aa1510b6b76591731a21f4df2`,
reads only the reviewed schema, current known-hash subset and evidence manifest,
and does not download/execute upstream code or read saves. HashNames is the
reviewed observed-hash allowlist; adding new hashes requires reviewing that subset.

The embedded `pragmata.evidence.en` v1.0.0 pack contains `pragmata-identifiers`
(408 MIT names), `pragmata-sections` (27 MIT-derived group labels) and
`pragmata-items` (90 CC0 original unmapped-ID descriptions). User packs can supply
names/descriptions/icons under another pack ID; item keys are invariant decimal
uint32 strings. Identifier and section keys are eight-digit lower-case hex.
See [pack provenance and import contract](SaveToolkit.Plugins.Pragmata/ReferencePacks/pragmata.evidence.en/data.source.md).
F4 reference fields remain read-only, imported labels are at most Inferred, and
reference limits do not become edit caps. No artwork or copyrighted game descriptions
are embedded. Counts in the new family tabs denote records, not completion totals.

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
quantities are eager, so bulk undo works normally. For inventories above 200,
the plugin warns to expand every quantity page first to retain undo; otherwise
the action explicitly returns `Undoable = false`. Internal bindings alone cannot
safely promise framework undo. No framework change was required for these samples.

F1's stock CLI resolves plugin IDs through `PluginViewDecoder`; it has no external
decoder-assembly registration option. The plugin-local research executable consumes
`ISaveDecoder` directly through the shared F1 engine, so there is no production plugin
dependency on Testing and no duplicate diff engine. Optional future request: discover
external decoder hooks in the stock CLI. F2/F3/F4/F5 required no framework changes.

## Sources and attribution

| Dataset / code | Source, author, license | Retrieved / transformations |
| --- | --- | --- |
| HashNames; `pragmata-identifiers`; section names / declared types | [Pinned RE_RSZ schema](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/rszpragmata.json), alphazolam / praydog, [MIT](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/LICENSE) | 2026-10-28; observed hash subset; name hashing, group labels, type cross-index. Full notice in THIRD_PARTY_NOTICES.txt. |
| Item ID placeholders / sample observation statistics | [Issue #21](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21), Save Toolkit contributors, [CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/) for original descriptions / computed facts | 2026-10-28; no gameplay name mapping; GUIDs fingerprinted; no sample payloads. |
| CityHash64 algorithm adaptation | [Google CityHash](https://github.com/google/cityhash), Google, MIT | Existing port; full notice in THIRD_PARTY_NOTICES.txt. |
| AES-OFB vectors | [NIST SP 800-38A](https://csrc.nist.gov/pubs/sp/800/38/a/final), NIST, US government publication | Existing F.4.1 test vectors. |
| DSSS / Mandarin / typed-stream observations | [Supplied verified research](../../docs/research/pragmata/README.txt), owner-supplied format evidence | Existing independently implemented crypto/format; no upstream editor code embedded. |

Research-only sources (no unlicensed datasets embedded):

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

# GOWTool MIT follow-up — 2026-10-29

**Result: code reuse is now explicitly licensed, but this revision supplies no additional
substantiated Ragnarök save capability.** Keep the existing Experimental counter/time writes,
Inferred/Unknown labels and integrity warning. No code, mappings, assets or reference packs were
imported, and no write range or confidence label was changed.

This is follow-up research from toolkit candidate `229f262f03ec44c0ad5a3470d9734bc148458a1c`;
it does not change that release candidate. Downloaded source was inspected as untrusted text only.
No upstream executable, build, DLL, submodule or script was run. No private configuration or
credential values were read. Samples were read in place; reports contain aggregate evidence only.

## Source and licence pins

Author/project: **kainotoa / GOWTool**. Retrieved 2026-10-29.

- [Issue #14 and completion](https://github.com/kainotoa/GOWTool/issues/14#event-34590165079).
- [Inspected merge, f2899f6c89cb85bf0f313f8a844fb1884ff23633](https://github.com/kainotoa/GOWTool/commit/f2899f6c89cb85bf0f313f8a844fb1884ff23633).
- [MIT addition, f0e92b0c720fd5abc44023a0d01b55d416516088](https://github.com/kainotoa/GOWTool/commit/f0e92b0c720fd5abc44023a0d01b55d416516088).
- [Exact upstream LICENSE](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/LICENSE):
  MIT, copyright (c) 2026 kainotoa; SHA-256
  `177ed236d31df7daf0535792fd3209f5c48a2bf6232f2477b12a314c01748cce`.
- [Change from the pre-licence parent, 42070038](https://github.com/kainotoa/GOWTool/compare/42070038b63ba67cc82c2e8c78f2cdae58e51be8...f2899f6c89cb85bf0f313f8a844fb1884ff23633):
  **only LICENSE added, 21 lines**; no new parser, hash algorithm or dataset in that change.

MIT is compatible with this project's GPL-3.0-or-later policy. A future code port must retain
the exact upstream copyright and full permission/warranty notice, and separately check dependency
terms. The root grant does not license a supplied game DLL or game assets. This documentation-only
assessment copies no implementation or dataset; it does not introduce an MIT-licensed runtime component.
The previous research-only disposition was an applicability gap, not a licence refusal.

## What the pinned implementation actually offers

All source links below refer to **f2899f6c89cb85bf0f313f8a844fb1884ff23633**, not a moving branch.
The archive contains 14 `.cpp` and 16 `.h` files; no bundled resource/skill/gear dictionary or
save-specific mapping dataset was found. Searches across those files and the entry point for
`GOWRSAVE`, `userpreferences`, `StdNameHash`, `Hacksilver`, `Kratos`, `Atreus`, `SkillTree`,
`EconomyXP`, `LZ4`, `WTOC` and `checksum` returned no matches. Code inspection, rather than keyword
absence alone, establishes the scope below.

| Area | Primary code evidence | Consequence for this plugin |
| --- | --- | --- |
| Target and commands | [README.md](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/README.md) explicitly targets God of War **2018** assets; [Source.cpp](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/Source.cpp) dispatches asset extraction/conversion and texture import. | No claimed Ragnarök PS5 save support. Do not conflate this repository with MorseTheCode/GodOfWarTool, smpack or JeanxPereira/GoWToolkit. |
| WAD parsing | [inc/Wad.h](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/inc/Wad.h), [src/Wad.cpp](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/src/Wad.cpp): group/type u16s, size u32, fixed name field and 16-byte alignment; mesh/rig/texture type tags. | Asset-entry framing, not a GOWRSAVE serializer, wallet grammar or integrity routine. |
| Texture hashes | [inc/Texpack.h](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/inc/Texpack.h), [src/Texpack.cpp](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/src/Texpack.cpp): `_fileHash`, `_userHash`, `_blockInfoOff`; `ContainsTexture`, `GetUserHash` and export compare or return stored fields. `Source.cpp` parses numeric filename suffixes and retains user hashes. | These are asset identity/lookups. No string-to-StdNameHash implementation, saved-ID crosswalk or checksum calculation is supplied. Matching widths do not establish matching namespaces. |
| LOD/mesh hashes | [src/Lodpack.cpp](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/src/Lodpack.cpp), [src/Formats.cpp](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/src/Formats.cpp): read asset hashes and use them to retrieve mesh buffers. | No resource names, gear catalog levels, individual skill names or saved unlock bits to join. Existing runtime/gear/cross-game packs supply more relevant facts already. |
| Compression/integrity | [src/krak.cpp](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/src/krak.cpp), [inc/krak.h](https://github.com/kainotoa/GOWTool/blob/f2899f6c89cb85bf0f313f8a844fb1884ff23633/inc/krak.h): external Oodle function loading and declarations, including a `crc` argument. | No save coverage, field exclusions, seed, key derivation or authentication repair. A decoder argument is not an integrity algorithm. This source does not supply the other asset tools' WTOC/LZ4 implementation. |

Selected source-byte SHA-256 pins (as downloaded, no newline normalization):

| Path | SHA-256 |
| --- | --- |
| `README.md` | `38bd96374da584d64b67057812dae895821864f132d003493fbf667f8debb941` |
| `Source.cpp` | `8ea1a8d6023e961a088c641d965d27a233f35d0dc4b6278e88aeb0c0cd73c725` |
| `src/Wad.cpp` | `82c4cf6f27bc4d05ed6efe950486508d167e634f334a98ea683f588495ff0332` |
| `inc/Texpack.h` | `087bed0daa787d7600e21874036221ad4f81b5c9f9ea138e3d0487a895548b8f` |
| `src/Texpack.cpp` | `e9df363154be0a509390d7a1c75292948e94077ed8ff0fed57f8889d48f402b6` |
| `src/Lodpack.cpp` | `b846dc48d7ea4a9388b4108c2aad07192e3c2276523548645621f22a8691712d` |
| `src/krak.cpp` | `402c663e397c61099ca37acbbe808124a8fc6fed4102c28c3826a1ab7c84ec07` |

## Sample checks and software regression evidence

Read-only input: the existing external `SAVETOOLKIT_ARTIFACTS` directory, selecting the
`PPSA08330_saves_dec*.zip` archive by the same shortest-path convention as `SampleSaves`.
An independent Python standard-library check read each `ZipInfo` occurrence in memory, including
duplicate paths and backups, without extraction:

- **156 records**: 152 gameplay and four preferences (76/two primaries plus their backups).
- **156/156** match their existing save kind (`8`/`31`) and revision `0x00513040` at offsets `8`/`12`.
- **152/152** gameplay records have the existing save directory marker at `0x440`.
- **156/156** have zero at offset `4`, where `WadFile::Read` would take its first asset-entry size.
  That narrow observation does not prove that no embedded asset data could exist. It supplies no
  positive WAD framing or asset-to-save identity evidence, and the upstream reader was not run on saves.

The existing plugin suite supplies stronger preservation and write-boundary checks. Release results:

| Input mode | Passed | Failed | Skipped | Mutation robustness |
| --- | --- | --- | --- | --- |
| External private samples | 270 | 0 | 0 | 1,000 cases: two synthetic and three sample seeds, 200 each |
| Existing, verified-empty sample directory | 220 | 0 | 50 | 400 cases: two synthetic seeds, 200 each |

Both fuzz runs used seed `0x40C0FFEE`, with zero failures, known failures or expected failures.
The sample-backed suite verifies byte-identical no-op serialization of all 156 records;
**7,070 positive counters across 76 primaries** were edited, reparsed, checked for changes only
in intended u32 ranges and restored byte-identically. Core scalar and grouped F6 undo/redo tests
passed. Conformance, reference packs, field fingerprints, malformed-input checks and existing
finite CRC/digest research regressions passed. These are software checks, not console acceptance.
The Release plugin/test dependency build completed with zero warnings and zero errors.
The repository documentation checker passed all 49 checks against the follow-up commit's code.

All downloads, build outputs, package downloads, test imports and results used the task root
`D:/Temp/saveeditor/test-scratch/gowtool-license-followup`. Reproduce in PowerShell 7 from the repo
root, creating the directories before setting the environment:

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/gowtool-license-followup'
New-Item -ItemType Directory -Force $scratch, "$scratch/tests", "$scratch/empty-samples" | Out-Null
$env:TEMP = $env:TMP = $env:TMPDIR = $scratch
$env:SAVETOOLKIT_TEST_TEMP = "$scratch/tests"
$env:NUGET_PACKAGES = "$scratch/nuget-packages"
$env:NUGET_HTTP_CACHE_PATH = "$scratch/nuget-http"
$env:DOTNET_CLI_HOME = "$scratch/dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet test --project plugins/GodOfWarRagnarok/SaveToolkit.Plugins.GodOfWarRagnarok.Tests --configuration Release --artifacts-path "$scratch/build" --results-directory "$scratch/results/samples" --report-xunit-trx --report-xunit-trx-filename samples.trx
# Verify this directory is empty before the second run; setting it suppresses ancestor fallback.
$env:SAVETOOLKIT_ARTIFACTS = "$scratch/empty-samples"
dotnet test --project plugins/GodOfWarRagnarok/SaveToolkit.Plugins.GodOfWarRagnarok.Tests --configuration Release --artifacts-path "$scratch/build" --no-build --results-directory "$scratch/results/empty" --report-xunit-trx --report-xunit-trx-filename empty.trx
```

## Smallest useful next inputs and maintainer follow-up

1. **Names/IDs:** a small, build-identified Ragnarök definition export containing one currently
   unmatched saved u64 and its exact name/category (and variant level if claimed), with provenance
   and byte order. For skills, include one of the 132 individual saved IDs and its exact skill name;
   a tree-group name or 2018 texture hash is insufficient. This is smaller and more useful than a
   whole game install. GOWTool's 2018 support cannot promise extraction from Ragnarök assets.
2. **State/dependencies:** one controlled same-build PS5 before/after pair for that named skill
   purchase or gear upgrade, plus an unchanged native-save control and UI XP/level/equip evidence.
   Resolve costs, prerequisites and derived fields before considering any new writer. See
   [FIELD_DISCOVERY.md](FIELD_DISCOVERY.md) and [OWNER_CHECKLIST.md](OWNER_CHECKLIST.md).
3. **Integrity:** a save-specific serialization/integrity description or redacted trace with exact
   coverage, field exclusions, seeds and key-derivation structure; never credential/key values.
   Asset hashes, compression flags and unsuccessful checksum searches do not satisfy this need.
4. **Parent documentation:** update `docs/LICENCE_EXCLUSIONS.md`'s upstream-request row from Open
   to MIT added / issue #14 completed; pin its GOWTool research-only row to this revision and MIT.
   Keep the technical applicability gap. This plugin-only task did not edit the central register.

No new pack or protocol content was added: **no central snapshot/count/hash update is required**.
No edit-coverage state changes are warranted. Owner console checks stay deferred, and only the
owner may approve Stable. No upstream message, issue, PR, push, merge or publication is part of this work.

# Elden Ring evidence corpus

Inspection date: **2026-10-28**. No save bytes, extracted game files, account identifiers or
passwords belong here. Samples remain in the private artifacts folder. This directory stores
reproducible assertions and limitations, not game acceptance claims.

## Available baseline

`PPSA03169_saves_dec.zip`: two `memory.dat` records, one primary and one backup, each
28,967,040 bytes; both have SHA-256 prefix `5ae1d00212aadbc1`. F1 lists **one generation**.
The two files are identical twins, **one independent snapshot**. Application/regulation version,
DLC ownership, in-game item menu observations and console acceptance are unknown.

Character 0/profile formats: 150/90. The existing parser's level/rune/history/name/time/attribute
assertions are retained. New source-guided measurements:

| Fact | Observation | Evidence level |
| --- | --- | --- |
| PlayerGameData | `0xA2FF`, length `0x1B0` | Source plus snapshot structure; no controlled pair |
| Held common/key | `0xA6A3`, counts 63/2, matching nonzero rows | Structural cross-check; not 65 unique user possessions |
| Storage common/key | `0x13A5E`, counts 0/0 | Empty-container structure only |
| Variable projectile list | 23 records, eight bytes each | Explains storage position; not ownership/equip proof |
| Scimitar instances | Canonical `0x006CF2A0`, two separate handles | Inferred ID/name join; no menu screenshot |
| Shield | `0x01CB2310` | Same limitation |
| Rowa Fruit | `0x400050F0`, quantity 12 | Same limitation; no proven cap |
| Smithing Stone [1] | `0x40002774`, quantity 3 | Same limitation |
| Cracked Pot | `0x4000251C`, quantity 1 | Container semantics unproven |
| Map: Limgrave, West | `0x40002198`, key array | No map/progress event interpretation |
| HP / FP / stamina triples | `504/504/476`, `88/88/88`, `97/97/97` | Current/effective/base source labels inferred |
| Archetype byte | `PlayerGameData+0xB7`, value 1 | Class mapping not promoted; nearby `+0xBB` is gift |

Source revision: ClayAmore/ER-Save-Editor `014107f0ca1cff867b5f7565cb79d6684f9b35df`.
See the plugin README attribution table and dataset sidecar for identity provenance.

## F1 reproduction

From repository root, set the private sample root:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$sample = Join-Path $env:SAVETOOLKIT_ARTIFACTS 'PPSA03169_saves_dec.zip'
dotnet run --project tools/SaveToolkit.SaveDiff -- list $sample
dotnet run --project tools/SaveToolkit.SaveDiff -- compare $sample $sample --before-gen 0 --after-gen latest --file memory.dat --context 0 --preview 1 --semantic eldenring
dotnet test --project plugins/EldenRing/SaveToolkit.Plugins.EldenRing.Tests
```

The first two commands were run against the existing baseline: two unchanged files, zero changed
bytes, zero semantic changes, zero decode failures, one identical backup pair. Comparing `0` with
`latest` is a self-comparison here. It establishes **no** temporal dependency or gameplay write.
Do not use the raw compare command above on newly captured changing saves without privacy masks.

`fixtures.json` uses the shared `savetoolkit-fixtures/1` schema and records that limitation explicitly.
`EvidenceTests.BaselineCorpus_VerifiesWithoutPretendingToHaveIndependentGenerations` runs F1's
`FixtureVerifier`, supplying `EldenRingEvidenceDecoder` and `EvidencePrivacy` from this plugin's
test project. The custom decoder lives in the test project because runtime plugins may depend on
SDK only, whereas the F1 decoder contract lives in Testing. The shared CLI does not auto-discover
this test-only hook; use that test for the manifest. CLI `--semantic eldenring` sees ordinary bound
view fields and does not traverse F8 inventory pages.

The hook emits only numeric profile version/held runes/counts and inventory identity/quantity paths.
It excludes names, profile settings, account/password data and raw blobs. Handle + inventory-index
paths align relocated physical rows; duplicate identities get explicit occurrence suffixes. A
synthetic 12→13 quantity change with physical relocation produces one F1 semantic change, no
spurious removal/addition. Decode-budget truncation is reported, not interpreted as removal.

For future changing captures, use `EvidencePrivacy.Masks` for **both** endpoints with zero byte
context. It allowlists only active v150 inventory-container ranges, player numeric words `+08..+6B`
and archetype `+B7`; all other bytes are excluded before reporting. Combining endpoint masks keeps
only their safe intersection if ranges relocate. Exclusions are counted as masked differences and
explicitly labelled privacy exclusions, **not proven save churn**. Synthetic tests verify excluded
changes never enter raw hex previews. No uncontrolled timestamp/clock ignore rule has been invented.

## Regulation observations

Read-only AES/IV decode yields the exact sampled DCX/DFLT variant. Declared zlib payload is
1,859,344 bytes; decoded BND4 is 37,745,424 bytes, bounded to 64 MiB. All 186 entry/name/payload
ranges and sampled PARAM long-offset row directories validate. BND version string is `11001000`,
not a game patch. EquipParamGoods has 1,808 rows/data version 3; EquipParamWeapon has 2,973/data
version 6. These counts are structural evidence only.

The sample's encoded BND format byte `0x74` corresponds to bit-reversed `0x2E`, using a **32-bit
payload offset followed by an ID**. Reading an eight-byte offset happens to work on entry 0 (ID 0)
and fails on entry 1; a known-sample regression prevents repeating that assumption.

PARAM numeric row IDs are not universally unique in the actual snapshot. The type-pointer flag
`0x80` supplies an authoritative long type offset; the legacy string word is stale/out of bounds
in three Cutscene tables. The narrow reader handles both facts without repairing or rewriting them.
It does not deserialize row field bodies, derive carry/storage limits, copy Paramdex schemas,
read FMG descriptions or enable writes. Unsupported regulation variants produce a warning and
preserve the allocation. Synthetic malformed-input tests cover headers, codec, sizes, decompression
budget, zlib corruption, directory counts/offsets/names and PARAM schema/body/name bounds.

## Gates

- Phase 1 infrastructure: implemented. Temporal/console proof remains owner-dependent.
- Phase 2: packs, F4 inspection, F8 browsing and directory-only regulation decode implemented.
  Versioned field schemas, item caps, broad reference identities/classes/graces/upgrades/AoW,
  localized descriptions/icons and DLC coverage remain open; unknown values remain read-only.
- Phases 3–6: no new gameplay writes. See [OWNER_CAPTURES.md](OWNER_CAPTURES.md).

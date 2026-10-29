# Equipment / attributes / respec field discovery (#40)

Research date: **2026-10-29**. Sources, exact revisions, licence decisions and access dates are in
the [README attribution table](../README.md#sources-and-attribution). Public downloads were read as
untrusted text in D: scratch; no upstream program was executed. No private sample bytes, extracted
regulation, credentials or whole decoded character records are committed.

## Corpus and differential method

The supplied artifacts directory contains exactly one matching archive among all four registered
Elden Ring title IDs: `PPSA03169_saves_dec.zip`. Its two `memory.dat` records are a primary and a
byte-identical console backup, one generation, one active slot (0), character/profile formats 150/90.
Other regions/classes/levels and independent gameplay generations are unavailable.

`FieldDiscoverySampleTests` uses the plugin's production `eldenring-evidence` hook through
`HookSaveDecoder` on **both** records. It compares their complete bounded semantic trees and asserts
no differences, then checks per-record attributes, level mirrors, named equipment, absent Larval Tear
rows and byte-identical serialization. This is a differential **negative/control-limited result**,
not an observed respec/equip transition. `SampleTests` independently pins the twins' byte identity.
The decoder adds only approved numeric/identity paths, never neighboring account/settings/password
values. Both endpoint masks now allow the modeled numeric equipment ranges and individual menu
words; a synthetic mixed allowed/excluded difference checks the mask without real secret data.

## Attributes and rune level

The version-bounded GaItem map walk locates PlayerGameData at file `0xA2FF`; this is a measured sample
location, not a fixed address for all saves. Eight little-endian u32 words at Player `+34..+50` are:

| Vigor | Mind | Endurance | Strength | Dexterity | Intelligence | Faith | Arcane |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 13 | 13 | 11 | 11 | 16 | 10 | 8 | 9 |

Their sum is 91. Player `+60` and the active menu summary `+22` both contain 12; thus
`rune level = sum(attributes) - 79` holds in every supplied record. Public editor UI and structure
notes corroborate the formula and 1–99 bounds (see cited sources). Those references do not replace
PS5 before/after evidence or establish this build's legal starting-class minima.

The existing attribute and level views incorrectly used the helper's default Verified confidence.
They now explicitly use **Inferred**, remain read-only, and carry provenance. The evidence decoder
emits `slots/<n>/attributes/<name>`, `slots/<n>/level` and `slots/<n>/profile-level` without changing
the existing prefix/inventory paths or their output order.

## Equipment joins, regulation and menu copies

The existing bounded reader has 18 known gear words (weapons/ammo, four armor pieces, four talismans),
ten quick items and six pouch slots: 34 candidates, 17 agreeing assigned joins, 17 unassigned.
All assigned identities have attributed catalog names. Scimitar instances remain distinct; no join
uses a name as identity or selects the first duplicate handle.

| Join | Evidence |
| --- | --- |
| Live handle → held inventory | Unique matching common row, nonzero quantity and category agreement. The left/right first Scimitars share ID `0x006CF2A0` but have different instance handles. |
| Held row → EquipData | Physical common record + `0x180`; first Scimitar record 2 → `0x182`, whereas acquisition index is `0x24D`. These indices are not interchangeable. |
| Weapon/armor → GaItem | Exact handle → exact full ID; duplicate/missing/category-inconsistent entries stay unresolved. |
| Talisman/goods | Direct handle category normalization; no fabricated GaItem map entry needed. |
| Assembly / equipped copy | Category-stripped assembly word after seven selectors; EquippedItems retains category-qualified ID. |
| PARAM directory | Every assigned gear/quick/pouch exact ID (category nibble removed) exists in the corresponding Weapon/Protector/Accessory/Goods PARAM directory in the decoded sample regulation. No affinity/upgrade rounding or payload-schema assumption. |
| Catalog | Exact full ID joins existing pinned names; Blue Cloth Cowl is `0x100A3930`. A name is identity evidence, not eligibility or a legal cap. |

New read-only profile candidates are based on **sample measurements**, checked independently in
`ProfileEquipmentTests`, rather than assuming the pinned common profile reader's gap words match.
For known gear word `w`, the two candidate arrays are at profile-relative `0x17E + 4*w` and
`0x1D6 + 4*w`. The array origins include the unknown gear words; do not compact them.

For each assigned weapon/ammo, the first array matches `0x80000000 | (fullId & 0x007FFFFF)`;
for assigned armor/talismans it matches `0x80000000 | fullId`. The second array matches
`fullId & 0x0FFFFFFF`. The first Scimitar live handle differs from its menu encoding, and both
Scimitars share that encoding despite different owned-instance handles. Profile data cannot uniquely
select an inventory instance. Masked weapon bits cannot be reconstructed from the menu word alone.
Unassigned sample gear has first word zero and second `0xFFFFFFFF`; these are observations only,
not a universal empty/unarmed setter. Some assigned candidates themselves represent unarmed choices.

The view and F1 expose both menu words for v150/profile-90 only. They compare encodings against a
resolved live identity, report disagreements without repair, and omit these candidates on unsupported
profiles. Synthetic tests separately cover weapon/armor/talisman encodings, disagreements in each
menu copy, profile 91 refusal and slot 9 addressing. No quick/pouch menu layout or equipment writer
is asserted. Existing ambiguity/missing/zero-quantity/category tests still protect the live joins.

## Larval Tears: negative result

The existing MIT goods catalog maps decimal **8185** (`0x1FF9`) to **Larval Tear**. Its canonical
identity is `0x40001FF9`, with direct goods handle `0xB0001FF9`. The sample's EquipParamGoods directory
has that exact row. Neither held common/key nor storage common/key contains that identity in either
record. This is **absence of an observed inventory row**, not evidence of an authoritative zero
respec balance, cumulative uses, rebirth unlock or quest state.

A synthetic key row with that handle and quantity two resolves correctly and rejects quantity edits.
It proves the reader's representation, not real PS5 ownership, capacity, consumption or insertion.
No special counter/view/write is added. A controlled acquisition followed by rebirth and a no-action
control is needed to separate ordinary inventory quantity from availability, consumption and flags.

## Dependency and integrity decision

All discovered fields stay read-only. Even preserving `sum(attributes)` during respec does not avoid
HP/FP/stamina, damage/scaling, resistance, requirements or maximum-load dependencies. Current/effective/
base triples remain separate: HP 504/504/476, FP 88/88/88, stamina 97/97/97. No stat-to-vital curve,
current-vital policy, effect order, rounding, maximum load or weight value was derived from one point.

Paramdex's cited weapon/protector/accessory definitions declare equipment `weight`. SpEffect declares
`maxHpRate`, `maxMpRate`, `maxStaminaRate` and `equipWeightChangeRate`. These facts identify concrete
equipment/effect dependencies; matching PARAM IDs does not validate this build's row field offsets or
calculation. This round reads directory IDs only and leaves the encrypted allocation unchanged.

The PS wrapper lacks PC MD5 prefixes, corroborated by the permissive PS writer and sampled boundaries.
That wrapper property does not settle internal gameplay hashes. SaveForge's equipment/hash chapters
conflict about recomputation/validation; restricted Hapfel notes describe per-value hash inputs.
Their PS5 application is unverified: this plugin has no located/repaired/verified internal hash block.
No claim that the game ignores it is adopted. Thus the requested checksum gate for **new** writers
is unmet, independently of incomplete modifier/profile/empty-slot transactions. Experimental maturity,
F6 preview and undo are available infrastructure, not proof that a new equip/respec write is valid.
Existing authorized write behavior is unchanged.

## Needed evidence and suggested coverage text

For ER04 obtain: one-stat and budget-preserving respec pairs plus no-action controls; starting classes
at multiple levels; damage/heal, gear modifier, rune/effect and load observations; both level mirrors;
an independently located and verified integrity block. Preserve each current/effective/base policy.

For ER05 obtain: owned-instance equip/swap-back for weapons/ammo/armor/talismans and empty/unarmed
choices, selector and unlocked-slot changes, corresponding menu copies, weight/effect deltas and
build-matched PARAM payload schemas; upgrade/AoW history remains a separate dependency.

Suggested `docs/EDIT_COVERAGE.md` replacement text (maintainer edit; this task edits only this plugin):

> ER04 — R/I: all eight attributes and rune level/profile mirror checked in JP v150/90, one snapshot
> plus identical backup; level = sum − 79. Larval Tear ID 8185 exists in catalog/regulation but no owned
> row or respec-state transition is sampled. Missing modifier-aware vitals/load, class minima, Larval
> Tear consumption/unlock semantics and verified internal-hash repair; retain research status.
>
> ER05 — R/I: 34 equipment candidates, 17 agreeing assigned handle/physical-index/ID/catalog joins,
> exact matching sample PARAM rows, plus two measured PS5 menu encodings at +17E/+1D6. Pinned upstream
> profile gaps differ; menu encoded IDs are not instance handles. Missing complete profile/effect/load,
> empty/unarmed/slot eligibility, upgrade/AoW history and verified internal-hash writer; no equip write.

Hapfel's current restricted revision has a refusal row in the plugin README. The same row should be
added to `docs/LICENCE_EXCLUSIONS.md` by the maintainer; that file is outside this task's edit scope.
No GPL-2.0-only material was selected or copied. No reference-pack file changed.

## Validation

Release validation on 2026-10-29: **413 passed, 0 failed, 0 skipped**, including all inherited
conformance checks and mutation fuzz (**200 cases**, one unique sample seed, seed `0x40C0FFEE`,
zero failures/known failures). Both primary and backup receive explicit field/regulation/no-op tests;
the fuzz harness deduplicates their identical bytes. API Release build: **0 warnings, 0 errors**.
`git diff --check` passes; all changed C# files are below 300 lines. No pack hash re-pin is needed.

Commands use the external read-only artifact directory and task scratch only:

```powershell
$scratch = 'D:/Temp/saveeditor/test-scratch/research-eldenring-equipment'
New-Item -ItemType Directory -Force "$scratch/tests" | Out-Null
$env:TEMP = $env:TMP = $env:TMPDIR = $scratch
$env:SAVETOOLKIT_TEST_TEMP = "$scratch/tests"
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet test --project plugins/EldenRing/SaveToolkit.Plugins.EldenRing.Tests -c Release
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj -c Release --warnaserror
```

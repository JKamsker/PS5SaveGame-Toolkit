# Final Fantasy VII Remake / Rebirth (`ff7r`)

**Maturity: Experimental.** A bounded PS5 linked-record explorer with in-place edits for narrowly
qualified gil, ordinary item-stack quantities and materia AP within the original star tier on Remake and retail Rebirth. The
supplied decrypted samples show no internal checksum or dependent metadata, and public editors/Save
Wizard codes patch these values directly. Empty, unknown and summon-exception rows are read-only;
demo gameplay remains read-only because its field meanings are inferred. No console loading test has
been performed; keep backups and treat every edit as untested.

## Supported titles and files

| Title | Variant | Gameplay | Common/system |
| --- | --- | --- | --- |
| PPSA01911 | Remake Intergrade, including INTERmission | `ff7remakeNNN`, `ff7remakeplusNNN`: 9,434,752 bytes | `ff7remakecommon`: 1,056 bytes |
| PPSA08664 | Rebirth retail | `ff7rebirthNNN`: 23,381,104 bytes | `ff7rebirthcommon`: 1,056 bytes |
| PPSA18936 | Rebirth demo | `ff7rebirthNNN`: 23,381,104 bytes | `ff7rebirthdemocommon`: 1,056 bytes |

`NNN` is exactly three decimal digits; optional `.sav` suffixes are accepted. Probe requires the
assigned title, filename, measured size and initial framing. Unfamiliar sizes/versions are rejected
with `SaveFormatException`, rather than silently reinterpreted. Common-file settings remain unmapped.
SFO, icons, keystones and unrelated files are not opened. Retail/demo have distinct schema identities;
matching dimensions do not establish conversion compatibility.

## Verified container layout

All integers are little endian. The shared walker checks each absolute next pointer, allocation size,
kind sequence, repeated tags, declared length, supported version words and terminal record. Strictly
forward links and a 512-record safety ceiling reject cycles, overlap, overflow and out-of-file access.
Only headers are decoded at open time; bodies stay in the original buffer.

| Relative offset | Size | Observation |
| --- | --- | --- |
| `+0x00` | 1 | Record kind; first gameplay record 0, common 5, tail 4 |
| `+0x01` | 1 | Slot/index-like byte; exposed raw |
| `+0x02` | 1 | State-like byte, 0 or 1 in these samples; exact meaning unknown |
| `+0x03` | 1 | Zero in these schemas |
| `+0x04` | 4 | Absolute next-record offset, with the terminal exception below |
| `+0x08` | 4 | Remake zero; Rebirth allocated content length, with terminal exceptions |
| `+0x0C` | 4 | Remake zero; Rebirth flags 1 or 3, exact meaning unknown |
| `+0x10` | 16 | Four repetitions of `RESD`, `LOSD`, `COSD`, `MLSD` or `NVSD` |
| `+0x20` | 4 | Version-like word; MLSD/NVSD upper bytes vary, semantics unknown |
| `+0x24` | 4 | Content length **including** the 24-byte content header at `+0x10` |
| `+0x28` | variable | Body, followed to the next record without decoding/rebuilding it |

Empty allocated records have all 24 content-header bytes zero. Their complete allocation is preserved
and explorable; an empty header does not justify discarding the body. A tail has only its 16-byte
envelope and must end at EOF. The explorer shows literal tail words, not normalized pointers.

| Variant/role | Tail words at `+0x00 / +0x04 / +0x08 / +0x0C` |
| --- | --- |
| Remake, both roles | `0x00010004 / 0 / 0 / 0` |
| Rebirth/demo common | `0x00010004 / 16 / 0 / 1` |
| Rebirth/demo gameplay | `0x00010004 / 0 / 16 / 1` |

The Rebirth common tail's **16 is not another absolute link**. Following it would loop into the common
record. This is why the tail marker, role and variant must be checked before following pointers.

### Record groups

Lengths below exclude the 16-byte envelope, but include the content header.

| Variant | Record indexes | Kind | Tag when populated | Length | Version-like word |
| --- | --- | --- | --- | --- | --- |
| Both, common | 0 | 5 | COSD | `0x400` | 1 |
| Remake | 0 / 19 | 0 / 2 | RESD | `0x50000` | 7 |
| Remake | 1–18 / 20–22 | 1 / 3 | LOSD | `0x66100` | 13 |
| Rebirth/demo | 0 / 81 | 0 / 2 | RESD | `0xE4320` | 14 |
| Rebirth/demo | 1–80 / 82–161 | 1 / 3 | LOSD | `0x14F50` | 27 |
| Rebirth/demo | 162–169 / 170–177 | 11 / 10 | MLSD | `0x69030` | low byte 2 |
| Rebirth/demo | 178–225 / 226–273 | 14 / 15 | NVSD | `0x2440` | low byte 1 |

Remake has 24 records including tail at `0x8FF670`; Rebirth/demo have 275 including tail at
`0x164C460`. Common has two records with tail at `0x410`. KingdomSaveEditor identifies Remake's
second RESD as previous common state; the UI avoids assigning that meaning to Rebirth without proof.

## Curated fields and confidence

All offsets below are **absolute**, into the first RESD record only. Previous-state copies are
available through the raw explorer and are not conflated with current gameplay.

| Meaning | Remake | Rebirth retail | Demo confidence |
| --- | --- | --- | --- |
| Play time, u32 seconds | `0x2B0` | `0x3C30` | Inferred retail mapping |
| Inventory item ID | `0x34DD8 + slot*24` | `0x4FA70 + slot*32` | Inferred retail mapping |
| Inventory quantity, i32 | ID offset +4 | ID offset +4 | Inferred retail mapping |
| Gil | Unique inventory entry with ID 20 | Unique inventory entry with ID 1 | Inferred, ID 1 |
| Current chapter ID, zero based | u8 `0x2E` | Unmapped | Unmapped |
| Materia ID, u32 | `0x1844 + slot*32` | `0xBFA6C + slot*32` | Inferred retail mapping |
| Materia AP, u32 | `0x1848 + slot*32` | `0xBFA70 + slot*32` | Inferred retail mapping |
| Materia level, u8 | `0x1840 + slot*32` | `0xBFA78 + slot*32` | Inferred retail mapping |

There are 2,048 inventory slots and 1,000 materia slots. `Verified` for retail field meanings means
the cited game-specific public schema supplies the interpretation and the layout was checked against
the PS5 samples. It does **not** mean PS5 editing or console loading was tested. Demo meanings remain
`Inferred`, even where plausible values occur at identical offsets. Raw body words and version/state/
flag semantics are `Unknown`. Record offsets, allocation boundaries and declared lengths are verified.

Gil is exposed only if exactly one matching entry exists. Unknown names remain `Unknown ID 0x...`;
no game-specific dictionary is shared across Remake/Rebirth. Empty slots and negative sentinel values
remain visible; negative quantities receive a warning. No arbitrary gameplay limits are imposed on
read-only values. Ordinary item stacks use an editor cap of 9,999, gil 9,999,999 and final-tier AP
999,999; these are not proven game caps. An original value above a cap remains restorable after
editing and refreshing bindings. Negative quantities, ambiguous gil, equipment, materials, key items,
cards and other progression counts are read-only. AP must remain within the original coherent star
tier. Every summon, unknown type and AP/level mismatch is read-only; no stored star is recalculated.
Names, categories and source-metadata descriptions use the embedded MIT reference packs below.

### Read-only characters and owned relationships

The **Characters** tab exposes seven candidate party records for each gameplay file. Remake uses the
measured `0x40` stride and Rebirth/demo use the measured `0x100` stride; level, XP, current/max HP and
(for Rebirth) weapon-detail references are read-only `Inferred` fields. The names and order are the
MIT resource order, while guest, past and cached records still need controlled screenshots.

Retail/demo Rebirth also exposes searchable F8 **Owned items** collections. Weapon rows join inventory instance/type
records at `0x5FA6C` to detail records at `0xB3A68` (stride `0x60`) and show character references at
`0xCC + index*0x100`. Materia rows join instance/type/quantity at `0x60A6C` to detail records at
`0xBFA68`, including the observed owner byte at `+0x11`. These are identity joins only; they never
enable an edit. The **Catalog** tab searches all item/materia/weapon metadata and can use optional
toolkit reference packs with catalog ids `ff7r-items`, `ff7r-materia`, `ff7r-weapons` and
`ff7r-characters`.

### Semantic safety evidence

The F1 differential engine is exercised by `RelationshipTests.F1DiffEvidence_DemoGenerationChangesBodiesOnly`.
The supplied demo snapshots differ in 800 body bytes and no record envelope/content-header/tail
bytes. `RelationshipTests` also checks every supplied retail/demo character weapon reference against
the 128 measured weapon-detail records and checks the first owned materia join. This evidence does
not prove gameplay behavior or console acceptance.

Across all primary supplied retail snapshots, **465 Remake and 530 Rebirth ordinary materia rows**
agree with the pinned AP curves and stored stars. This supports narrowing existing AP edits to the
original tier, not enabling star transitions. The Rebirth Titan type `14011` illustrates why all
summons remain disabled: repeated-zero source thresholds disagree with stored levels in three retail
samples. Empty, unknown, incoherent and out-of-policy values remain visible and byte-preserved.

The **Set AP within current star tier** action uses an F4 picker over owned eligible types and F6
planning over selected rows or all copies of that type. F8 select-all-filtered scopes are resolved by
Core. Preview reports old/new AP, skipped rows/reasons and the console warning. It changes no bytes;
apply revalidates the plan and is one undo step. `MateriaPlanningTests` uses F1 `ByteDiffer` to prove
that apply changes only the selected four-byte AP words, then checks reopen and exact undo/redo.
Character XP/HP/level, equipment references, sockets, materia moves, loadouts and creation/removal
remain disabled because no controlled before/after capture proves their complete dependencies.

Examples asserted by tests (primary files):

| Title / file | Gil | Play seconds |
| --- | ---: | ---: |
| PPSA01911 / ff7remake000 | 44,269 | 117,739 |
| PPSA01911 / ff7remake001 | 831 | 6,786 |
| PPSA01911 / ff7remake010 | 54,419 | 120,384 |
| PPSA01911 / ff7remakeplus000 | 17,469 | 13,160 |
| PPSA08664 / ff7rebirth000 | 77,530 | 155,133 |
| PPSA08664 / ff7rebirth001 | 2,000 | 1,035 |
| PPSA18936 / ff7rebirth000 | 2,000 | 4,064 |
| PPSA18936 / ff7rebirth001 | 2,000 | 4,080 |

## Round-2 catalogs, progress and remaining scope

Two embedded F2 packs are pinned to the supported title/build names. They provide 369 item types
(including 32 weapons) and 54 materia types for Remake; 934 item types (including 51 weapons) and
88 materia types for Rebirth. Every record has a name, category, source-metadata description and
pinned MIT attribution. Descriptions explain classification, AP thresholds or identity domains;
they are **not a complete set of game effect descriptions**. F8 searches the whole dataset before
paging. Imported document-pinned packs can supply richer descriptions, with their own provenance,
without changing the embedded edit policy. Character and owned-item names also show attribution.

The **Cards** collection contains 145 individual Rebirth card types, with stored inventory quantities.
Its 16 booster-pack types remain separate in the item catalog. Source labels duplicate display
number 012 and omit 013; these labels are retained with an explicit warning, not corrected by an
unverified numeric formula. Quantity, deck membership and opponent wins remain separate domains.

The **Progress** collection shows 69 distinct Remake candidate bits for obtained dresses, battle
intel entries/completion/rewards and VR missions. Rebirth/demo show 470 candidate bits for hard
chapter flags, Queen's Blood opponents, World Intel discovery/completion and Chocobo-stop
discovery/repair. Exact region and domain/state filters cover Grasslands, Junon, Corel, Gongaga,
Cosmo Canyon and Nibel. Fields expose the source byte/bit and remain read-only **Inferred**.
Shared source addresses and all `Intel Entries` groups were excluded because repeated placeholder
addresses cannot distinguish individual objectives. A stored set bit is not a verified completion
or reward. No percentages, universal completion preset, map-coordinate guesses or flag writes ship.

This completes the scoped catalog/F4/F8 infrastructure and read-only progress/collection delivery.
Full Phase 3 remains blocked on controlled character/gear/star-transition evidence. Full Phase 4
still needs effect descriptions/icons and persistent loadouts, and Phase 5 still needs separately
researched folios, crafting, enemy intel, relationships and minigame modules. Fandom page/revision
retrieval returned HTTP 403/402 during this round; no unpinned CC BY-SA text was embedded. The
MIT data path above was available. Any later wiki extract must pin revisions and preserve
CC BY-SA attribution/share-alike in its own dataset, source sidecar and UI provenance.

### Exact owner capture and validation checklist

Use a disposable slot and capture **Remake main, INTERmission, retail Rebirth and demo separately**.
For every pair record title ID, game patch, chapter, difficulty, slot, character/region, exact menu
values and reproducible gameplay steps; include decrypted same-slot before/after files and screenshots.

- [ ] One XP level-up: current and max HP/MP, XP/level before/after; distinguish cached/live/guest
  records and identify Yuffie/Sonon, past characters and Rebirth guests explicitly.
- [ ] One ordinary materia AP change below a threshold, then one star-up crossing it. Capture AP,
  stars, equipped character/socket and the relevant spell/ability menu. For Rebirth capture one
  summon-tier increase with AP unchanged; do not use ordinary AP rules for summons.
- [ ] Swap one owned weapon, armor and accessory separately; move one materia between sockets,
  including a linked pair and summon slot. Record type/instance IDs, all owners, sockets, weapon
  level/proficiency and folio state. Swap back and compare decrypted bytes.
- [ ] Acquire one Queen's Blood card, open one booster pack, change one deck and beat one named
  opponent as separate pairs. Supply the menu display number/name for source-labeled cards 012/013.
- [ ] Complete one World Intel objective, discover then repair one Chocobo stop, collect its reward,
  purchase one folio node, craft one item, unlock one outfit and finish one minigame as separate pairs.
  Record active region and objective states, counters, rewards and actor changes at each step.
- [ ] Capture chapter-select/new-game-clear and quest-reset pairs separately from current chapter.
  Do not infer historical clear, unlock and reward state from a single shared flag.
- [ ] Import one spare edited save per writable variant: check gil/item/AP menus and actual effects,
  play a battle, resave, decrypt and re-import. Confirm the intended scalar survives and no other
  bytes, ownership/detail links or previous-state/common files were normalized. Report any load
  error, missing reward, inventory loss or unexplained state change before expanding write scope.

## Integrity, crypto and compression

The supplied decrypted payloads contain directly readable framing, arrays and numerical fields.
No decryption or decompression is needed to expose these structures. No `GVAS` signature occurs
in any of the 55 supplied FFVII payload entries. These are custom linked records, not a demonstrated
Unreal tagged-property stream. Unknown bodies may still contain compressed/custom structures.

The Round 2 search is recorded in [`research/integrity_search.py`](research/integrity_search.py). It
tests each record body, record including its content header, the file through the tail, and the full
file against reflected and non-reflected CRC-32/CRC-32C/CRC-64 variants (init/xorout 0 and all ones),
Adler-32, FNV-1a 32/64, xxHash 32/64, MurmurHash3, byte/word sums, and truncated MD5/SHA-1. Every
4/8-byte word in record envelopes, content headers and tails is checked, including whole-file ranges
with the candidate word omitted. The three 1,056-byte common files produced **2,738 candidates and
zero matches each**; the same script is reproducible for every archive entry.

The stronger differential evidence covers all primary gameplay snapshots. In the paired demo saves
`ff7rebirth000` → `ff7rebirth001`, 800 bytes changed in record bodies and **zero bytes changed in any
record envelope, 24-byte content header or terminal tail**. The test
`DemoSnapshotDiffs_DoNotChangeRecordMetadata` asserts this directly. Remake backup twins are byte
identical. No header/tail word changed in response to a gameplay change, so there is no observed
per-record or whole-file checksum field to update. This cannot rule out an undocumented encrypted
outer container or a dependency in an unobserved save state.

Independent public evidence points the same way: the [Save Wizard FFVII Remake code listing](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/main/SaveWizard-Codes/FFVII-Remake%20Codes%20%5BSaveWizard%5D.txt)
writes gil, inventory and materia/AP addresses directly and documents no checksum-fix operation;
the [Remake](https://game-tools-collection.com/final-fantasy-vii-remake/save-editor) and
[Rebirth](https://game-tools-collection.com/final-fantasy-vii-rebirth/save-editor) editors likewise
write fixed fields in PC saves. These are not proof of PS5 console acceptance, but are consistent with
the sample differential result.

Writable fields are patched in place and `Serialize()` re-walks the complete output before returning
it. Unknown bytes, padding, trailing data and all unedited records remain byte-for-byte unchanged.
The plugin does not claim that a modified file will load on a console until that is tested.

## Bounded views and performance

Overview contains only headline facts and a 2,048-entry scalar scan to find gil. The initial record
table has at most 275 rows; record sections are grouped in lazy batches of 32. F8 inventory/materia/catalog/progress/owned collections return at most 200 rows per query; retained legacy lazy endpoints have at most 128 rows. Every record has a paged hex view plus a lazy tree reaching **every** aligned
body word with u32, i32, f32 and ASCII interpretations. Word pages have at most 64 rows and intermediate
ranges at most 32 children. NaN/Infinity interpretations are text, keeping the view JSON-serializable.
Blob requests are capped at 64 KiB and copied to protect the document's backing buffer.

Open retains one file buffer, the small header index, catalog lookups and fixed-slot edit policies; there are no body copies or eager gameplay view trees.
Tests check each supplied sample opens in under two seconds and the initial view stays below 2,500
nodes. No framework changes, runtime file/network access or extra NuGet dependencies are required.

## Research log and validation

Research date: 2026-10-28. Read upstream code as untrusted text; no downloaded code was executed.

1. Started with the repository's three title reports and the linked-chunk reconnaissance.
2. Read KingdomSaveEditor headers, common/chapter models, inventory/materia structures and write path.
   Its fixed Remake layouts were not applied to Rebirth.
3. Located game-tools-collection through web/GitHub searches; inspected its separate Remake and
   Rebirth templates, gil lookup routines, item/materia names and public tests. The discovered Nexus
   editor remains a lead only; no proprietary/unlicensed implementation was copied.
4. Independently walked every archive payload, retaining duplicate ZIP occurrences and backup twins.
   Confirmed 40 Remake entries (38 gameplay, 2 common), 12 retail Rebirth (11 gameplay, 1 common),
   and 3 demo (2 gameplay, 1 common). All chains end exactly at EOF. Existing Remake archive twins
   are byte-identical; no retail/demo console backup pairs exist in the supplied archives.
5. Confirmed empty slots, MLSD/NVSD groups, Rebirth flags and the nonzero common tail pointer.
   Compared public field offsets to all primary slots, including INTERmission and both demo saves.
6. Tested bounds, cycles, tags/lengths/versions, tail variants, no-op preservation, lazy navigation,
   writable edit round trips, atomic range rejection, undo restoration and buffer isolation. Arbitrary
   body corruption remains outside the structural walker's guarantees.

Reproduce aggregate measurements without extracting any samples:

```powershell
python plugins/FinalFantasy7/research/inspect_samples.py C:/path/to/main/artifacts
dotnet test --project plugins/FinalFantasy7/SaveToolkit.Plugins.FinalFantasy7.Tests
```

Sample-dependent tests skip when their title archive is missing. Synthetic common-format and explorer
tests run without samples. The conformance suite retains its six-files-per-title default; the dedicated
format test walks and no-op serializes **all** matching sample files, including backup/generation files.
Edit conformance nudges a writable field on selected Remake and retail Rebirth samples; dedicated
tests verify in-place byte scope, round-trip reopening, undo-by-old-value and atomic range rejection.

Round-2 local validation: **79/79 plugin tests, 0 skipped**, with the supplied archives and the API build passing with zero warnings/errors. The new tests include Core F4/F6/F8 session integration; a separate whole-Core-suite run was not needed for this plugin-only change. All 55 sample entries round-tripped;
every measured sample Open call stayed below the two-second test bound.

## Open problems

- Confirm that edited files load on a PS5 and establish real in-game value limits; no Stable claim.
- Confirm retail meanings/ranges and demo mappings against controlled in-game changes. Demo remains
  read-only until its offsets are independently confirmed.
- Map Rebirth chapter/difficulty/party, common settings and MLSD/NVSD semantics.
- Decode deeper record structures from evidence; no generic Unreal property parser is assumed.
- Validate additional patches/regions/sizes explicitly; current gates intentionally accept only the
  measured layouts. Rejecting an unsupported file does not modify it.

## SDK requests

None required. The existing lazy sections, field confidence and paged blob API support bounded editing.

## Sources and attribution

| Dataset | Pinned source | Licence / modifications |
| --- | --- | --- |
| Remake item categories/names (369) | [GTC resource.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/utils/resource.ts) | MIT, RyudoSynbios; extracted metadata, merged weapons, original summaries |
| Remake weapons (32) | Same pinned Remake resource | MIT; extracted type/name rows, original identity description |
| Remake materia (54) | Same pinned Remake resource | MIT; category/AP arrays, original AP summaries |
| Rebirth item categories/names (934) | [GTC resource.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils/resource.ts) | MIT, RyudoSynbios; merged weapons, separated 145 cards/16 boosters, original summaries |
| Rebirth weapons (51) | Same pinned Rebirth resource | MIT; type/name rows, original identity description |
| Rebirth materia (88) | Same pinned Rebirth resource | MIT; category/AP arrays, summon restrictions in descriptions |
| Remake progress candidates (69) | [GTC template.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/template.ts) | MIT; literal labels/addresses and state grouping, ambiguity filtering |
| Rebirth progress candidates (470) | [GTC template.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/template.ts) | MIT; distinct region/state bits, excluded placeholders/aliases |
| Legacy names.json / equipment.json and character order | Revisions `e38d207c2210d210d9b06ebd2a0a62b4e16c7aed` / `c7bdd6d8228304b87dcdf15e57155d092eb06fd2`, linked below | MIT; retained prior extracts and notice |

Retrieved 2026-10-28. Catalog source sidecars and full MIT notices accompany
[Remake](SaveToolkit.Plugins.FinalFantasy7/ReferencePacks/ff7r.remake.en/catalog.source.md) and
[Rebirth](SaveToolkit.Plugins.FinalFantasy7/ReferencePacks/ff7r.rebirth.en/catalog.source.md).
The [progress sidecar](SaveToolkit.Plugins.FinalFantasy7/Resources/progress.source.md) documents
selection/exclusion rules. Manifest attribution is shown by F2's Sources panel; F4 candidates and
F8 descriptions/source columns retain provenance. No wiki text, artwork, NC parser or unlicensed
implementation was bundled. Existing research-only sources and legacy extracts follow.


- Local starting evidence: [format overview](../../docs/formats/README.md),
  [Remake](../../docs/formats/PPSA01911.md), [Rebirth](../../docs/formats/PPSA08664.md),
  [demo](../../docs/formats/PPSA18936.md). Independently repeated measurements above.
- [KingdomSaveEditor, pinned revision](https://github.com/Xeeynamo/KingdomSaveEditor/tree/37a7a9dde19463a0d5f0c9588c634d6129e536f6/KHSave.LibFf7Remake):
  `Chunks/Chunk.cs`, `ChunkCommon.cs`, `ChunkChapter.cs`, `SaveFf7Remake.cs`, `Models/Inventory.cs`,
  `Models/Materia.cs`, `Types/InventoryType.cs`. Used as format/offset research. GPL-3.0; no source
  implementation or name tables copied from this project.
- [game-tools-collection Remake schema](https://github.com/RyudoSynbios/game-tools-collection/tree/e38d207c2210d210d9b06ebd2a0a62b4e16c7aed/src/lib/templates/final-fantasy-vii-remake/saveEditor):
  `template.ts` supplies absolute field offsets; `utils.ts` supplies the inventory gil search.
- [game-tools-collection Rebirth schema](https://github.com/RyudoSynbios/game-tools-collection/tree/e38d207c2210d210d9b06ebd2a0a62b4e16c7aed/src/lib/templates/final-fantasy-vii-rebirth/saveEditor):
  separate offsets/strides, play seconds, gil ID 1, materia/AP layout. Both games' `utils/resource.ts`
  supply item and materia names, extracted as data into `Resources/names.json` (337/54 Remake and
  883/88 Rebirth names). MIT, copyright 2024 RyudoSynbios; notice retained in
  [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). All parser/view code here is independently written.
- [game-tools-collection pinned resource revision](https://github.com/RyudoSynbios/game-tools-collection/tree/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils):
  character order and the Rebirth weapon list used by the read-only relationship view. The small
  `Resources/equipment.json` extract is MIT-derived, retains the same copyright notice, and carries
  no artwork, descriptions or game limits. The current plugin fallback catalog is text-only; an
  imported `IReferenceContext` pack can replace names without plugin I/O.
- [Public Remake editor](https://game-tools-collection.com/final-fantasy-vii-remake/save-editor) and
  [Rebirth editor](https://game-tools-collection.com/final-fantasy-vii-rebirth/save-editor): platform scope
  is PC/Steam, not evidence of PS5 console compatibility.
- [Nexus Rebirth editor](https://www.nexusmods.com/finalfantasy7rebirth/mods/2072) and its
  [web version](https://ff-7-rebirth-save-editor.vercel.app/): gameplay capability research lead only;
  implementation licence/PS5 compatibility unverified, no code reused.
- [Save Wizard FAQ](https://www.savewizard.net/faqs/): documents that Save Wizard supports PS4 saves on
  PS5, while native PS5 saves are outside its supported scope; this is why its direct-write codes are
  supporting evidence for the decrypted layout only, not a console-load guarantee.

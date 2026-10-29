# Final Fantasy VII Remake / Rebirth (`ff7r`)

**Maturity: Experimental, v0.5.0.** The owner-authorized write-enable round turns on mapped existing
character values, ordinary materia AP/star transitions, owned Rebirth weapon references, mapped
non-equipment counts and individual progress bits on Remake/INTERmission, retail Rebirth and demo.
Edit coverage adds previewed current-HP/MP recovery, batch character levels with synchronized XP,
and a bounded first-record stored-time setter over the same Experimental write path.
All writable fields are **Inferred**. Not validated on console. The game may reject or overwrite the edited save.
Internal integrity is unconfirmed. Unknown bytes are preserved and `Serialize()` structurally reparses
its output; this does not prove gameplay compatibility. Keep a backup and use a disposable slot.

## Enabled writes and their evidence

Owner evidence is tracked separately in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).
It is validation work, not a prerequisite to using these Experimental controls. The table includes
the retained gil/ordinary-stack writers. Except for Rebirth weapon references, these write kinds
are available on Remake/INTERmission, retail Rebirth and demo when their per-row eligibility checks pass.

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Gil | Inferred | Exactly one matching inventory type (20 Remake, 1 Rebirth/demo), nonnegative original count; editor bound 9,999,999 or original value if higher | Unknown internal integrity; count word only; Overview and Inventory aliases must agree | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): load, spend, resave/reimport |
| Existing ordinary item stacks | Inferred | Known embedded `Items` type and nonnegative original count; editor bound 9,999 or original value if higher; direct edits permit zero | Unknown; count word only; no allocation or acquisition-flag update | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): item count, use, persistence |
| Current HP | Inferred | Public u32 offsets; sample values; bound by original current/max HP | Unknown internal integrity; structural reparse | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): load, battle, resave/reimport |
| Current MP | Inferred | Public u32 offsets; sample values; bound by original current/max MP | Unknown; structural reparse | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): spend MP, resave/reimport |
| Character XP | Inferred | Pinned MIT character curves; updates level atomically | Unknown; structural reparse | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): progression/stats/abilities |
| Character level | Inferred | Same variant/character curves; sets XP to minimum tier value | Unknown; structural reparse | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): level-up and derived effects |
| Recover selected current HP/MP (#40, shortlist #1) | Inferred | Existing eligible character setters and opening stored maxima; raises only deficient current values, never lowers over-max values | Unknown; only selected current-vital words change; structural reparse | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): damage/heal/MP use per variant and active character; battle/resave |
| Batch selected character levels (#40, shortlist #8) | Inferred | Existing variant/character curves; preview includes derived XP at the minimum target-tier threshold; ambiguous tiers and locked records skipped | Unknown; selected level/XP pairs only; cached maxima, abilities and rewards are not repaired | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): controlled level-up separately for Remake, INTERmission, retail/demo; abilities/battle/resave |
| Stored first-record play time (#40, shortlist #15) | Inferred write; retail meaning Verified, demo Inferred | Pinned public u32-seconds offsets and sample values; range zero through immutable opening seconds, including any unusually high original | Unknown; one u32 word only; older records and other menu/play counters preserved | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): displayed time, timed no-action pairs, native resave/normalization per variant |
| Rebirth owned weapon reference | Inferred | Unique inventory/detail joins; original inferred source type family; rejects foreign, duplicate, dangling instances | Unknown; only reference word changes | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): sockets/capacity, proficiency, folios, combat |
| Ordinary AP, including tier crossing | Inferred | 995 coherent retail rows and pinned strict AP curves; updates stored stars; F6 preview/apply | Unknown; AP word and dependent star byte only | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): below/at/above star threshold, ability effect |
| Ordinary stored stars | Inferred | Same curves; sets AP to target tier threshold | Unknown; AP/star pair reparsed | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): both increase and decrease, equipped copies |
| Other existing mapped non-equipment counts | Inferred | Known embedded type with a nonnegative original count; excludes Weapons, Armor, Accessories and Chocobo Gear; range 0 to original observed category maximum (minimum upper bound 1) | Unknown; count word only; flags/decks/rewards untouched | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): materials, tokens, keys, cards/boosters, progression categories |
| Individual progress candidate bits | Inferred | 69 Remake / 470 Rebirth/demo distinct pinned public-template mappings | Unknown; neighboring bits preserved; rewards/actors/counters not repaired; softlocks possible | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): each domain, rewards/actors, chapter reset and reversal |

Character HP/MP/XP/level writes require an original level within the source curve. Current HP/MP
range from zero to the greater of original current and original maximum; maxima remain read-only.
Weapon-reference writes additionally require unique matching inventory/detail instances with count
one and no other character referencing the original or target instance. Eligibility and editor bounds
are established from the opened bytes; imported catalogue metadata cannot widen them.

Completion adds two F6 presets over these existing write kinds: **restock existing consumables**
(only positive ordinary `Items` stacks below a chosen target of 1–99; no reduction or allocation),
and **set selected ordinary materia stars** (AP changes to the chosen tier threshold). Both preview
targets, skipped rows/reasons, dependent changes, attribution and integrity warnings; accept F8
select-all-matching scopes; reject stale/tampered plans; and apply/undo/redo as one history entry.
99 is the upstream editor policy, not a proven game cap. These presets need the same #31 console checks.

The Inventory restock button binds to the `inventory` table selection; both Materia AP/star
buttons bind to `materia` through SDK-41 `SelectionTableId`. Explicit selections include rows on
unloaded pages. Select-all-matching retains the exact server query, empty-slot visibility and
exclusions. With no selection these sibling buttons use document scope (all eligible stacks, or
all eligible owned copies of the chosen materia type). Explicit empty scopes, zero matches and
fully excluded queries change nothing. Unmounting the table resets its selection and disables
the bound control until the table is mounted again. Character controls retain their existing scopes.
`BulkSelectionDeclarationTests`, `BulkSelectionPlanningTests` and `BulkSelectionConformanceTests`
add synthetic regression coverage; these new tests have not yet been executed in this implementation
worker. Browser request wiring and sample-backed validation are assigned to independent validation.

### Edit-coverage actions and stored time (#40)

The **Characters** tab provides **Recover selected characters' current HP/MP** and
**Set selected character levels**. Both use F6 previews with old/new values, skipped-row reasons,
source attribution and the common console/integrity warning. Select rows and use either action in
the character table to send those explicit row keys; an empty table selection changes nothing.
The separate controls above the table use document scope and target all seven displayed records.
Character queries and foreign/noncanonical keys cannot broaden that scope. Records outside these
seven mapped slots, including unqualified guest/past records, are not targets. Original out-of-curve
levels remain locked. Recovery never changes maxima, XP, equipment or status effects. Level targets
use the existing 1–50 Remake/INTERmission or 1–70 Rebirth/demo curves; duplicate-zero guest tiers
that cannot represent the requested level coherently are skipped. Selecting the current level
preserves its XP tuple. Apply is atomic and one undo/redo entry, including restoration of an
inconsistent opening XP/level pair even when XP already equals the target tier's threshold. The
preview retains that unchanged XP as a derived consequence so history replays the complete pair;
dismissing a preview writes nothing. Stale or altered plans reject.

The Overview **Play time** duration field edits only the first RESD u32 seconds at `0x2B0` (Remake/
INTERmission) or `0x3C30` (Rebirth/demo). Its conservative bound is the observed opening value of
this document, retained across view refreshes: reduce it to any whole second including zero, then
restore it up to that opening value. This is editor policy, not a proven game cap or an increase-time
preset. No historical records or other timers are synchronized. The pinned MIT
[Remake template](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/template.ts)
and [Rebirth template](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/template.ts)
were rechecked on 2026-10-29 for these offsets and seconds conversion; progression evidence remains
the attributed [character-curve sidecar](SaveToolkit.Plugins.FinalFantasy7/Resources/character-curves.source.md).
All three shortlist items are implemented within these bounds; console acceptance is still unverified.

Validation on 2026-10-29: `dotnet test --project plugins/FinalFantasy7/SaveToolkit.Plugins.FinalFantasy7.Tests`
passed **444 tests, 0 failures, 0 skips**, with all three private sample archives supplied and test
scratch on D:. This includes the 22 original coverage cases and 20 review regression cases,
all-sample byte-identical no-op round trips, conformance and mutation fuzzing. Tests cover each of the four file variants, preview/cancel,
multi-row apply/undo/redo, scope/curve limits, locked and unknown rows, stale/tampered plans,
exact write offsets and export/reopen framing. Internal integrity remains unconfirmed; no checksum
or signature algorithm is asserted. `CharacterActionRegressionTests` covers repeated apply/undo/redo
of coherent and inconsistent opening pairs (including unchanged target XP), table-cell action placement,
Cloud-only changes, document scope and explicit empty selections. Six additional React checks ran
from task scratch against the plugin's actual synthetic Characters view: both table controls sent
only Cloud's key or an empty selection, and both separate controls retained document scope.
The full Release API build passed with **0 warnings and 0 errors**.

## Framework feature adoption

| Feature | Status | Adoption / concrete reason and verification |
| --- | --- | --- |
| F1 SaveDiff | Used | Existing `ByteDiffer` golden-byte tests; shared `FixtureManifest`/`FixtureVerifier` self-comparison fixtures for all three titles; an SDK-native `ff7r-character-evidence` hook for stable character paths; and F8 traversal tests. These are repeatability fixtures, not invented controlled events. |
| F2 reference packs | Used | Build-scoped embedded MIT item/weapon/materia packs plus attributed icon and Rebirth map-asset datasets, per-dataset attribution and Sources panel; optional imported text/images through pinned `IReferenceContext`. SDK-03 `EmbeddedReferenceContext.Load` decodes the direct-open fallback once, with title/build-scoped `Pin` and bounded catalog reads; historical fallback attribution, literal resource availability (including Windows separators), missing-image provenance and host row overrides are retained. Pre-migration record/image and direct/hosted protocol fingerprints, pack validity and provenance tests cover compatibility. |
| F3 rich table cells | Used | Inventory/catalog row-detail sections retain attributed official descriptions and imported `ImageNode` icons. Bound numeric cells carry confidence/warnings. Tested detail nodes, imported image identity and `GetAsset` routing. |
| F4 reference pickers | Used | Both AP/star actions use an owned-eligible materia `ReferenceSpec`. Inventory/materia type IDs publish browse-only `ReferenceSpec`s. Main Owned items F8 tables display type IDs as text; only retained legacy owned-page endpoints bind browse-only weapon/materia type fields. Writable weapon references remain **instance IDs**, while catalog keys are **type IDs**; substituting that catalog would select the wrong domain. |
| F5 reversible structure | N/A | No safe allocation/removal/reorder writer exists: free-slot initialization, serial uniqueness, acquisition order and all inventory/detail/owner dependencies remain unknown. Current writers patch fixed-size existing values. Owner acquisition/discard/capacity pairs are required before a structural transaction can be implemented. |
| F6 planning / #27 scopes | Used | Existing AP/star/restock presets plus current-HP/MP recovery and character-level targets with derived XP; pure previews, warnings, skipped rows, full-content revalidation through shared `ActionPlans`, and one undo group. Inventory/materia sibling controls bind to their corresponding F8 tables through SDK-41 and support explicit/document/Core-resolved query scopes; seven-row character actions retain their explicit eager-table or document scopes. `MateriaPlanningTests`, `PresetPlanningTests`, `CharacterCoverageTests`, `BulkSelectionDeclarationTests`, `BulkSelectionPlanningTests`, tab-scoped opt-in `BulkSelectionConformanceTests`. |
| F7 document groups | N/A | No implemented operation changes more than the primary gameplay file; common and prior-state RESD dependencies are unmapped. Grouping them would claim a coupling that has not been established. Existing backup mirroring remains framework-owned. |
| F8 queryable tables | Used | Complete inventories, materia, catalogs, cards, progress, owned relationships, linked-header index (including common files), named enemy records (161 Remake / 230 Rebirth) and the 388-row research collection. `GameplayCollection` and `CollectionQuery` use SDK `InMemoryCollection<T>` with `ToTableNode`; whole-model search/filter/sort precedes page-only rendering. Legacy raw-word/lazy endpoints remain bounded. |
| F9 graphs / maps | Used | Six attributed Rebirth regional backgrounds with local field targets and distinct bit domains; unverified coordinates remain list-only; seven read-only folio candidate graphs (238 bits) and seven Rebirth equipment-reference graphs. Accessible text alternatives, bounded/lazy groups, JSON/SDK validation and Core edit/undo refresh tests. No fabricated prerequisite edges, socket links, capacity, coordinates or weapon-upgrade grid. |
| F10 exact / nullable / union | N/A | Mapped payload values are fixed-width u8/u32/i32 integers and bits, exactly represented by `IntegerSpec`/`FieldValue` and the framework's integer transport. There are no decimal text numbers, nullable scalar encodings or tagged unions to represent. Sentinels remain literal bytes rather than guessed nulls. |
| F11 shared codecs | N/A | Mapped structures are directly readable in decrypted custom linked records; no demonstrated Kraken/SQLite structure requires a codec. Opaque bodies remain preserved, including any unrecognized compressed structures. No local codec or native dependency is used. |
| F12 | Used | `FinalFantasy7Plugin` exposes the SDK-only `ff7r-character-evidence` hook with bounded, stable character paths; `HookSaveDecoder` and F8 traversal tests cover deterministic reads, host caps, foreign-file rejection and byte preservation. |
| F13 | Used | Inventory/materia type fields and retained legacy owned-page type fields use browse-only references; main owned F8 type cells are text. Existing F8 catalog queries cover bounded filter/sort; integer duration remains appropriate for whole-second play time. N/A for non-mutating inspection actions because this plugin has no message-only action to publish. |
| F14 empty slots | Used | Inventory, materia and Rebirth owned weapon/materia relationships each publish one F8 table with empty-row metadata. Confirmed empty slots are hidden by default, labelled `Empty slot` when shown, and counted before paging. Native keys, raw fields, F6 query visibility/exclusions and byte-identical no-op saves are covered by `EmptySlotTests`. |

## Completion-round spatial and research views

**Regional maps** groups the six mapped Rebirth regions. Each marker represents one existing
candidate bit, retaining separate discovery/completion/repair domains and an Inferred badge.
Found/Missing means bit 1/0 only. When a bundled or document-pinned Rebirth map asset resolves, each section displays
the attributed, downscaled PowerPyx regional background. Candidate bits have no verified native
positions, so markers remain list-only and the text alternative is authoritative. The map and its
local editable checklist expand together, so navigation does not rely on an unloaded F8 row.
Published map status refreshes on edit/undo/redo. Remake has no geographic mapping. This does not
implement teleportation, fast travel or aggregate completion.

**Equipment and folios** exposes seven Rebirth/demo character groups. Folios show 238 distinct
literal source bits; bit 1 is an inferred stored unlock, while bit 0 has Unknown availability.
There are no invented edges or SP costs. Equipment graphs show the source's eight weapon and eight
armor materia reference positions plus the weapon instance. Eight stored positions do not mean
eight usable sockets. All graph targets open local read-only raw controls. Ambiguous/missing detail
joins preserve the raw instance. These graphs do **not** supply the Remake weapon-upgrade grid:
upgrade-node identities, prerequisite topology, SP accounting and socket capacities remain unmapped.

**Progress** also has an F8 read-only research collection: 220 crafting unlock/done and 168 minigame
candidate bits. These new literal source mappings do not implement a crafting/purchase/reward writer.
They omit dynamic expressions, hidden/disabled flags, duplicate addresses and the Queen's Blood
aliases already present in Progress. The new **Enemy intel** F8 collection reads named entry/assessed/
staggered bits and defeated counts (Remake u32, Rebirth u16) by source record index. The Rebirth
summary exposes five relationship raw words; craftsmanship level/XP share an ambiguous upstream
address and are deliberately shown as one raw word. Complete gameplay semantics, challenge
scores/rewards and chapter-reset behavior still need known-state captures. Every primary retail/demo
sample is checked for exact bit/raw-byte reads, distinct identities and no-op byte preservation.

Attribution and reproducible extraction: [spatial/research source sidecar](SaveToolkit.Plugins.FinalFantasy7/Resources/spatial-research.source.md).
The pinned template is read as untrusted text; the two extractors require its exact SHA-256.
Owned-item queries build one detail/owner index per query instead of rescanning 1,000 details for
each row/page. Duplicate detail instances are displayed as ambiguous instead of silently choosing
one. Indexes are rebuilt from current bytes, avoiding stale owner labels after edit/undo.

Full requirement/acceptance gap audit and remaining work: [COMPLETION.md](COMPLETION.md).

XP is bounded by the upstream editor policy of 9,999,999, ordinary AP by 999,999, Remake levels by 50
and retail/demo Rebirth levels by 70. Duplicate zero XP thresholds in INTERmission make level 2 the
lowest coherent zero-XP tier. Original above-policy values remain restorable. No unrelated stored
maximum, cached stat, previous-state record, socket, ownership byte or common file is normalized.
A previously inconsistent original XP/level pair is accepted only as the exact original pair for undo;
new conflicting pairs reject atomically. Gil aliases with conflicting targets also reject atomically.

Unknown/empty/negative-sentinel rows and ambiguous gil stay protected. Summon/repeated-zero and
incoherent ordinary AP/star rows have no qualified dependency mapping: applying the ordinary rule
could replace a distinct progression state, so these remain read-only. Gear quantities would create
instances without matching detail records, and remain read-only. Derived HP/MP maxima remain caches.
Remake gear, armor/accessory/socket moves, loadouts, allocation/removal and common/multi-file changes
have no prepared writer with complete structure/ownership handling; no guessed F5/F7 transaction is
introduced. Unmapped structures and repeated placeholder progress addresses remain inaccessible.

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
The record walker decodes only headers; bodies stay in the original buffer. Opening a gameplay
document also reads mapped body values to establish quantity, character, weapon and materia edit
policies. It does not decode or rebuild all record bodies.

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

### Malformed-input rejection

`Open` rejects unsupported titles, filenames, measured file lengths and malformed record framing with
`SaveFormatException` and a user-facing reason. Failures inside the chain include the record's absolute
offset; the initial size/header gate reports a schema mismatch. `Probe` is a cheap initial-header check,
so a matching probe does not establish that later records are valid. The F1 evidence hook validates the
complete chain before emitting values, warnings or depth-truncation output; a host byte-limit violation
throws `InvalidDataException` before reading the file. Short bodies are rejected even if file metadata
still reports the original size.

Unknown body words and slot/index-like bytes remain opaque and lossless. No body word controls an
allocation or traversal count. Random or extreme body values cannot establish corruption without a
verified format invariant; this is not checksum verification or console acceptance. These layouts have
no demonstrated GVAS length-prefixed property stream or verified integrity field (see below).

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
available through the raw explorer and are not conflated with current gameplay. Current MP/max MP
are u32 words at character base +0x18/+0x1C; XP is +0x20, HP/max HP +0x10/+0x14, level +0x00.

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
no game-specific dictionary is shared across Remake/Rebirth. Confirmed empty slots are hidden by default
and can be shown as `Empty slot`; other unknown IDs and zero/negative quantities remain visible.
Negative quantities receive a warning. No arbitrary gameplay limits are imposed on
read-only values. Ordinary item stacks use an editor cap of 9,999, gil 9,999,999 and ordinary AP
999,999; these are not proven game caps. Direct AP edits can cross every supported ordinary tier;
their upper bound retains a higher original AP value, while the bulk AP action caps targets at
999,999. An original value above a cap remains restorable after
editing and refreshing bindings. Negative quantities, ambiguous gil and instance-bearing gear quantities are read-only. Existing mapped
non-equipment counts are writable with the warnings above. Qualified ordinary AP can cross tiers and
updates stored stars atomically; every summon, unknown type and AP/level mismatch remains read-only.
Names, categories and source-metadata descriptions use the embedded MIT reference packs below.
Stored first-record play time is writable within the immutable opening-seconds bound described above.
Remake chapter ID, type IDs and raw record/body controls remain read-only. Character
maxima and all folio, socket, enemy-intel, relationship and crafting/minigame research values are
also read-only; writable progress candidates are the separate Progress/local map bit controls.

### Characters and owned relationships

The **Characters** tab exposes seven candidate party records for each gameplay file, starting at
absolute `0x30`. Remake uses the measured `0x40` stride and Rebirth/demo use the measured `0x100` stride; level, XP, current/max HP/MP and
(for Rebirth) weapon-detail references are `Inferred` fields with the write scope listed above. The names and order are the
MIT resource order, while guest, past and cached records still need controlled screenshots.

Retail/demo Rebirth also exposes searchable F8 **Owned items** collections. Weapon rows join inventory instance/type
records at `0x5FA6C` to detail records at `0xB3A68` (stride `0x60`) and show character references at
`0xCC + index*0x100`. Materia rows join instance/type/quantity at `0x60A6C` to detail records at
`0xBFA68`, including the observed owner byte at `+0x11`. These read-only relationship tables expose identity joins; the character weapon field separately
enables qualified existing-instance changes. The **Catalog** tab searches all item/materia/weapon metadata and can use optional
toolkit reference packs with catalog ids `ff7r-items`, `ff7r-materia`, `ff7r-weapons` and
`ff7r-characters`.

### Empty-slot presentation (F14 / #38)

Inventory and materia each use one complete F8 table, with search/filter/sort over the full array.
The unused `Slots N–M` section builder and owned-item lazy section builder have been removed.
Bounded legacy page endpoints remain for existing sessions and use the same empty flags and labels;
new views never publish linked inventory pages. Empty visibility changes presentation only.

The sample-checked vacant type markers are `0xFFFFFFFF` for all 2,048-slot inventories,
`0` for Remake's 1,000 materia slots, and `0xFFFFFFFF` for Rebirth/demo materia. Rebirth's
128 owned weapon slots and 1,000 owned materia slots retain the existing unused-instance/type
rule (either instance or type is `0xFFFFFFFF`), now exposing rather than dropping those rows.
`TotalCount` includes every slot. Text and exact filters run first, `HiddenEmptyCount` counts
matching vacant rows, and visibility is applied before sorting/paging and F6 query-scope selection.
Unknown non-sentinel IDs and zero quantities alone are never evidence of vacancy.

Research rechecked 2026-10-29 against the pinned [GTC Remake utility/schema](https://github.com/RyudoSynbios/game-tools-collection/tree/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor)
and [Rebirth utility/schema](https://github.com/RyudoSynbios/game-tools-collection/tree/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor),
plus direct reads of every supplied gameplay sample. No upstream implementation was copied.
`EmptySlotTests` verifies each sample's markers/counts and raw IDs, shown/hidden key stability,
one inventory table, matching hidden counts, bounded paging, visible edits at native byte offsets,
undo restoration, and Core F6 select-all visibility/exclusions. Catalogues, cards, progress,
enemy intel, character records and raw record/body inspection are not vacant inventory slots;
their zero/unknown states remain visible, including allocated linked records with empty headers.

The F14 round was validated with all three sample archives present and a clean API build.
Current suite results are recorded under Research log and validation.

### Semantic safety evidence

The F1 differential engine is exercised by `RelationshipTests.F1DiffEvidence_DemoGenerationChangesBodiesOnly`.
The supplied demo snapshots differ in 800 body bytes and no record envelope/content-header/tail
bytes. `RelationshipTests` also checks every supplied retail/demo character weapon reference against
the 128 measured weapon-detail records and checks the first owned materia join. This evidence does
not prove gameplay behavior or console acceptance.

Across all primary supplied retail snapshots, **465 Remake and 530 Rebirth ordinary materia rows**
agree with the pinned AP curves and stored stars. The write-enable decision now allows source-derived star transitions with warnings; these
observations still do not establish console compatibility. The Rebirth Titan type `14011` illustrates why all
summons remain disabled: repeated-zero source thresholds disagree with stored levels in three retail
samples. Empty, unknown and incoherent rows remain read-only. Coherent ordinary rows with original
AP above the editor cap remain editable up to that original value. All original values are
byte-preserved until edited.

The **Set AP and derived stars** action uses an F4 picker over owned eligible types and F6
planning over selected rows or all copies of that type. F8 select-all-filtered scopes are resolved by
Core. Preview reports old/new AP, dependent old/new stars, skipped rows/reasons and integrity/console warnings. It changes no bytes;
apply revalidates the plan and is one undo step. `MateriaPlanningTests` uses F1 `ByteDiffer` to prove
that apply changes only the selected four-byte AP words and derived star bytes, then checks reopen
and exact group undo/redo. Direct star edits also update AP using the source threshold.

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
88 materia types for Rebirth. Every record has a name, category, source-metadata description,
catalogue icon and pinned attribution. Materia and weapon descriptions now include attributed
official Square Enix / PlayStation guidance; save-ID joins and exact effects remain unverified.
F8 searches the whole dataset before paging. Imported document-pinned packs can supply richer
descriptions, with their own provenance, without changing the embedded edit policy. Character and
owned-item names also show attribution.

The **Cards** collection contains 145 individual Rebirth card types, with stored inventory quantities.
Its 16 booster-pack types remain separate in the item catalog. Source labels duplicate display
number 012 and omit 013; these labels are retained with an explicit warning, not corrected by an
unverified numeric formula. The Cards collection is a read-only summary: it sums matching inventory
quantities by type. Editing qualified counts happens in Inventory. Quantity, deck membership and
opponent wins remain separate domains.

The **Progress** collection shows 69 distinct Remake candidate bits for obtained dresses, battle
intel entries/completion/rewards and VR missions. Rebirth/demo show 470 candidate bits for hard
chapter flags, Queen's Blood opponents, World Intel discovery/completion and Chocobo-stop
discovery/repair. Exact region and domain/state filters cover Grasslands, Junon, Corel, Gongaga,
Cosmo Canyon and Nibel. Fields expose the source byte/bit as writable **Inferred** candidates with explicit dependency/softlock warnings.
Shared source addresses and all `Intel Entries` groups were excluded because repeated placeholder
addresses cannot distinguish individual objectives. A stored set bit is not a verified completion
or reward. No percentages, universal completion preset or map-coordinate guesses ship. Individual bits do not
repair dependencies or grant rewards.

The scoped catalog/F4/F8 delivery remains available; the enabled Phase 3 scalar/reference writes
and Phase 5 individual bits are Experimental while owner validation continues in #31. Full Phase 4
still needs complete per-build effect/save-ID crosswalks and persistent loadouts. The shipped folio,
crafting, enemy-intel, relationship and minigame candidate readers still need verified gameplay
dependencies and complete writers, as described above. Fandom page/revision
retrieval returned HTTP 403/402 during the earlier metadata round; this asset round uses pinned
Final Fantasy Wiki image URLs, official Square Enix / PlayStation descriptions and PowerPyx map
captures under the owner decision dated 2026-10-29. Rights-reserved publisher assets are marked
with that exact basis in both pack manifests and Sources panels. The MIT data path remains the
identity/AP source, and the asset sidecars record image processing and attribution.

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

Overview contains only headline facts and a 2,048-entry scalar scan to find gil. The record-header
index uses F8 (at most 275 records); record sections are grouped in lazy batches of 32. All F8
collections return at most 200 rows per query; retained legacy lazy endpoints have at most 128 rows. Every record has a paged hex view plus a lazy tree reaching **every** aligned
body word with u32, i32, f32 and ASCII interpretations. Word pages have at most 64 rows and intermediate
ranges at most 32 children. NaN/Infinity interpretations are text, keeping the view JSON-serializable.
Blob requests are capped at 64 KiB and copied to protect the document's backing buffer.

Open retains one file buffer, the small header index, catalog lookups and fixed-slot edit policies; there are no body copies or eager gameplay view trees.
Tests check complete record walks, byte-identical round trips and an initial view below 2,500 nodes.
Correctness tests do not gate on wall-clock speed, which varies with CI scheduling and machine load.
No framework changes, runtime file/network access or extra NuGet dependencies are required.

Collection queries use the shared SDK `InMemoryCollection<T>` engine. Inventory/materia query native
slot indexes through `GameplayCollection`, compare numbers without formatting/parsing and only build
rich cells for the requested page. Immutable catalogue projections are cached lazily per document;
owned relationship queries build text entries directly, while owner references remain live. Pinned
record descriptions/source strings are reused, including across repeated stacks in action previews.
The thin
`CollectionQuery` adapter retains FFVII's validation messages, signed-64-bit numeric comparer with
ordinal text fallback, whitespace-only search normalization and ascending ordinal key tie breaks
(also for descending column sorts). F14 still uses the native slot predicates; only requested page
rows render bindings. Characterization tests pin these policies before the helper migration (#40).

The shared helper migrations are complete: `BoundSaveDocument` supplies field binding and atomic
scalar rollback, `InMemoryCollection<T>`/`ToTableNode` supply collection querying/presentation, and
`ReferenceCatalogReader.ReadBounded` supplies bounded imported-catalogue traversal. Fixed-address
payload reads and the linked-record walker remain local; no missing binary-cursor SDK capability
is implied.

Historical plugin performance review (#40, 2026-10-29), before the later SDK allocation optimization
in `63be7a2`: Release, .NET SDK 10.0.400, largest sample
`PPSA08664/ff7rebirth000` (23,381,104 bytes), pinned built-in packs, synchronous per-thread allocations.
Five warmups and 21 measurements with tiered compilation disabled; elapsed/allocation medians are
independent. Sample import and reference-store setup are outside timings. Pages contain 50 rows;
search uses `1`, sorting uses descending quantity, and empty rows are included. Plans use document scope.

| Operation | Before → after milliseconds | Before → after allocated bytes |
| --- | --- | --- |
| Open pinned + first root view | 4.747 → 4.561 | 24,247,112 → 24,427,688 |
| First root view alone | 0.087 → 0.079 | 156,632 → 156,632 |
| Inventory page / search / sort | 1.169 / 1.269 / 1.152 → 0.339 / 0.574 / 0.236 | 2,894,368 / 3,035,880 / 3,094,216 → 724,008 / 952,192 / 791,712 |
| Materia page / search / sort | 0.504 / 0.786 / 0.677 → 0.152 / 0.472 / 0.184 | 1,521,336 / 1,635,464 / 1,697,080 → 542,304 / 706,336 / 589,808 |
| Owned materia page | 1.186 → 0.492 | 3,033,800 → 1,136,872 |
| Item catalogue first query | 0.530 → 0.593 | 2,368,400 → 2,238,680 |
| Item catalogue subsequent page | 0.466 → 0.113 | 2,368,480 → 319,296 |
| Restock preview | 0.546 → 0.435 | 682,408 → 302,648 |
| Materia AP preview | 0.007 → 0.007 | 6,568 → 4,576 |
| Synthetic 2,048-stack restock preview | 1.449 → 0.814 | 3,914,808 → 777,160 |

Open retains an additional ~180 KB of stable slot keys. The catalogue cache pays its construction
cost on the first query and retains that immutable projection for subsequent queries; cold catalogue
latency is not improved. These are warm-process measurements, not process-startup/static-resource
loading measurements. Timing changes are diagnostic, never test gates. Sixteen new sample-free cases
cover allocation ceilings, current values after edits/restoration, relationship/legacy equivalence,
owner freshness and canonical action selection. That historical plugin suite and API Release build
passed; current suite results are recorded below. The reproducible harness, original source snapshot, all
collection measurements and test-first failure logs remain at
`D:/Temp/saveeditor/review-perf-finalfantasy7/` (`run.ps1`, `before-stable.txt`, `after-stable.txt`).

Imported reference indexes use `ReferenceCatalogReader.ReadBounded` with an explicit 250,000-record
budget, matching the host's default per-catalog limit. Numeric ID normalization, source-order
overrides, embedded fallback and numeric result ordering stay local. The helper advances short
pages by actual progress and rejects budget exhaustion or inconsistent paging with
`InvalidOperationException`; neither overrides nor fallback are cached as a complete index on failure.
These helper-required failures replace the old loop's silent acceptance of incomplete catalogues.

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
python -I plugins/FinalFantasy7/research/inspect_samples.py C:/path/to/main/artifacts
$env:SAVETOOLKIT_ARTIFACTS='C:/path/to/main/artifacts'
dotnet test --project plugins/FinalFantasy7/SaveToolkit.Plugins.FinalFantasy7.Tests
```

Sample-dependent tests skip when their title archive is missing. Synthetic common-format, gameplay-framing
and explorer tests run without samples. Artificial gameplay bodies are not evidence of real gameplay
semantics; their independent record-group fixture covers all three layouts, corruption rejection,
empty allocations and byte preservation. The conformance suite retains its six-files-per-title default; the dedicated
format test walks and no-op serializes **all** matching sample files, including backup/generation files.
Edit conformance nudges writable fields on selected samples; dedicated
tests verify in-place byte scope, round-trip reopening, undo-by-old-value and atomic range rejection.

Malformed-input coverage also checks every common-file prefix, gameplay truncation at every partial
record/content header and body boundary, extreme metadata lengths and short reads despite unchanged
file metadata. Direct F1-hook tests require rejection before any output for corrupt later record
families and tails, even with shallow depth or a full sink; private-sample mutations complement the
synthetic cases. Random/all-`FF` bodies and opaque slot bytes exercise every queryable collection and
Rebirth folio/socket expansion with byte-identical serialization. These are robustness checks, not
new claims about gameplay validity or internal checksums.

Historical completion validation used the supplied archives and a clean API build. Tests include Core F4/F6/F8/F9 session integration; a separate whole-Core-suite run was not needed for this plugin-only change. All 55 sample entries round-tripped;
every measured sample Open call stayed below two seconds in that historical local run.

Maintainability review (#40, 2026-10-29), after the robustness, SDK-helper and performance rounds:
the complete sample-backed Release plugin suite and API Release build with `--warnaserror` passed.
Fourteen added characterization cases cover
exact multi-fault rejection precedence, synthetic protocol/plan fingerprints, fallback names and
shared byte-scope assertions. These characterize existing behavior before cleanup; no functional
regression is claimed. Existing no-op round trips, rejection-before-evidence checks and allocation
bounds remain green. No runtime strings, IDs,
confidence labels, write warnings or edit policies changed. Earlier counts above describe their
respective historical rounds.

Documentation accuracy validation (#40, 2026-10-29): **401 passed, 0 failed, 0 skipped** with all
three sample archives present; API Release build with `--warnaserror`: **0 warnings / 0 errors**.
This pass changes documentation only; runtime strings, protocol fingerprints and write policies
are unchanged.

For the sample-backed run, set `SAVETOOLKIT_ARTIFACTS` to the external archive root,
`SAVETOOLKIT_TEST_TEMP` to the D: test-scratch root and `TEMP`/`TMP` to a D: task folder in the same
shell command, then run `dotnet test --project plugins/FinalFantasy7/SaveToolkit.Plugins.FinalFantasy7.Tests --configuration Release`.

## Open problems

- Confirm that edited files load on a PS5 and establish real in-game value limits; no Stable claim.
- Confirm retail meanings/ranges and demo mappings against controlled in-game changes. Demo writes use
  inferred retail mappings with explicit warnings and need their own validation.
- Map Rebirth chapter/difficulty/party, common settings and MLSD/NVSD semantics.
- Decode deeper record structures from evidence; no generic Unreal property parser is assumed.
- Validate additional patches/regions/sizes explicitly; current gates intentionally accept only the
  measured layouts. Rejecting an unsupported file does not modify it.

## SDK requests

No framework bug blocks the shipped subset. The shared collection and bounded catalogue-reader
requests are resolved by the adopted helpers described above. F12/F13 are adopted as recorded in
the feature table. The plugin has no message-only inspection action to mark `Mutates = false`.

Resolved performance request (#40): SDK commit `63be7a2` replaced per-row reference-type entries
with value-type entries and reuses one list for matching and sorting. The earlier request for
fewer intermediate materializations is implemented; its old allocation figures do not describe
the current helper. Full-model key validation, empty counts and ordinal tie breaks remain.
`ReadBounded` remains the complete one-time index reader behind the document catalogue cache;
no repeated catalogue traversal was found in paging/search/sort/planning. This documentation pass
does not change SDK code or remeasure the historical performance table.

SDK-07 versioned preset storage and the pure `IPresetDocument` contract are now consumed by the
FFVII adapter. The adapter publishes exact title/variant/schema/file-role boundaries, leaves the
actual game build unknown, uses the host-pinned reference content identity, and persists only
domain-qualified semantic targets. Host storage, revision tokens, compatibility gates and choice UI
remain framework-owned; the plugin performs no I/O. Import/export and native loadout presets remain
open follow-ups. F5/F7 exist already and need no replacement API.

### Semantic presets and SDK-07 adoption

The plugin now has an internal foundation plus a thin, pure SDK-07 `IPresetDocument` adapter.
`SemanticPresets` captures typed parameters for exactly the five
existing F6 actions: `inventory/restock-existing`, `materia/set-ap-within-tier`, `materia/set-stars`,
`character/recover-current-vitals`, and `character/set-level`. It retains explicit document,
all-eligible, all-copies and selected semantics. Only scopes supported by the operation are accepted;
an empty selected scope remains empty. Preview re-enters `DocumentPlanning`; the existing F6
planners, validation, derived AP/star or level/XP changes and history remain authoritative.

Selected inventory targets retain raw catalog type IDs and source provenance. A current unique
stack can move to a different slot; multiple matching stacks require an explicit current candidate
choice or an explicitly captured all-matches policy. Character targets retain canonical schema
indexes, guarded by the exact captured title ID and `Ff7Variant`. Retail Rebirth and Demo reject
interchange despite equal dimensions. Common/system documents publish no preset actions.

Rebirth selected materia targets retain the native owned instance **and** catalog type. Resolution
joins the current owned inventory to the current AP-editable detail row; those physical indexes
are different domains. Missing/duplicate details, duplicate inventory instances, mismatched detail
types and owned counts other than one remain unresolved with stable reasons and all relevant
candidate placements. Nothing repairs those relationships or silently selects a first match.
All-copies scope uses current owned joins; unowned detail records are not promoted to owned copies.
Remake selected-materia capture rejects because its current model has no proven owned-instance
identity; its existing type-wide all-copies planner remains available. Summon and incoherent
AP/star eligibility remains the F6 planner's decision.

`SemanticPresetTests`, `SemanticMateriaPresetTests`, `SemanticPresetCompatibilityTests`,
`SemanticPresetHistoryTests` exercise authored synthetic framing/rows, moved targets, zero/one/many
matches, malformed duplicate joins, pure preview, retained skips, tampered/rescoped F6 plans and
one-unit byte-exact apply/undo/redo across all three variants. These are consumer/helper tests,
not UI, persistence, console-validation or native-loadout evidence. Existing framing, owned
projection, research-quality and planning regressions remain unchanged.

`Sdk07PresetAdapterTests` additionally exercise the public capture/resolution path, typed payloads,
current-slot movement, explicit empty selection, duplicate/missing owned joins, exact retail/demo
compatibility variants, host preview/apply, and one-unit undo/redo. Actual console patch/build
remains **unknown**; a pack's display/build label cannot establish it. The host's pinned
content identity (including locale/origin and validated content fingerprints) is authoritative.
The published host contract now passes its document-pinned `ReferenceFieldService` and
`ReferenceContext` through capture, plan coercion and summaries. The materia descriptors still
set `ReferenceSpec.AllowCustom` for the two raw IDs, so the descriptor layer remains permissive;
FF7 canonical invariant-decimal parsing, known-ID/current-eligibility checks and the existing F6
planner reject unknown or ineligible IDs before planning, mutation or history. This is a
host/adopter validation seam, not permission to acquire items or bypass the pinned picker/catalog
when it is available. The earlier pre-R4 `ReferenceContext.None` behavior is historical only.
Browser acceptance against a running host and private sample-backed validation are outside this
plugin-only change and remain pending. No direct weapon edit, socket/materia movement, allocation,
owner/detail repair or complete native loadout is added.

This foundation reuses the existing source-qualified mappings and curves documented under
[enabled writes](#enabled-writes-and-their-evidence), the pinned
[Rebirth catalog source](SaveToolkit.Plugins.FinalFantasy7/ReferencePacks/ff7r.rebirth.en/catalog.source.md)
and [character-curve source](SaveToolkit.Plugins.FinalFantasy7/Resources/character-curves.source.md).
It introduces no new gameplay schema or reference dataset.

## Sources and attribution

| Dataset | Pinned source | Licence / modifications |
| --- | --- | --- |
| Remake/Rebirth enemy indices and names (161/230), read-only enemy/relationship/craftsmanship layouts | Pinned MIT GTC `resource.ts` and `template.ts` for each game, linked in the [source sidecar](SaveToolkit.Plugins.FinalFantasy7/Resources/spatial-research.source.md) | MIT, RyudoSynbios; extracted unambiguous indices, retained display labels/groups, independently implemented fixed-width reads; no game-effect prose or art |
| Rebirth folio candidates (238), crafting (220), minigame (168), equipment-reference offsets | [GTC template.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/template.ts), [shift overrides](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils.ts) | MIT, RyudoSynbios; filtered literal candidate metadata and independently implemented reference inspection; no topology/cost/art imports; sidecar and full notice retained |
| Regional map backgrounds | [PowerPyx, published 2024-02-29; retrieved 2026-10-29](https://www.powerpyx.com/final-fantasy-7-vii-rebirth-full-world-map-revealed/) | Rights reserved by Square Enix; used with attribution per owner decision 2026-10-29; six downscaled WebP backgrounds, no imported coordinates |
| Catalogue icons | [Final Fantasy Wiki Remake item images, retrieved 2026-10-29](https://finalfantasy.fandom.com/wiki/Category:Final_Fantasy_VII_Remake_item_images) | Rights reserved by Square Enix; used with attribution per owner decision 2026-10-29; selected 64px WebP icons |
| Common item effects | [Gamer Guides consumables reference](https://www.gamerguides.com/final-fantasy-vii-remake-intergrade/guide/appendix/items/consumables), retrieved 2026-10-29 | Rights reserved by Gamer Guides; used with attribution per owner decision 2026-10-29; summaries for 21 common consumables; item/save joins remain unverified |
| Official materia and weapon guidance | [Square Enix](https://www.square-enix-games.com/en_US/news/final-fantasy-vii-remake-screenshots), [PlayStation Blog](https://blog.playstation.com/archive/2019/11/25/new-final-fantasy-vii-remake-screens-feature-moogles-weapon-abilities-materia-mini-games-and-more), retrieved 2026-10-29 | Rights reserved by Square Enix; used with attribution per owner decision 2026-10-29; publisher text paraphrased with Square Enix / PlayStation Blog source credit; save joins and exact effects remain unverified |
| Remake item categories/names (369) | [GTC resource.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/utils/resource.ts) | MIT, RyudoSynbios; extracted metadata, merged weapons, original summaries |
| Remake weapons (32) | Same pinned Remake resource | MIT; extracted type/name rows, original identity description |
| Remake materia (54) | Same pinned Remake resource | MIT; category/AP arrays, original AP summaries |
| Rebirth item categories/names (934) | [GTC resource.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils/resource.ts) | MIT, RyudoSynbios; merged weapons, separated 145 cards/16 boosters, original summaries |
| Rebirth weapons (51) | Same pinned Rebirth resource | MIT; type/name rows, original identity description |
| Rebirth materia (88) | Same pinned Rebirth resource | MIT; category/AP arrays, summon restrictions in descriptions |
| Remake progress candidates (69) | [GTC template.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/template.ts) | MIT; literal labels/addresses and state grouping, ambiguity filtering |
| Rebirth progress candidates (470) | [GTC template.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/template.ts) | MIT; distinct region/state bits, excluded placeholders/aliases |
| Character XP curves (7 Remake/INTERmission, shared main-seven Rebirth curve) | [Remake resource.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/utils/resource.ts), [Rebirth resource.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils/resource.ts) | MIT, RyudoSynbios; inserted explicit level-one zero, bounded Remake to 50 tiers; see `Resources/character-curves.source.md` |
| Legacy names.json / equipment.json and character order | Revisions `e38d207c2210d210d9b06ebd2a0a62b4e16c7aed` / `c7bdd6d8228304b87dcdf15e57155d092eb06fd2`, linked below | MIT; retained prior extracts and notice |

The earlier research datasets were retrieved 2026-10-28. Imported map backgrounds and icons were
retrieved 2026-10-29, as recorded in their pack manifests and asset provenance. Catalog source sidecars and full MIT notices accompany
[Remake](SaveToolkit.Plugins.FinalFantasy7/ReferencePacks/ff7r.remake.en/catalog.source.md) and
[Rebirth](SaveToolkit.Plugins.FinalFantasy7/ReferencePacks/ff7r.rebirth.en/catalog.source.md).
The [progress sidecar](SaveToolkit.Plugins.FinalFantasy7/Resources/progress.source.md) documents
selection/exclusion rules. Manifest attribution is shown by F2's Sources panel; F4 candidates and
F8 descriptions/source columns retain provenance. The [catalog sidecars](SaveToolkit.Plugins.FinalFantasy7/ReferencePacks/)
record icon, map and official-description processing. Existing research-only sources and legacy
extracts follow.


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
  `Resources/equipment.json` extract is retained as legacy source data with the same copyright
  notice and contains no artwork, descriptions or game limits; the unused runtime lookup was removed.
  Current weapon names use the F2 packs, which also ship attributed descriptions and icon links.
  The host resolves bundled icons/maps through the pinned reference context; imported packs can
  replace names/assets without plugin I/O.
- [Public Remake editor](https://game-tools-collection.com/final-fantasy-vii-remake/save-editor) and
  [Rebirth editor](https://game-tools-collection.com/final-fantasy-vii-rebirth/save-editor): platform scope
  is PC/Steam, not evidence of PS5 console compatibility.
- [Nexus Rebirth editor](https://www.nexusmods.com/finalfantasy7rebirth/mods/2072) and its
  [web version](https://ff-7-rebirth-save-editor.vercel.app/): gameplay capability research lead only;
  implementation licence/PS5 compatibility unverified, no code reused.
- [Save Wizard FAQ](https://www.savewizard.net/faqs/): documents that Save Wizard supports PS4 saves on
  PS5, while native PS5 saves are outside its supported scope; this is why its direct-write codes are
  supporting evidence for the decrypted layout only, not a console-load guarantee.

## Licence exclusions

No current licence-based refusal is recorded for this plugin. The table retains the earlier
map-art restriction and records the current disposition of imported publisher media. Missing
captures, unverified coordinates and save-ID/effect joins remain evidence gaps.

| Source / revision | Licence found | Disposition | Alternative | Unblock |
| --- | --- | --- | --- | --- |
| [PowerPyx regional maps](https://www.powerpyx.com/final-fantasy-7-vii-rebirth-full-world-map-revealed/), published 2024-02-29; research retrieval 2026-10-28, asset retrieval 2026-10-29 | Earlier research: none found; current notice: Rights reserved by Square Enix; used with attribution per owner decision 2026-10-29 | Resolved: imported (owner decision 2026-10-29); six downscaled WebP backgrounds | Earlier research used an external link without importing art; unverified marker coordinates remain list-only | Completed for map art; verified coordinates need separate evidence |
| [Final Fantasy Wiki Remake item images](https://finalfantasy.fandom.com/wiki/Category:Final_Fantasy_VII_Remake_item_images), retrieved 2026-10-29 | Rights reserved by Square Enix; used with attribution per owner decision 2026-10-29 | Resolved: imported (owner decision 2026-10-29); selected 64px WebP icons | Raw IDs and attributed text remain available when an icon is absent; earlier HTTP 403/402 retrieval failures were availability gaps | Completed for selected icons; no current licence blocker |

## Synthetic mutation fuzz testing (#40)

Three 1,056-byte independently authored `ContainerTests.Common` seeds cover the Remake, Rebirth and
Rebirth demo common-file envelopes and linked-record framing. Each baseline opens and builds its
read-only view. These are common/settings container seeds, not gameplay-body coverage.
`CoverageFixture` loads private samples and is intentionally not used; full synthetic gameplay
allocations are 9-23 MiB and remain covered by focused fixture tests. No private save bytes or
authentication material are embedded.

`MutationFuzzSyntheticSeedsOpenAndRender` checks every unmutated seed before the inherited
`MutationFuzz` fact supplies deterministic mutations. No samples are required for these checks.

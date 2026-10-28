# Live NewAge storage (0.6.0)

Experimental, native PS5 sample profile only. No on-console acceptance, current party
identity or authoritative cache claim. **Integrity unknown — the game may reject
this save.** Known LSPK v18 MD5+1 is repaired and checked. Missing/unsupported
families retain raw bytes and explicit diagnostics, rather than guessed values.

## Independent implementation and evidence

Research lead: [DanFinchUK format documentation, revision 9578ff7](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/FORMAT.md),
revisited 2026-10-28; [upstream limitations](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/LIMITS.md).
The upstream implementation has no established reuse licence. No implementation,
shift solver or executable was copied or run. Independently authored Python probes
ran with `python -I` outside the repository; committed C# and synthetic vectors
were written from measured format facts. Existing MIT LSLib GUID decoding is reused.
No save fragment, game artwork, private identifier or extracted dataset is embedded.

Important PS5 differences measured against the existing typed NewAge buffer:

| Storage | Measured supported PS5 form |
| --- | --- |
| Directory | Existing 315 descriptors, arena base +48, 2,618,936-byte buffer. No fixed PC descriptor count. |
| Ownerlist candidates | 199 unique bounded candidates; aligned absolute 32-byte records with exact component row counts and bounded u32 EntityId rows. Duplicate candidates are excluded. Positions are not trusted as authoritative current owners. |
| Health | 380 × 32-byte records, **no section prefix**. First four i32 words are current/max/temp/temp-max candidates, final 16 bytes preserved. Whole nominal profile must validate nonnegative bounded values. |
| Stats | 256 × 36-byte records; **opaque 8 bytes per record**, six i32 ability candidates at +8..+28, final word preserved. Whole profile must validate abilities 1..40. The PC 20-byte stream-prefix assumption is not used. |
| Classes | 256 class selections through bounded 40-byte heap records: class/subclass GUIDs and exact u64 level. Read-only, unjoined; no XP/progression changes. |
| Spell books | 291 books, 2,242 occurrences. Arena-relative book ranges contain 72-byte SpellData records whose **first word** is an arena-relative SpellId pointer; bounded string pointer/length at SpellId +8/+16. Wrong-profile pointers/identifiers reject the family. No string scanning or alternate plausible-name heuristic. |
| Stack groups | 38 explicit NewStack pointers → 32-byte group records → member EntityId pointers and StackEntry ranges. 39 member quantities; one group has two members. StackEntry = u16 member index, opaque u16, u32 contribution. All opaque padding preserved. |
| Containers | 554 nominal rows, 315 empty, 239 nonempty; 638 entries. First range is u16 **keys**, second range is u64 pointers to ContainerSlotData (arena-relative). This order differs from the research prose. CSD points absolutely to EntityId. |
| Container ordinals | CSD +8 is preserved separately from the map key. Three entries in one sample container have non-sequential ordinals; these are not “repaired” or overwritten. |

All pointers are checked against the appropriate address convention and declared
component width/range. Absolute EntityId pointers cannot point inside GUIDs.
Stack contributions must be positive, member indices in range, records/amounts
unaliased and each member represented. Missing/stub groups are unavailable; no
implicit quantity of one is synthesized. Container keys and current item references
must be unique within each container, and CSD records cannot be shared by containers.

Creator candidates use exact `Entity` UUID → typed `TemplateID` (UUID or textual GUID) → LSF
`CurrentTemplate` matches, excluding conflicting object labels and duplicate Creator mappings. Repeated templates may share an identical candidate label. Save-derived Stats IDs
are labels of original object candidates, not imported friendly names or current
party identities. Repeated EntityId GUID occurrences remain distinct. A body,
stats entity and spell-state entity are never joined by proximity, class similarity,
largest book, transform or a guessed ownerlist rotation.

## Enabled fixed-width edits

- Member quantity: contribution-count minimum through imported member total.
  Every contribution stays positive; zero/removal and missing stacks are excluded.
  Distribution is calculated deterministically from the original contributions,
  so scalar undo restores their exact original distribution and opaque u16 padding.
- Current HP candidate: 0 through stored maximum. Temporary HP candidate: 0 through
  max(imported temporary HP, stored temporary maximum). Maximum fields, GUID tails,
  death/status/resource dependencies and duplicate epochs are preserved.
- Effective ability-cache candidates: 1 through each imported value. No base-score,
  ASI, feat, respec, class-level, proficiency or boost synchronization is promised.
- Two-key reorder: F6 requires exactly two current entries in one source/container,
  previews old/new u16 map keys and warnings, then swaps just those keys. F5 keeps
  a scoped inverse memento of all live patches; it does not revert unrelated raw/JSON
  edits. Membership, pointers, array sizes, dense ordinals and generation words do
  not change. Visible grid-order semantics remain unvalidated.
- Restore imported quantities: F6 changes only selected eligible members, lists
  already-imported/non-stack rows as skipped, and supports document scope plus
  Core-resolved filtered query scopes/exclusions. Empty selection stays empty.

Every output re-parses NewAge, compares supported current values/map keys and
diagnostics, re-parses LSF, repairs/rechecks the v18 digest, and reopens every
package resource. Byte tests cover intended ranges and untouched Globals attributes
and compressed package entries. No-op and full undo return exact original bytes.

## Views, budgets and feature limits

Live values, slots, classes and books use F8 bounded pages, text search, exact
column filters and deterministic source/tie ordering. Sorts use current values.
Only a requested page binds fields/builds rich evidence/action cells. Books expand
to F9 presence graphs and bound raw-ID fields; all states Unknown, no prerequisite
edges, every node has text and a field target. Books over 512 occurrences retain
the raw fallback. Neither presence nor a graph node means learned/prepared/usable.

Budgets: existing 64 MiB blob / 4,096 descriptors; owner arrays ≤100,000 entries
each and ≤1,000,000 total; stacks/slots ≤100,000; books/classes ≤10,000 source rows;
≤4,096 spells/book and ≤100,000 total spell occurrences; ≤32 classes/source row;
identifier strings ≤512 printable ASCII bytes. Unknown layouts fail with a format
diagnostic. No recursive bag traversal is attempted, so a corrupt graph cannot
cause unbounded recursion or an invented recursive inventory total.

## Still unavailable

Party/body/stats/spell joins, player-owned gold totals, learned/prepared spell
selection, class/progression/respec, equipment, new entities, deletion, transfer,
split/merge, archetype relocation, story/approval and event dependencies remain
unimplemented. No opaque pointer writer is exposed. New captures are needed to
calibrate authoritative joins, transient states and dependencies; warnings alone
cannot repair unknown cross-references. Full roadmap phases are not claimed done.

## Exact owner validation (#31)

Use a spare slot and record native game build/region/ruleset/mods, source rows,
shown before/after values and warnings, excluding account identifiers.

1. Untouched toolkit export → console load → native resave → reload → decrypted
   redump. Capture an idle no-action resave pair before interpreting churn.
2. Identify one ordinary settled stack/owner; capture consume-one and gold ±1
   separately. Lower one supported member quantity, then inspect inventory/use/trade/
   rest, unrelated owners, native resave/reload/redump. Repeat a multi-contribution
   member and an immediate-versus-settled stack rearrangement.
3. Independent damage/heal/temp-HP pairs. Edit one supported candidate at a time,
   including 0 and stored maximum, then check sheet, combat, death/status behavior,
   rest and other characters, resave/reload/redump.
4. One effective ability at a time, with/without boosting equipment. Reduce by one,
   inspect effective and base sheets/checks/combat, level-up/respec behavior and
   native resave/reload. Repeat each supported ability; record regeneration.
5. Swap two ordinary unequipped entries in one container. Inspect visual grid,
   quantities, equipment and ownership; test undo/redo, resave/reload/redump.
   Include a container with non-sequential CSD ordinals before broadening semantics.
6. Lower two quantities, preview imported restoration, test explicit selection,
   filtered select-all with an exclusion, document scope and single-step undo/redo;
   console-load and verify only intended stacks before native resave/reload/redump.
7. Preserve the prior metadata and broader identity/transfer/equipment/spell/story
   captures in EVIDENCE.md. Only the owner may promote Stable.

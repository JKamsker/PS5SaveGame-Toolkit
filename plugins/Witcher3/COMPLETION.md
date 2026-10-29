# Witcher3 completion audit — 2026-10-28

Asset-round follow-up (2026-10-29): [ASSETS.md](ASSETS.md) supersedes the earlier publisher-asset
refusals and missing-icon statements below. Version 0.7.0 imports attributed icons, wiki-transcribed
tooltips/effects and separate regional reference backgrounds. Native saved-tag region/coordinate
joins remain unknown; console/gameplay and structural-write evidence requirements are unchanged.

Compared every plan requirement/acceptance criterion and all #25 handoffs with starting local
`main` **1f0d1f1**. Branch: `plugin/witcher3-completion`. All repository changes are inside this
plugin. Writes remain Experimental; no PS5 load/acceptance is claimed.

## Requirement disposition

| ID | Final classification | Delivered or exact remaining need |
| --- | --- | --- |
| R1 | Owner-blocked #31 | Controlled buy/use/drop/repair/stash/equip/XP/skill pairs with complete metadata; existing warned writes stay enabled. |
| R2 | Owner-blocked #31 | Spare-slot console load, intended/unrelated gameplay, native save/reload/re-export; human approves Stable. |
| R3 | Done for available distributable references; owner-blocked #31 for exact installed build | Existing 4,011 item/107 ability/225 card packs, names/categories/descriptions/provenance. Matching legal caps, localization/art and initialization definitions need legitimate matching resources. No inferred legal-cap table. |
| R4 | Done for shipped scope | F2 reference packs, validation and pinned context supplied by framework. Private installed-build packs require owner data, not new plugin I/O. |
| R5 | Done for plugin hook | Existing F1 native decoder plus sample semantic/byte tests cover new refill. F12 follow-up registers the production SDK hook for CLI discovery/selection; positional identity limitations are explicit. |
| R6 | Done | `Open(file, references)` and immutable pinned catalogs/assets. |
| R7 | Done | F3 rich details, badges/confidence and provenance. Icons omitted because no safely redistributable game art was supplied. |
| R8 | Done | F4 catalog references and F8 complete catalog queries. |
| R9 | Done for shipped IDs | Item/ability/Gwent inspect references and new read-only native ability identity references. Identity replacement lacks a safe native transaction. F13 follow-up enables browse-only pickers and read-only attributed lookup actions. |
| R10 | Owner-blocked #31 | F5 framework exists, but safe native item creation/removal/identity and reference cleanup needs add/remove/split/merge/equip captures. |
| R11 | Owner-blocked #31 for native structural operation | Scalar group undo/redo is tested; no structural writer exists to safely checkpoint. No substitute history model added. |
| R12 | Done | F6 repair/refill old/new previews, skipped rows, warnings, explicit/document/resolved-query scopes and grouped undo. |
| R13 | Done | Pure F6 planning and apply-time revalidation; direct invocation, forged and stale plans rejected. |
| R14 | Owner-blocked #31 | Mid/endgame/current patch, HoS/B&W, NG+, Ciri, stash, currencies, socketed/enchanted, large/multi-chunk/long-name fixtures. Existing synthetic chunk tests retained. |
| R15 | Owner-blocked #31 | Exact-build same-playthrough platform pairs where feasible; no format conversion or unsupported-platform claims. |
| R16 | Done for native F9 presentation; owner-blocked #31 for semantic joins/transactions | Native tag memberships and mutation prerequisites now visible. Matching native tag→region/name/coordinates, quest GUID/dependency/recipe-grant definitions and controlled world transitions remain absent. |
| R17 | Owner-blocked #31 | Independent card purchase/win/leader/deck, recipe learn, signpost discover, Place-of-Power visit/use and cutoff captures. Existing card-count writes remain enabled. |
| R18 | Owner-blocked #31 | Legitimate matching installed-build resource export for semantics/localization/art. Approved licensed community embeddings already used; no further permission gate invented. |
| R19 | Owner-blocked #31 | Exact objective/branch pairs, timed/repeated facts and later continuation/world revisit; no guessed story writer. |
| R20 | Done for supported F9 | Native skill graphs, mutation prerequisite DAG, linked map-tag lists, stable targets and text alternatives. No invented skill edges or map positions. |
| R21 | Done via framework F9 | Existing accessible renderer receives bounded nodes, branches/states/costs, filters/zoom and targets. External maps avoid importing NC content. Native regional coordinates still need R16. |
| R22 | Done for supported edits | Core semantic review and F6 grouped actions; plugin emits selected `.sav`, Core may mirror identical backups. `.req` is an unchanged content list, not a coupled rewrite. |

## Acceptance disposition

| Plan acceptance criterion (in original order) | Result |
| --- | --- |
| 1. Plugin scope, small composition, no tooling/saves/assets | Done. All C# files remain under 300 physical lines; no hooks or build files added. |
| 2. Exact compatibility matrix | Observed tuple/title/four distinct fixtures and backup twins documented; actual console patch/DLC/NG/mod metadata and wider profiles are owner-blocked #31. |
| 3. Counts/known values/exact facts consumption | Existing all-sample tests retained, including 102/16/4/13 ability-related arrays, 27 Gwent indices/35 copies, recipes/journal/facts and opaque words/timestamps. |
| 4. Native identity, sourced names, ownership/raw fallback | Done for observed player records. No guessed Ciri/stash/Roach joins; corresponding ownership captures remain in #31. |
| 5. Licensed resources, attribution/no I/O | Done. Existing CC BY-SA/MIT packs and notices retained; new spatial sidecar, README, Info.References and UI provenance. NC community map is link only. |
| 6. No-op/undo bytes, reparse/unrelated preservation | Done for implemented writes. New refill has exact full-body and native F1 checks on all eight files; existing relocation/metadata audits remain. |
| 7. Atomic bounds/identity/ownership/overflow rejection | Done for exposed scalar/actions, including direct wrong-type/range regression fixes. Unsafe structural and prerequisite-dependent transaction capabilities are absent, not partially enabled. |
| 8. One transaction/undo and repeat-safe effects | Done for repair/refill; refill never exceeds opening quantities and repeated application is a no-op. Structural refund/identity transactions remain owner-blocked. |
| 9. Synthetic corruption + sample conformance | Done for software scope; new mutation/map corruption, 4,096-skill and 1,650-tag bounds tests, navigation, history, F8 and F6 coverage. Exact final counts are recorded in README/issue handoff. |
| 10. Bounded full-list queries | Done. Inventory/catalog/world plus all ability arrays and map tags use F8, at most 200 rows; linked native details remain lazy and graphs split at 512 items. |
| 11. Preview exclusions/companions/one undo | Done for implemented actions; repair/refill previews are pure, revalidated and undoable. No companion mutation is proven necessary. |
| 12. PS5 load/gameplay/save/reload/re-export | Owner-blocked #31, including every new write family; parsing is not console acceptance. |
| 13. Accurate docs, pinned rights, Experimental | Done. Updated README feature/evidence tables, sources/limits and this audit. Only the owner can promote Stable. |

## Additional owner checklist for #31

Retain every earlier EVIDENCE.md capture/console item. For each record include exact title/region,
console patch, DLC/NG/mod state, full decrypted slot and companions, native BEFORE/AFTER/AFTER-RELOAD
and action logs; first capture a no-action save/load/save control. Keep decrypted data private.

1. **Refill:** use a spare save with ordinary unequipped, unslotted, untagged Bread and/or Bottled water
   quantity above one. Open it, lower an eligible quantity, preview/refill to its opening quantity,
   inspect skipped equipped/quest/currency records. Test one selected row; two eligible rows together;
   full document; and filtered all-matching with one exclusion. Confirm one-step undo/redo, unchanged
   high quantities, food/equipment slots, consumption, money, quests/rewards/achievements. Load,
   native-save, reload and decrypt/re-export each trial; report persistence/rejection/rebuilding.
2. **Graph interpretation:** purchase one non-core rank and equip/unequip it separately; capture free,
   spent/path points and prerequisites. For mutation research supply B&W quest-unlocked pre/post
   research/activation/respec, displayed costs/consumed mutagens and active effects. Confirm graph
   arrows against the native menu; existing definitions alone do not establish research/availability.
3. **Map semantics:** separate discover-one-signpost, use-fast-travel, visit-one-Place-of-Power,
   consume-that-power, and disable/complete-one-site captures. Record native names, region and
   displayed marker/point state. Supply matching native tag/location/resource definitions if possible;
   they are needed for friendly region/coordinate joins and a coherent discovery/reward transaction.
4. **Still absent structures:** exact add/delete/split/merge and stash/Roach/combat equip/unequip pairs,
   including displaced gear, sockets/enchants, large stash and NG+; matching templates/definition data.
   Unknown ownership/cleanup cannot safely be enabled by adding a warning.
5. **Compatibility/remaining reads:** actual build/DLC/NG/mod metadata for existing four snapshots,
   current/mid/endgame/HoS/B&W/Ciri/multiple-currency/large/long-name saves; deck/recipe/quest-cutoff
   pairs, exact branch/objective pairs and later continuation/world revisit before story transactions.

## Won't do

- Unrestricted complete-all-quests, guessed dependency/identity allocation or forced schema/platform
  conversion: cannot preserve unknown related systems.
- Invent native tag/region/coordinate joins or import unnecessary community marker/code datasets.
  The asset-round owner decision now permits selected attributed publisher artwork/text; see ASSETS.md.
- Plugin-local F12/F13 substitutes: the merged framework hooks, inspection and catalogue contracts are used directly.

Agent work complete; remaining items are owner-blocked (#31). F12/F13 follow-up is implemented
on `plugin/witcher3-f12f13`; see README for adoption, CLI selection and the explicit identity caveat.

# Round 2 evidence and remaining work — 2026-10-28

Historical note: the [unlicensed-source unblock](unlicensed.md) supersedes the
licence-only REE dataset refusal below; attributed factual subsets are now imported.
The current plugin README also supersedes this round's read-only family/reference
fields, research-only decoder and inventory-query limitations. The later
[asset round](assets.md) imports wiki text/images and Itemaps atlas coordinates
with attribution. The sections below preserve the 2026-10-28 round's evidence.

This round extends Phase 2 read-only integration and the existing quantity
operation's Phase 3 review/validation. It does not complete the causal mapping
or console requirements of either phase. Maturity remains Experimental.

## Public source review

| Source | Rights / revision | Verified result and limitation |
| --- | --- | --- |
| [Mission JSON](https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/data/json/natives/stm/singletonuserdata/missionuserdata.user.3.json) | Egg Targaryen / dzxrly, [MIT](https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/LICENSE), retrieved 2026-10-28 | Six observed PS5 inventory IDs match `OpenConditionItemID` in 18 PC mission units. Their localized strings are GUID references, not resolved names. Source-role category is Inferred; names, caps and PS5 effects are Unknown. Six cross-references are embedded separately as MIT F2 data. |
| [Runtime inventory/name resolver](https://github.com/vaultic13/neuro-pragmata/tree/ae2ee572410e353ceae19bdc23f83f71687267dd/autorun/pragmata/bindings) | Vaultic, [MIT](https://github.com/vaultic13/neuro-pragmata/blob/ae2ee572410e353ceae19bdc23f83f71687267dd/LICENSE), retrieved 2026-10-28 | Reads `InventoryManager.getItemIDs/getItemQuantity`, resolves `ObjectIDs` and `getKindHash`, and asks `GuiDataManager.getItemData` for localized text. No exported ID/name table or proven save cap. Research only; no Lua executed or copied. |
| [Wiki items](https://pragmata.fandom.com/wiki/Category%3AItems), [Lunafilament](https://pragmata.fandom.com/wiki/Lunafilament) | Pragmata Wiki contributors, community CC BY-SA text; retrieved 2026-10-28 | Names/descriptions exist without numeric save IDs. Public indexed text reviewed; direct access to some pages was restricted. No text/media embedded and no hash guessed from a name. Embedding approval is already granted; the missing numeric linkage is the blocker. |
| [REE save editor](https://github.com/kvasszn/ree-save-editor) | No declared license as checked 2026-10-28 | Existing dump/format leads remain research-only. No unlicensed dictionary imported. |
| [RE_RSZ schema](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/rszpragmata.json) | alphazolam / praydog, MIT; existing pinned source | Existing 408 identifier names and 27 grouping labels retained. Class/field names do not establish enum values, progress semantics, item names or limits. |

Downloads stayed in `%TEMP%/pragmata-roadmap-r2`; downloaded executable code was
read as text and never run. The local generator reads only hash-pinned JSON and
the committed observation manifest. No sample payload was copied into this plugin.

## Read-only collections and references

F8 replaces positional evidence pages with queryable array shells and bounded
rows. Progress, Quests and World include stored-array counts; no completion
percentages or guessed state labels are displayed. Query summaries include up
to twelve direct fields and opaque fingerprints; nested lists and complete
read-only fields stay in details. Search is over that declared summary scope.
Ordinal sorting has a physical row-key tie-break. Offset paging is supported;
cursors and invalid columns/bounds are rejected.

Collection and row keys use fixed plaintext offsets. They survive paging,
filtering, sorting, raw scalar edits and enumeration reordering in the document.
They are not a promise of semantic identity across versions or structural edits.
F1's research decoder continues to use stronger canonical IDs where possible.

F2 pack v1.1.0 adds six `pragmata-item-evidence` records. Attribution is next to
the data, in the pack manifest, README, Info.References, field descriptions and
item details/Sources. The MIT notice ships with the pack. `pragmata-items`
retains 90 Unknown names and no limits. F4 fields remain read-only; imported
labels/limits do not authorize writes.

## Quantity operation and F1 preservation

The existing fixed-size quantity action now uses F6. Preview is pure and reports
raw-ID old/new quantities, known aliases, skipped no-ops, scope and warnings.
Explicit empty selection means none. Unknown/duplicate selections, duplicate
item IDs, unsupported query scopes and unexposed target bindings reject before
writing. Apply recomputes the plan and rejects altered/stale consequences. Core
requires a preview token, records one undo group and rejects token reuse.

All eleven gameplay samples undergo synthetic action/edit/reopen/reverse tests.
F1 reports complete semantic inputs and only inventory-quantity paths; every
changed plaintext byte belongs to quantity storage. Each reversal restores
the entire original encrypted file byte-for-byte. Existing independent golden
hash checks and all twelve no-op identities remain covered.

This proves fixed-size storage preservation. It does not prove legal quantities,
causal in-game effects, unlock dependencies, platform equivalence or console
acceptance. No new primitive gameplay family, legal-max preset, progression
action or structural operation is enabled. F8 evidence queries cannot drive
quantity actions. Phase 4 still needs a lossless serializer and controlled
add/remove/reorder captures.

See the [exact extended owner checklist](README.md#scope-and-owner-checklist).

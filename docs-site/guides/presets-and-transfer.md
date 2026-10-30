---
title: Save, reuse and transfer an action preset
description: Capture a supported action, resolve its current targets, and move presets between toolkit installations.
---

# Save, reuse and transfer an action preset

Use a preset to repeat a supported action with saved parameters. Saving, renaming, deleting or
importing a preset changes the toolkit's preset library; it does not edit your save. Applying it
requires a separate preview on the document you currently have open.

The shipped semantic consumer is FinalFantasy7: editable gameplay saves for Remake, Rebirth and
Rebirth Demo. Common files are read-only. Its five actions restock existing consumables, set
materia AP within tier, set materia stars, recover current character vitals, or set character
level. They use the existing planners and eligibility checks. They do not allocate items,
equip weapons, create sockets or build a native loadout.

Other documents can expose action-parameter capture without a semantic preset adapter. A saved
record alone does not promise it can be previewed or applied. Follow the row's readiness reason;
package import/export remains available even on documents without planned preset actions.

## Save one action

1. Open your gameplay document and expand **Toolkit presets**.
2. Under **Save an action preset**, enter a name and choose an action.
3. Choose the scope offered for that action, then enter its parameters.
4. Select **Save preset**. Your document and Undo history are unchanged.
5. Find it under **Saved presets** and select **Preview** before applying anything.

These are the FF7 scopes; the menu shows only the scopes supported by the selected action:

| Scope | What is saved and used at the next preview |
| --- | --- |
| Whole document | The action and parameters, evaluated against the current document's eligible existing records. It does not capture every displayed value. |
| All eligible | For consumables and character actions, the current eligible records across the document, including unloaded rows. It does not preserve a table filter. |
| All copies | For materia actions, all current eligible owned copies of the chosen materia type. This is an explicit type-wide request. |
| Selected rows | Explicit semantic targets from the action's bound table: consumable type, character index, or owned materia instance in Rebirth/Demo. Remake materia does not offer this scope. An empty selected list stays empty. |

For **Selected rows**, select the intended rows in that action's table. Clear a whole-query
selection first; it cannot be captured as selected rows. A selected consumable type may match
multiple current stacks and require a choice. A selected materia instance follows its identity
rather than a saved row position. Changing table pages does not turn the selection into the
whole document. Ordinary planned actions have separate [selection rules](action-effects.md#choose-the-actions-scope).

## Check compatibility and current ownership

Open **Compatibility boundaries** on the saved row. The host compares preset schema, plugin ID
and exact plugin version, title, preset kind, and the build, variant, reference-pack identity,
schema fingerprint, file role and capability schema published by the consumer. A required missing
or different boundary blocks use. Two unknown optional values can match, so “build unknown” is
not evidence that two game builds behave alike. Read-only and unsupported-action reasons can
also prevent application even when stored boundaries match.

The **Ready to apply** filter describes library readiness; it does not resolve ownership or
replace preview validation. Filter by name/action, title, build, plugin, compatibility or date
to find a preset. The list displays the first 50 matches; narrow the filter for later matches.

In **Preset preview**, read **Owned-instance resolution**:

| State | What to do |
| --- | --- |
| Resolved | Check the proposed before/after values and skipped rows. Resolution alone does not guarantee a change. |
| Missing | The target or a previously chosen candidate is absent now. Open the intended save or capture a new preset; the toolkit will not create the missing record. |
| Ambiguous | Inspect each current candidate's label, role, path and technical provenance. Choose the intended candidate and select **Preview again**. The choice belongs to this revision and is never saved into the preset. |
| Unsupported | The current document cannot resolve that identity, or its identity/ownership metadata is invalid or unsupported. Read the reason and use a supported document/action. There is no forced-match option. |

Any unresolved target blocks the entire preset. Review warnings, errors, derived consequences
and skipped-row reasons. The preset preview displays at most 50 changes and 50 skipped rows and
reports when more exist; that visible subset is not a full review of the larger plan.

Select **Apply preset** only after checking the new plan. An effective application is one Undo
step; use **Changes against original** to [review the result](metadata-review.md). A no-op has
no effective edits and adds no history step. The preset Apply button can still be available for
an empty valid plan; its success toast alone does not prove bytes changed.

If the preview is stale, wait for document refresh and use **Preview again**. A changed document,
preset or current target requires a new plan. Resolve any refresh error before applying; close
the preview if necessary and recover the document first. **Cancel** or Escape dismisses review
without applying the plan and returns focus to its opener.

## Export a preset package

Choose **Export presets** in the document toolbar, select saved presets, then **Download … presets**.
The JSON package contains semantic actions, compatibility metadata and toolkit provenance; it
contains no save payloads or save paths. It is separate from **Export edited saves**, which
downloads a save ZIP. Select 1–256 presets within the 512 KiB package limit. If the selection is
too large, export fewer; if a preset was removed, reopen the export list and select again.

## Import without applying

1. Choose **Import presets** and select a toolkit preset JSON package, up to 512 KiB and 256 presets.
2. Read the package preview and its compatibility with your current document. Incompatible presets
   can be stored for later; importing does not make them applicable here.
3. For duplicates, choose **Keep both (use a distinct name)** or the specific existing preset to
   replace. An ID or name conflict never silently replaces a library record.
4. Select **Review resulting names**, check the names and replacements, then **Confirm import**.
5. Return to **Toolkit presets** and preview a compatible preset separately when ready to use it.

Cancel leaves the library unchanged. Import previews expire after ten minutes and become stale
when the library changes. Use **Preview file again** to review fresh choices. If another process
changed the library on disk, use **Reload library and preview again**; recheck the resulting
names before confirming. For validation errors use **Retry validation** after correcting the
file or choose a valid package. The resulting library must also fit its 256-preset and storage
byte limits; remove unused presets or import fewer records if that validation fails. Transfer
leaves save bytes, dirty state and Undo history unchanged.

## What the tests establish

The merged [FF7 adapter](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/FinalFantasy7/SaveToolkit.Plugins.FinalFantasy7/FinalFantasy7PresetDocument.cs),
[adapter tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/FinalFantasy7/SaveToolkit.Plugins.FinalFantasy7.Tests/Sdk07PresetAdapterTests.cs)
and [materia scope tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/FinalFantasy7/SaveToolkit.Plugins.FinalFantasy7.Tests/Sdk07MateriaScopeTests.cs)
cover semantic identity, current resolution and scope boundaries.
[FF7 browser states](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/web/e2e/ff7-states.e2e.ts)
and [history journeys](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/web/e2e/ff7-history.e2e.ts)
exercise the UI; [transfer](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/web/e2e/preset-transfer.e2e.ts)
and [disk-change recovery](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/web/e2e/preset-transfer-recovery.e2e.ts)
cover the separate library workflow. Fixture and byte/history checks do not prove console
load, gameplay effect or native re-save acceptance. Keep an untouched backup and test your export
on a disposable slot.

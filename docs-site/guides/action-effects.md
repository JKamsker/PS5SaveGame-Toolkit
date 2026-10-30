---
title: Preview an action and understand its effects
description: Choose the intended action scope, inspect typed consequences, and recover stale or empty plans.
---

# Preview an action and understand its effects

Choose a supported planned action to review its proposed changes before committing. Current
examples include FF7's existing consumable/materia/character actions and the
[manual Tsushima caption](manual-caption.md). Their canonical field changes are available today.
The additional typed **Action effects** presentation is shipped in the host and UI, with
synthetic tests. No production plugin in this checkout implements its effect provider;
Gearbox's production action-effect adoption has not landed. Do not expect Gearbox transfers or
imports to show these typed effect rows today.

## Choose the action's scope

Check the scope text in the dialog before selecting **Preview**:

| Selection | Scope used by an ordinary planned action |
| --- | --- |
| Explicit rows in the action's bound table | Those row keys, including selected rows on unloaded pages. |
| Whole filtered-query selection | Matches across the full collection, including unloaded pages, minus excluded rows. It is bound to the query revision. |
| No selected rows and no whole-query selection | Document scope; the plugin defines which existing records are eligible. |
| Unreachable or ambiguous table binding | Action is unavailable until the binding can be resolved. |

Typing a table filter alone does not select every matching row. An action can reject a scope it
does not support: the caption action accepts only document scope. **Selected rows** in a saved
preset is a separate explicit capture, where an empty list remains empty; see
[preset scopes](presets-and-transfer.md#save-one-action).

## Read the preview

Enter parameters and select **Preview**. Read the before/after values, derived consequences,
warnings, errors and **Skipped rows** reasons. **Change parameters** returns to the form;
preview the new values before applying. The tables and skipped list use 100 rows per page.
Inspect later pages as needed; the first page is not the entire plan.

When a provider supplies typed effects, the **Action effects** table shows each effect's label,
before/after payload, kind, confidence, warning, description, source and supplied path. Effects
also remain in the ordered **Preview changes** list alongside ordinary fields. Appearing in both
views does not mean the effect happens twice. The effect-only table retains their relative order.

Expand a payload to inspect its complete typed value. **Copy exact** copies that typed display
text when browser clipboard access is available; it is not a save-file export. A shortened summary
does not discard the expanded payload. Distinguish **(absent)**, **null (explicit)** and
**text: null**, as well as exact integers and tagged union values. Never infer a missing record
from a `null` label or a rounded number.

`StructuralMemento` describes a structural consequence restored through the whole action's
snapshot. `Observation` describes a reported consequence. Neither row is an independent scalar
editor or a per-field Revert target. Canonical fields still use their normal validation and write
policy. A confidence label or source annotation describes the stated evidence; it does not prove
console acceptance or grant write permission.

## Apply, cancel or recover

Select **Apply** only after reviewing the current plan. An effective planned action is one history
unit; use document **Undo** to reverse the whole operation and **Redo** to restore it. For structural
effects, undo restores the whole-action snapshot rather than writing individual effect payloads.
Use [Changes against original](metadata-review.md) for the resulting known field differences;
it is separate from the prospective effect table and is not a persistent effect log.

**Cancel** before Apply leaves the proposed action unapplied. An empty plan has no effective
changes and its ordinary action **Apply** button is disabled. Skipped rows can explain why nothing
was eligible. Do not broaden the scope until you understand the reason.

Document, selection or parameter changes can make a preview stale. Wait for refresh, inspect the
scope again and select **Preview again**. If refresh fails, choose **Retry document refresh**;
Apply remains blocked. A rejected stale plan must not be reused. Read validation errors, correct
the parameters or scope and generate a fresh plan. For a lost workspace,
[reimport your retained original or export](troubleshooting.md#the-workspace-disappeared-or-a-request-is-stale).

## What is demonstrated today

The [effect renderer](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/web/src/components/document/review/ActionPreviewChanges.tsx)
is exercised by [component tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/web/src/components/document/review/ActionPreviewEffects.test.tsx)
and the [synthetic browser journey](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/web/e2e/action-effects.e2e.ts).
That browser journey intercepts responses to check ordered typed rows, absent/null distinctions,
long payloads, pagination, copying, keyboard access and responsive widths. Its Apply response is
also synthetic; it does not validate a production plugin's mutation or native save acceptance.

[Host effect tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/tests/SaveToolkit.Core.Tests/ActionEffectTests.cs)
and [authority-boundary tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/tests/SaveToolkit.Core.Tests/ActionEffectAuthorityBoundaryTests.cs)
exercise the contract using test providers. The production scalar journeys linked above establish
their own narrower behavior. They do not establish Gearbox structural effect planning, rollback
or console acceptance.

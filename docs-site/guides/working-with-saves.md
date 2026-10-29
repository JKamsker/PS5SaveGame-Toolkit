---
title: Working with saves
description: Understand evidence, review a small edit, and export without overwriting your original.
---

# Working with saves

Use the [quickstart](../getting-started/quickstart.md) to launch locally. The [repository user guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/USER_GUIDE.md) owns the complete workflow and title-by-title limits. This page is a checklist for your first change.

## Read the evidence first

Check the detected title and format/version against the plugin README linked from the [support table](https://github.com/JKamsker/PS5SaveGame-Toolkit#supported-games-and-formats). Recognition alone does not prove every field or write is understood.

| Label | What it tells you |
| --- | --- |
| Verified | The stated claim has evidence. Read the description to understand its scope. |
| Inferred | A plausible interpretation still needs confirmation. |
| Unknown | Meaning or behavior is not established. |

Confidence in a byte layout is separate from confidence in a gameplay effect. A catalogue name, map or dependency graph does not establish ownership, prerequisites, completion or a safe unlock. Read-only fields may be intentionally protected.

## Make one small change

1. Import a copy and open the relevant document.
2. Browse or search the table; check that the row and owner are the ones you intended.
3. Choose one supported existing value within the published bounds. Read any warning and confirmation.
4. For a bulk action, inspect its preview and skipped rows before applying it. A preview explains the planned changes within its stated limits.
5. Review the **Changes** panel. If anything is unexpected, undo and inspect again. History has limits; export or retain the original rather than relying on unlimited undo.

Related files may open as an explicit group with coordinated history. Do not independently substitute files from another save set. Follow the plugin's documented grouping and backup rules.

## Export and test separately

Export to a new location. The toolkit checks output by reopening it, but plugins remain responsible for format integrity and game acceptance is unproven. Keep the exported path layout intact and use your save manager's restore process.

Try the export on a disposable spare slot before trusting it. The game may reject, normalize or overwrite an edit even if parsing and export checks pass. Retain the original outside the toolkit folder.

For exact path and backup behavior, read the [workspace export contract](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/API.md#workspace-export). For stale-workspace, unsupported-file and connection errors, see [troubleshooting](troubleshooting.md).

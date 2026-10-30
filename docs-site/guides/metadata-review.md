---
title: Review changes and find the owning field
description: Compare original and current values, inspect projections and member context, and recover an incomplete or stale review.
---

# Review changes and find the owning field

After an edit, open **Changes against original**. This compares the document's current known
fields with their imported values. It is a semantic review, not a complete binary diff or an
action log. Undoing an edit or returning a value to its original can remove its row.

## Check the value and its context

1. Read the row's **Original** and **Current** values. An absent value is different from explicit
   `null` or text containing the word `null`.
2. Read confidence and warnings. **Derived · read only** means the value follows a primary field;
   it does not become independently editable because it appears here.
3. In a grouped document, check the member role and relative path. The same field label can
   appear in different files. Group title and provenance appear when supplied.
4. Expand **Also shown as … projections** when available. These are explicitly linked aliases of
   a canonical field, not additional independent edits. **Changed projection** identifies an
   alias that also changed. Equal values or similar labels alone do not establish an alias.
5. Open **Technical details** for opaque field IDs, canonical identity or a supplied alias path
   when you need to distinguish targets.

Use **Find a changed field** to search returned labels, IDs, paths, aliases and member context.
The count reports matches in this review response; a filter does not expand the server's evidence
coverage. Tables show 100 rows per page with **Previous** and **Next**. The named table region
can receive keyboard focus for scrolling.

The host can obtain metadata for a requested field without opening every table page. You do not
need to visit every row to make a supported action reviewable. This still depends on what the
plugin publishes; unknown fields and omitted coverage do not become understood by browsing.

## Jump, revert or undo

**Jump** navigates to the current target when it remains available. Navigation does not write a
value. An unavailable target has a disabled button and a reason; refresh after structural changes
instead of guessing an ID or editing another row with a similar name.

**Revert** restores a safely editable primary field to its imported value and recomputes dependent
values. Check the refreshed review afterward. Added, removed, unavailable and derived fields may
not support per-field revert. Use document **Undo** for the operation that changed them, subject
to history limits. Group operations share coordinated history; reverting a primary is not a way
to replay only one structural consequence.

## Read coverage before trusting an empty list

**No semantic changes.** means no differences in a complete semantic review. It does not prove
byte equality, integrity or gameplay acceptance. If **Review coverage is incomplete** appears,
only the listed changes are proven. The panel reports examined and omitted counts, sometimes
**at least** an omitted count, and a byte budget. **No proven changes in the reviewed portion.**
does not establish that the unreviewed portion is unchanged. Read every coverage warning and
retain your original; filtering or paging cannot recover evidence the host did not capture.

If loading fails, use **Retry changes**. A stale Jump or Revert refreshes the review; inspect the
new rows before retrying. If document refresh fails, recover it first through the document's
refresh controls. An expired workspace requires [reimporting](troubleshooting.md#the-workspace-disappeared-or-a-request-is-stale).

## Current consumers and evidence

Pragmata publishes requested quantities and explicit legacy raw/inspection aliases;
[quantity metadata tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/Pragmata/SaveToolkit.Plugins.Pragmata.Tests/QuantityMetadataTests.cs)
check one canonical write and read-only projections. Gearbox publishes targeted metadata and
real grouped role/path context;
[session tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/Gearbox/SaveToolkit.Plugins.Gearbox.Tests/FieldMetadataR6SessionTests.cs)
check approved edits and coordinated history. XorJson's
[metadata adoption tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/XorJson/SaveToolkit.Plugins.XorJson.Tests/MetadataAdopterTests.cs)
cover targeted lookup, read-only limits, structural rebinding and current group identities.
These are bounded consumer claims, not universal field coverage or proof of gameplay meaning.

[Queryable semantic review tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/tests/SaveToolkit.Core.Tests/QueryableSemanticReviewTests.cs)
exercise host review. The [semantic-review browser tests](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/web/e2e/semantic-review-ux.e2e.ts)
use synthetic responses to test aliases, unavailable targets, incomplete coverage and recovery;
they do not establish those semantics for a real title. For repeated UI projections in UnrealGvas,
see [canonical field references](../tutorials/field-references.md).

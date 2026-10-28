# 2018 community hash crosswalk candidates

Source: [ProtoBuffers / xcier, GoW Save Lab](https://github.com/xcier/GoW-2018-PS4-editor/blob/9342d5f3967ef74bac1161581dc4eab8bb41760d/app/resources/database/gow2018_items.json),
revision `9342d5f3967ef74bac1161581dc4eab8bb41760d`; retrieved 2026-10-28.
The editor credits the God of War IDs community spreadsheet; that attribution is retained.

Licence: **No licence published; reused with attribution per owner decision 2026-10-28**.
No licence file is published in the pinned repository tree; `licenseUrl` points to the source,
not a licence grant. Original JSON SHA-256:
`ce7b3e4aab1a72a6ad9b7365946a6072327b6c2d2c7cb1591956aa31e07e7213`.

Imported all **1,682 unique factual ID/name/category/internal-name records**, including gear and
quest/lore entries. Keys are already **literal bytes**, unlike the numeric runtime spreadsheet;
they must not be reversed again. Output hash is pinned in `pack.json`. Prefix every display name
with `2018 candidate:`, categories with `2018 /`, and set confidence to Unknown. Missing internal
names become `Unknown`. Omit slot addresses, `safe_edit`, game text/media, code and all legal maxima.
The source's safe-edit flags and fixed PS4 multi-slot offsets do not transfer to Ragnarök.

Reproduction: [`import_community_tables.py`](../../../research/import_community_tables.py), using
the hash-checked source downloads in OS scratch storage. No upstream implementation is executed.

PS5 verification: across all 76 primary gameplay saves, 16 distinct IDs occur in counted resource
rows, all already present in the runtime table. **Zero** 2018 crosswalk IDs match the parsed sorted
directory, nested-ID arrays or individual grouped-flag IDs. No source establishes named Ragnarök
gear/equipped links or quest completion. Those mappings stay Unknown. The catalog remains browsable
in F2; an otherwise unknown exact match can expose an attributed, Unknown, read-only F4 reference.
Runtime and existing PS5/raven mappings take precedence. No insertion or ID-replacement writer is added.

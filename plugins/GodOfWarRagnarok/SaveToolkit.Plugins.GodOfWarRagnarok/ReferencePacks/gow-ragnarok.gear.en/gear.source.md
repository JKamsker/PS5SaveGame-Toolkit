# Gear definition and catalog-level candidates

Source: [God of War Ragnarök community gear spreadsheet](https://docs.google.com/spreadsheets/d/17JWS2EyV68tRAajJIungRvhL5mmePIyGUNqekWu1lBc/edit),
[CSV export](https://docs.google.com/spreadsheets/d/17JWS2EyV68tRAajJIungRvhL5mmePIyGUNqekWu1lBc/export?format=csv).
Accessed 2026-10-29. Contributors: cfemen / Fixer / Sasha / Vinny / community gear-sheet contributors.
No immutable Google revision ID was supplied. The reviewed export is pinned by SHA-256:
`cdac528fb32f7f93cc27e7d0cdde0e7f205cba4d55b9bfaf63e2689e90343133`.

Licence: **No licence published; reused with attribution per owner decision 2026-10-28**.
This records the repository owner's reuse policy, not an upstream licence grant.

Transformation: 2,163 factual rows become 2,092 unique IDs. Numeric hex hashes, including
short values with omitted leading zeroes, become literal little-endian byte keys. Preserve
name/type disagreements by joining distinct source strings; do not select a preferred variant.
Of the repeated IDs, 48 have differing name/type/level tuples. Missing, nonnumeric or conflicting
levels are `Unknown`; numeric levels describe the source's definition variant, never a legal maximum.
Placeholder names retain Unknown confidence. Notes, presets, prose, assets and code are not imported.
Regenerate with [the hash-checked original importer](../../../research/import_gear_table.py).

Across all 76 primary PS5 gameplay records, 141 distinct definition hashes match this table;
118 have specific candidate names and 57 have numeric catalog levels. These are Inferred joins,
not a PS5 UI comparison. The gear block contains 7,443 records, including unmatched definitions.
See [field discovery](../../../FIELD_DISCOVERY.md) for framing, negative results and dependencies.
No gear, slot or skill writer was introduced. The existing five SDK-03 pack fingerprints stay unchanged.

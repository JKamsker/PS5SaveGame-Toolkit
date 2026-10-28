# Runtime ID and skill-tree facts

Source: [mooofin / God-of-War-Ragnarok](https://github.com/mooofin/God-of-War-Ragnarok/tree/7b1730217b92d27ab910d6d421efbd503cc109b2),
revision `7b1730217b92d27ab910d6d421efbd503cc109b2`; retrieved 2026-10-28.
Authors: mooofin and the community spreadsheet contributors; the bundled sheet has no further author notice.

Licence: **No licence published; reused with attribution per owner decision 2026-10-28**.
The pinned repository tree contains no licence file. `licenseUrl` links to the source as evidence,
not to a licence grant.

| Input | SHA-256 (original bytes) |
| --- | --- |
| [`item#.zip`](https://github.com/mooofin/God-of-War-Ragnarok/blob/7b1730217b92d27ab910d6d421efbd503cc109b2/item%23.zip) | `a3c1f0a9737dce922b578f4108db5f861e74bb9d90919a75923c833bd5fb78bd` |
| `Tabellenblatt1.html` member | `548e77e8b6235be25fba42da9d16e2262881b1a369817768b80fe3edbda11093` |
| [`gow.CT`](https://github.com/mooofin/God-of-War-Ragnarok/blob/7b1730217b92d27ab910d6d421efbd503cc109b2/gow.CT) | `365a53fd4e47c70dc612f9583a5893d3f8d2d6deca2e128cdddf32d74294236d` |

Changes: extract only factual numeric IDs, short names, category/annotation columns and four
named `GetSkillTree` arguments. Convert numeric runtime u64s to **literal little-endian byte keys**.
The spreadsheet has 962 rows, 961 unique IDs: duplicate `1A6D7C011ADC7D16` is identical and merged.
233 entries have a specific supplied name; 728 blank/dash/Unnamed/(CUT)/generic Unique Resources
entries remain explicitly Unknown, with the original name in `attributes.sourceName`.
Category normalization is presentation only. No acquisition note or preset proves a legal maximum.
No CE/Lua/assembler implementation, bundled JavaScript/CSS, game prose, image or executable is imported.

`gow-ragnarok-runtime`: 961 records. `gow-ragnarok-trees`: four records.
Output hashes are pinned in `pack.json`. Reproduce with the plugin-local
[`import_community_tables.py`](../../../research/import_community_tables.py) and the three hash-checked
downloads named in its docstring; it reads the ZIP as data and executes no source content.

Sample evidence across all 76 primary PPSA08330 gameplay records:

- 211 distinct runtime resource IDs occur in counted wallet rows; 66 saves have matches and ten
  early saves have empty resource lists. 78 of those IDs have specific source names. All 211
  hero rows in late generation-zero SAVE0 match the runtime table; duplicates in other containers
  retain their own identities and quantities.
- Four group IDs match, in every save: Axe `0000001013865FC6` (28 rows), Blades
  `38EA4A0828266819` (26), Spear `014BAA9043ACBB11` (27), Atreus `07A134D7DF345D66` (28).
  Group roles are **Inferred** from the PC source. The fifth group `011CBBEE78698EC9` (23) stays
  Unknown; it is not named Freya by elimination. No individual skill hash, unlock bit, prerequisite
  or player-facing completion state is established by these group joins.
- There are zero item-table matches in the parsed directory, nested-ID arrays or individual flag IDs.
  Runtime Valhalla armour rows are resource/unlock candidates, not equipped-gear identities.
- Kratos/Atreus/Freya XP labels remain PC candidates. The two Atreus variants retain distinct raw
  hashes and the source's playing-as context in attributes; their PS5 ownership is unverified.

Runtime names reach F8 resource/XP rows, browse-only F4 references, writable counter descriptions,
inspection/F6 labels and attributed F12 decoder labels. Group roles and their provenance reach the
flag collection and decoder. Original sourced resource/raven names take precedence; unknown raw IDs
and all quantity limits/warnings are preserved. No integrity repair was found; see RESEARCH.md.

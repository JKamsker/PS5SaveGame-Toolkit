# Armor names and IDs

- Source: https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/armor_name.rs
- Author/project: ClayAmore / ER-Save-Editor contributors.
- Licence: [MIT](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/LICENSE-MIT); full notice in [LICENSE-MIT.txt](LICENSE-MIT.txt).
- Retrieved: 2026-10-28; pinned revision `014107f0ca1cff867b5f7565cb79d6684f9b35df`.
- Upstream SHA-256 (original bytes): `dbfbf4da769d6bd29b2ed22a86358e7d88b841a225bb10ce3e3174af959249df`.
- Records: **653**, including **30** empty upstream names.
- Changes: parsed every literal ID/name pair without executing Rust; preserved all names verbatim,
  including test/unused entries; OR'ed representable IDs with category prefix `0x10000000`; sorted by ID.
  Out-of-category upstream sentinels retain a noncanonical `category:upstream:decimal` key.
  Empty names display their raw catalog key, retaining the empty string in `upstreamName`.
  Added provenance, schema and uncertainty metadata. No descriptions, artwork or legal limits copied.
- Coverage: complete for this source table/revision, not every game version or DLC. Names alone
  never authorize item creation, quantity changes, equipment compatibility or upgrade writes.

This sidecar documents the MIT identity-label contribution to the `eldenring-items` F2 dataset.
The combined v3.0.0 dataset also contains selected publisher text/icons, with separate provenance in
[visuals.source.md](visuals.source.md). Identity labels retain MIT; the combined dataset declares
publisher rights under the owner decision of 2026-10-29. The shared picker, Sources panel and
catalog/inventory details expose attribution.

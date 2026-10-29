# Ash names and IDs

- Source: https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/aow_name.rs
- Author/project: ClayAmore / ER-Save-Editor contributors.
- Licence: [MIT](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/LICENSE-MIT); full notice in [LICENSE-MIT.txt](LICENSE-MIT.txt).
- Retrieved: 2026-10-28; pinned revision `014107f0ca1cff867b5f7565cb79d6684f9b35df`.
- Upstream SHA-256 (original bytes): `48dd5cd1f306f05be427cd30c9c8236b1df27ff96d31e025789f34fc59d92bbd`.
- Records: **218**, including **1** empty upstream names.
- Changes: parsed every literal ID/name pair without executing Rust; preserved all names verbatim,
  including test/unused entries; OR'ed representable IDs with category prefix `0x80000000`; sorted by ID.
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

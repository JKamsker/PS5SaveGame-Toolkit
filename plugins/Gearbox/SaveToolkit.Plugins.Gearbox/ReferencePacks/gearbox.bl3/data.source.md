# Serial reference data

Source: https://github.com/apocalyptech/bl3-cli-saveedit/tree/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/resources

Author: CJ Kucera / apocalyptech. Licence: [zlib](https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/COPYING.txt); notice retained in plugins/Gearbox/Notices. Retrieved 2026-10-28.

Converted the three bundled editor databases to keyed F2 records; retained asset paths, 1-based indices and all version widths. Names are family labels, not complete item cards. No artwork. Game patch/DLC/hotfix state unknown; serial database version 62.

Upstream resource generation provenance is described in [bl3save/resources/README.md](https://github.com/apocalyptech/bl3-cli-saveedit/tree/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/resources/README.md). The licensed editor resources remain the binary source. This pack also includes factual/text-only community labels from the pinned [Sandsmark editor revision](https://github.com/sandsmark/borderlands3-save-editor/tree/a1c54a2fd6c190029e3e724ac0a0a5c65d3b4fc2); no code, icons, artwork, screenshots or extracted game assets were imported. The unlicensed records use the exact attribution **No licence published; reused with attribution per owner decision 2026-10-28**. Underlying game asset identifiers remain Gearbox/2K game facts.

Original compressed-file SHA-256:

- `bl3save/resources/inventoryserialdb.json.xz`: `e01b46aef421b33bb73c5cc05b39a920b321d5ef2390689a497defce99b2586d`
- `bl3save/resources/balance_name_mapping.json.xz`: `7f683814b0d6c631d738e9895e46655fc213aed3ec7cc7ec7490d11ff3691bf3`
- `bl3save/resources/balance_to_inv_key.json.xz`: `5ee97267c59e76f8d125e770a332038bc9227599fae5de2c30317ca6ec29235d`

Balance catalogue keys are lower-case object paths; duplicate object paths are collapsed for keyed lookup (BL3 2,848 slots → 2,803 distinct paths; TTWL 2,037 → 2,030). Every original serial index, including repeated assets, is retained separately in `serial-indices.json`. This normalization changes neither serial numbers nor their indices.

Completion 1.2.0: serial index display names use the original asset object token; full raw paths remain in `attributes.asset` and descriptions. Labels are not localized names or combat-stat claims. No indices renumbered.

Asset round 1.3.0: curated balances and exact unique-directory serial records now carry optional assetId references. Publisher image provenance and mapping limits are separate in assets.source.md; binary layout indices/widths remain unchanged.

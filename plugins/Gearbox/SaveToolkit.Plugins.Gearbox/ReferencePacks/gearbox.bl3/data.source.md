# Serial reference data

Source: https://github.com/apocalyptech/bl3-cli-saveedit/tree/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/resources

Author: CJ Kucera / apocalyptech. Licence: [zlib](https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/COPYING.txt); notice retained in plugins/Gearbox/Notices. Retrieved 2026-10-28.

Converted the three bundled editor databases to keyed F2 records; retained asset paths, 1-based indices and all version widths. Names are family labels, not complete item cards. No artwork. Game patch/DLC/hotfix state unknown; serial database version 62.

Upstream resource generation provenance is described in [bl3save/resources/README.md](https://github.com/apocalyptech/bl3-cli-saveedit/tree/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/resources/README.md). Only the resources bundled in the licensed editor revision were converted; no NC bl3data/wldata exports, wiki text, icons or external generated datasets were imported. Underlying game asset identifiers remain Gearbox/2K game facts.

Original compressed-file SHA-256:

- `bl3save/resources/inventoryserialdb.json.xz`: `e01b46aef421b33bb73c5cc05b39a920b321d5ef2390689a497defce99b2586d`
- `bl3save/resources/balance_name_mapping.json.xz`: `7f683814b0d6c631d738e9895e46655fc213aed3ec7cc7ec7490d11ff3691bf3`
- `bl3save/resources/balance_to_inv_key.json.xz`: `5ee97267c59e76f8d125e770a332038bc9227599fae5de2c30317ca6ec29235d`

Balance catalogue keys are lower-case object paths; duplicate object paths are collapsed for keyed lookup (BL3 2,848 slots → 2,803 distinct paths; TTWL 2,037 → 2,030). Every original serial index, including repeated assets, is retained separately in `serial-indices.json`. This normalization changes neither serial numbers nor their indices.

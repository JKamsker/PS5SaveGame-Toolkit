# Serial reference data

Source: https://github.com/apocalyptech/ttwl-cli-saveedit/tree/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/ttwlsave/resources

Author: CJ Kucera / apocalyptech and Abram Hindle. Licence: [zlib](https://github.com/apocalyptech/ttwl-cli-saveedit/blob/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/COPYING.txt); notice retained in plugins/Gearbox/Notices. Retrieved 2026-10-28.

Converted the three bundled editor databases to keyed F2 records; retained asset paths, 1-based indices and all version widths. Names are family labels, not complete item cards. No artwork. Game patch/DLC/hotfix state unknown; serial database version 59.

Upstream resource generation provenance is described in [ttwlsave/resources/README.md](https://github.com/apocalyptech/ttwl-cli-saveedit/tree/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/ttwlsave/resources/README.md). The licensed editor resources remain the binary source. This pack also includes factual mapping labels from the pinned [TTWLSaveEditor revision](https://github.com/somefunguy/TTWLSaveEditor/tree/c6d701ec4a5ae1c115017de54d39df4813c035c1); no code, icons, artwork, screenshots or extracted game assets were imported. The unlicensed records use the exact attribution **No licence published; reused with attribution per owner decision 2026-10-28**. Underlying game asset identifiers remain Gearbox/2K game facts.

Original compressed-file SHA-256:

- `ttwlsave/resources/inventoryserialdb.json.gz`: `c0c2d5875c6f3ef91655523c3db98c39f85fa2de2be38a3e77f57a7f044bcebb`
- `ttwlsave/resources/balance_name_mapping.json.gz`: `b9c2bf442169a0f102ab516244071464aed3d445a3ef7e0019cfe01556670282`
- `ttwlsave/resources/balance_to_inv_key.json.gz`: `c1726466a09572233987e7187dd59b7742e4488f44b87b611e0fefd21953372e`

Balance catalogue keys are lower-case object paths; duplicate object paths are collapsed for keyed lookup (BL3 2,848 slots → 2,803 distinct paths; TTWL 2,037 → 2,030). Every original serial index, including repeated assets, is retained separately in `serial-indices.json`. This normalization changes neither serial numbers nor their indices.

Completion 1.2.0: serial index display names use the original asset object token; full raw paths remain in `attributes.asset` and descriptions. Labels are not localized names or combat-stat claims. No indices renumbered.

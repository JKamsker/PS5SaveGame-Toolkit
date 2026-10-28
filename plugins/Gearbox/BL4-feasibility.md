# Borderlands 4: PS5 evidence gate

Research rechecked 2026-10-28. **Research only; no PS5 decoder, no writes, PPSA01494 excluded.**

## Independently reproduced sample observations

`Bl4EvidenceTests` reads the private `PPSA01494_saves_dec.zip` through `SampleSaves`, without copying
data into the repository. There is one `gbxsavegame.sav` occurrence, no backup, **1,888 bytes**.
It has no GVAS magic and consists of **118 distinct 16-byte blocks**. The public plugin probe rejects
it and `Info.Games` does not claim PPSA01494.

This is consistent with block encryption but establishes neither AES nor a gameplay/profile/settings
role. The name and size alone cannot identify a character payload. No decryption, key trial,
account-ID discovery or YAML parsing was attempted. The file is left untouched.

## Pinned codec leads and applicability

- [glacierpiece utility, 3668c8a](https://github.com/glacierpiece/borderlands-4-save-utility/tree/3668c8abcbe7a51175243e1b1d495518af514dcb)
  provides MIT PC crypto research. Its item decoder is explicitly experimental; its reported item
  statistics are not evidence for PS5 serial fields. Reference only, no code/data imported or run.
- [iyre encryption notes, c9be7ba](https://github.com/iyre/bl4-save-tools/blob/c9be7baec6f1df0c348eaaebbed331d635a1f7a9/docs/encryption.md)
  describe the PC pipeline: Steam/EGS-specific account derivation, AES-256-ECB/PKCS7, zlib-compressed
  YAML and a little-endian Adler32/uncompressed-length trailer. These are upstream PC claims, not
  reproduced PS5 results. Steam and Epic derivation rules do not establish any PS5 derivation.
  MIT project; reference only, no implementation imported or executed.
- [mi5hmash resigner, 31b5a92](https://github.com/mi5hmash/Borderlands4SaveDataResigner/tree/31b5a923a1360ba5003fd77e4c89f1f8ed5c7c9c)
  explicitly limits support to PC Steam and Epic Games Store. MIT; read as codec/applicability evidence,
  never used on the PS5 blob. It does not establish PS5 account binding or file roles.
- [Nicnl serial research, 285f9e1](https://github.com/Nicnl/borderlands4-serials/tree/285f9e1e03c2a46c5e0c45844a682063f655439b)
  and other AGPL editors are research leads, not bundled implementations. No serial assets, icons,
  descriptions or AGPL code from these projects are embedded in Gearbox's F2 packs.

The current evidence cannot select a PS5 codec or a matching character/profile file. A speculative
AES implementation accepting owner IDs would obscure that gap. Role classification comes first.

## Exact owner capture checklist

1. Use the same PS5 build, title region and account throughout. Record game version, DLC/hotfix state,
   slot and the visible state; keep any account-derived material local and out of logs/reports.
2. Supply complete decrypted dumps after: first launch/fresh profile; one settings-only toggle; first
   active character/manual gameplay save. Include every file and available backup. Note which files
   appeared, vanished or changed; do not rename an unknown file to a presumed character role.
3. From the same active slot, capture a no-action save/reload/save control, then independent pairs for
   one known money change and one legitimate item pickup. Transcribe the amounts/item card. Include
   matching profile/settings files and backups on both sides.
4. Establish an independently documented **owner-local PS5** derivation route. It must not assume a
   Steam/EGS ID mapping. Supply reproducible nonconfidential format invariants or a proven local
   preprocessing route, without sharing credentials, private keys or account-specific secrets.
5. Only after a role and codec are demonstrated on multiple independent samples: verify padding,
   compression boundaries, trailer lengths/Adler32, unknown YAML/token retention, corruption rejection
   and byte-identical full-file no-op round trips. Bound all allocations and decompression output.
6. Remain read-only until the owner validates a separately implemented write family on a backed-up
   spare slot (load, intended effect, re-save, reload and compare). This round implements no BL4 writes.

## Result

Phase 5's evidence/status track is documented and regression-tested. PS5 file roles and derivation
remain unestablished (GEARBOX-R18/R19/R22); actual BL4 decoding and write support remain blocked on
the captures and lawful local codec evidence above. BL3/Wonderlands progress is independent of this gate.

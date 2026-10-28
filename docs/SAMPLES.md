# Sample saves

Decrypted PS5 dumps live in `artifacts/` (git-ignored, never commit them). Layout inside each zip:
`<TITLE>/sdimg_<dir>/...` plus the console backup twin `sdimg_sce_bu_<dir>/...`. Many zips contain the
same paths several times (snapshots appended); the importer exposes them as generations 0, 1, ...
`sce_sys/param.sfo` is sometimes a regenerated placeholder (MAINTITLE = title id).

Identities were confirmed by the reconnaissance pass; per-title evidence, format notes and a ranked
roadmap live in [docs/formats/](formats/README.md). Entries marked `?` are still unconfirmed.

| Title | Game | Main files | Notes |
| --- | --- | --- | --- |
| PPSA01288 | Sackboy: A Big Adventure | `SaveGame/General`, `LevelImages_*` | |
| PPSA01325 | ASTRO's PLAYROOM | `sce_sdmemory/memory.dat` (1 MiB) | save-data memory API blob |
| PPSA01463 | Borderlands 3 | `profile`, `1` | |
| PPSA01474 | Ratchet & Clank: Rift Apart | `i29-s.save`, `i29-userprefs.save` | |
| PPSA01493 | Tiny Tina's Wonderlands | `profile`, `1` | same layout as PPSA01463 |
| PPSA01494 | Borderlands 4 | `gbxsavegame.sav` | |
| PPSA01506 | Immortals Fenyx Rising | `AutoSave0x.sav`, `Options.sav` | |
| PPSA01603 | Hogwarts Legacy | `HL00xx/ue4savegame.ps5.sav` | Unreal |
| PPSA01651 | YouTube (app) | `STARBOARD/*`, `memory.dat` | not a game |
| PPSA01802 | Kena: Bridge of Spirits (from icon0.png key art) | `ue4savegame.dpx.sav` | Unreal |
| PPSA01876 | Far Cry 6 | `profile.dat` | |
| PPSA01911 | Final Fantasy VII Remake Intergrade | `ff7remake0xx` (9 MiB each) | |
| PPSA01982 | DOOM Eternal | `PROF` | |
| PPSA02199 | STAR WARS Jedi: Fallen Order | `ue4savegame.dpx.sav` | Unreal |
| PPSA02225 | Ghost of Tsushima | `suckerpunchUserN/dis.dat`, `memory.dat` | |
| PPSA02343 | ? (JSON profile) | `ProfileData/ue4savegame.dpx.sav` | Unreal |
| PPSA02530 | Pragmata | `data0xx*.bin` | RE Engine DSSS/Mandarin; analysis in `PRAGMATA_PS5_Save_Editor_Toolkit.zip` |
| PPSA02745 | Tales of Arise | `SaveData_00x.sav`, `SystemSaveData.sav` | |
| PPSA03169 | Elden Ring (JP) | `memory.dat` (29 MiB) | |
| PPSA04018 | The Witcher 3 (Complete Edition) | `CheckPt.*.sav`, `Manual.*.sav`, `*.req` | |
| PPSA04027 | Cyberpunk 2077 | `sav.dat`, `metadata.9.json`, `settings.json`, `user.gls` | |
| PPSA06251 | Star Ocean: The Divine Force | `SAVEDATA_000xx.DAT`, `SYSDATA` | |
| PPSA07696 | Ridge Racer 2 (PSP classic, UCES00422) | `DATA.BIN`, `imgui_emu_option.json` | emulated PSP save |
| PPSA07821 | Overwatch 2 (settings) | `settings/data` | |
| PPSA08330 | God of War Ragnarök | `GOWRSAVEn` | |
| PPSA08664 | Final Fantasy VII Rebirth | `ff7rebirth0xx` (23 MiB) | |
| PPSA10665 | Final Fantasy XVI | `FAITHDATAnnnn/game-save-data` | same family as PPSA13803 |
| PPSA10695 | Atomic Heart (custom SaveExtension, not GVAS) | `ue4savegame.dpx.sav` | Unreal |
| PPSA13803 | Final Fantasy XVI (demo) | `FAITHDATA*/game-save-data*` | |
| PPSA16064 | ? (JSON + XOR-1 settings) | `Character*/ue4savegame.dpx.sav`, `GlobalSaveDataDefault` | Unreal |
| PPSA18463 | Baldur's Gate 3 | `Save0001/AutoSave_0.lsv`, `Profile/*.lsf/.lsx` | Larian LSPK/LSF |
| PPSA18936 | Final Fantasy VII Rebirth (demo) | `ff7rebirth00x` | |
| PPSA26344 | Ghost of Yōtei | `suckerpunchAutoN/dis.dat` | same family as PPSA02225 |

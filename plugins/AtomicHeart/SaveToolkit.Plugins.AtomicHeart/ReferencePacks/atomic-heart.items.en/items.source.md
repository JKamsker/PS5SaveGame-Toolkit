# Item-label dataset provenance

Source: [AtomicHeartMenu features.cpp](https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/src/features/features.cpp), revision fe36f468baacc996c252a1d3231d0d20009127bd, lines 836–893. Retrieved 2026-10-28.

Copyright (C) 2026 Skorchekd (Atomic Heart Menu).
Licensed GPL-3.0-or-later: https://www.gnu.org/licenses/gpl-3.0.html
Upstream license: https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/LICENSE
Upstream notice: https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/NOTICE

Additional attribution required by upstream GPLv3 section 7 terms:
- Skorchekd, original author of Atomic Heart Menu;
- Dumper-7 by Encryqed;
- MinHook by Tsuda Kageyu;
- Dear ImGui by Omar Cornut (ocornut).

Changes: selected all 37 weapon and 8 ammunition label/object-name pairs. Removed duplicate object-name suffixes to make exact short asset keys. Added broad categories and original caution descriptions. No trainer code, offsets, executable or statistical limits are included in the original GPL subset. Some entries may be unused or DLC-dependent; they do not establish PS5 compatibility. Unmapped resource and consumable IDs stay raw; toolkit-authored identifier formatting in detail cells is explicitly not localized game text. This derived dataset remains GPL-3.0-or-later; it is not presented as original game terminology authored by this toolkit.

The repository is GPL-3.0-or-later. These notices accompany the data, appear in the plugin README, PluginInfo references, and F2 per-field attribution. No content from Atomic Heart Fandom (CC BY-NC-SA) is embedded.

## Asset round, 2026-10-29

The original 45 GPL identifier/label pairs and upstream credits remain. Twelve weapon labels now use official English names (original runtime labels retained as attributes), with short official excerpts and 64x64 publisher renders. Slimao Ltd. / Mundfish retains rights to those additions: `Rights reserved by Slimao Ltd.; used with attribution per owner decision 2026-10-29`. Individual official URLs are in each record and `assets.source.md`.

Three exact sample identifiers gain screenshot-based English labels/icons: `DA_Item_Resources_Metal_Parts`, `DA_Item_Synthetic_Material_Resource`, `DA_Consumable_aid_small`. The NORA screenshot shows the Small Neuromed Capsule's 50-health effect and recipe (2 Synthetic Material, 4 Biomaterials). These joins are Inferred; these records are explicitly reference-only for asset replacement. Existing Amount warnings and observed bounds are retained. No new cap, replacement writer, Neuropolymer/Jelly join or module/skill native ID is asserted. Screenshot provenance and rights are in `assets.source.md`; reference-only skill costs are in `guide.source.md`.

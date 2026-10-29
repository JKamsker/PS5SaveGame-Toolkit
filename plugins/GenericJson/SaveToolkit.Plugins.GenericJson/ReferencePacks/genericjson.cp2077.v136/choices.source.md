# Cyberpunk v136 choice annotations

- Source: https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/plans/GenericJson.md
- Author: PS5 Save Game Toolkit contributors; original factual annotations and uncertainty notes.
- Original annotations licence: GPL-3.0-or-later, https://www.gnu.org/licenses/gpl-3.0.html
- Recorded 2026-10-28; rechecked 2026-10-29.
- Coverage: six raw choice IDs already documented by the byte-reconnaissance plan, under
  `/gameplay/difficulty` + `GameDifficulty` and `/video/display` + `FrameRateModes` only.
- Modifications: original short descriptions, categories and word splitting (`VeryHard`);
  one official description added as documented below. No save payload, localization table,
  icon or code was copied. JSON minified; manifest hashes updated; pack version 1.1.0.
- The imported v136 choice list remains authoritative. Missing catalogue entries or duplicate IDs
  retain the recorded enum fallback. Unknown choices cannot be synthesized by this pack.
- Gameplay labels, frame rates, resolutions and console behavior are not verified by this dataset.

## Official Performance Mode description

- Source: https://www.cyberpunk.net/en/news/41435/patch-1-5-next-generation-update-list-of-changes
- Author / rights holder: CD PROJEKT RED. Published 2022-02-15; retrieved 2026-10-29.
- Licence: Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29
- Rights/source URL: https://www.cyberpunk.net/en/
- Extraction: one sentence, verbatim, from the next-generation Performance Mode section.
  Added historical context and an explicit inferred raw-ID association / unverified installed-build
  warning. This is release-note text, not a recovered v136 localization string or benchmark result.
- The official page calls the other PS5 mode **Ray Tracing Mode**. No verified join to the raw
  `Quality` choice exists here; its original uncertainty description is retained. The difficulty
  IDs likewise remain raw labels, without speculative joins to localized menu names or icons.
- Both display records share the dataset's publisher/source attribution; `Performance` also has
  its exact record-level source URL. The original `Quality` annotation remains contributor work.
- No binary assets added: **0 bytes**, no assets dataset or unresolved asset IDs.

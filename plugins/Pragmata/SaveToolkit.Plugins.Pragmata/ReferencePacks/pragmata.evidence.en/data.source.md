# Pragmata reference pack 1.2.0

`pragmata-identifiers.json` and `pragmata-sections.json` derive from
[alphazolam RE_RSZ, revision 871a1d5c4c7b81c60c966d73ee63f4c4413ab56e](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/rszpragmata.json).
Authors: alphazolam; praydog (RSZ dumps); Save Toolkit contributors (group labels).
License: [MIT](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/LICENSE).
Retrieved: 2026-10-28. Modifications: selected 408 observed identifier spellings,
checked Murmur3 hashes, extracted 27 section names, wrote group labels and explicit
unverified-semantics descriptions. No executable upstream code, icons or game text.
The full MIT notice is in `plugins/Pragmata/THIRD_PARTY_NOTICES.txt`.

`pragmata-items.json` contains 90 observed numeric IDs and original placeholder
descriptions, by Save Toolkit contributors, [CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/).
Source: [issue #21 / the plugin evidence manifest](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21).
Retrieved/generated: 2026-10-28. Modifications: decimal ID keys, hex placeholder
names, explicit Unknown confidence; no player-facing names or guessed limits.

`pragmata-item-evidence.json` derives from [PRAGMATA-MaximumTimeLimit mission JSON,
revision cfa68e987bf126daa3ce62d589ac2b7ef05abf2b](https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/data/json/natives/stm/singletonuserdata/missionuserdata.user.3.json).
Author: Egg Targaryen / dzxrly; Save Toolkit contributors (PS5 cross-index and descriptions).
License: [MIT](https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/LICENSE),
full notice in `LICENSE.Mission.MIT.txt`. Retrieved: 2026-10-28. Source SHA-256:
`f51e68117901a9bdf0ea24ae0154962e327499fc0986d4561ddb641bf0489fc7`.
Modifications: selected six exact numeric `OpenConditionItemID` values that occur
in the observed PS5 inventory and their 18 mission ID hashes (three per item).
Added original context descriptions and a `mission-opening-condition` source-role
category; no localized names, descriptions, game artwork or time-limit edits copied.
The numeric cross-reference is verified against the source and all eleven PS5
inventories. Its gameplay interpretation is **Inferred**: the PC build/platform
equivalence and PS5 effects are unverified. Player-facing names and stack limits
remain **Unknown**. Mission reward/clear counts are never used as inventory limits.
This MIT dataset remains separate from the CC0 item placeholders.

`pragmata-missions.json` uses the same author, pinned mission source, retrieval
date, SHA-256 and MIT licence/notice above. Modifications: extract all 43 mission
ID hashes, category hashes, raw difficulty values, display order (when listed),
opening-item IDs, chapter-condition/message GUIDs and extra-mission flags.
Labels and descriptions are original raw-ID context, not localized game text.
The F9 graph uses only the six existing inventory cross-references and their
18 target missions. Edges describe PC `OpenConditionItemID` data, one condition
among others; PS5 unlock, ownership and completion states remain Unknown.
Read-only F4 catalogue fields and F13 inspection display full record attribution.

Regenerate with `python -I plugins/Pragmata/scripts/build-reference-pack.py --rsz
<reviewed-MIT-schema.json> --missions <reviewed-MIT-mission.json>`. Both source SHA-256 values are checked; record-file hashes are
recomputed with LF newlines. Pack version must change when semantic content changes.

External F2 packs can supply names/descriptions/icons under catalog `pragmata-items`
using exact invariant decimal uint32 IDs and a different pack ID. Identifier and
section catalog keys are eight-digit lower-case hex. Attribution remains mandatory
for every dataset and image. The plugin forwards assets through the pinned context
and shows image provenance in row details. No networking or file I/O occurs in the
plugin. User packs do not authorize new write policies: inventory item references
are experimentally writable under the owner write-enable decision, while evidence
catalogue fields are browse-only. Imported mappings are at most Inferred, and quantities retain their existing storage
bounds rather than treating an imported limit as PS5 evidence. Unknown IDs survive
without substitution. The current game build string is unconfirmed, so the embedded
pack is build-agnostic; explicitly verify compatibility before importing other data.

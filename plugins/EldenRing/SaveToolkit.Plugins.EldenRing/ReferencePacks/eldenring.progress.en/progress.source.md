# Progress and class candidate provenance

Pinned revision: `014107f0ca1cff867b5f7565cb79d6684f9b35df`; retrieved 2026-10-28.
Author: ClayAmore / ER-Save-Editor contributors. MIT; full notice: LICENSE-MIT.txt.

| Source file | Original SHA-256 |
| --- | --- |
| [graces.rs](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/graces.rs) | `404ceccbad413c23ee5c2a79fa836006d276ffae355f2914aaf9034b71d470f5` |
| [classes.rs](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/classes.rs) | `9be056a27423f4745e86c1393c631c5b56d7a0c994b520ead06c8dce92702181` |
| [event_flags.rs](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/event_flags.rs) | `8d10136a6f079f9661333ca5488379de315f1cfe105f9279e9f590fabaca773b` |
| [map_name.rs](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/map_name.rs) | `2eda78620723e68ec9769465224a2ccce01ec0bfab8c3e7bcc423e9799b46d24` |
| [bosses.rs](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/bosses.rs) | `0e50d2ce32e1e19490e5834431560bee17d1dc3e2a173b6fae87b014635d687a` |
| [maps.rs](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/maps.rs) | `034450369b627cba24cb171fc565279d9cc9c6df81a7649b596870bf66a433a9` |
| [bit.rs](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/util/bit.rs) | `d198adad3fa0d6e71cadb29522bb038742c356c6c005fc0dc42b9dbaf762eb5d` |
| [events.rs](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/vm/events.rs) | `b88ae8439089a8407adedc1325a14b40ce57352d4853766131b57805b32d5e10` |
| [LICENSE-MIT](https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/LICENSE-MIT) | `249b6ba67172fdf76ea07854e98a096383036d97d2e9fabed278fd47679a81fd` |

Names/IDs unchanged in resources; upstreamName preserves exact text even where F2 trims display whitespace.
Explicit table joins only; source boss group comments retained verbatim.
Offsets/bits are the source's bounded sparse lookup, not eventId/8 or a universal block formula.
These are Inferred read-only candidates on v150; no progress is certified from a set/unset bit.
No art/coordinates, summoning pools, quest edges, DLC coverage or legal class minima are asserted.
This pack contains no art/coordinates. The separate names pack supplies optional local map art and
inferred placements; Map Genie remains an external link. Class starting values are reference facts,
never a respec writer.
Regenerate from the repository root with `python -I plugins/EldenRing/Evidence/import-progress-catalog.py <scratch>`.

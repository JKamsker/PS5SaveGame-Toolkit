# Pragmata reference pack 1.0.0

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

Regenerate with `python -I plugins/Pragmata/scripts/build-reference-pack.py --rsz
<reviewed-MIT-schema.json>`. The source SHA-256 is checked; record-file hashes are
recomputed with LF newlines. Pack version must change when semantic content changes.

External F2 packs can supply names/descriptions/icons under catalog `pragmata-items`
using exact invariant decimal uint32 IDs and a different pack ID. Identifier and
section catalog keys are eight-digit lower-case hex. Attribution remains mandatory
for every dataset and image. The plugin forwards assets through the pinned context
and shows image provenance in row details. No networking or file I/O occurs in the
plugin. User packs do not authorize writes: item reference fields remain read-only,
imported mappings are at most Inferred, and quantities retain their existing storage
bounds rather than treating an imported limit as PS5 evidence. Unknown IDs survive
without substitution. The current game build string is unconfirmed, so the embedded
pack is build-agnostic; explicitly verify compatibility before importing other data.

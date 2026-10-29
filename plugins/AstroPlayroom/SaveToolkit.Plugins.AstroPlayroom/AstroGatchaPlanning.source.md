# Gatcha budget arithmetic

- Source: Gage / PowerPyx, 2020-11-17,
  https://www.powerpyx.com/astros-playroom-all-collectible-locations-artefacts-puzzle-pieces/
- Rechecked: 2026-10-28, PlayStation Labo section.
- Licence: no reuse grant established; external research link only. No guide prose, artwork or
  coordinates copied by the arithmetic implementation. This is not a licence refusal: owner
  decisions 2026-10-28/29 permit attributed facts and publisher assets. The plugin separately
  embeds attributed PowerPyx screenshot derivatives/captions documented in
  `ReferencePacks/astro.catalogue.en/assets.source.md`.
  The factual 100-coin attempt cost is independently represented as a constant.
- Implementation: original Toolkit arithmetic under GPL-3.0-or-later,
  https://www.gnu.org/licenses/gpl-3.0.html . Target is max(current coins, attempts × 100), never an
  accumulating award. Attempts are restricted to 0..17 so the requested budget cannot exceed the
  private samples' observed 1749 coins. The resulting target can be higher when the current
  balance already exceeds that budget; it is never reduced. No sample bytes are distributed.
- Confidence: Inferred. Actual cost/cap and accepted edited balances still require owner validation.
  The UI/action/preview, README and Info.References link the research; no completion guarantee.

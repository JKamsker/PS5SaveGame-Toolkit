# Eikon ability reference adaptation

Final Fantasy Wiki contributors, retrieved 2026-10-28. This dataset is an
adaptation under [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/).
Retain attribution and share this adaptation under that licence.

The reviewed extract in `abilities.source.tsv` pins every article revision and
Eikon relationship independently. The [base-game navbox revision 3770527](https://finalfantasy.fandom.com/wiki/Template:Navbox_abilities_FFXVI?oldid=3770527)
provides a second check on the eight base-game groups. Leviathan and Ultima
annotations come from their character articles, pinned in each record. Individual
ability articles supply AP facts where available. The Limit Break infobox labels
both costs Upgrade, while its detailed AP table labels the final 2000 AP stage
Master; this adaptation follows the detailed table. Wings of Light uses that
feat label rather than the article infobox's combined Megaflare label.

Modifications: 48 names across ten groups; selected individual AP facts;
original stable slug keys, reference-stage relationships and warnings. This wiki
adaptation imports no game artwork/prose, save data or Nex-to-save-index mapping. Missing AP
costs stay Unknown, including the five Leviathan abilities. An absent learn cost
does not assert a free unlock. These are reference progression diagrams, not
learned/mastered/unlocked status or authoritative build/mode/DLC limits.

The source slugs are plugin reference identities only. The existing three named
Nex skill IDs remain separate; none is joined to the 68 saved rank bytes.
The asset round adds separately attributed publisher icons and short game-text
excerpts from Game8; see [media.source.md](media.source.md). They are not covered
by this wiki adaptation's licence. Regenerate the original catalogue offline with
`research/build_ability_catalog.py`, then run `research/apply_game_assets.py`.

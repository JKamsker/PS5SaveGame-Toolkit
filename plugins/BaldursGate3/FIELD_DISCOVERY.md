# Gold and base ability field discovery (#40)

Research date: **2026-10-29**. Scope: decrypted PS5 PPSA18463 samples only.
This is correctness, data preservation and malformed-input research. No security
review or console write validation was performed. Public sources and licence
decisions are listed in the README's Sources and attribution / Licence exclusions.
All downloaded material was read as untrusted text; none was executed or copied
into the implementation. No private samples, extracted resources or dumps are committed.

## Decision

- **Gold storage:** expose original, read-only **Inferred** contribution sums for
  three unambiguously matched storage candidates. A fourth candidate has no explicit
  stack contribution and stays **Unknown**; do not assign it quantity one. No player
  balance or gold writer is added.
- **Character base abilities:** negative result for an authoritative character join.
  Two character-creation arrays are point-buy-shaped, but an owner mismatch prevents
  naming either as a particular party character's base scores. Keep them in raw
  inspection and document the candidates below. Existing effective-cache controls
  are not base-score/respec controls.

## Public research

Every source below was accessed **2026-10-29**. Repository revisions are pinned
where available. These are research leads, not proof that a PC layout applies to PS5.

| Source | Relevant finding and limit |
| --- | --- |
| Norbyte, [LSFCommon.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSF/LSFCommon.cs), [LSFReader.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/Resources/LSF/LSFReader.cs), [licence](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LICENSE) | MIT reference for LSOF sections, compression and typed attributes. ScratchBuffer preserves opaque content; converting LSF to XML does not itself decode NewAge. Existing plugin codecs/writers are reused. |
| LennardF1989, Norbyte and contributors, [LSLib issue #127](https://github.com/Norbyte/lslib/issues/127) | Explains NewAge's component-oriented serialization and names relevant character-creation, stats and stack families. The discussion does not provide a universal tagged `Abilities` array or a safe gold transaction. No issue-attached template/code reused. |
| Hawk-on, [FIELD_MAPPING.md at 6a62466c](https://github.com/Hawk-on/bg3-save-editor/blob/6a62466c77ad4939fc5e6fdcbc2e4188efdfcd15/FIELD_MAPPING.md), [MIT licence](https://github.com/Hawk-on/bg3-save-editor/blob/6a62466c77ad4939fc5e6fdcbc2e4188efdfcd15/LICENSE) | Distinguishes template ItemList Amounts from runtime inventory gold in NewAge. Its character ability location is explicitly tentative. Neither is a PS5 ownership proof. |
| DanFinchUK, [FORMAT.md at 9578ff7c](https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/FORMAT.md) | Documents indexed StackEntry contributions, effective stats, repeated EntityId GUIDs and owner/subsystem caveats. PC packed-stream prefixes differ from the accepted PS5 profile. Independently rechecked storage facts; no code reused. No licence published; reused with attribution per owner decision 2026-10-28. |
| Serofix-code, [editor README at e6213431](https://github.com/Serofix-code/bg3-save-editor/tree/e62134311f1ec737472448e4bfc9dda72afd4f3e) | Distinguishes extracted-stack editing from in-game Script Extender gold/ability commands. Command generation does not prove a packed-save dependency writer. No code reused. |
| Norbyte, [Stats.h](https://github.com/Norbyte/bg3se/blob/60fb4caf99c08de604abac26e07af13a2e0a028d/BG3Extender/GameDefinitions/Components/Stats.h), [CharacterCreation.h](https://github.com/Norbyte/bg3se/blob/60fb4caf99c08de604abac26e07af13a2e0a028d/BG3Extender/GameDefinitions/Components/CharacterCreation.h), [Inventory.h](https://github.com/Norbyte/bg3se/blob/60fb4caf99c08de604abac26e07af13a2e0a028d/BG3Extender/GameDefinitions/Components/Inventory.h), [licence](https://github.com/Norbyte/bg3se/blob/60fb4caf99c08de604abac26e07af13a2e0a028d/LICENSE) | Live-memory definitions distinguish abilities, modifiers/skills, creation arrays, stack quantities and inventory weight. Memory structures are not disk offsets. MIT plus Commons Clause v1.0; code reuse refused, facts only. |
| bg3.wiki contributors, [Gold](https://bg3.wiki/wiki/Gold), [Abilities](https://bg3.wiki/wiki/Abilities), [copyright policy](https://bg3.wiki/wiki/bg3wiki:Copyrights) | Factual game rules: gold is a weighted item; creation point buy is distinct from final scores, bonuses and feats. No wiki prose or dataset copied. Legacy NC prose remains excluded. |
| u/Xx360rapidz and respondents, [modded save transfer to PS5](https://www.reddit.com/r/BaldursGate3/comments/16ax5k8/), September 2023–February 2024 discussion | Conflicting firsthand reports about which PC modifications survive a native save/load and cross-save. These describe live-game/mod workflows, not offsets or successful PS5 packed-save editing. No code, cheat tables or prose reused. |

The consulted sources do not establish a single serialized party-gold attribute.
This is a bounded negative finding, not a claim that no such state exists in any build.

## Corpus and differential limits

The external artifacts path contains one BG3 archive, `PPSA18463_saves_dec.zip`.
`fixtures.json` pins its identity without embedding it. It contains generation 0
only: one `AutoSave_0.lsv` and its byte-identical backup, plus five claimed profile
resources with identical backups. Thus all **12 claimed files** are covered, but
there is **one independent gameplay state**, no gold-gain/spend, respec, equipment
toggle or no-action control pair. Backup equality yields zero changed bytes and
cannot prove gameplay authority or recalculation.

Both gameplay copies were decoded through the plugin's LSPK and LSF readers:

| Resource, in each copy | Gold-like Item Stats | Ability evidence / limitation |
| --- | ---: | --- |
| `meta.lsf` | 0 | No tagged Abilities/BaseAbilities or gold balance attribute. |
| `Globals.lsf` | 4 | Accepted NewAge profile: 256 Stats rows, 2 creation-stats rows, 4 level-up ability arrays. |
| `LevelCache/TUT_Avernus_C.lsf` | 7 | LSF decodes; its nested NewAge is rejected by the existing `Unaligned LSMF directory` guard. Inner candidates remain unanalysed, not absent. |
| `LevelCache/SYS_CC_I.lsf` | 0 | NewAge decodes; the two creation/allocation component families above are absent. |

All eleven gold-like Item occurrences lack direct Amount/Quantity/StackAmount/Owner
attributes. No `Abilities`, `BaseAbilities`, `Gold` or `PartyGold` tagged attribute
exists in those eight decoded LSF copies. Profile/configuration files are not
used as gameplay state; their existing parser/no-op tests cover every copy.

The tutorial level's unsupported NewAge is a concrete remaining parser coverage
gap. It prevents a complete cross-resource ECS census. No alignment guard was
relaxed, blob normalized or guessed level offset installed for this task.

## Gold: representation and joins

The accepted Globals profile stores positive quantities through:

1. NewStackComponent's arena-relative pointer to a 32-byte Stack record.
2. Stack's member EntityId-reference range and StackEntry range.
3. Each 8-byte StackEntry: u16 member index, preserved u16 padding, positive u32
   contribution at +4. Sum all contributions for that member; do not read the
   container slot's +8 word as an amount.

The previous gold classifier inverted TemplateID → Entity only for unique templates.
Four Creators share a gold template here, so that test discarded all candidates.
The new classification does not invert that relationship. A Creator Entity is
eligible only when its TemplateID is unambiguous and **every** Item occurrence for
that template has gold Stats. This identifies the gold category without claiming
which of those four Item nodes represents the particular instance.

Preserve all repeated EntityId occurrences of each Creator GUID, then independently
require one current container entry and one explicit stack member for that GUID.
Do not choose an EntityId row by ordinal or spatial proximity. This is a persistent
GUID storage join; it does not prove that different subsystem epochs are current.

| Candidate storage, same in both copies | EntityId occurrences | Container row | Stack entity row | Explicit contribution sum | Party body |
| --- | ---: | ---: | ---: | ---: | --- |
| Coin/pile category A | 7 | 143 | none | Unknown | Unknown |
| Coin/pile category B | 7 | 244 | 4690 | 4 | Unknown |
| Coin/pile category C | 7 | 331 | 4691 | 2 | Unknown |
| Coin/pile category D | 7 | 85 | 4698 | 2 | Unknown |

The unique slot references use different occurrences (2177, 2302, 2403, 2108)
from the stack references; exact row equality was another missing join. The current
inventory graph reaches two entries from party roots, and none of these gold
candidates is among them. World/trader ownership remains possible, not established.
**4 + 2 + 2 is not a player or party balance.** Missing StackEntry data is not
evidence for a singleton coin or a zero balance.

The existing F8 gold table now exposes those three **original imported** quantities
as read-only Inferred fields, with occurrence provenance, unknown owners and explicit
snapshot wording. They deliberately do not track later raw storage edits. Missing
or duplicate joins remain Unknown; conflicting Creator/template meanings are excluded.
No balance aggregation, quantity distribution, stack creation/removal or new setter
is installed. Existing generic experimental stack controls retain their prior scope.

## Base abilities: negative result and candidates

`game.stats.v3.StatsComponent` in accepted Globals has 36-byte rows with six i32
effective/cache candidates at +8 in STR/DEX/CON/INT/WIS/CHA order. The existing party
bridge selects stats rows 2, 5 and 3. These are not proof of base scores.

The promising separate storage is
`game.character_creation.v1.CharacterCreationStatsComponent`: two 88-byte rows.
Each has an arena-relative [begin,end) range at **+72/+80**, spanning seven i32s:

| Creation row | Stored array | Hypothesis: omit sentinel, add 8 | Point-buy cost | Raw owner entity |
| --- | --- | --- | ---: | ---: |
| 0 | 0, 0, 5, 6, 4, 2, 7 | 8, 13, 14, 12, 10, 15 | 27 | 246 |
| 1 | 0, 7, 5, 6, 2, 4, 0 | 15, 13, 14, 10, 12, 8 | 27 | 247 |

The hypothetical six base values fit the 8–15 point-buy range. Adding CON +1 / CHA +2
to the first reproduces stats row 2: 8,13,15,12,10,17. Adding STR +2 / CON +1 to
the second reproduces row 5: 17,13,15,10,12,8. These arithmetic matches are **Inferred
relationships only**, not a measured respec or a proven identity bridge.

The contradiction is specific: creation row 1's owner is entity **247**, which the
existing party bridge associates with stats row **3**, containing 10,10,3,2,1,1.
The numerical match at stats row 5 belongs to entity **249**. Creation UUIDs also do
not match the party Creator TemplateIDs. Do not repair this by matching row order,
class, name or whichever effective array looks plausible.

`game.character_creation.v0.LevelUpComponentAbilities` is another candidate:
four 16-byte ranges, each containing seven **zero** i32s. Those are not six usable
base scores. Sparse progression level-up pointers and selector tables may record
choices, but their owner/epoch bridge and the authoritative base-allocation writer
remain unresolved. No new named base-score field or respec setter is exposed.

## Dependencies and the write gate

The factual point-buy rules are narrower than a generic “1–20” constraint: creation
buys 8–15 scores with 27 points, costs rise above 13, and +2/+1 bonuses are separate.
Feats, permanent boosts, equipment overrides and transformed forms alter final scores;
20 is not a universal final-score cap. An effective array cannot simply be inverted.
Ability modifiers use floor((score−10)/2); skills, saves, attack/spell calculations,
AC/initiative, maximum HP and carrying capacity may depend on the result. No PS5
stored modifier/cache writer, invalidation path or respec transaction is established.

Gold also contributes carried weight (the cited item rule is 0.001 kg per coin).
The Globals directory has no named Weight component; absence from this profile is
not proof that the game recomputes it or that no other resource/cache participates.
The internal weight unit, rounding, nested-container propagation, magic-pocket/trade
distribution and relevant cached totals have not been validated.

Existing no-op compression/serialization is proven, including both gameplay copies,
all profile twins and the rebuilt LSF resources. That is necessary preservation
evidence, not sufficient gameplay evidence for either writer. The requested
Experimental + preview + undo write gate is therefore not met; new fields stay read-only.

To unblock: obtain a native no-action control; then exact +1 pickup/spend/trade and
split/merge gold pairs with visible individual/party gold and weight. Capture one
base respec choice separately from its +2/+1 bonus, feat and equipment toggles,
recording base/effective sheets and modifiers for each character. Resolve the
creation-owner mismatch and tutorial NewAge layout; inspect resave/reload and all
affected caches before implementing a bounded coupled transaction.

## Validation

Baseline: 377 passed, 0 skipped. Added tests were written before implementing the
gold classifier/view. New coverage pins both gameplay copies, explicit positive and
missing contributions, conflicting template/Creator meanings, repeated GUIDs,
ambiguous slots/stacks, read-only field rejection, point-buy candidates and the
contradictory owner, and the unsupported tutorial blob. A handwritten compressed
synthetic LSF exercises split GUID occurrences and multi-contribution members.
Existing allocation test files and limits are unchanged. The attributed whole-view
fingerprints are intentionally refreshed for the four newly visible gold candidates.

Final counts and exact reproduction commands are in [README.md](README.md#validation).

## Suggested central documentation changes (not applied)

`docs/EDIT_COVERAGE.md`, BG07:

> R — Inferred gold storage: four Creator GUID categories, three explicit imported
> contribution sums (4,2,2), no proven party owner; fourth amount Unknown. Base
> template classification and cross-occurrence stack/container joins are read-only.
> Player balance, weight and spend/trade dependencies still need controlled captures.

BG09 (retain the separate appearance gap):

> N for authoritative base-score respec / appearance. Two creation point-buy-shaped
> arrays located (+72/+80 ranges in 88-byte rows); one raw owner contradicts the
> numerical effective-score match. Effective caches remain BG03; identity, bonuses,
> modifiers/invalidation and respec transaction unproven.

Keep live gold/base-score/spell **edits** outside the shortlist. If BG07 is moved
from N to R under the document's candidate-inclusive definition, reconcile its BG3
and overall totals; it does not make a player-balance writer ready.

The new Commons Clause code-reuse exclusion in README also needs a maintainer row
in `docs/LICENCE_EXCLUSIONS.md`. Both central files are outside this task's allowed scope.

The current documentation command reports **27 passed, one failed**:
`test_readme_licence_exclusions_match_register_in_both_directions` detects exactly
that missing mirror. Insert the following under `## README exclusion decisions`
and reconcile the register's summary counts (BG3 README exclusions 3, Still refused
2, total 4; aggregate README exclusions 65, Still refused 6, total 91):

| Plugin | Source / revision | Licence found | Status | Disposition | Alternative | Unblock |
| --- | --- | --- | --- | --- | --- | --- |
| BaldursGate3 | [Norbyte/bg3se, 60fb4caf99c08de604abac26e07af13a2e0a028d](https://github.com/Norbyte/bg3se/tree/60fb4caf99c08de604abac26e07af13a2e0a028d), accessed 2026-10-29 | [MIT License + “Commons Clause” License Condition v1.0](https://github.com/Norbyte/bg3se/blob/60fb4caf99c08de604abac26e07af13a2e0a028d/LICENSE) | Still refused | No-sale restriction; component-definition code reuse excluded under rule 9's commercial-use restriction policy. This is not plain MIT. | Read for facts only; independently measured PS5 storage and used existing plugin primitives. No bg3se code or implementation copied, ported or executed. | Compatible source-specific grant or a revision without the restrictive condition. Central-register row is proposed in FIELD_DISCOVERY.md; docs/ is outside this task's edit scope. |

These counts are a suggested update against the inspected register, not permission
to overwrite another task's later additions.

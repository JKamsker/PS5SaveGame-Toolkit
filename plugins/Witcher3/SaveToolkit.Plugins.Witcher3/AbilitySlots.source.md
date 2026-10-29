# Ability slot presentation evidence

Researched 2026-10-29. No downloaded source was executed or copied into the plugin.

- [cahian converter, `skills.py` at 7ae8b6f](https://github.com/cahian/witcher3-remastered-to-nextgen-save-converter/blob/7ae8b6fe71c4b36e571246ad25629d03c7ec8219/w3save/skills.py),
  cahian, MIT ([notice](../SaveConverter-LICENSE.txt)). The bounded tagged-structure reader retains
  property-free records and distinguishes them from named zero-rank/core skill definitions.
- [PlayerAbilityManager at a85e52c](https://github.com/Lknechtli/BetterCombatEvolved/blob/a85e52c3e3841863999b45afc2a0fd1f284a6bc0/Witcher3/content/scripts/game/gameplay/ability/PlayerAbilityManager.ws),
  CD PROJEKT RED; mirrored/modified by Lknechtli and contributors. Rights reserved by CD PROJEKT RED;
  used with attribution per owner decision 2026-10-29. Research only: `UnequipSkill` clears
  `socketedSkill` to `S_SUndefined`; equip assigns a skill. Mutagen equip assigns `item`, and
  unequip assigns an invalid item ID. This is older/modded code, not PS5 version proof.

Independent PS5-byte verification covers four primary saves and their four identical backup twins:
each has six property-free skill records at native positions 0, 29, 57, 78, 79 and 82; 16 skill
sockets with saved ID/group/unlock metadata but no `socketedSkill`; and four mutagen sockets with
equipment/group/unlock metadata but no `item`. These absent occupancy fields are inferred default
empty sockets. Nonempty unknown records, unknown socket properties and present mutagen `item`
structures are conservatively retained as visible. Explicit `S_SUndefined` recognition has
synthetic coverage only. No arbitrary zero quantities, ranks or IDs are treated as empty.

Only presentation flags were added. Existing native/positional keys, properties, byte buffers and
serialization remain intact. Synthetic complete-model paging tests and sample query/edit/history
tests verify filtering before paging, hidden counts, F6 scope resolution and unchanged slot identity.

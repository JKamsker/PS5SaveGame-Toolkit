# Character progression data attribution

Author/project: RyudoSynbios / Game Tools Collection, copyright 2024 RyudoSynbios.
Licence: [MIT](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/LICENSE.md).
The full notice is retained in the plugin's `THIRD-PARTY-NOTICES.txt`.
Retrieved 2026-10-28, pinned revision `c7bdd6d8228304b87dcdf15e57155d092eb06fd2`.

- [Remake resource.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/utils/resource.ts)
- [Rebirth resource.ts](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils/resource.ts)
- Source update semantics: each game's adjacent `utils.ts` and `template.ts` at that revision.

`character-curves.json` extracts only the public `characterLevels` integer arrays. Remake's seven
arrays are prefixed with an explicit level-one zero and truncated to the template's 50-level bound.
Yuffie/Sonon have another source zero at level two; selecting a level uses its minimum XP and
selecting XP uses the highest reached tier. Level-one zero for these two cannot be newly selected
as a coherent tier; exact original tuples remain restorable. Rebirth's main-party array is repeated
for the seven displayed records; guest/past curves and additional records are excluded.

The existing Rebirth weapon names at this revision group seven types per main character:
1001–1007, 1008–1014, 1015–1021, 1022–1028, 1029–1035, 1036–1042, 1043–1049.
This grouping is **Inferred**, anchored to the original owned weapon's family, with guest types
1050/1051 excluded. It is not a verified socket-capacity or gameplay-legality table.

No game artwork, private sample bytes, GPL implementation or unlicensed data is included.

"""Extract unshifted crafting/minigame bit candidates from the pinned MIT template.

Run: python -I build_research_data.py <scratch/rebirth-template.ts>
Does not evaluate TypeScript, dynamic loops, offsets, placeholder aliases or game scripts.
"""

from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import sys

content = Path(sys.argv[1]).read_bytes()
if hashlib.sha256(content).hexdigest() != "9ca05dfc4ff2093f705640b2c22a3236ed4acb9dc99c2ee2541712e15fcae32c":
    raise ValueError("Expected the pinned, unmodified upstream template")
source = ("https://github.com/RyudoSynbios/game-tools-collection/blob/"
          "c7bdd6d8228304b87dcdf15e57155d092eb06fd2/"
          "src/lib/templates/final-fantasy-vii-rebirth/saveEditor/template.ts")
names = {}
rows = []
for line in content.decode("utf-8").splitlines():
    indent = len(line) - len(line.lstrip())
    name = re.match(r'\s*name: "([^"]+)",', line)
    if name:
        names = {depth: value for depth, value in names.items() if depth < indent}
        names[indent] = name[1]
    match = re.search(r'\{ offset: (0x[\da-f]+), bit: (\d+), label: "([^"]+)"', line)
    if not match or "hidden: true" in line or "disabled: true" in line:
        continue
    path = list(names.values())
    module = next((value for value in ["Item Transmuter", "Mini-Games"] if value in path), None)
    # Queen's Blood opponents already live in the existing progress collection; do not duplicate aliases.
    if module is None or "Queen's Blood" in path:
        continue
    offset, bit = int(match[1], 16), int(match[2])
    rows.append(dict(id=f"research/{offset:x}/{bit}", name=match[3], region=module,
                     category=" / ".join(path[1:]), offset=offset, bit=bit, source=source))

counts = Counter((row["offset"], row["bit"]) for row in rows)
rows = [row for row in rows if counts[row["offset"], row["bit"]] == 1]
if {row["region"] for row in rows} != {"Item Transmuter", "Mini-Games"}:
    raise ValueError("Expected separately mapped crafting and minigame groups")
target = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.FinalFantasy7/Resources/rebirth-research.json"
target.write_text(json.dumps(rows, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
print(dict(Counter(row["region"] for row in rows)))

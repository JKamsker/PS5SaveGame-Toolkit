"""Extract literal Rebirth folio candidates from pinned source text; never execute it.

Usage: python -I build_folio_data.py <scratch/rebirth-template.ts>
The upstream overrideShift removes the character container shift for abilities.
No prerequisite, cost, layout coordinate or game-effect description is inferred.
"""

from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import sys

REVISION = "c7bdd6d8228304b87dcdf15e57155d092eb06fd2"
EXPECTED_SHA256 = "9ca05dfc4ff2093f705640b2c22a3236ed4acb9dc99c2ee2541712e15fcae32c"
source_path = Path(sys.argv[1])
content = source_path.read_bytes()
if hashlib.sha256(content).hexdigest() != EXPECTED_SHA256:
    raise ValueError("Expected the pinned, unmodified upstream template")

source = ("https://github.com/RyudoSynbios/game-tools-collection/blob/"
          f"{REVISION}/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/template.ts")
rows = []
character = None
category = None
for line in content.decode("utf-8").splitlines():
    section = re.search(r'id: "abilities-(\d+)-%index%"', line)
    if section:
        character = int(section[1])
    if 'name: "Materia"' in line and character is not None:
        break
    name = re.search(r'name: "([^"]+)"', line)
    if name:
        category = name[1]
    flag = re.search(r'\{ offset: (0x[\da-f]+), bit: (\d+), label: "([^"]+)"', line)
    if character is None or not flag or "hidden: true" in line:
        continue
    offset, bit = int(flag[1], 16), int(flag[2])
    rows.append(dict(id=f"folio/{offset:x}/{bit}", name=flag[3], region=str(character),
                     category=category, offset=offset, bit=bit, source=source))

counts = Counter((row["offset"], row["bit"]) for row in rows)
rows = [row for row in rows if counts[row["offset"], row["bit"]] == 1]
if {row["region"] for row in rows} != set(map(str, range(7))):
    raise ValueError("Expected seven separately mapped folio groups")
target = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.FinalFantasy7/Resources/rebirth-folios.json"
target.write_text(json.dumps(rows, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Extracted {len(rows)} distinct candidate bits")

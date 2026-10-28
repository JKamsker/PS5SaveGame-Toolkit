"""Extract enemy index/name facts from pinned MIT resource text without executing it.

Usage: python -I build_enemy_data.py <scratch folder containing GAME-utils-resource.ts>
Display numbers remain source labels; record offsets use the independent numeric index.
"""

from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.FinalFantasy7/Resources"
for game, expected in [
    ("remake", "f1209a4913eba248ab545a7fd2c5d7ab6feaa861ede7ef0447c307f6cde051bf"),
    ("rebirth", "28cda367f9c1f3243af28791ae7b1e0db08ec035e549dafbcaf637828d63f761"),
]:
    content = (Path(sys.argv[1]) / f"{game}-utils-resource.ts").read_bytes()
    if hashlib.sha256(content).hexdigest() != expected:
        raise ValueError(f"Expected pinned unmodified {game} resource.ts")
    section = re.search(r"export const enemies = \[(.*?)\n\];", content.decode("utf-8"), re.S)[1]
    category = game.title()
    rows = []
    for line in section.splitlines():
        group = re.match(r'\s*name: "([^"]+)",', line)
        if group:
            category = group[1]
        row = re.search(r'\{ index: (0x[\da-f]+), name: "([^"]+)" \}', line)
        if row:
            rows.append(dict(index=int(row[1], 16), name=row[2], category=category))
    counts = Counter(row["index"] for row in rows)
    if not rows or any(value != 1 for value in counts.values()):
        raise ValueError("Enemy indices must be present and unambiguous")
    (root / f"{game}-enemies.json").write_text(json.dumps(rows, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(game, len(rows))

"""Import public CSV facts as data, never source code. No private-save input.

Usage: python -B import_gear_table.py D:/scratch/gear.csv
The pinned SHA-256 rejects changed exports; review a new revision before updating it.
"""

import csv
import hashlib
import io
import json
from pathlib import Path
import re
import sys

SOURCE = "https://docs.google.com/spreadsheets/d/17JWS2EyV68tRAajJIungRvhL5mmePIyGUNqekWu1lBc/edit"
SOURCE_HASH = "cdac528fb32f7f93cc27e7d0cdde0e7f205cba4d55b9bfaf63e2689e90343133"
LICENSE = "No licence published; reused with attribution per owner decision 2026-10-28"
PACK_ID = "gow-ragnarok.gear.en"
CATALOG_ID = "gow-ragnarok-gear"


def generate(path: Path) -> None:
    data = path.read_bytes()
    if hashlib.sha256(data).hexdigest() != SOURCE_HASH:
        raise ValueError("Public gear CSV revision differs from the reviewed export")
    groups: dict[str, list[tuple[int, list[str]]]] = {}
    for number, row in enumerate(csv.reader(io.StringIO(data.decode("utf-8-sig"))), 1):
        if len(row) >= 6 and re.fullmatch(r"[0-9a-fA-F]{1,16}", row[1]):
            key = int(row[1], 16).to_bytes(8, "little").hex().upper()
            groups.setdefault(key, []).append((number, [cell.strip() for cell in row[2:6]]))
    if len(groups) != 2092:
        raise ValueError("Unexpected public gear ID count")
    records = []
    for key, rows in groups.items():
        names = list(dict.fromkeys(cells[0] for _, cells in rows if cells[0]))
        categories = list(dict.fromkeys(cells[1] for _, cells in rows if cells[1]))
        levels = set(cells[2] for _, cells in rows if cells[2])
        # Disagreements stay visible; do not choose a preferred name or legal range.
        name = " / ".join(names) or "Unknown gear ID " + key
        known = bool(names) and all(n.lower() not in ("-", "unnamed", "broken")
                                    and "not sure" not in n.lower() for n in names)
        level = next(iter(levels)) if len(levels) == 1 else "Unknown"
        if not re.fullmatch(r"(?:[1-9]|10|9\.[123])", level):
            level = "Unknown"
        records.append({
            "id": key, "name": name, "category": " / ".join(categories) or "Unknown",
            "description": "PC gear definition candidate; PS5 identity and catalog level inferred. "
                           "Catalog level is not a decoded upgrade word or a legal maximum. "
                           "Ownership, equip state and dependency repair remain unverified.",
            "source": SOURCE,
            "attributes": {"level": level, "confidence": "Inferred" if known else "Unknown",
                           "sourceRows": ",".join(str(n) for n, _ in rows)},
        })
    folder = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.GodOfWarRagnarok" / "ReferencePacks" / PACK_ID
    folder.mkdir(parents=True, exist_ok=True)
    records_bytes = (json.dumps({"records": records}, ensure_ascii=False, separators=(",", ":")) + "\n").encode()
    (folder / "gear.json").write_bytes(records_bytes)
    manifest = {
        "schema": 1, "id": PACK_ID, "version": "1.0.0",
        "title": "PC gear definitions and catalog levels (PS5 inferred)",
        "plugins": ["gow-ragnarok"], "titleIds": ["PPSA08330"],
        "gameBuilds": ["PS5 revision 0x00513040"], "locale": "en",
        "datasets": [{"id": CATALOG_ID, "kind": "records", "title": "Gear definition candidates",
                      "files": [{"path": "gear.json", "sha256": hashlib.sha256(records_bytes).hexdigest()}],
                      "attribution": {"sourceUrl": SOURCE,
                                      "author": "cfemen / Fixer / Sasha / Vinny / community gear-sheet contributors",
                                      "license": LICENSE, "licenseUrl": SOURCE, "retrieved": "2026-10-29",
                                      "modifications": "Extracted hash/name/type/level facts; normalized numeric hashes to literal LE keys; "
                                                       "merged duplicates with visible name/type disagreements; missing or conflicting levels stay Unknown. "
                                                       "No code, notes, presets, assets or legal maxima imported."}}],
    }
    (folder / "pack.json").write_bytes((json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode())


if __name__ == "__main__":
    generate(Path(sys.argv[1]))

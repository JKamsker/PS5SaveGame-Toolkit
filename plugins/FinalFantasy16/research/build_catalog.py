"""Rebuild the attributed F2 pack offline from its reviewed, licensed TSV extract.

Run with python -I plugins/FinalFantasy16/research/build_catalog.py.
No downloads, game files, or third-party code are executed.
"""
import csv
import hashlib
import json
from pathlib import Path

PACK = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.wiki.en"


def write_json(path, value):
    content = (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    path.write_bytes(content)
    return hashlib.sha256(content).hexdigest()


def main():
    with (PACK / "items.source.tsv").open(encoding="utf-8", newline="") as source:
        rows = list(csv.DictReader(source, delimiter="\t"))
    seen = set()
    records = []
    for row in sorted(rows, key=lambda row: int(row["id"])):
        key = row["id"]
        if key in seen or str(int(key)) != key or int(key) <= 0:
            raise ValueError(f"Invalid or duplicate item id: {key}")
        seen.add(key)
        if not row["name"].strip() or "<" in row["name"] or not row["source"].startswith("https://"):
            raise ValueError(f"Invalid plain-text name/source: {key}")
        records.append({"id": key, "name": row["name"], "category": row["category"], "source": row["source"]})
    digest = write_json(PACK / "items.json", {"records": records})
    write_json(PACK / "pack.json", {
        "schema": 1, "id": "ffxvi.wiki.en", "version": "1.0.0",
        "title": "FFXVI English wiki labels (partial, CC BY-SA 3.0)",
        "plugins": ["ffxvi"], "titleIds": ["PPSA10665", "PPSA13803"],
        "gameBuilds": [], "locale": "en",
        "description": "Partial name/category crosswalk; no gameplay caps, icons or availability claims.",
        "datasets": [{
            "id": "ffxvi_items", "kind": "records", "title": "English item and equipment labels",
            "files": [{"path": "items.json", "sha256": digest}],
            "attribution": {
                "sourceUrl": "https://finalfantasy.fandom.com/wiki/Final_Fantasy_XVI_translations?oldid=4007473",
                "author": "Final Fantasy Wiki contributors; PS5 Save Game Toolkit contributors",
                "license": "CC-BY-SA-3.0", "licenseUrl": "https://creativecommons.org/licenses/by-sa/3.0/",
                "retrieved": "2026-10-28",
                "modifications": "Extracted English names and categories, joined factual numeric identifiers, omitted unmatched labels, prose, art and limits. Adaptation remains CC BY-SA 3.0."
            }
        }]
    })
    print(f"Wrote {len(records)} records; SHA-256 {digest}")


if __name__ == "__main__":
    main()

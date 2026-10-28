"""Extract MIT-licensed Nex layout annotations; read text only, never execute it.

python -I plugins/FinalFantasy16/research/build_layout_catalog.py <scratch>/ffxvi-nex-layouts
Use revision d3ad0627a29af168916ad574dadbca6759e2ac48. No game files are required.
"""
import argparse
import hashlib
import json
from pathlib import Path

REVISION = "d3ad0627a29af168916ad574dadbca6759e2ac48"
SOURCE = f"https://github.com/Nenkai/ffxvi-nex-layouts/blob/{REVISION}/"
PACK = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.nex.en"


def write_json(name, data):
    content = (json.dumps(data, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    (PACK / name).write_bytes(content)
    return hashlib.sha256(content).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("layouts", type=Path)
    args = parser.parse_args()
    PACK.mkdir(parents=True, exist_ok=True)
    datasets = []
    for filename, catalog, category in [("GameMap.layout", "ffxvi_maps", "map"),
                                         ("SummonMode.layout", "ffxvi_summons", "summon")]:
        rows = {}
        for line in (args.layouts / filename).read_text(encoding="utf-8-sig").splitlines():
            if not line.startswith("set_comment|"):
                continue
            _, key, subkey, _, label = line.split("|", 4)
            row = rows.setdefault(key, {"id": key, "name": label.strip(), "category": category,
                                        "source": SOURCE + filename, "attributes": {}})
            if subkey:
                row["attributes"][f"subrow_{subkey}"] = label.strip()
        if catalog == "ffxvi_maps":
            # Explicitly documented above the GameMap columns, rather than in set_comment.
            rows["602000"] = {"id": "602000", "name": "Hideaway 2", "category": category,
                              "source": SOURCE + filename, "attributes": {
                                  "variantNote": "Subrow 1 selected for user situation 15020 (pink sky). Other subrows are not enumerated by this extract."}}
        records = sorted(rows.values(), key=lambda row: int(row["id"]))
        digest = write_json(f"{catalog}.json", {"records": records})
        datasets.append({"id": catalog, "kind": "records", "title": f"Nex {category} research annotations",
                         "files": [{"path": f"{catalog}.json", "sha256": digest}],
                         "attribution": {"sourceUrl": SOURCE + filename, "author": "Nenkai and ffxvi-nex-layouts contributors",
                                         "license": "MIT", "licenseUrl": SOURCE + "LICENSE", "retrieved": "2026-10-28",
                                         "modifications": "Extracted set_comment identifiers and annotations; grouped subrows without inventing asset paths; added documented Hideaway variant note. Research labels remain Inferred."}})
        print(f"{catalog}: {len(records)} records, {digest}")
    write_json("pack.json", {"schema": 1, "id": "ffxvi.nex.en", "version": "1.0.0",
                            "title": "FFXVI Nex map/summon annotations (MIT, Inferred)", "plugins": ["ffxvi"],
                            "titleIds": ["PPSA10665", "PPSA13803"], "gameBuilds": [], "locale": "en",
                            "datasets": datasets})


if __name__ == "__main__":
    main()

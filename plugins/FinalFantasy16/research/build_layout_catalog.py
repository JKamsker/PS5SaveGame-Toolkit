"""Extract MIT-licensed Nex layout annotations; read text only, never execute it.

python -I plugins/FinalFantasy16/research/build_layout_catalog.py <scratch>/ffxvi-nex-layouts
Use revision d3ad0627a29af168916ad574dadbca6759e2ac48. No game files are required.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

REVISION = "d3ad0627a29af168916ad574dadbca6759e2ac48"
SOURCE = f"https://github.com/Nenkai/ffxvi-nex-layouts/blob/{REVISION}/"
PACK = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.nex.en"
LAYOUT_HASHES = {
    "GameMap.layout": "3cea6f79ed8c213b1a3a0f9004b449f51f84e1e7f6b27c9779a913d102d4890b",
    "SummonMode.layout": "cbfe507a1ffb95c45a6111c87142289857823ddcad741360056a6f014652f8f8",
    "Skill.layout": "2f7a37651c0a41920b38a2628a8cf3b9aabe313bad5aac19d4682fcd6dcd6668",
}


def write_json(name, data):
    content = (json.dumps(data, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    (PACK / name).write_bytes(content)
    return hashlib.sha256(content).hexdigest()


def validate_layouts(layouts):
    for filename, expected in LAYOUT_HASHES.items():
        content = (layouts / filename).read_bytes().replace(b"\r\n", b"\n")
        if hashlib.sha256(content).hexdigest() != expected:
            raise ValueError(f"{filename} does not match reviewed revision {REVISION}")


def annotation_records(layouts, filename, catalog, category):
    rows = {}
    for line in (layouts / filename).read_text(encoding="utf-8-sig").splitlines():
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
    return sorted(rows.values(), key=lambda row: int(row["id"]))


def write_annotation_dataset(layouts, filename, catalog, category):
    records = annotation_records(layouts, filename, catalog, category)
    digest = write_json(f"{catalog}.json", {"records": records})
    dataset = {"id": catalog, "kind": "records", "title": f"Nex {category} research annotations",
               "files": [{"path": f"{catalog}.json", "sha256": digest}],
               "attribution": {"sourceUrl": SOURCE + filename, "author": "Nenkai and ffxvi-nex-layouts contributors",
                               "license": "MIT", "licenseUrl": SOURCE + "LICENSE", "retrieved": "2026-10-28",
                               "modifications": "Extracted set_comment identifiers and annotations; grouped subrows without inventing asset paths; added documented Hideaway variant note. Research labels remain Inferred."}}
    print(f"{catalog}: {len(records)} records, {digest}")
    return dataset


def skill_records(layouts):
    # These are Nex skill IDs, not demonstrated indices into a PS5 rank array.
    skill_source = (layouts / "Skill.layout").read_text(encoding="utf-8-sig")
    skills = [{"id": key, "name": name.strip(), "category": "skill-research", "source": SOURCE + "Skill.layout",
               "description": "Explicit upstream skill-ID annotation. No association to a saved rank byte, flag bit or AP cost has been established.",
               "attributes": {"confidence": "Inferred", "saveIndexMapping": "Unknown"}}
              for name, key in re.findall(r"^// - (.+?) \(skill (\d+)\)", skill_source, re.MULTILINE)]
    if len(skills) != 3 or len({row["id"] for row in skills}) != len(skills):
        raise ValueError("Pinned Skill.layout must contain exactly three distinct named skill annotations")
    return skills


def write_skill_dataset(layouts):
    skills = skill_records(layouts)
    digest = write_json("ffxvi_skills.json", {"records": skills})
    return {"id": "ffxvi_skills", "kind": "records", "title": "Nex skill-ID research (not save-rank indices)",
            "files": [{"path": "ffxvi_skills.json", "sha256": digest}],
            "attribution": {"sourceUrl": SOURCE + "Skill.layout", "author": "Nenkai and ffxvi-nex-layouts contributors",
                            "license": "MIT", "licenseUrl": SOURCE + "LICENSE", "retrieved": "2026-10-28",
                            "modifications": "Extracted three explicitly named skill IDs; no save index, rank, cost, prerequisite or Eikon assignment inferred."}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("layouts", type=Path)
    args = parser.parse_args()
    validate_layouts(args.layouts)
    PACK.mkdir(parents=True, exist_ok=True)
    datasets = []
    for filename, catalog, category in [("GameMap.layout", "ffxvi_maps", "map"),
                                       ("SummonMode.layout", "ffxvi_summons", "summon")]:
        datasets.append(write_annotation_dataset(args.layouts, filename, catalog, category))
    datasets.append(write_skill_dataset(args.layouts))
    write_json("pack.json", {"schema": 1, "id": "ffxvi.nex.en", "version": "1.1.0",
                            "title": "FFXVI Nex map/summon/skill annotations (MIT, Inferred)", "plugins": ["ffxvi"],
                            "titleIds": ["PPSA10665", "PPSA13803"], "gameBuilds": [], "locale": "en",
                            "datasets": datasets})


if __name__ == "__main__":
    main()

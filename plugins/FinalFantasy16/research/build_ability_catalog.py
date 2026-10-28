"""Rebuild the CC BY-SA ability reference pack offline from the reviewed TSV.

Keys are plugin reference slugs, never save skill indices or Nex IDs. No downloads.
"""
import csv
import hashlib
import json
from pathlib import Path

PACK = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.abilities.en"


def write_json(path, value):
    content = (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    path.write_bytes(content)
    return hashlib.sha256(content).hexdigest()


def main():
    with (PACK / "abilities.source.tsv").open(encoding="utf-8", newline="") as source:
        rows = list(csv.DictReader(source, delimiter="\t"))
    records = []
    seen = set()
    for row in sorted(rows, key=lambda row: row["id"]):
        key = row["id"]
        if not key or key in seen or not row["name"].strip():
            raise ValueError(f"Missing/duplicate ability identity: {key}")
        seen.add(key)
        stages = row["stages"].split(",")
        if stages[0] != "learn" or len(set(stages)) != len(stages):
            raise ValueError(f"Invalid ability stages: {key}")
        attributes = {"abilityType": row["abilityType"], "stages": row["stages"],
                      "relationSource": row["relationSource"], "costEvidence": row["costEvidence"],
                      "confidence": "Inferred", "saveIndexMapping": "Unknown"}
        for stage in stages:
            if stage not in {"learn", "upgrade", "master"}:
                raise ValueError(f"Unknown stage: {stage}")
            cost = row[stage + "Ap"]
            if cost:
                if not cost.isdecimal() or not 0 <= int(cost) <= 100000:
                    raise ValueError(f"Invalid AP annotation: {key}")
                attributes[stage + "Ap"] = cost
        for url in (row["source"], row["relationSource"]):
            if not url.startswith("https://finalfantasy.fandom.com/wiki/") or "?oldid=" not in url:
                raise ValueError(f"Unpinned source: {key}")
        records.append({"id": key, "name": row["name"], "category": row["category"], "source": row["source"],
                        "description": "Reference progression only. Individual AP costs are not cumulative, "
                        "and are not validated against this save's build, mode or DLC. Availability, saved rank, "
                        "flag/AP accounting and skill-index association remain Unknown. Not validated on console.",
                        "attributes": attributes})
    digest = write_json(PACK / "abilities.json", {"records": records})
    write_json(PACK / "pack.json", {
        "schema": 1, "id": "ffxvi.abilities.en", "version": "1.0.0",
        "title": "FFXVI Eikon ability references (CC BY-SA 3.0)", "plugins": ["ffxvi"],
        "titleIds": ["PPSA10665", "PPSA13803"], "gameBuilds": [], "locale": "en",
        "description": "Base-game and Rising Tide reference names/stages/AP annotations. No save-index joins or art.",
        "datasets": [{"id": "ffxvi_abilities", "kind": "records", "title": "Eikon ability progression references",
                      "files": [{"path": "abilities.json", "sha256": digest}], "attribution": {
                          "sourceUrl": "https://finalfantasy.fandom.com/wiki/Template:Navbox_abilities_FFXVI?oldid=3770527",
                          "author": "Final Fantasy Wiki contributors; PS5 Save Game Toolkit contributors",
                          "license": "CC-BY-SA-3.0", "licenseUrl": "https://creativecommons.org/licenses/by-sa/3.0/",
                          "retrieved": "2026-10-28", "modifications": "Curated names, Eikon groups, progression stages and individual AP facts "
                          "from pinned articles; original slug keys and warnings. No images/prose or save-index mapping. "
                          "Missing AP annotations stay Unknown. Adaptation remains CC BY-SA 3.0."}}]})
    print(f"Wrote {len(records)} ability references; SHA-256 {digest}")


if __name__ == "__main__":
    main()

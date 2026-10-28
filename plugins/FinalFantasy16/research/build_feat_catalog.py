"""Build a reference-only vanilla Eikon/command crosswalk from the reviewed MIT TSV.

python -I plugins/FinalFantasy16/research/build_feat_catalog.py
Upstream code is never run. Do not include the mod's new/replacement command IDs.
"""
import csv
import hashlib
import json
from pathlib import Path

PACK = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.feats.en"
SOURCE = "https://github.com/mThund3R/Logos-Unleashed/blob/6424cb4e60bd31005ef7f0cfb42d4e5360660fc6/"


def write_json(name, data):
    content = (json.dumps(data, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    (PACK / name).write_bytes(content)
    return hashlib.sha256(content).hexdigest()


def main():
    with (PACK / "feats.source.tsv").open(encoding="utf-8", newline="") as source:
        rows = list(csv.DictReader(source, delimiter="\t"))
    commands = {}
    feats = []
    seen = set()
    for row in rows:
        summon, command, name = row["summonId"], row["commandId"], row["name"]
        if summon in seen or not name or not summon.isdecimal() or not command.isdecimal():
            raise ValueError("Invalid/duplicate feat crosswalk")
        seen.add(summon)
        description = "Vanilla restoration path in UndoSpecialAttack; Odin 281 also restored in SwitchOdin. " \
                      "Reference relationship only; no saved rank, AP cost, flag index or PS5 activation is proven."
        record = {"id": command, "name": name, "category": "eikonic-feat", "source": SOURCE + "LogosUnleashed/Mod.cs",
                  "description": description, "attributes": {"confidence": "Inferred"}}
        if command in commands and commands[command]["name"] != name:
            raise ValueError("Conflicting command name")
        commands[command] = record
        feats.append(record | {"id": summon, "attributes": {"commandId": command, "confidence": "Inferred"}})
    datasets = []
    for catalog, records in [("ffxvi_commands", commands.values()), ("ffxvi_feats", feats)]:
        digest = write_json(catalog + ".json", {"records": sorted(records, key=lambda row: int(row["id"]))})
        datasets.append({"id": catalog, "kind": "records", "title": "Vanilla Eikonic feat research crosswalk",
                         "files": [{"path": catalog + ".json", "sha256": digest}],
                         "attribution": {"sourceUrl": SOURCE + "LogosUnleashed/Mod.cs", "author": "mThund3R, Logos-Unleashed",
                                         "license": "MIT", "licenseUrl": SOURCE + "LICENSE", "retrieved": "2026-10-28",
                                         "modifications": "Selected seven vanilla feat command IDs and eight summon-mode associations from restoration code. Omitted custom/modded commands and inactive commented-out Wings of Light. PS5 joins remain Inferred."}})
    write_json("pack.json", {"schema": 1, "id": "ffxvi.feats.en", "version": "1.0.0",
                            "title": "FFXVI Eikonic feat reference research (MIT)", "plugins": ["ffxvi"],
                            "titleIds": ["PPSA10665", "PPSA13803"], "gameBuilds": [], "locale": "en", "datasets": datasets})
    print(f"Wrote {len(commands)} command names and {len(feats)} reference feat associations")


if __name__ == "__main__":
    main()

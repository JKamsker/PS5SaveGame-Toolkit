"""Build attributed reference data from a reviewed MIT RE_RSZ schema and the evidence manifest.

No downloads, no upstream code execution, no save payload reads. Run with python -I.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

SOURCE = "https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/rszpragmata.json"
LICENSE = "https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/LICENSE"


def write(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rsz", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    plugin = root / "SaveToolkit.Plugins.Pragmata"
    pack = plugin / "ReferencePacks/pragmata.evidence.en"
    pack.mkdir(parents=True, exist_ok=True)
    raw_schema = args.rsz.read_bytes()
    if hashlib.sha256(raw_schema).hexdigest() != "67e5ed72e0e9efff0287214ad36737a8959d799aa1510b6b76591731a21f4df2":
        raise ValueError("Schema does not match the pinned MIT RE_RSZ revision")
    schema = json.loads(raw_schema)
    names = {v["name"] for v in schema.values()}
    for entry in schema.values():
        names.update(f["name"] for f in entry["fields"])
    known_path = plugin / "Resources/HashNames.json"
    known = json.loads(known_path.read_text(encoding="utf-8"))
    known = {h: name for h, name in known.items() if name in names}
    write(known_path, known)
    evidence = json.loads((root / "evidence/manifest.v1.json").read_text(encoding="utf-8"))
    identifiers = [{"id": h, "name": name, "category": "class" if "." in name else "field",
                    "description": "Hash-verified identifier spelling. Gameplay meaning and legal values are unconfirmed.",
                    "source": SOURCE} for h, name in known.items()]
    sections = []
    for section in evidence["sections"]:
        name = known[section["hash"]]
        short = re.sub(r"Manager$", "", name.split(".")[-2])
        short = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", short)
        sections.append({"id": section["hash"], "name": short, "category": "section",
                         "description": f"{name}; grouping inferred from the identifier, not a verified in-game effect.",
                         "attributes": {"identifier": name, "confidence": "Inferred"}, "source": SOURCE})
    item_path = "section[class=3a83076c]/3185b7a5[]/1b5a30c0"
    items = [{"id": row["id"], "name": f"Unmapped item 0x{int(row['id']):08x}", "category": "unmapped",
              "description": "Observed inventory ID. Player-facing name, description, legal limit and unlock dependencies are unknown.",
              "attributes": {"confidence": "Unknown"}} for row in evidence["identifiers"] if row["path"] == item_path]
    datasets = []
    for catalog, rows, author, license_name, license_url, source in [
        ("pragmata-identifiers", identifiers, "alphazolam; praydog (RSZ dumps)", "MIT", LICENSE, SOURCE),
        ("pragmata-sections", sections, "alphazolam; praydog; Save Toolkit contributors (group labels)", "MIT", LICENSE, SOURCE),
        ("pragmata-items", items, "Save Toolkit contributors", "CC0-1.0", "https://creativecommons.org/publicdomain/zero/1.0/",
         "https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21"),
    ]:
        filename = catalog + ".json"
        write(pack / filename, {"records": rows})
        datasets.append({"id": catalog, "kind": "records", "title": catalog,
                         "files": [{"path": filename, "sha256": hashlib.sha256((pack / filename).read_bytes()).hexdigest()}],
                         "attribution": {"sourceUrl": source, "author": author, "license": license_name,
                                         "licenseUrl": license_url, "retrieved": "2026-10-28",
                                         "modifications": "Selected observed hashes; verified spelling; original group labels and explicit unknown-ID descriptions. No game text or artwork."}})
    write(pack / "pack.json", {"schema": 1, "id": "pragmata.evidence.en", "version": "1.0.0",
                               "title": "Pragmata observed identifiers (not a gameplay dictionary)", "plugins": ["pragmata"],
                               "titleIds": ["PPSA02530"], "gameBuilds": [], "locale": "en", "datasets": datasets})
    print(f"Built {len(identifiers)} identifiers, {len(sections)} sections, {len(items)} unknown item IDs.")


if __name__ == "__main__":
    main()

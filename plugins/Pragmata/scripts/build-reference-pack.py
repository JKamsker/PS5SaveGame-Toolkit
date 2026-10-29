"""Build attributed reference data from reviewed, SHA-pinned community inputs.

No downloads, no upstream code execution, no save payload reads. Run with python -I.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

SOURCE = "https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/rszpragmata.json"
LICENSE = "https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/LICENSE"
MISSION_SOURCE = "https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/data/json/natives/stm/singletonuserdata/missionuserdata.user.3.json"
MISSION_LICENSE = "https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/blob/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b/LICENSE"
COMMUNITY_REVISION = "4125e497f1ae84cc746bb4a99da612c48bea161e"
COMMUNITY_ROOT = f"https://github.com/kvasszn/ree-save-editor/blob/{COMMUNITY_REVISION}/assets/pragmata/"
COMMUNITY_LICENSE = "No licence published; reused with attribution per owner decision 2026-10-28"
COMMUNITY_HASHES = {
    "rszpragmata.json": "0857b7c165e10e9ceeef65eab80ef84157262bcc03f3da8f50108a3b95d4dad8",
    "strings_pragmata.txt": "ce4616cd876ac4b02a5fc541782c49d00a5f55b8b46eac8956aa46d5f709665d",
    "enumspragmata.json": "c6a828b65ca065cd05003395e6d1b7a8d08872190dacca904899a615f9527c3b",
}
RESTORED_NAMES = {"27945a5a": "Slot", "619456d1": "_IsFirstGetWeapon",
                  "6f1ca4a8": "_IsFirstGetActiveSkill", "bc922b61": "Detail",
                  "e205fff9": "_AcquireContentsHashes"}


def write(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def community_datasets(directory, plugin, known):
    inputs = {}
    for filename, digest in COMMUNITY_HASHES.items():
        raw = (directory / filename).read_bytes()
        if hashlib.sha256(raw).hexdigest() != digest:
            raise ValueError(f"Community input does not match pinned revision: {filename}")
        inputs[filename] = raw
    strings = set(inputs["strings_pragmata.txt"].decode("utf-8").splitlines())
    if not set(RESTORED_NAMES.values()).issubset(strings):
        raise ValueError("Restored identifiers are missing from the pinned string dump")
    write(plugin / "Resources/RestoredHashNames.json", RESTORED_NAMES)
    restored = [{"id": h, "name": name, "category": "field",
                 "description": "Hash-verified identifier spelling; PS5 gameplay meaning and legal values remain Unknown.",
                 "attributes": {"confidence": "Inferred"}, "source": COMMUNITY_ROOT + "strings_pragmata.txt"}
                for h, name in RESTORED_NAMES.items()]
    hashes = {name: h for h, name in (known | RESTORED_NAMES).items()}
    enums = json.loads(inputs["enumspragmata.json"])
    fields = []
    enum_types = set()
    for entry in json.loads(inputs["rszpragmata.json"]).values():
        if entry["name"] not in hashes:
            continue
        for field in entry["fields"]:
            if field["name"] not in hashes:
                continue
            declared = field["original_type"]
            if declared in enums:
                enum_types.add(declared)
            fields.append({
                "id": f"{hashes[entry['name']]}/{hashes[field['name']]}",
                "name": field["name"], "category": entry["name"],
                "description": "Community RSZ declaration for a known save class/field; PC/PS5 schema equivalence and gameplay meaning are unverified.",
                "source": COMMUNITY_ROOT + "rszpragmata.json",
                "attributes": {"classHash": hashes[entry["name"]], "fieldHash": hashes[field["name"]],
                               "declaredType": declared, "storageType": field["type"],
                               "isArray": str(field["array"]).lower(), "size": str(field["size"]),
                               "alignment": str(field["align"]), "confidence": "Inferred"},
            })
    members = []
    for enum_type in sorted(enum_types):
        # Preserve aliases: the reverse numeric-to-name half loses alias spellings.
        aliases = {}
        for name, value in enums[enum_type].items():
            if not re.fullmatch(r"-?\d+", name):
                aliases.setdefault(str(int(value)), []).append(name)
        for value, names in sorted(aliases.items(), key=lambda pair: int(pair[0])):
            members.append({"id": f"{enum_type}:{value}", "name": " / ".join(names), "category": enum_type,
                            "description": "Declared enum member; not evidence of legal PS5 values, flag combinations or gameplay effects.",
                            "source": COMMUNITY_ROOT + "enumspragmata.json",
                            "attributes": {"enumType": enum_type, "value": value, "confidence": "Inferred"}})
    author = "kvasszn / REE Save Editor; praydog (RSZ dump tooling)"
    return [("pragmata-restored-identifiers", restored, author, COMMUNITY_LICENSE, COMMUNITY_ROOT + "strings_pragmata.txt", COMMUNITY_ROOT + "strings_pragmata.txt"),
            ("pragmata-field-schema", sorted(fields, key=lambda row: row["id"]), author, COMMUNITY_LICENSE, COMMUNITY_ROOT + "rszpragmata.json", COMMUNITY_ROOT + "rszpragmata.json"),
            ("pragmata-enum-members", members, author, COMMUNITY_LICENSE, COMMUNITY_ROOT + "enumspragmata.json", COMMUNITY_ROOT + "enumspragmata.json")]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rsz", type=Path, required=True)
    parser.add_argument("--missions", type=Path, required=True)
    parser.add_argument("--community-dir", type=Path, required=True, help="Directory with the three reviewed REE dumps")
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
              "source": "https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21",
              "attributes": {"confidence": "Unknown"}} for row in evidence["identifiers"] if row["path"] == item_path]
    resource_labels = json.loads((pack / "currency-labels.source.json").read_text(encoding="utf-8"))
    named_items = {record["id"]: record for record in resource_labels["records"]}
    if not named_items.keys() <= {record["id"] for record in items}:
        raise ValueError("Named resources must already occur in the observed inventory ID list")
    items = [named_items.get(record["id"], record) for record in items]
    raw_missions = args.missions.read_bytes()
    if hashlib.sha256(raw_missions).hexdigest() != "f51e68117901a9bdf0ea24ae0154962e327499fc0986d4561ddb641bf0489fc7":
        raise ValueError("Mission data does not match the pinned MIT source revision")
    observed_ids = {row["id"] for row in items}
    cross_references = {}
    mission_records = []
    mission_data = json.loads(raw_missions)[0]["app.MissionUserdata"]
    display_order = {str(key): index + 1 for index, key in enumerate(mission_data["MissionDisplayOrderArray"])}
    for entry in mission_data["Units"]:
        unit = entry["app.MissionUserdata.Unit"]
        item_id = str(unit["OpenConditionItemID"])
        mission_id = str(unit["ID"]["app.ObjectDefine"]["Hash"])
        mission_records.append({
            "id": mission_id, "name": f"Mission 0x{int(mission_id):08x}",
            "category": f"category-hash-{unit['CategoryHash']:08x}",
            "description": "PC mission reference data; localized name and PS5 completion/unlock state are Unknown. "
                           "OpenConditionItemID is one stored opening condition, not a complete unlock rule.",
            "source": MISSION_SOURCE,
            "attributes": {"confidence": "Inferred", "nameConfidence": "Unknown",
                           "openingItemId": item_id, "difficultyValue": str(unit["Difficulty"]),
                           "displayOrder": str(display_order.get(mission_id, "not listed")),
                           "chapterConditionGuid": unit["OpenCondition"]["app.ChapterFlagID"]["_Guid"],
                           "nameMessageGuid": unit["NameText"]["app.TextMessageData"]["_MessageId"],
                           "extraMission": str(unit["IsExtraMission"]).lower(),
                           "platform": "PC; PS5 semantics unverified"},
        })
        if item_id in observed_ids:
            cross_references.setdefault(item_id, set()).add(str(unit["ID"]["app.ObjectDefine"]["Hash"]))
    mission_items = [{
        "id": item_id, "name": f"Mission opening-condition reference 0x{int(item_id):08x}",
        "category": "mission-opening-condition",
        "description": f"PC mission data references this exact numeric ID as OpenConditionItemID in {len(missions)} mission units. "
                       "The ID is observed in the PS5 inventory. Its player-facing name, description and legal limit remain Unknown; PS5 gameplay effects are unverified.",
        "source": MISSION_SOURCE,
        "attributes": {"confidence": "Inferred", "nameConfidence": "Unknown", "role": "app.MissionUserdata.Unit.OpenConditionItemID",
                       "missionIds": ",".join(sorted(missions, key=int)), "platform": "PC; PS5 ID overlap only"},
    } for item_id, missions in sorted(cross_references.items(), key=lambda pair: int(pair[0]))]
    datasets = []
    for catalog, rows, author, license_name, license_url, source in [
        ("pragmata-identifiers", identifiers, "alphazolam; praydog (RSZ dumps)", "MIT", LICENSE, SOURCE),
        ("pragmata-sections", sections, "alphazolam; praydog; Save Toolkit contributors (group labels)", "MIT", LICENSE, SOURCE),
        ("pragmata-items", items, "Save Toolkit contributors", "CC0-1.0", "https://creativecommons.org/publicdomain/zero/1.0/",
         "https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21"),
        ("pragmata-item-evidence", mission_items, "Egg Targaryen / dzxrly; Save Toolkit contributors (PS5 cross-index)", "MIT", MISSION_LICENSE, MISSION_SOURCE),
        ("pragmata-missions", mission_records, "Egg Targaryen / dzxrly; Save Toolkit contributors (reference labels)", "MIT", MISSION_LICENSE, MISSION_SOURCE),
    ] + community_datasets(args.community_dir, plugin, known):
        filename = catalog + ".json"
        write(pack / filename, {"records": rows})
        modifications = "Selected observed hashes; verified spelling; original group labels and explicit unknown-ID descriptions. No game text or artwork."
        if catalog in ("pragmata-item-evidence", "pragmata-missions"):
            modifications = "Selected numeric mission IDs, opening conditions and reference metadata; cross-indexed PS5 inventory observations. No localized text or artwork."
        elif license_name == COMMUNITY_LICENSE:
            modifications = "Selected identifier spellings, class/field declarations and enum members/aliases; cross-indexed known save hashes. No localized game text, artwork or code."
        datasets.append({"id": catalog, "kind": "records", "title": catalog,
                         "files": [{"path": filename, "sha256": hashlib.sha256((pack / filename).read_bytes()).hexdigest()}],
                         "attribution": {"sourceUrl": source, "author": author, "license": license_name,
                                         "licenseUrl": license_url, "retrieved": "2026-10-28",
                                         "modifications": modifications}})
    next(dataset for dataset in datasets if dataset["id"] == "pragmata-items")["attribution"] = resource_labels["attribution"]
    write(pack / "pack.json", {"schema": 1, "id": "pragmata.evidence.en", "version": "1.3.0",
                               "title": "Pragmata observed identifiers (not a gameplay dictionary)", "plugins": ["pragmata"],
                               "titleIds": ["PPSA02530"], "gameBuilds": [], "locale": "en", "datasets": datasets})
    print(f"Built {len(identifiers)} identifiers, {len(sections)} sections, {len(items)} item IDs ({len(named_items)} Inferred names), {len(mission_items)} mission item cross-references, {len(mission_records)} missions.")
    for dataset in datasets:
        if dataset["attribution"]["license"] == COMMUNITY_LICENSE:
            record_count = len(json.loads((pack / dataset['files'][0]['path']).read_bytes())["records"])
            print(f"Built {dataset['id']}: {record_count} records.")


if __name__ == "__main__":
    main()

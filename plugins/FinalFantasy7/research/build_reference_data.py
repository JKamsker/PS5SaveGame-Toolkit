"""Extract data from pinned MIT templates as text, never importing/executing them.

Run with python -I build_reference_data.py <scratch/FinalFantasy7>.
Descriptions summarize source metadata; they are not imported game effect text.
"""

import collections
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

REVISION = "c7bdd6d8228304b87dcdf15e57155d092eb06fd2"
UPSTREAM = "https://github.com/RyudoSynbios/game-tools-collection"
ROOT = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.FinalFantasy7"
SCRATCH = Path(sys.argv[1])


def upstream(game, relative):
    path = f"src/lib/templates/final-fantasy-vii-{game}/saveEditor/{relative}"
    content = subprocess.check_output(
        ["git", "-C", str(SCRATCH / "gtc"), "show", f"{REVISION}:{path}"]
    ).decode("utf-8")
    return content, f"{UPSTREAM}/blob/{REVISION}/{path}"


def array(text, name):
    match = re.search(r"export const " + name + r" = \[(.*?)\n\];", text, re.S)
    if not match:
        raise ValueError(f"Missing array {name}")
    return match[1]


def named_rows(text):
    return re.findall(r'\{ index: (0x[\da-f]+).*?name: "([^"]+)" \}', text)


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    content = json.dumps(value, ensure_ascii=False, indent=2) + "\n"
    path.write_text(content, encoding="utf-8", newline="\n")
    return hashlib.sha256(content.encode()).hexdigest()


def record(identifier, name, category, description, source, attributes=None):
    return dict(id=str(identifier), name=name, category=category,
                description=description, source=source, attributes=attributes or {})


def catalog(game, text, source):
    categories_name = "itemCategories" if game == "remake" else "itemTypes"
    categories = {int(key, 16): name for key, name in named_rows(array(text, categories_name))}
    items = {}
    for line in array(text, "itemList").splitlines():
        match = re.search(r'index: (0x[\da-f]+).*?name: "([^"]+)"', line)
        if not match:
            continue
        identifier, name = int(match[1], 16), match[2]
        category_key = "category" if game == "remake" else "type"
        category_id = int(re.search(category_key + r": (0x[\da-f]+)", line)[1], 16)
        category = categories[category_id]
        attributes = {}
        description = f"{category} entry in the {game.title()} reference table."
        if category == "Queen's Blood Cards":
            if name.startswith("QB Booster Pack:"):
                category = "Queen's Blood Booster Packs"
                description = "Queen's Blood booster pack inventory type; not an individual card or deck entry."
            else:
                description = ("Queen's Blood inventory card type; quantity is separate from decks and opponent wins. "
                               "Source display numbers are unverified: the source duplicates 012 and omits 013.")
                attributes["sourceDisplayNumber"] = re.search(r"Card (\d+)", name)[1]
        elif category == "Accessories":
            description = "Accessory type; effect text and equip restrictions are not supplied by this source."
        items[identifier] = record(identifier, name, category, description, source, attributes)

    weapons = []
    for key, name in named_rows(array(text, "weaponList")):
        identifier = int(key, 16)
        entry = record(identifier, name, "Weapons",
                       "Weapon type; ownership uses an instance reference, not this type ID. "
                       "Socket counts and equipment eligibility remain unverified.", source)
        weapons.append(entry)
        if identifier in items and items[identifier]["name"] != name:
            raise ValueError(f"Conflicting weapon type {identifier}")
        items[identifier] = entry

    materia_categories = {int(key, 16): name for key, name in named_rows(array(text, "materiaTypes"))}
    materias = []
    for line in array(text, "materiaList").splitlines():
        match = re.search(r'index: (0x[\da-f]+), type: (0x[\da-f]+), ap: \[([^]]+)\], name: "([^"]+)"', line)
        if not match:
            continue
        identifier, category_id = int(match[1], 16), int(match[2], 16)
        thresholds = [int(value.strip()) for value in match[3].split(",")]
        category = materia_categories[category_id]
        description = (f"{category}. Source AP thresholds: {', '.join(map(str, thresholds))}. "
                       "PS5 star transitions are unverified.")
        if category_id == 4:
            description = "Summon materia; source AP arrays do not establish summon tier. AP editing is disabled."
        materias.append(record(identifier, match[4], category, description, source,
                               {"apThresholds": ",".join(map(str, thresholds))}))
    return {"ff7r-items": list(items.values()), "ff7r-materia": materias, "ff7r-weapons": weapons}


def progress(game, text, source):
    names = {}
    candidates = []
    for line in text.splitlines():
        indent = len(line) - len(line.lstrip())
        name = re.match(r'\s*name: "([^"]+)",', line)
        if name:
            names = {depth: value for depth, value in names.items() if depth < indent}
            names[indent] = name[1]
        match = re.search(r'\{ offset: (0x[\da-f]+), bit: (\d+), label: "([^"]+)"', line)
        if not match or "hidden: true" in line or "disabled: true" in line:
            continue
        path = list(names.values())
        region = next((value.removesuffix(" Region") for value in path
                       if value.endswith(" Region") or value == "Grasslands"), "General")
        if game == "remake":
            selected = "Battle Intel" in path or "Obtained Dresses" in path
        else:
            selected = ("Cleared Chapters on Hard" in path or "Queen's Blood" in path or
                        (region != "General" and any(value in path for value in ["World Intel", "Chocobo Stops"])))
            # Upstream repeats placeholder bits in Intel Entries. Exclude the entire group.
            selected = selected and "Intel Entries" not in path
        if not selected:
            continue
        offset, bit = int(match[1], 16), int(match[2])
        candidates.append(dict(name=match[3].strip(), region=region, category=" / ".join(path[-2:]),
                               offset=offset, bit=bit, source=source))
    addresses = collections.Counter((row["offset"], row["bit"]) for row in candidates)
    rows = [row for row in candidates if addresses[row["offset"], row["bit"]] == 1]
    for row in rows:
        row["id"] = f"{game}/{row['offset']:x}/{row['bit']}"
    return rows


for game, titles, builds in [
    ("remake", ["PPSA01911"], ["Remake Intergrade"]),
    ("rebirth", ["PPSA08664", "PPSA18936"], ["Rebirth", "Rebirth Demo"]),
]:
    text, source = upstream(game, "utils/resource.ts")
    folder = ROOT / "ReferencePacks" / f"ff7r.{game}.en"
    datasets = []
    for identifier, entries in catalog(game, text, source).items():
        digest = write_json(folder / f"{identifier}.json", {"records": sorted(entries, key=lambda entry: int(entry["id"]))})
        datasets.append(dict(id=identifier, kind="records", title=f"{game.title()} {identifier[5:]}",
                             files=[dict(path=f"{identifier}.json", sha256=digest)],
                             attribution=dict(sourceUrl=source, author="RyudoSynbios / Game Tools Collection",
                                              license="MIT", licenseUrl=f"{UPSTREAM}/blob/{REVISION}/LICENSE.md",
                                              retrieved="2026-10-28", modifications="Extracted type IDs, names, categories and AP tables. Added original metadata summaries; no game effect text or artwork.")))
    write_json(folder / "pack.json", dict(schema=1, id=f"ff7r.{game}.en", version="1.0.0",
                                         title=f"FFVII {game.title()} reference metadata", plugins=["ff7r"],
                                         titleIds=titles, gameBuilds=builds, locale="en", datasets=datasets))
    template, template_source = upstream(game, "template.ts")
    rows = progress(game, template, template_source)
    write_json(ROOT / "Resources" / f"{game}-progress.json", rows)
    print(game, {dataset["id"]: len(json.loads((folder / dataset["files"][0]["path"]).read_text(encoding="utf-8"))["records"]) for dataset in datasets}, "progress", len(rows))

"""Build the attributed reference pack from hash-checked, pinned API responses.

Run with Python -I; downloads belong in a scratch directory, never in the repository.
Requires beautifulsoup4. This developer utility is not used by the plugin at runtime.
"""

import argparse
import hashlib
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

# -I intentionally excludes the script directory from module search paths.
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.dont_write_bytecode = True
from wiki_tables import lines, load_source, name, records, slug, tables, text, write_json

CHARACTERS = ["Alphen", "Shionne", "Rinwell", "Law", "Kisara", "Dohalim"]
WEAPONS = dict(zip(["Swords", "Rifles", "Books", "Gauntlets", "Shields", "Rods"], CHARACTERS))
ARMOR = dict(zip(["Mails", "Dresses", "Cloaks", "Vests", "Guards", "Capes"], CHARACTERS))
LEGACY_ITEMS = {label: str(i + 1) for i, label in enumerate([
    "Apple Gel", "Peach Gel", "Lemon Gel", "Grape Gel", "Orange Gel", "Pineapple Gel",
    "Gold Gel", "Treat", "Heavy Treat", "Panacea Bottle", "Life Bottle", "Elixir", "Omega Elixir"])}
TITLES = {"items": "Items", "equipment": "Equipment", "artes": "Artes",
          "skills": "Skills", "titles": "Title panels", "skill-nodes": "Title skill nodes",
          "characters": "Characters", "progress": "Recipe, fish and artifact guides"}


def character_for(title):
    candidate = title.removeprefix("ToA - ").split(":")[0]
    return {"Shionne Imeris": "Shionne", "Dohalim il Qaras": "Dohalim"}.get(candidate, candidate)


def add(output, dataset, source, label, category, attributes, description="", key=None):
    attributes = {"serializedId": "Unknown", **attributes}
    key = key or ":".join(filter(None, [slug(category), slug(attributes.get("character", "")), slug(label)]))
    record = {"id": key, "name": label, "category": category, "source": source["url"],
              "description": description or "Reference only; save-state identity is not established.",
              "attributes": attributes}
    if dataset in ["equipment", "artes"] and any(r["id"] == key for r in output[dataset]):
        # Some wiki rows share an English label but have different acquisition/variant facts.
        record["id"] += ":variant:" + hashlib.sha256(description.encode()).hexdigest()[:12]
        record["attributes"]["sourceVariant"] = "Duplicate published name; kept separately, not a new save ID."
    output[dataset].append(record)
    return record


def extract_table(output, source, headers, section, table):
    title = source["title"]
    first = headers[0]
    name_column = 1 if first in ["Icon", "Artifact", "Title", "Fish", "Rod"] else 0
    for entry in records(table, name_column):
        cells = entry["cells"]
        label = name(cells[name_column])
        attrs = {"sourceRevision": str(source["revision"]), "section": section}
        # A rowspanned heading can occupy more than one physical column (skills).
        facts = [h + ": " + text(cells[i]) for i, h in enumerate(headers)
                 if i != name_column and i in cells and text(cells[i])]
        description = "\n".join(facts + entry["notes"])
        if first == "Arte Name":
            attrs["character"] = character_for(title)
            attrs["requirements"] = text(cells[len(headers) - 1])
            partner = headers.index("Partner") if "Partner" in headers else None
            if partner is not None:
                attrs["partner"] = text(cells[partner])
            category = section.split(" (")[0] or "Arte"
            add(output, "artes", source, label, category, attrs, description)
        elif first == "Icon" and headers[1] == "Skill Name":
            # The first header spans Base and DLC columns; read the physical columns explicitly.
            attrs["character"] = "; ".join(lines(cells[2]))
            attrs["baseStacks"] = "; ".join(lines(cells[3]))
            attrs["dlcStacks"] = "; ".join(lines(cells[4]))
            description = "Users: " + attrs["character"] + "\nBase stacks (same user order): " + attrs["baseStacks"]
            description += "\nDLC stacks (same user order): " + attrs["dlcStacks"] + "\n" + text(cells[5])
            add(output, "skills", source, label, section, attrs, description, key="skill:" + slug(label))
        elif first == "Title":
            attrs["character"] = character_for(title)
            attrs["requirements"] = text(cells[5])
            attrs["completionBonus"] = text(cells[4])
            panel = add(output, "titles", source, label, "Title", attrs, description)
            nodes = cells[2].find_all("li")
            costs = lines(cells[3])
            if len(nodes) != 5 or len(costs) != len(nodes):
                raise ValueError("Unexpected skill panel/cost shape: " + title + ": " + label)
            for index, (node, cost) in enumerate(zip(nodes, costs)):
                node_attrs = {**attrs, "title": label, "titleKey": panel["id"], "spCost": cost,
                              "position": str(index), "state": "Unknown"}
                add(output, "skill-nodes", source, text(node), "Title skill", node_attrs,
                    "Title: " + label + "\nSP cost as published: " + cost + "\nRequirements: " + attrs["requirements"]
                    + "\nPanel bonus: " + attrs["completionBonus"]
                    + "\nTitle unlock, node purchase and panel completion are separate, unproven save states.",
                    key=panel["id"] + ":node:" + str(index))
        elif title == "ToA - Cooking" or title == "ToA - Fishing":
            category = "Recipe" if title.endswith("Cooking") else "Fish"
            add(output, "progress", source, label, category, attrs, description)
        elif title == "ToA - Artifacts":
            add(output, "progress", source, label, "Artifact", attrs, description)
            add(output, "items", source, label, "Artifact", attrs, description)
        elif title.startswith("ToA - Weapons (") or title.startswith("ToA - Armor ("):
            group = title.split("(")[1].rstrip(")")
            category = "Weapon" if "Weapons" in title else "Armor"
            attrs["character"] = (WEAPONS if category == "Weapon" else ARMOR)[group]
            attrs["type"] = group
            add(output, "equipment", source, label, category, attrs, description)
        elif title == "ToA - Accessories" or ": Costumes" in title or ": Haircut, Style" in title or title == "ToA - Attachments":
            category = "Accessory" if title.endswith("Accessories") else "Costume" if ": Costumes" in title else "Style" if ": Haircut" in title else "Attachment"
            attrs["character"] = character_for(title) if ":" in title else "All"
            add(output, "equipment", source, label, category, attrs, description)
        else:
            category = {"ToA - Consumables": "Consumable", "ToA - Materials": "Material",
                        "ToA - Accessory Materials": "Accessory material", "ToA - Valuables": "Valuable",
                        "ToA - Fishing Items": "Fishing item"}.get(title)
            if category is None:
                raise ValueError("Unclassified table: " + title + ": " + str(headers))
            key = LEGACY_ITEMS.get(label) if category == "Consumable" else None
            if key is not None:
                attrs["serializedId"] = key
                attrs["identityEvidence"] = "Legacy inferred PS4 mapping; only Apple Gel has a PS5 acquisition observation."
                attrs["editingGuardrail"] = "15 (plugin policy; not a proven effective cap)"
            add(output, "items", source, label, category, attrs, description, key=key)


def extract_characters(output, source, parsed):
    raw = parsed["wikitext"]["*"].split("<noinclude>")[0]
    for line in raw.splitlines():
        match = re.match(r"\s*\|([a-z_]+)=(.*)", line)
        if not match or match[1] in ["media", "state", "cat", "input_parameter"]:
            continue
        group, value = match.groups()
        value = re.sub(r"<!--.*?-->", "", value)
        names = [m[0] or m[1] for m in re.findall(r"\[\[([^\]]+)\]\]|\{\{[Pp]\|([^|}]+)\|[^}]+\}\}", value)]
        for label in names:
            label = label.split("|")[-1].replace("\u200e", "")
            short = {"Shionne Vymer Imeris Daymore": "Shionne", "Dohalim il Qaras": "Dohalim"}.get(label, label)
            attrs = {"role": group, "sourceRevision": str(source["revision"])}
            if short in CHARACTERS:
                attrs.update(character=short, statusIndex=str(CHARACTERS.index(short)), mappingConfidence="Inferred")
            add(output, "characters", source, label, "Playable" if short in CHARACTERS else group.replace("_", " ").title(), attrs,
                "Character or group named by the source navigation template. This is not a party/recruitment flag.",
                key="character:" + slug(short))


def build(cache, output_directory, sources_path):
    sources = json.loads(Path(sources_path).read_text(encoding="utf-8"))
    output = defaultdict(list)
    counts = {}
    for source in sources:
        parsed, soup = load_source(source, cache)
        for element in soup.select("style, sup.reference, .mw-editsection"):
            element.decompose()
        before = sum(map(len, output.values()))
        if source["title"] == "Template:ToA Characters":
            extract_characters(output, source, parsed)
        elif source["title"] != "ToA - Boost Strikes":
            for headers, section, table in tables(soup):
                extract_table(output, source, headers, section, table)
            # Independent coverage check against named source templates, not the HTML row parser.
            # Comments contain expressly unpublished/unnamed-animation guesses and are excluded.
            published = re.sub(r"<!--.*?-->", "", parsed["wikitext"]["*"], flags=re.DOTALL)
            expected = {label.strip() for label in re.findall(
                r"\{\{(?:Item|Arte|Skill|Title)\s*\|\s*([^|}\n]+)", published, flags=re.IGNORECASE)}
            exported = {record["name"] for rows in output.values() for record in rows if record["source"] == source["url"]}
            if missing := expected - exported:
                raise ValueError("Named source rows were omitted: " + source["title"] + ": " + repr(sorted(missing)))
            source["publishedNames"] = len(expected)
        # Boost strikes occur in each participating character's page, with that character's requirements.
        counts[source["title"]] = sum(map(len, output.values())) - before
        source["wikitextSha256"] = hashlib.sha256(parsed["wikitext"]["*"].encode()).hexdigest()

    destination = Path(output_directory)
    destination.mkdir(parents=True, exist_ok=True)
    manifest = {"schema": 1, "id": "talesofarise.catalog", "version": "2.0.0",
                "title": "Tales of Arise reference catalogs", "plugins": ["tales-of-arise"],
                "titleIds": ["PPSA02745"], "locale": "en", "datasets": []}
    for dataset, title in TITLES.items():
        unique = {}
        for record in output[dataset]:
            if record["id"] in unique:
                old = unique[record["id"]]
                if old["name"] != record["name"]:
                    raise ValueError("Catalog key collision: " + record["id"])
                # Character groups can be repeated in different navigation roles.
                if dataset != "characters":
                    raise ValueError("Duplicate record requiring explicit reconciliation: " + record["id"])
                continue
            unique[record["id"]] = record
        rows = sorted(unique.values(), key=lambda r: r["id"])
        path = destination / (dataset + ".json")
        write_json(path, {"records": rows})
        urls = sorted({r["source"] for r in rows})
        manifest["datasets"].append({"id": dataset, "kind": "records", "title": title,
            "files": [{"path": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}],
            "attribution": {"sourceUrl": urls[0], "author": "Aselia Wiki contributors",
                "license": "CC-BY-SA-3.0", "licenseUrl": "https://creativecommons.org/licenses/by-sa/3.0/",
                "retrieved": "2026-10-28", "modifications": "Extracted complete named table rows from pinned revisions; normalized whitespace, retained published facts and unknowns, omitted images, added symbolic identities. All sources are listed per record and in source-revisions.json. Derived data remains CC BY-SA 3.0. Legacy numeric consumables retain inferred identity, not newly verified IDs."}})
        print(dataset, len(rows))
    write_json(destination / "pack.json", manifest)
    write_json(destination / "source-revisions.json", [{**s, "extractedRecords": counts[s["title"]]} for s in sources])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", required=True)
    parser.add_argument("--sources", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    build(args.cache, args.output, args.sources)

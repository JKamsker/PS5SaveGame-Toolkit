"""Import reviewed factual TGA/Paramdex data from scratch. No download or upstream execution.

Run: python -I import-community-catalog.py <scratch directory>.
All archive/source bytes are pinned; only literal data is read from upstream scripts.
"""

import argparse
import importlib.util
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
# -I deliberately excludes the script directory from sys.path; load this local helper explicitly.
HELPER = Path(__file__).with_name("community_sources.py")
SPEC = importlib.util.spec_from_file_location("community_sources", HELPER)
sources_module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sources_module)
S = sources_module
ROOT = "CheatTable/CheatEntries/Open - The Grand Archives - Elden Ring/"
OUTPUT = Path(__file__).resolve().parent.parent / "SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.community.en"
BST_HASH = "d3cd15912bf2c4ff7c67a1f3ec2aff78ec887e4d8e435e3cab036f80cb8f4c82"
BST_URL = f"https://github.com/ClayAmore/ER-Save-Lib/blob/{S.LIB_REVISION}/src/res/eventflag_bst.txt"
GROUPS_HASH = "38a09ba5bde95f4f67029c5af993514d26cfcd4c78ff11038647e99dccb1ce45"
GROUPS_URL = "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/items.rs"


def items(sources, scratch):
    text, url, source_hash = sources.read("tga.zip", ROOT + "MiscWIP/Dropdowns/ItemDropdownBase.xml")
    rows = {}
    categories = {0: "weapon", 1: "armor", 2: "talisman", 4: "goods", 8: "ash-of-war"}

    def add(identifier, label, source, sha256):
        category = categories.get(identifier >> 28, "upstream-sentinel")
        key = f"0x{identifier:08X}" if category != "upstream-sentinel" else f"tga:{identifier:08X}"
        if key not in rows:
            rows[key] = S.record(key, label, category, source, S.TGA_REVISION, sha256, {},
                                 "Community identity/group facts; Inferred on PS5. No legal quantity, compatibility or ownership implied.")
        return rows[key]

    for line in ET.fromstring(text).findtext("DropDownList").splitlines():
        identifier, name = line.split(":", 1)
        add(int(identifier, 16), name, url, source_hash)
    archive = sources.archives["tga.zip"]
    for path in sorted(archive.namelist()):
        if "/MassItemGib/" not in path or not path.endswith(".cea"):
            continue
        suffix = path.split("/", 1)[1]
        text, url, source_hash = sources.read("tga.zip", suffix)
        relative = suffix.split("/MassItemGib/", 1)[1]
        content = relative.split("/", 1)[0].strip("- ") if relative.startswith("--") else "Unspecified"
        group = Path(relative).stem
        # Select only active literal tuple lines; quantities and script bodies are deliberately discarded.
        for identifier, label in re.findall(r"^\s*\{\s*0x([0-9a-fA-F]+),[^\r\n]*?\},?\s*--\s*([^\r\n]+)", text, re.M):
            row = add(int(identifier, 16), label.strip(), url, source_hash)
            attrs = row["attributes"]
            for key, value in (("groups", group), ("contentGroups", content), ("groupSources", url)):
                values = set(attrs.get(key, "").split(" | ")) - {""}
                attrs[key] = " | ".join(sorted(values | {value}))
    data = (scratch / "items.rs").read_bytes()
    if S.digest(data) != GROUPS_HASH:
        raise ValueError("Unreviewed inherited item groups")
    sources.files[GROUPS_URL] = GROUPS_HASH
    groups = re.findall(r'item_groups\.insert\("([^"]+)"\.to_string\(\), vec!\[(.*?)\]\);',
                        data.decode("utf-8-sig"), re.S)
    if len(groups) != 19:
        raise ValueError("Unexpected inherited item group count")
    for group, members in groups:
        for identifier in re.findall(r"^\s*0x([0-9A-Fa-f]+),", members, re.M):
            value = int(identifier, 16)
            row = add(value, f"0x{value:08X}", GROUPS_URL, GROUPS_HASH)
            attrs = row["attributes"]
            existing = set(attrs.get("editorGroups", "").split(" | ")) - {""}
            attrs["editorGroups"] = " | ".join(sorted(existing | {group}))
            attrs["editorMembershipOccurrences"] = str(int(attrs.get("editorMembershipOccurrences", "0")) + 1)
            attrs["editorGroupSource"] = GROUPS_URL
            attrs["editorGroupSha256"] = GROUPS_HASH
    return sorted(rows.values(), key=lambda row: row["id"])


def flags(sources, scratch):
    data = (scratch / "eventflag_bst.txt").read_bytes()
    if S.digest(data) != BST_HASH:
        raise ValueError("Unreviewed event block map")
    sources.files[BST_URL] = BST_HASH
    blocks = {}
    for line in data.decode("utf-8-sig").splitlines():
        group, block = map(int, line.split(","))
        if group in blocks:
            raise ValueError("Duplicate event block key")
        blocks[group] = block
    text, url, source_hash = sources.read("tga.zip", ROOT + "Event Flags/Event Flag Manager/.cea")
    # Remove Lua long comments before selecting data tables, so disabled/commented recipes stay excluded.
    text = re.sub(r"--\[(=*)\[.*?\]\1\]", "", text, flags=re.S)
    text = text[:text.index("local stateGroupsDlc =")]
    tables = list(re.finditer(r"local (stateGroups(?:Dlc\d+)?) = \{", text))
    rows = []
    for index, table in enumerate(tables):
        content = {"stateGroups": "Base game", "stateGroupsDlc1": "DLC1 (source group)",
                   "stateGroupsDlc3": "DLC3 (source group)"}[table.group(1)]
        end = tables[index + 1].start() if index + 1 < len(tables) else len(text)
        body = text[table.end():end]
        groups = list(re.finditer(r'^\s*\{category = "([^"]+)",\s*(?:subcategory = "([^"]+)",\s*)?name = "([^"]+)", flags = \{(.*?)\}\}', body, re.S | re.M))
        if not groups:
            raise ValueError("No literal state groups")
        for group in groups:
            category, subcategory, name, flag_text = group.groups()
            region = f"{content} · {category}" + (f" · {subcategory}" if subcategory else "")
            identity = S.digest(f"{content}\t{category}\t{subcategory}\t{name}".encode())[:12]
            for match in re.finditer(r"^\s*FLAG\((\d+)[^\r\n]*", flag_text, re.M):
                event = int(match.group(1))
                role = "NG+ persistence" if "NG+ Persistence" in match.group() else "Source group member"
                attrs = {"schema": "eldenring-progress-candidate/1", "eventId": str(event),
                         "region": region, "regionId": S.digest(region.encode())[:16],
                         "sourceCategory": category, "contentGroup": content, "flagRole": role,
                         "lookupSource": BST_URL, "lookupSha256": BST_HASH,
                         "lookupRevision": S.LIB_REVISION, "stateGroup": name}
                if event // 1000 in blocks:
                    attrs.update(offset=str(blocks[event // 1000] * 125 + event % 1000 // 8), bit=str(7 - event % 8))
                label = f"{role} · {name}" if role == "NG+ persistence" else name
                rows.append(S.record(f"event-flag:{event}:{identity}", label, "event-flag", url,
                                     S.TGA_REVISION, source_hash, attrs,
                                     "Read-only source flag candidate, not verified PS5 completion. Group predicates and writers are not ported. "
                                     "Raw bits do not establish quests, DLC ownership, NG+ cycle or reset semantics."))
    unique = {}
    for row in rows:
        key = row["id"]
        if key in unique:
            previous = unique[key]
            previous["attributes"].pop("sourceOccurrences", None)
            if previous != row:
                raise ValueError(f"Conflicting repeated source flag: {key}")
            previous["attributes"]["sourceOccurrences"] = "2"
        else:
            unique[key] = row
    return sorted(unique.values(), key=lambda row: row["id"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("scratch", type=Path)
    scratch = parser.parse_args().scratch
    sources = S.Sources(scratch)
    # Review statements as source text, and retain their hashes with the data's provenance.
    for archive in S.ARCHIVES:
        sources.read(archive, "README.md")
    item_rows = items(sources, scratch)
    flag_rows = flags(sources, scratch)
    layouts = S.paramdex(sources)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "param-rows.json").unlink(missing_ok=True)  # Remove the obsolete v1.0 dump on regeneration.
    definitions = [
        ("eldenring-tga-items", "Community item identities and groups", "items.json", item_rows, "tga.zip"),
        ("eldenring-tga-flags", "Community base/DLC/NG+ flag candidates", "flags.json", flag_rows, "tga.zip"),
        ("eldenring-param-layouts", "PARAM technical layout facts", "param-layouts.json", layouts, "paramdex.zip"),
    ]
    datasets = []
    for identifier, title, filename, rows, archive in definitions:
        if len(rows) != len({row["id"] for row in rows}):
            raise ValueError(f"Duplicate identities in {identifier}")
        repo, revision, _ = S.ARCHIVES[archive]
        authors = "The Grand Archives (Dasaav/Umgak, Apricus, Mar_Veloz and contributors); ClayAmore (inherited item groups)" if archive == "tga.zip" else "soulsmods / Paramdex contributors"
        modifications = "Inferred on PS5; no legal caps, ownership or build compatibility implied. Literal facts only; no code, game descriptions, art, quantity recipes or editor limits. Source-byte hashes and transformations: community.source.md."
        default_source = f"https://github.com/{repo}/tree/{revision}"
        if filename == "items.json":
            default_source = f"https://github.com/{repo}/blob/{revision}/" + S.quote(ROOT + "MiscWIP/Dropdowns/ItemDropdownBase.xml", safe="/")
            modifications += f" Groups follow the exact MassItemGib source filenames; inherited editorGroups: {GROUPS_URL}. Revisions/hashes are dataset/sidecar metadata."
        if filename == "flags.json":
            authors = "The Grand Archives (Dasaav/Umgak and contributors); ClayAmore / ER-Save-Lib event block map"
            default_source = f"https://github.com/{repo}/blob/{revision}/" + S.quote(ROOT + "Event Flags/Event Flag Manager/.cea", safe="/")
            modifications = "Schema: eldenring-community-flags/2. " + modifications
            modifications += f" Explicit block map: {BST_URL}; MSB-first addressing, bounded at runtime. Separate raw group members; no predicates/writers, verified completion, DLC ownership or NG+ reset semantics."
        if filename == "param-layouts.json":
            modifications += " Field sources: ER/Defs/<category>.xml at the pinned revision; versions/types appear once per :schema summary. No decoded field values or compatibility claim."
        rows = S.compact_records(rows, filename, default_source)
        sha256 = S.write_json(OUTPUT / filename, {"records": rows})
        dataset = S.dataset(identifier, title, filename, sha256, repo, revision, authors, modifications)
        dataset["attribution"]["sourceUrl"] = default_source
        datasets.append(dataset)
        print(f"{identifier}: {len(rows)} records; SHA-256 {sha256}")
    S.write_json(OUTPUT / "pack.json", {"schema": 1, "id": "eldenring.community.en", "version": "1.1.0",
                                     "title": "Elden Ring attributed community facts", "plugins": ["eldenring"],
                                     "titleIds": [], "gameBuilds": [], "locale": "en", "datasets": datasets})
    lines = ["# Community facts provenance", "", f"Licence: **{S.LICENSE}**.",
             f"Retrieved {S.RETRIEVED}. This records reuse authorization, not an upstream licence grant.",
             "No root licence was found in the pinned TGA, Paramdex or ER-Save-Lib trees.", "",
             "Reproduce with `python -I plugins/EldenRing/Evidence/import-community-catalog.py <scratch>`.",
             "The script reads reviewed archives/text only; it never downloads or executes upstream code.",
             "Archive hashes are pinned in `community_sources.py`. All extracted source-byte hashes follow.", "",
             "Only literal item/name/category membership and state-group event IDs are imported from TGA.",
             "Script quantities, predicates, runtime pointers and writers are discarded.",
             "ClayAmore items.rs contributes 19 inherited TGA groups / 545 active occurrences (544 distinct memberships); its MIT adaptation",
             "does not relicense the original TGA data. Commented IDs and corrupted name comments are excluded.",
             "Multi-flag groups are separate raw observations, never an inferred Boolean completion test.",
             "Commented groups are excluded. One identical repeated flag is retained with sourceOccurrences=2.",
             "DLC1/DLC3 are upstream group labels; they do not assert PS5 support, installation or ownership.",
             "Flag addressing uses the ER-Save-Lib sparse group map: block * 125 + (event % 1000) / 8,",
             "LSB bit 7 - (event % 8). Unmapped or out-of-range candidates remain in F2 but are not read.", "",
             "Paramdex subset: equipment/goods/gems, arts, graces, classes, reinforcement and NG+ scaling layouts.",
             "XML contributes only technical declarations and version/type metadata;",
             "DisplayName, Description, defaults, editor bounds, game artwork and all other text nodes are excluded.",
             "No offsets or legal caps are inferred, and no PARAM field values are decoded or written.", "",
             "## Compact storage (v1.1.0)", "",
             "JSON is minified. Attribution holds dataset-constant source URLs, pinned revisions, uncertainty and",
             "flag schema eldenring-community-flags/2. All source-byte hashes are listed below rather than repeated",
             "per record. Names are verbatim; upstreamName appears only when F2 whitespace trimming would change it.",
             "Item source overrides remain only when different from the dataset default dropdown. MassItemGib",
             "groups are exact source filenames without .cea (including content folder groups); editorGroups",
             "always come from the pinned ClayAmore items.rs. These joins resolve the complete group provenance.",
             "Flag source and sparse-lookup revision/hash are dataset/sidecar constants. Category event-flag and",
             "candidate schema are dataset-wide. stateGroup duplicated name (before the NG+ persistence prefix).",
             "Layout field sources are ER/Defs/<category>.xml at the pinned Paramdex revision. Per-table type/",
             "version facts live in the :schema record; field order is the :field:<index> native key suffix.", "",
             "Removed all 8,651 PARAM row labels (eleven Names tables) and their standalone browse catalog.",
             "No save decoder or inventory/equipment/progress/regulation view consumed those labels; the only",
             "consumer was the dump browsing itself. No sample-derived selection or future-content restriction",
             "was introduced: all 3,221 TGA identities, 1,748 flags and 1,072 layout records remain intact.",
             "Inventory names and read-only pickers still use the complete original and TGA name catalogs.", "",
             "## Dataset default sources", ""]
    lines.extend(f"- {dataset['id']}: {dataset['attribution']['sourceUrl']}" for dataset in datasets)
    lines.extend(["", "| Source (author/project named in URL) | Original SHA-256 |", "| --- | --- |"])
    lines.extend(f"| {url} | `{sha256}` |" for url, sha256 in sorted(sources.files.items()))
    (OUTPUT / "community.source.md").write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    references = ["// Generated factual source URLs by Evidence/import-community-catalog.py.",
                  "namespace SaveToolkit.Plugins.EldenRing;", "", "internal static class CommunitySourceReferences", "{",
                  "    public static IReadOnlyList<string> All =>", "    ["]
    references.extend(f'        "{url}",' for url in sorted(sources.files))
    references.extend(["    ];", "}", ""])
    (OUTPUT.parent.parent / "CommunitySourceReferences.cs").write_text("\n".join(references), encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()

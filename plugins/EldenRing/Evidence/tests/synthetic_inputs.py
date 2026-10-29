"""Small, original input examples for unavailable community and Compass sources."""

import json
import zipfile

from PIL import Image

from importer_support import sha256


def community_inputs(module, scratch):
    root = module.ROOT
    item_xml = '<Root><DropDownList>40000001: Seed \nF0000001:Sentinel</DropDownList></Root>'
    recipes = """{ 0x40000001, 99 }, -- Recipe label must not replace dropdown
{ 0x00000002, 1 }, -- Weapon
-- { 0x40000003, 1 }, -- Disabled
"""
    flags = '''local stateGroups = {
{category = "Graces", subcategory = "Region", name = " Shared ", flags = {
FLAG(76100),
FLAG(76100),
FLAG(60730), -- NG+ Persistence
}},
--[[
{category = "Disabled", name = "Excluded", flags = {
FLAG(123),
}},
]]
}
local stateGroupsDlc1 = {
{category = "Bosses", name = "Expansion", flags = {
FLAG(76800),
FLAG(999999),
}},
}
local stateGroupsDlc3 = {
{category = "Bosses", name = "Future source label", flags = {
FLAG(80000),
}},
}
local stateGroupsDlc = {}
FLAG(999), -- Outside selected tables
'''
    xml = '''<PARAMDEF><ParamType>TEST_PARAM</ParamType><DataVersion>3</DataVersion>
<FormatVersion>203</FormatVersion><Fields>
<Field Def="u32 identity = 999"><DisplayName>Excluded</DisplayName><Maximum>9999</Maximum></Field>
<Field Def=" dummy8 pad[2] "><Description>Excluded prose</Description></Field>
</Fields></PARAMDEF>'''
    tables = ("EquipParamWeapon", "EquipParamProtector", "EquipParamAccessory", "EquipParamGoods",
              "EquipParamGem", "SwordArtsParam", "BonfireWarpParam", "CharaInitParam",
              "ReinforceParamWeapon", "ClearCountCorrectParam")
    contents = {
        "tga.zip": {"README.md": "Synthetic review statement", root + "MiscWIP/Dropdowns/ItemDropdownBase.xml": item_xml,
                    root + "MassItemGib/-- Base/Seeds.cea": recipes,
                    root + "MassItemGib/Other.cea": recipes,
                    root + "Event Flags/Event Flag Manager/.cea": flags},
        "paramdex.zip": {"README.md": "Synthetic review statement",
                         **{f"ER/Defs/{table}.xml": xml for table in tables}},
    }
    archives = {}
    for filename, files in contents.items():
        with zipfile.ZipFile(scratch / filename, "w") as archive:
            for name, text in files.items():
                entry = zipfile.ZipInfo("fixture/" + name, date_time=(2026, 1, 1, 0, 0, 0))
                archive.writestr(entry, text.encode("utf-8"))
        repo, revision, _ = module.S.ARCHIVES[filename]
        archives[filename] = repo, revision, sha256((scratch / filename).read_bytes())
    groups = '\n'.join(f'item_groups.insert("Group {i}".to_string(), vec![\n 0x40000001,\n 0x40000001,\n // 0x40000003,\n]);'
                       for i in range(19)).encode()
    blocks = b"76,26\n60,10\n80,30\n"
    (scratch / "items.rs").write_bytes(groups)
    (scratch / "eventflag_bst.txt").write_bytes(blocks)
    return archives, groups, blocks


def visual_inputs(module, scratch):
    scratch.mkdir()

    def table(name, rows):
        (scratch / f"{name}.ts").write_text("export const rows = " + json.dumps(rows, ensure_ascii=False)[:-1] + ",];",
                                           encoding="utf-8", newline="\n")

    # Same icon under two native IDs also exercises content-address deduplication.
    for filename, source in module.TABLES.items():
        records = json.loads((module.PACK / (filename + ".json")).read_text(encoding="utf-8"))["records"]
        choices = [row for row in records if row["attributes"]["upstreamName"]][:3]
        if source == "goods":
            choices += [row for row in records if row["attributes"]["upstreamId"] == "8600"]
        if source == "armor":
            choices += [row for row in records if row["name"] == "Blue Cloth Cowl"]
        rows = [{"id": int(row["attributes"]["upstreamId"]), "name": row["name"],
                 "icon": 1 + index % 2, "description": ["Publisher café", "Second paragraph"],
                 "category": "Consumable"} for index, row in enumerate(choices)]
        for row in rows:
            if source == "goods" and row["id"] == 8600:
                row["name"] = "Map: Limgrave, West"
        # Deliberate name mismatch, no description and absent icon must remain identity-only.
        rows.extend([{"id": int(records[-1]["attributes"]["upstreamId"]), "name": "Wrong name",
                      "icon": 1, "description": ["Excluded"], "category": "Consumable"},
                     {"id": 123, "name": "Missing", "icon": 0, "description": []}])
        table(source, rows)
    table("world-map-legacy-conv", [
        {"srcMapId": "m10_00_00", "srcX": 0, "srcZ": 0, "addX": 7200, "addZ": 16000, "master": "M01"},
        {"srcMapId": "m10_00_00", "srcX": 500, "srcZ": 500, "addX": 7500, "addZ": 16000, "master": "M00"}])
    table("markers", [{"entityId": 1, "mapId": "m60_28_60_00", "x": 0, "z": 0},
                      {"entityId": 2, "mapId": "m61_28_60_00", "x": 0, "z": 0},
                      {"entityId": 2, "mapId": "m61_28_60_00", "x": 1, "z": 0}])
    table("graces", [{"flagId": 100, "name": "Grace", "bonfireEntityId": 1},
                     {"flagId": 200, "name": "Ambiguous", "bonfireEntityId": 2},
                     {"flagId": 201, "name": "Missing", "bonfireEntityId": 3}])
    bosses = [(101, "m61_14_30_01", 0, 0), (102, "m10_00_00_00", 0, 0),
              (103, "m10_00_00_00", 490, 490), (104, "m99_00_00_00", 0, 0),
              (105, "m60_00_00_00", 0, 0), (106, "m60_28_60_00", 0, 0),
              (106, "m60_28_60_00", 1, 0), (107, "m60_07_15_02", 0, 0)]
    table("bosses", [{"defeatFlagId": event, "name": "", "mapId": map_id, "x": x, "z": z}
                     for event, map_id, x, z in bosses])
    for icon in (1, 2):
        target = scratch / f"packages/data/images/icons/items/{icon}.webp"
        target.parent.mkdir(parents=True, exist_ok=True)
        Image.new("RGBA", (80, 40), (90, 120, 180, 200)).save(target, "WEBP", lossless=True, exact=True)
    for layer in ("M00", "M01", "M10"):
        target = scratch / f"packages/data/images/map-tiles/{layer}/base/3/1/2.webp"
        target.parent.mkdir(parents=True, exist_ok=True)
        Image.new("RGBA", (256, 256), (20, 80, 40, 255)).save(target, "WEBP", lossless=True, exact=True)
    pins = [{"path": path.relative_to(scratch).as_posix(), "sha256": sha256(path.read_bytes())}
            for path in sorted(scratch.rglob("*")) if path.is_file()]
    (module.ROOT / "Evidence/visual-inputs.json").write_text(json.dumps(pins), encoding="utf-8")

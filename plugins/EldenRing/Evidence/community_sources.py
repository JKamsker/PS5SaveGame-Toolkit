"""Pinned, text-only reference extraction helpers. Never execute upstream code."""

import hashlib
import json
from urllib.parse import quote
import xml.etree.ElementTree as ET
import zipfile

LICENSE = "No licence published; reused with attribution per owner decision 2026-10-28"
RETRIEVED = "2026-10-29"
TGA_REVISION = "393c439e72ad48128efdc5e30fc3feb4bfbd210f"
PARAMDEX_REVISION = "ff7245e524329bc3eab00036723d2bd53384cedf"
LIB_REVISION = "664ff9e83b7022a6fdc5a8332e8b7b22dce34e0f"
ARCHIVES = {
    "tga.zip": ("The-Grand-Archives/Elden-Ring-CT-TGA", TGA_REVISION,
                "c894e3a47900f175fe3688686fdcda78534f3cd44dc29550e402f7e4a940e7a9"),
    "paramdex.zip": ("soulsmods/Paramdex", PARAMDEX_REVISION,
                     "c1b5b54dc838936e1cacbc7d495b3c762a2638e2e1cc8b270646a687a1236eee"),
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


class Sources:
    def __init__(self, scratch):
        self.files = {}
        self.archives = {}
        for name, (_, _, expected) in ARCHIVES.items():
            data = (scratch / name).read_bytes()
            if digest(data) != expected:
                raise ValueError(f"Unreviewed archive: {name}")
            self.archives[name] = zipfile.ZipFile(scratch / name)

    def read(self, archive, suffix):
        matches = [name for name in self.archives[archive].namelist() if name.endswith('/' + suffix)]
        if len(matches) != 1:
            raise ValueError(f"Expected one source: {suffix}")
        data = self.archives[archive].read(matches[0])
        repo, revision, _ = ARCHIVES[archive]
        url = f"https://github.com/{repo}/blob/{revision}/{quote(suffix, safe='/')}"
        self.files[url] = digest(data)
        return data.decode("utf-8-sig"), url, digest(data)


def record(key, name, category, source, revision, source_hash, attributes, description):
    if not name.strip():
        raise ValueError(f"Missing name: {key}")
    return {"id": key, "name": name, "category": category, "source": source,
            "description": description, "attributes": {
                "sourceRevision": revision, "sourceSha256": source_hash,
                "upstreamName": name, **attributes}}


def paramdex(sources):
    tables = ("EquipParamWeapon", "EquipParamProtector", "EquipParamAccessory", "EquipParamGoods",
              "EquipParamGem", "SwordArtsParam", "BonfireWarpParam", "CharaInitParam",
              "ReinforceParamWeapon", "ClearCountCorrectParam")
    fields = []
    for table in tables:
        text, url, source_hash = sources.read("paramdex.zip", f"ER/Defs/{table}.xml")
        root = ET.fromstring(text)
        metadata = {"table": table, "paramType": root.findtext("ParamType"),
                    "dataVersion": root.findtext("DataVersion"), "formatVersion": root.findtext("FormatVersion")}
        definitions = root.findall("Fields/Field")
        fields.append(record(f"{table}:schema", table + " layout", table, url, PARAMDEX_REVISION, source_hash,
                             {**metadata, "fieldCount": str(len(definitions))},
                             "Layout facts only. Version equality alone does not establish PS5 field compatibility or legal limits."))
        for index, field in enumerate(definitions):
            # Strip default values; exclude DisplayName, Description, editor maxima and every other text node.
            declaration = field.attrib["Def"].split("=", 1)[0].strip()
            fields.append(record(f"{table}:field:{index}", declaration, table, url, PARAMDEX_REVISION, source_hash,
                                 {**metadata, "fieldIndex": str(index)},
                                 "Technical type/name/width only; no game descriptions, editor bounds or decoded save value."))
    return fields


def compact_records(rows, filename, default_source):
    """Keep runtime facts; provenance constants live in attribution and the source sidecar."""
    for row in rows:
        row.pop("description", None)
        if row.get("source") == default_source or (filename == "param-layouts.json" and not row["id"].endswith(":schema")):
            row.pop("source", None)
        attributes = row["attributes"]
        for key in ("sourceRevision", "sourceSha256", "editorGroupSource", "editorGroupSha256", "groupSources"):
            attributes.pop(key, None)
        if attributes.get("upstreamName") == row["name"].strip():
            attributes.pop("upstreamName", None)
        if filename == "flags.json":
            row.pop("category")  # The dataset is exclusively event-flag candidates.
            for key in ("schema", "lookupSource", "lookupSha256", "lookupRevision", "stateGroup"):
                attributes.pop(key, None)
        if filename == "param-layouts.json":
            attributes.pop("table", None)  # Already category and the native key prefix.
            if not row["id"].endswith(":schema"):
                for key in ("paramType", "dataVersion", "formatVersion", "fieldIndex"):
                    attributes.pop(key, None)  # Per-table summary, or ordinal encoded in the key.
        if not attributes:
            row.pop("attributes")
    return rows


def write_json(path, value):
    content = json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n"
    path.write_text(content, encoding="utf-8", newline="\n")
    return digest(content.encode())


def dataset(identifier, title, filename, sha256, repo, revision, author, modifications):
    return {"id": identifier, "kind": "records", "title": title,
            "files": [{"path": filename, "sha256": sha256}], "attribution": {
                "sourceUrl": f"https://github.com/{repo}/tree/{revision}", "author": author,
                "license": LICENSE, "licenseUrl": f"https://github.com/{repo}/tree/{revision}",
                "retrieved": RETRIEVED, "modifications": modifications}}

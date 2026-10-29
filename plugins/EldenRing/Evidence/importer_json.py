"""The identity/progress importers' pretty-JSON byte contract (UTF-8, LF, final newline)."""

import hashlib
import json


def write_json(path, value):
    content = json.dumps(value, ensure_ascii=False, indent=2) + "\n"
    path.write_text(content, encoding="utf-8", newline="\n")
    return hashlib.sha256(content.encode("utf-8")).hexdigest()

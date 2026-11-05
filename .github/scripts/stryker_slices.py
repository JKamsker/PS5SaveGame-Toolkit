"""Select a stable rotating partition and write an explicit Stryker mutate list."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path


SLICE_COUNT = 9
HASH_SEED = "9402"  # Fixed workload-calibrated seed; changing it reassigns every file.


def slice_index(path):
    """Use project-relative POSIX paths; additions never reassign existing files."""
    digest = hashlib.sha256(f"{HASH_SEED}:{path}".encode("utf-8")).digest()
    return int.from_bytes(digest, "big") % SLICE_COUNT + 1


def source_files(project):
    return sorted(path.relative_to(project).as_posix() for path in project.rglob("*.cs")
                  if not {"bin", "obj"}.intersection(path.relative_to(project).parts))


def select_files(files, selection):
    if selection == "all":
        return sorted(files)
    if selection not in {str(index) for index in range(1, SLICE_COUNT + 1)}:
        raise ValueError(f"slice must be 1–{SLICE_COUNT} or all")
    return sorted(path for path in files if slice_index(path) == int(selection))


def resolve_slice(selection, now=None):
    if selection:
        return selection
    return str((now or datetime.now(timezone.utc)).date().toordinal() % SLICE_COUNT + 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, choices=["Core", "Sdk"])
    parser.add_argument("--slice", default="")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--scratch", type=Path, required=True)
    args = parser.parse_args()
    selection = resolve_slice(args.slice)
    files = select_files(source_files(args.root / "src" / f"SaveToolkit.{args.project}"), selection)
    if not files:
        parser.error("selected slice has no source files; refusing an unbounded mutation run")
    config = json.loads((args.root / "tests/stryker-config.json").read_text(encoding="utf-8"))
    config["stryker-config"]["reporters"] = ["progress", "html", "json"]
    # Exact project-relative globs avoid matching a same-named file in another folder.
    config["stryker-config"]["mutate"] = files
    args.scratch.mkdir(parents=True, exist_ok=True)
    (args.scratch / "stryker-config.json").write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
    manifest = {"project": args.project, "slice": selection, "count": SLICE_COUNT, "files": files}
    (args.scratch / "slice.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"{args.project}: slice {selection}/{SLICE_COUNT}, {len(files)} files")
    if output := os.environ.get("GITHUB_OUTPUT"):
        with open(output, "a", encoding="utf-8") as stream:
            stream.write(f"slice={selection}\ncount={SLICE_COUNT}\nfiles={len(files)}\n")


if __name__ == "__main__":
    main()

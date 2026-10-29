"""Stage only the reviewed public documentation allowlist, never repository data."""

import argparse
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "docs-site"


def prepare(destination):
    destination.mkdir(parents=True, exist_ok=False)
    for name in json.loads((SOURCE / "public-files.json").read_text(encoding="utf-8")):
        source = (SOURCE / name).resolve()
        if not source.is_relative_to(SOURCE.resolve()) or not source.is_file():
            raise ValueError(f"Invalid public source: {name}")
        target = destination / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    (destination / ".nojekyll").touch()
    # Render the supported scaffold itself rather than maintaining another copy of its code.
    examples = destination / "examples"
    examples.mkdir()
    tokens = {
        "@PROJECT@": "SaveToolkit.Plugins.ExampleGame", "@NAME@": "ExampleGame",
        "@ID@": "example-game", "@GAME_LITERAL@": '"Example Game"',
        "@GAMES@": 'new GameTitle("PPSA00000", "Example Game")',
    }
    for kind in ("Plugin", "Document"):
        text = (ROOT / f"scripts/templates/plugin/{kind}.cs.template").read_text(encoding="utf-8")
        for token, value in tokens.items():
            text = text.replace(token, value)
        if "@" in text:
            raise ValueError(f"Unresolved scaffold token in {kind}")
        (examples / f"ExampleGame{kind}.cs").write_text(text, encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", required=True, type=Path)
    args = parser.parse_args()
    # The build owns an empty stage directory; refuse to overwrite existing content.
    if args.destination.exists() and not any(args.destination.iterdir()):
        args.destination.rmdir()
    prepare(args.destination)

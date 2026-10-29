"""Bundle the pinned renderer and inline its two attribution logos for fully local rendering."""

import argparse
import base64
from pathlib import Path
import shutil


def bundle(package, stage):
    destination = stage / "assets/redoc"
    destination.mkdir(parents=True, exist_ok=True)
    script = (package / "bundles/redoc.standalone.js").read_text(encoding="utf-8")
    # Redoc CE otherwise fetches these two attribution images from its CDN. Preserve
    # the marks and attribution unmodified while removing that runtime dependency.
    for name in ("logo-mini.svg", "logo-mini-full.svg"):
        remote = f"https://cdn.redoc.ly/redoc/{name}"
        if script.count(remote) != 1:
            raise ValueError(f"Renderer attribution URL changed: {remote}. Review the pinned upgrade.")
        data = base64.b64encode((destination / name).read_bytes()).decode("ascii")
        script = script.replace(remote, f"data:image/svg+xml;base64,{data}")
    (destination / "redoc.standalone.js").write_text(script, encoding="utf-8")
    shutil.copyfile(package / "LICENSE", destination / "LICENSE.txt")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--stage", required=True, type=Path)
    args = parser.parse_args()
    bundle(args.package, args.stage)
